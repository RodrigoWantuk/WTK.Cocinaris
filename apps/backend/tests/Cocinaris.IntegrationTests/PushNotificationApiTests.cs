using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cocinaris.Api.Planning;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Planning.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0062 Slice 7: subscribing, owner isolation, cleanup, and one notification a day.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private const string Push = "/api/v1/notifications/push";
    // 09:00 in America/Sao_Paulo.
    private static readonly DateTimeOffset SendingHour = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class SettableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakePushSender(string? publicKey = "public-key") : IWebPushSender
    {
        public ConcurrentQueue<(string Endpoint, string Payload)> Sent { get; } = new();

        public ConcurrentDictionary<string, PushSendOutcome> Outcomes { get; } = new();

        public string? PublicKey { get; } = publicKey;

        public Task<PushSendOutcome> SendAsync(PushSubscriptionEntry subscription, string payload, CancellationToken cancellationToken)
        {
            Sent.Enqueue((subscription.Endpoint, payload));
            return Task.FromResult(Outcomes.GetValueOrDefault(subscription.Endpoint, PushSendOutcome.Delivered));
        }
    }

    private static object PushBody(string endpoint)
    {
        var point = new byte[65];
        point[0] = 0x04;
        return new { endpoint, p256dh = Base64Url.EncodeToString(point), auth = Base64Url.EncodeToString(new byte[16]) };
    }

    private (WebApplicationFactory<Program> Host, FakePushSender Sender, SettableTime Time) PushHost(string? publicKey = "public-key")
    {
        var sender = new FakePushSender(publicKey);
        var time = new SettableTime(SendingHour);
        var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWebPushSender>();
            services.AddSingleton<IWebPushSender>(sender);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
        }));
        return (host, sender, time);
    }

    private static Task<int> RunPushAsync(WebApplicationFactory<Program> host) =>
        PlanReminderPushWorker.RunOnceAsync(host.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, CancellationToken.None);

    private static async Task SetZoneAsync(HttpClient client, string csrf, string zone, string language = "es")
    {
        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new
            {
                language = new { action = "confirm", value = language, durability = "durable" },
                timeZone = new { action = "confirm", value = zone, durability = "durable" },
            }),
        };
        patch.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.True((await client.SendAsync(patch)).IsSuccessStatusCode);
    }

    /// <summary>An accepted menu with tomorrow's dinner, whose onion is missing, so <c>buy</c> is due today.</summary>
    private static async Task AcceptMenuNeedingABuyAsync(HttpClient client, string csrf, DateOnly today)
    {
        var stew = await CreateRecipeAsync(client, csrf, "Ensopado secreto", ("Cebola", 2m, "unit"));
        var menu = (await (await PostAsync(client, csrf, "/api/v1/planning/menus", new { startDate = today, endDate = today.AddDays(6) }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("menuId").GetGuid();
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menu}/entries", new { recipeId = stew, plannedDate = today.AddDays(1), mealType = "dinner" }, Guid.NewGuid());
        Assert.True((await PostAsync(client, csrf, $"/api/v1/planning/menus/{menu}/acceptance", new { })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task WithoutKeysPushIsUnavailableAndTheJobDoesNothing()
    {
        var (host, sender, _) = PushHost(publicKey: null);
        await using var _host = host;
        using var client = PlanClient(host, "push-off");
        var csrf = await CsrfAsync(client);

        var status = await client.GetFromJsonAsync<JsonElement>(Push);
        Assert.False(status.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("publicKey").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(client, csrf, HttpMethod.Put, $"{Push}/subscription", PushBody("https://fcm.googleapis.com/fcm/send/off"))).StatusCode);
        Assert.Equal(0, await RunPushAsync(host));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task SubscribingValidatesTheAddressAndStaysWithItsOwner()
    {
        var (host, _, _) = PushHost();
        await using var _host = host;
        using var owner = PlanClient(host, "push-owner");
        using var stranger = PlanClient(host, "push-stranger");
        var csrf = await CsrfAsync(owner);
        var strangerCsrf = await CsrfAsync(stranger);
        const string endpoint = "https://fcm.googleapis.com/fcm/send/owner";

        // The test host signs everyone in, so the requirement is read from the routes themselves.
        var routes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Where(item => item.RoutePattern.RawText?.StartsWith(Push, StringComparison.Ordinal) == true).ToList();
        Assert.Equal(3, routes.Count);
        Assert.All(routes, route => Assert.NotEmpty(route.Metadata.GetOrderedMetadata<IAuthorizeData>()));

        Assert.True((await owner.GetFromJsonAsync<JsonElement>(Push)).GetProperty("available").GetBoolean());

        // The address is one the server will call, so anything but a known push service is refused.
        foreach (var bad in new[] { "http://fcm.googleapis.com/x", "https://169.254.169.254/latest", "https://localhost/x", "https://fcm.googleapis.com.evil.example/x" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, csrf, HttpMethod.Put, $"{Push}/subscription", PushBody(bad))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, csrf, HttpMethod.Put, $"{Push}/subscription", new { endpoint, p256dh = "short", auth = "short" })).StatusCode);

        // A write without the CSRF token is refused.
        using (var noToken = new HttpRequestMessage(HttpMethod.Put, $"{Push}/subscription") { Content = JsonContent.Create(PushBody(endpoint)) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.SendAsync(noToken)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(owner, csrf, HttpMethod.Put, $"{Push}/subscription", PushBody(endpoint))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(owner, csrf, HttpMethod.Put, $"{Push}/subscription", PushBody(endpoint))).StatusCode);

        async Task<List<PushSubscriptionRecord>> RowsAsync()
        {
            using var scope = host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PushSubscriptions.AsNoTracking().Where(item => item.Endpoint == endpoint).ToListAsync();
        }

        var rows = await RowsAsync();
        var ownerId = Assert.Single(rows).OwnerUserId;

        // Another person's removal answers the same and changes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(stranger, strangerCsrf, $"{Push}/subscription/removal", new { endpoint })).StatusCode);
        Assert.Equal(ownerId, Assert.Single(await RowsAsync()).OwnerUserId);

        // The same browser signed in as someone else moves to them.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(stranger, strangerCsrf, HttpMethod.Put, $"{Push}/subscription", PushBody(endpoint))).StatusCode);
        var moved = Assert.Single(await RowsAsync());
        Assert.NotEqual(ownerId, moved.OwnerUserId);
        Assert.Null(moved.LastSuccessAt);

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(stranger, strangerCsrf, $"{Push}/subscription/removal", new { endpoint })).StatusCode);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task AnOwnerKeepsOnlyTheNewestSubscriptions()
    {
        var (host, _, time) = PushHost();
        await using var _host = host;
        using var client = PlanClient(host, "push-many");
        var csrf = await CsrfAsync(client);
        for (var index = 0; index < PushSubscriptionRules.MaxPerOwner + 2; index++)
        {
            time.Now = SendingHour.AddMinutes(index);
            Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, csrf, HttpMethod.Put, $"{Push}/subscription", PushBody($"https://fcm.googleapis.com/fcm/send/{index}"))).StatusCode);
        }

        using var scope = host.Services.CreateScope();
        var endpoints = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PushSubscriptions.AsNoTracking().Select(item => item.Endpoint).ToListAsync();
        Assert.Equal(PushSubscriptionRules.MaxPerOwner, endpoints.Count);
        Assert.DoesNotContain("https://fcm.googleapis.com/fcm/send/0", endpoints);
        Assert.DoesNotContain("https://fcm.googleapis.com/fcm/send/1", endpoints);
    }

    [Fact]
    public async Task AnOwnerGetsOneGenericNotificationADayAtTheSendingHour()
    {
        var (host, sender, time) = PushHost();
        await using var _host = host;
        using var client = PlanClient(host, "push-daily");
        var csrf = await CsrfAsync(client);
        await SetZoneAsync(client, csrf, "America/Sao_Paulo");
        var today = new DateOnly(2026, 11, 10);
        await AcceptMenuNeedingABuyAsync(client, csrf, today);
        const string phone = "https://fcm.googleapis.com/fcm/send/phone";
        const string dead = "https://updates.push.services.mozilla.com/wpush/v2/dead";
        await SendAsync(client, csrf, HttpMethod.Put, $"{Push}/subscription", PushBody(phone));
        await SendAsync(client, csrf, HttpMethod.Put, $"{Push}/subscription", PushBody(dead));
        sender.Outcomes[dead] = PushSendOutcome.Gone;

        // Outside the hour, nothing goes out and the day stays available.
        time.Now = SendingHour.AddHours(-1);
        Assert.Equal(0, await RunPushAsync(host));
        Assert.Empty(sender.Sent);

        time.Now = SendingHour;
        Assert.Equal(1, await RunPushAsync(host));
        Assert.Equal(2, sender.Sent.Count);

        // No food, recipe, or restriction reaches the push service.
        Assert.All(sender.Sent, item => Assert.Equal("""{"type":"plan_reminder","language":"es"}""", item.Payload));

        using (var scope = host.Services.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PushSubscriptions.AsNoTracking().ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(phone, row.Endpoint);
            Assert.Equal(SendingHour, row.LastSuccessAt);
        }

        // A second run, as after a restart in the same hour, sends nothing.
        Assert.Equal(0, await RunPushAsync(host));
        Assert.Equal(2, sender.Sent.Count);

        // The next day the reminder (now for today's dinner) goes out again.
        time.Now = SendingHour.AddDays(1);
        Assert.Equal(1, await RunPushAsync(host));
        Assert.Equal(3, sender.Sent.Count);
    }

    [Fact]
    public async Task NoZoneNothingDueAndOneBadOwnerDoNotReachOrStopTheOthers()
    {
        var (host, sender, _) = PushHost();
        await using var _host = host;
        var today = new DateOnly(2026, 11, 10);
        async Task<HttpClient> OwnerAsync(string subject, bool zone, bool menu, string endpoint)
        {
            var client = PlanClient(host, subject);
            var csrf = await CsrfAsync(client);
            if (zone)
            {
                await SetZoneAsync(client, csrf, "America/Sao_Paulo", "pt-BR");
            }

            if (menu)
            {
                await AcceptMenuNeedingABuyAsync(client, csrf, today);
            }

            Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, csrf, HttpMethod.Put, $"{Push}/subscription", PushBody(endpoint))).StatusCode);
            return client;
        }

        using var noZone = await OwnerAsync("push-no-zone", zone: false, menu: true, "https://fcm.googleapis.com/fcm/send/no-zone");
        using var nothing = await OwnerAsync("push-nothing", zone: true, menu: false, "https://fcm.googleapis.com/fcm/send/nothing");
        using var failing = await OwnerAsync("push-failing", zone: true, menu: true, "https://fcm.googleapis.com/fcm/send/failing");
        using var fine = await OwnerAsync("push-fine", zone: true, menu: true, "https://fcm.googleapis.com/fcm/send/fine");
        sender.Outcomes["https://fcm.googleapis.com/fcm/send/failing"] = PushSendOutcome.Failed;

        Assert.Equal(2, await RunPushAsync(host));
        Assert.Equal(["https://fcm.googleapis.com/fcm/send/failing", "https://fcm.googleapis.com/fcm/send/fine"], sender.Sent.Select(item => item.Endpoint).Order());

        // A failed send keeps the subscription for tomorrow.
        using var scope = host.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PushSubscriptions.AnyAsync(item => item.Endpoint == "https://fcm.googleapis.com/fcm/send/failing"));
    }

    [Fact]
    public async Task ConcurrentRunsClaimADayOnce()
    {
        var (host, _, _) = PushHost();
        await using var _host = host;
        using var client = PlanClient(host, "push-claim");
        await CsrfAsync(client);
        var owner = Guid.CreateVersion7();
        var day = new DateOnly(2026, 11, 10);

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IPushSubscriptionStore>().TryClaimDayAsync(owner, day, CancellationToken.None);
        }));

        Assert.Equal(1, claims.Count(item => item));
        using var later = host.Services.CreateScope();
        var store = later.ServiceProvider.GetRequiredService<IPushSubscriptionStore>();
        Assert.False(await store.TryClaimDayAsync(owner, day.AddDays(-1), CancellationToken.None));
        Assert.True(await store.TryClaimDayAsync(owner, day.AddDays(1), CancellationToken.None));
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(status);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static WebPushSender RealSender(HttpMessageHandler handler, string? publicKey = null, string? privateKey = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        var generatedPublic = Base64Url.EncodeToString([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
        var generatedPrivate = Base64Url.EncodeToString(parameters.D!);
        return new WebPushSender(
            Options.Create(new WebPushOptions { PublicKey = publicKey ?? generatedPublic, PrivateKey = privateKey ?? generatedPrivate, Subject = "https://cocinaris.example" }),
            new SingleClientFactory(handler),
            NullLogger<WebPushSender>.Instance);
    }

    private static PushSubscriptionEntry BrowserSubscription(string endpoint)
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = browser.ExportParameters(includePrivateParameters: false).Q;
        return new PushSubscriptionEntry(Guid.NewGuid(), endpoint, Base64Url.EncodeToString([0x04, .. point.X!, .. point.Y!]), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    [Fact]
    public async Task TheRealSenderEncryptsSignsAndReadsThePushServiceAnswer()
    {
        const string payload = """{"type":"plan_reminder","language":"pt-BR"}""";
        var accepted = new RecordingHandler(HttpStatusCode.Created);
        var sender = RealSender(accepted);
        Assert.NotNull(sender.PublicKey);
        Assert.Equal(PushSendOutcome.Delivered, await sender.SendAsync(BrowserSubscription("https://fcm.googleapis.com/fcm/send/real"), payload, CancellationToken.None));
        var (request, body) = Assert.Single(accepted.Requests);
        Assert.Equal("https://fcm.googleapis.com/fcm/send/real", request.RequestUri!.ToString());
        Assert.Contains("aes128gcm", request.Content!.Headers.ContentEncoding);
        Assert.StartsWith("vapid", request.Headers.Authorization?.Scheme ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plan_reminder", Encoding.UTF8.GetString(body), StringComparison.Ordinal);

        Assert.Equal(PushSendOutcome.Gone, await RealSender(new RecordingHandler(HttpStatusCode.Gone)).SendAsync(BrowserSubscription("https://fcm.googleapis.com/fcm/send/gone"), payload, CancellationToken.None));
        Assert.Equal(PushSendOutcome.Gone, await RealSender(new RecordingHandler(HttpStatusCode.NotFound)).SendAsync(BrowserSubscription("https://fcm.googleapis.com/fcm/send/none"), payload, CancellationToken.None));
        Assert.Equal(PushSendOutcome.Failed, await RealSender(new RecordingHandler(HttpStatusCode.InternalServerError)).SendAsync(BrowserSubscription("https://fcm.googleapis.com/fcm/send/down"), payload, CancellationToken.None));

        // A row that no longer passes the rules is never called.
        var refused = new RecordingHandler(HttpStatusCode.Created);
        Assert.Equal(PushSendOutcome.Failed, await RealSender(refused).SendAsync(BrowserSubscription("https://internal.example/x"), payload, CancellationToken.None));
        Assert.Empty(refused.Requests);
    }

    [Fact]
    public void InvalidKeysLeaveTheSenderOff() =>
        Assert.Null(RealSender(new RecordingHandler(HttpStatusCode.Created), publicKey: "bad", privateKey: "bad").PublicKey);
}
