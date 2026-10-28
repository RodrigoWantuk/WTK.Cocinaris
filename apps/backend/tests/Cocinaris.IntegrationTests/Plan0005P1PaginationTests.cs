using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;

namespace Cocinaris.IntegrationTests;

/// <summary>
/// PLAN-0005 P1 pagination and query coverage against the shared PostgreSQL fixture.
/// </summary>
[Collection("Plan0005SharedPostgres")]
public sealed class Plan0005P1PaginationTests
{
    private readonly Plan0005SharedPostgresFixture _fixture;

    /// <summary>Creates pagination tests bound to the shared PLAN-0005 PostgreSQL fixture.</summary>
    public Plan0005P1PaginationTests(Plan0005SharedPostgresFixture fixture) => _fixture = fixture;

    /// <summary>TEST-0005-077/080: sixty-plus lots paginate without duplicates or omissions; page size enforced.</summary>
    [Fact]
    public async Task SixtyLotsPaginateWithoutDuplicatesOrOmissionsAndPageSizeIsEnforced()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var marker = $"plan0005-page-{Guid.NewGuid():N}";

        for (var index = 0; index < 60; index++)
        {
            var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), $"{marker}-{index:D2}", 1m + (index % 7));
            Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        }

        var oversized = await client.GetAsync($"/api/v1/inventory/lots?pageSize=101&search={marker}");
        Assert.True((int)oversized.StatusCode is 400 or 422);

        var collected = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var path = cursor is null
                ? $"/api/v1/inventory/lots?pageSize=25&search={marker}"
                : $"/api/v1/inventory/lots?pageSize=25&search={marker}&cursor={Uri.EscapeDataString(cursor)}";
            var body = await client.GetFromJsonAsync<JsonElement>(path);
            var items = body.GetProperty("items").EnumerateArray().ToArray();
            Assert.InRange(items.Length, 0, 25);
            foreach (var item in items)
            {
                collected.Add(item.GetProperty("lotId").GetGuid());
            }

            cursor = body.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Equal(60, collected.Count);
        Assert.Equal(60, collected.Distinct().Count());
        Assert.Null(cursor);
    }

    /// <summary>TEST-0005-075/076/079: default active filter, owner-scoped search, tampered cursor rejected.</summary>
    [Fact]
    public async Task DefaultActiveFilterSearchAndTamperedCursorBehaveAsSpecified()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var marker = $"plan0005-filter-{Guid.NewGuid():N}";

        var active = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), $"{marker}-active", 5m);
        var activeTwo = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), $"{marker}-active-two", 6m);
        var depletable = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), $"{marker}-deplete", 1m);
        var activeId = (await active.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var activeTwoId = (await activeTwo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var depletableId = (await depletable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        await AdjustAsync(client, csrf, depletableId, depletable.Headers.ETag!.Tag, Guid.NewGuid().ToString(), 1m);

        var defaultList = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots?search={marker}");
        var depletedList = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots?status=depleted&search={marker}");
        Assert.Equal(2, defaultList.GetProperty("items").GetArrayLength());
        var defaultIds = defaultList.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("lotId").GetGuid()).ToArray();
        Assert.Contains(activeId, defaultIds);
        Assert.Contains(activeTwoId, defaultIds);
        Assert.Single(depletedList.GetProperty("items").EnumerateArray());

        var first = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots?pageSize=1&search={marker}");
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        var tampered = await client.GetAsync($"/api/v1/inventory/lots?cursor={Uri.EscapeDataString(cursor!)}x");
        var problem = await tampered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, tampered.StatusCode);
        Assert.Equal("invalid_cursor", problem.GetProperty("errorCode").GetString());
    }

    private static object CreateLot(string productName, decimal measuredValue) =>
        new { productName, quantity = new { measuredValue, unit = "Gram", availabilityState = (string?)null }, storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null };

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
        return session.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string csrf, string key, string productName, decimal measuredValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = JsonContent.Create(CreateLot(productName, measuredValue)) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> AdjustAsync(HttpClient client, string csrf, Guid lotId, string etag, string key, decimal value)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{lotId}/adjustments")
        {
            Content = JsonContent.Create(new { type = "Consume", value, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed class CocinarisFactory(string connectionString, bool authenticate) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Cocinaris", connectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
                services.PostConfigure<OpenIdConnectOptions>("oidc", options =>
                {
                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        AuthorizationEndpoint = "https://identity.integration.test/authorize",
                        Issuer = "https://identity.integration.test"
                    };
                    options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
                });
                if (authenticate)
                {
                    services.AddAuthentication(TestAuthenticationHandler.TestScheme)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.TestScheme, _ => { });
                }
            });
        }

        public async Task EnsureDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        }
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "plan0005-p1-user";
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim("iss", "https://integration.test")], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
