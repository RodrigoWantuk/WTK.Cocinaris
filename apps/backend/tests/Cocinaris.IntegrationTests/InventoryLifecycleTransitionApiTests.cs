using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0037 Phase 1: lifecycle transitions through the HTTP boundary against PostgreSQL.</summary>
public sealed class InventoryLifecycleTransitionApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4")
        .WithDatabase("cocinaris_lifecycle")
        .WithUsername("cocinaris_test")
        .WithPassword("development-only-test-password")
        .Build();

    private CocinarisFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new CocinarisFactory(_postgres.GetConnectionString());
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task PartialFreezeSplitsTheLotAtomicallyAndRecordsBothHistories()
    {
        using var client = Client("partial-freeze-user");
        var csrf = await CsrfAsync(client);
        var (lotId, etag) = await CreateLotAsync(client, csrf, 1000m, "Pantry", "Sealed");

        var response = await TransitionAsync(client, csrf, lotId, etag, Guid.NewGuid(), new { kind = "Freeze", quantity = new { measuredValue = 400, unit = "Gram" } });
        var source = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(etag, response.Headers.ETag!.Tag);
        Assert.Equal(600m, source.GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal("Pantry", source.GetProperty("storageLocation").GetString());
        Assert.Equal("AsAcquired", source.GetProperty("lifecycle").GetProperty("state").GetString());

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots");
        var derived = list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("lotId").GetGuid() != lotId);
        Assert.Equal(400m, derived.GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal("Freezer", derived.GetProperty("storageLocation").GetString());
        Assert.Equal("Sealed", derived.GetProperty("packageState").GetString());
        Assert.Equal("Frozen", derived.GetProperty("lifecycle").GetProperty("state").GetString());
        Assert.Equal(lotId, derived.GetProperty("lifecycle").GetProperty("parentLotId").GetGuid());
        Assert.Equal(source.GetProperty("productId").GetGuid(), derived.GetProperty("productId").GetGuid());
        var derivedId = derived.GetProperty("lotId").GetGuid();

        var sourceHistory = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");
        var split = sourceHistory.EnumerateArray().First();
        Assert.Equal("TransitionSplit", split.GetProperty("type").GetString());
        Assert.Equal(1000m, split.GetProperty("previousQuantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(600m, split.GetProperty("resultingQuantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal("Freeze", split.GetProperty("transition").GetProperty("kind").GetString());
        Assert.Equal(derivedId, split.GetProperty("transition").GetProperty("relatedLotId").GetGuid());

        var derivedHistory = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{derivedId}/history");
        var created = Assert.Single(derivedHistory.EnumerateArray());
        Assert.Equal("TransitionDerived", created.GetProperty("type").GetString());
        Assert.Equal("Frozen", created.GetProperty("transition").GetProperty("resultingState").GetString());
        Assert.Equal("Freezer", created.GetProperty("transition").GetProperty("resultingStorageLocation").GetString());
        Assert.Equal(lotId, created.GetProperty("transition").GetProperty("relatedLotId").GetGuid());
    }

    [Fact]
    public async Task WholeLotOpenRecordsALifecycleTransition()
    {
        using var client = Client("open-user");
        var csrf = await CsrfAsync(client);
        var (lotId, etag) = await CreateLotAsync(client, csrf, 2m, "Pantry", "Sealed", "Unit");

        var response = await TransitionAsync(client, csrf, lotId, etag, Guid.NewGuid(), new { kind = "Open", targetStorageLocation = "Refrigerator" });
        var lot = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Opened", lot.GetProperty("packageState").GetString());
        Assert.Equal("Refrigerator", lot.GetProperty("storageLocation").GetString());
        Assert.Equal(JsonValueKind.String, lot.GetProperty("lifecycle").GetProperty("openedAt").ValueKind);
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");
        var entry = history.EnumerateArray().First();
        Assert.Equal("LifecycleTransition", entry.GetProperty("type").GetString());
        Assert.Equal("Pantry", entry.GetProperty("transition").GetProperty("previousStorageLocation").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("transition").GetProperty("relatedLotId").ValueKind);
    }

    [Fact]
    public async Task ReplayReturnsTheOriginalResultAndDoesNotSplitTwice()
    {
        using var client = Client("replay-user");
        var csrf = await CsrfAsync(client);
        var (lotId, etag) = await CreateLotAsync(client, csrf, 1000m, "Freezer", null);
        var key = Guid.NewGuid();
        var body = new { kind = "StartThaw", quantity = new { measuredValue = 250, unit = "Gram" } };

        var first = await TransitionAsync(client, csrf, lotId, etag, key, body);
        // The original ETag is now stale; the same key still replays instead of failing with 412.
        var replay = await TransitionAsync(client, csrf, lotId, etag, key, body);
        var reused = await TransitionAsync(client, csrf, lotId, first.Headers.ETag!.Tag, key, new { kind = "StartThaw", quantity = new { measuredValue = 100, unit = "Gram" } });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_key_reused");
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots");
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task PreconditionsRulesAndOwnershipAreEnforced()
    {
        using var owner = Client("transition-owner");
        using var other = Client("transition-other");
        var csrf = await CsrfAsync(owner);
        var otherCsrf = await CsrfAsync(other);
        var (lotId, etag) = await CreateLotAsync(owner, csrf, 500m, "Refrigerator", null);

        await AssertProblemAsync(await TransitionAsync(other, otherCsrf, lotId, etag, Guid.NewGuid(), new { kind = "Freeze" }), HttpStatusCode.NotFound, "resource_not_found");
        await AssertProblemAsync(await TransitionAsync(owner, csrf, lotId, null, Guid.NewGuid(), new { kind = "Freeze" }), HttpStatusCode.PreconditionRequired, "precondition_required");
        await AssertProblemAsync(await TransitionAsync(owner, csrf, lotId, etag, Guid.NewGuid(), new { kind = "CompleteThaw" }), HttpStatusCode.UnprocessableEntity, "domain_rule_violated");
        await AssertProblemAsync(await TransitionAsync(owner, csrf, lotId, etag, Guid.NewGuid(), new { kind = "Freeze", quantity = new { measuredValue = 100, unit = "Milliliter" } }), HttpStatusCode.UnprocessableEntity, "domain_rule_violated");
        await AssertProblemAsync(await TransitionAsync(owner, csrf, lotId, etag, Guid.NewGuid(), new { kind = "Teleport" }), HttpStatusCode.UnprocessableEntity, "domain_rule_violated");

        var moved = await TransitionAsync(owner, csrf, lotId, etag, Guid.NewGuid(), new { kind = "Move", targetStorageLocation = "Pantry" });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        await AssertProblemAsync(await TransitionAsync(owner, csrf, lotId, etag, Guid.NewGuid(), new { kind = "Freeze" }), HttpStatusCode.PreconditionFailed, "precondition_failed");
    }

    private HttpClient Client(string subject)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        return client;
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
        return session.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<(Guid LotId, string ETag)> CreateLotAsync(HttpClient client, string csrf, decimal value, string storage, string? packageState, string unit = "Gram")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots")
        {
            Content = JsonContent.Create(new { productName = "Chicken thighs", quantity = new { measuredValue = value, unit }, storageLocation = storage, packageState })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var lot = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (lot.GetProperty("lotId").GetGuid(), response.Headers.ETag!.Tag);
    }

    private static async Task<HttpResponseMessage> TransitionAsync(HttpClient client, string csrf, Guid lotId, string? etag, Guid key, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{lotId}/transitions") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", key.ToString());
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string errorCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == statusCode, $"Expected {(int)statusCode}, got {(int)response.StatusCode}: {body}");
        Assert.Equal(errorCode, JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString());
    }

    private sealed class CocinarisFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public async Task EnsureDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Cocinaris", connectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
                services.AddAuthentication(TestAuthenticationHandler.TestScheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.TestScheme, _ => { });
            });
        }
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "integration-user";
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim("iss", "https://integration.test")], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
