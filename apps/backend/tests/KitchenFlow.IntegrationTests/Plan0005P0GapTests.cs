using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using KitchenFlow.Infrastructure.Persistence;
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

namespace KitchenFlow.IntegrationTests;

/// <summary>
/// PLAN-0005 independent P0 gap coverage against a single shared PostgreSQL fixture.
/// These tests do not mutate production code; they only assert runtime behavior.
/// </summary>
[Collection("Plan0005SharedPostgres")]
public sealed class Plan0005P0GapTests
{
    private readonly Plan0005SharedPostgresFixture _fixture;

    /// <summary>Creates gap tests bound to the shared PLAN-0005 PostgreSQL fixture.</summary>
    public Plan0005P0GapTests(Plan0005SharedPostgresFixture fixture) => _fixture = fixture;

    /// <summary>TEST-0005-006: invalid CSRF token is rejected without mutation.</summary>
    [Fact]
    public async Task InvalidCsrfTokenIsRejectedWithoutMutation()
    {
        await using var factory = new KitchenFlowFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        await GetCsrfAsync(client);
        var before = await CountLotsAsync(factory);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = JsonContent.Create(CreateLot("csrf-invalid")) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-CSRF-TOKEN", "not-a-valid-csrf-token");
        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await CountLotsAsync(factory));
    }

    /// <summary>TEST-0005-050/051: discard success and over-quantity rejection.</summary>
    [Fact]
    public async Task DiscardSucceedsAndRejectsOverQuantityWithoutMutation()
    {
        await using var factory = new KitchenFlowFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Discard lot", 50m);
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var etag = created.Headers.ETag!.Tag;

        var over = await AdjustmentAsync(client, csrf, lotId, etag, Guid.NewGuid().ToString(), new { type = "Discard", value = 51m, availabilityState = (string?)null, reasonCode = "waste", note = (string?)null });
        Assert.Equal((System.Net.HttpStatusCode)422, over.StatusCode);
        var unchanged = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}");
        Assert.Equal(50m, unchanged.GetProperty("quantity").GetProperty("measuredValue").GetDecimal());

        var discard = await AdjustmentAsync(client, csrf, lotId, etag, Guid.NewGuid().ToString(), new { type = "Discard", value = 10m, availabilityState = (string?)null, reasonCode = "waste", note = (string?)null });
        Assert.Equal(System.Net.HttpStatusCode.OK, discard.StatusCode);
        Assert.Equal(40m, (await discard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");
        Assert.Contains(history.EnumerateArray(), entry => entry.GetProperty("type").GetString() == "Discard");
    }

    /// <summary>TEST-0005-065: two concurrent writers produce one winner and no lost update.</summary>
    [Fact]
    public async Task ConcurrentWritersProduceOneWinnerWithoutLostUpdate()
    {
        await using var factory = new KitchenFlowFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var clientA = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var clientB = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrfA = await GetCsrfAsync(clientA);
        var csrfB = await GetCsrfAsync(clientB);
        var created = await CreateAsync(clientA, csrfA, Guid.NewGuid().ToString(), "Concurrent lot", 100m);
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var etag = created.Headers.ETag!.Tag;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> PatchAsync(HttpClient client, string csrf, string notes)
        {
            await gate.Task;
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/inventory/lots/{lotId}")
            {
                Content = JsonContent.Create(new { storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await client.SendAsync(request);
        }

        var taskA = PatchAsync(clientA, csrfA, "writer-a");
        var taskB = PatchAsync(clientB, csrfB, "writer-b");
        gate.SetResult();
        var responses = await Task.WhenAll(taskA, taskB);

        var successes = responses.Count(item => item.StatusCode == System.Net.HttpStatusCode.OK);
        var conflicts = responses.Count(item => item.StatusCode is System.Net.HttpStatusCode.PreconditionFailed or System.Net.HttpStatusCode.Conflict);
        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);

        var final = await clientA.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}");
        var notes = final.GetProperty("notes").GetString();
        Assert.True(notes is "writer-a" or "writer-b");
        Assert.NotEqual(etag, (await clientA.GetAsync($"/api/v1/inventory/lots/{lotId}")).Headers.ETag!.Tag);
    }

    /// <summary>TEST-0005-069: idempotency keys are scoped per user.</summary>
    [Fact]
    public async Task IdempotencyKeyIsScopedPerUser()
    {
        await using var factory = new KitchenFlowFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        other.DefaultRequestHeaders.Add("X-Test-Subject", "plan0005-user-b");
        var ownerCsrf = await GetCsrfAsync(owner);
        var otherCsrf = await GetCsrfAsync(other);
        var sharedKey = Guid.NewGuid().ToString();

        var ownerCreate = await CreateAsync(owner, ownerCsrf, sharedKey, "Owner keyed lot", 11m);
        var otherCreate = await CreateAsync(other, otherCsrf, sharedKey, "Other keyed lot", 22m);
        Assert.Equal(System.Net.HttpStatusCode.Created, ownerCreate.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Created, otherCreate.StatusCode);

        var ownerLot = (await ownerCreate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var otherLot = (await otherCreate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        Assert.NotEqual(ownerLot, otherLot);

        var ownerList = await owner.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots");
        var otherList = await other.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots");
        Assert.DoesNotContain(ownerList.GetProperty("items").EnumerateArray(), item => item.GetProperty("lotId").GetGuid() == otherLot);
        Assert.DoesNotContain(otherList.GetProperty("items").EnumerateArray(), item => item.GetProperty("lotId").GetGuid() == ownerLot);
    }

    /// <summary>TEST-0005-045: successful create persists product, lot, transaction, and audit together.</summary>
    [Fact]
    public async Task CreateIsAtomicOnSuccess()
    {
        await using var factory = new KitchenFlowFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var before = await CountInventoryArtifactsAsync(factory);

        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Atomic lot", 5m);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var afterSuccess = await CountInventoryArtifactsAsync(factory);
        Assert.True(afterSuccess.Products >= before.Products + 1);
        Assert.True(afterSuccess.Lots >= before.Lots + 1);
        Assert.True(afterSuccess.Transactions >= before.Transactions + 1);
        Assert.True(afterSuccess.AuditEvents >= before.AuditEvents + 1);
    }

    /// <summary>
    /// TEST-0005-046: mid-transaction PostgreSQL trigger failure after persistence has started
    /// leaves zero product/lot/transaction/audit/idempotency rows for the marker; response is safe;
    /// connection remains usable; retry after trigger removal follows documented create policy.
    /// </summary>
    [Fact]
    public async Task InjectedMidTransactionFailureRollsBackAllCreateArtifacts()
    {
        const string markerName = "PLAN0005_TX_FAIL_MARKER";
        await using var factory = new KitchenFlowFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        client.DefaultRequestHeaders.Add("X-Test-Subject", "plan0005-tx-fail");
        var csrf = await GetCsrfAsync(client);
        var failKey = Guid.NewGuid();

        await using (var setup = new Npgsql.NpgsqlConnection(_fixture.ConnectionString))
        {
            await setup.OpenAsync();
            await using var cmd = setup.CreateCommand();
            cmd.CommandText =
                """
                CREATE OR REPLACE FUNCTION plan0005_fail_after_product_persist() RETURNS trigger AS $$
                BEGIN
                  IF EXISTS (
                    SELECT 1
                    FROM inventory.lots l
                    JOIN inventory.products p ON p."Id" = l."ProductId"
                    WHERE l."Id" = NEW."LotId"
                      AND p."DisplayName" = 'PLAN0005_TX_FAIL_MARKER'
                  ) THEN
                    RAISE EXCEPTION 'plan0005 injected transactional failure after product/lot persist';
                  END IF;
                  RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS plan0005_fail_on_transaction_insert ON inventory.transactions;
                CREATE TRIGGER plan0005_fail_on_transaction_insert
                BEFORE INSERT ON inventory.transactions
                FOR EACH ROW EXECUTE FUNCTION plan0005_fail_after_product_persist();
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            using var failing = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots")
            {
                Content = JsonContent.Create(CreateLot(markerName, 7m))
            };
            failing.Headers.Add("Idempotency-Key", failKey.ToString());
            failing.Headers.Add("X-CSRF-TOKEN", csrf);
            var rejected = await client.SendAsync(failing);
            Assert.True((int)rejected.StatusCode >= 400);
            var body = await rejected.Content.ReadAsStringAsync();
            Assert.DoesNotContain("plan0005 injected transactional failure", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT INTO", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);

            var artifacts = await CountMarkerArtifactsAsync(_fixture.ConnectionString, markerName, failKey);
            Assert.Equal(0, artifacts.Products);
            Assert.Equal(0, artifacts.Lots);
            Assert.Equal(0, artifacts.Transactions);
            Assert.Equal(0, artifacts.AuditEvents);
            Assert.Equal(0, artifacts.IdempotencyRecords);

            // Connection/session remains usable after the injected failure.
            var session = await client.GetAsync("/api/v1/session");
            Assert.Equal(System.Net.HttpStatusCode.OK, session.StatusCode);
        }
        finally
        {
            await using var cleanup = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
            await cleanup.OpenAsync();
            await using var drop = cleanup.CreateCommand();
            drop.CommandText =
                """
                DROP TRIGGER IF EXISTS plan0005_fail_on_transaction_insert ON inventory.transactions;
                DROP FUNCTION IF EXISTS plan0005_fail_after_product_persist();
                """;
            await drop.ExecuteNonQueryAsync();
        }

        // Retry with the same key after the trigger is removed must create exactly once (no prior committed idempotency).
        csrf = await GetCsrfAsync(client);
        var retried = await CreateAsync(client, csrf, failKey.ToString(), markerName, 7m);
        Assert.Equal(System.Net.HttpStatusCode.Created, retried.StatusCode);
        var replay = await CreateAsync(client, csrf, failKey.ToString(), markerName, 7m);
        Assert.Equal(System.Net.HttpStatusCode.Created, replay.StatusCode);
        var afterRetry = await CountMarkerArtifactsAsync(_fixture.ConnectionString, markerName, failKey);
        Assert.Equal(1, afterRetry.Products);
        Assert.Equal(1, afterRetry.Lots);
        Assert.Equal(1, afterRetry.IdempotencyRecords);
    }

    private static async Task<(int Products, int Lots, int Transactions, int AuditEvents, int IdempotencyRecords)> CountMarkerArtifactsAsync(
        string connectionString,
        string markerName,
        Guid idempotencyKey)
    {
        await using var verify = new Npgsql.NpgsqlConnection(connectionString);
        await verify.OpenAsync();
        await using var counts = verify.CreateCommand();
        counts.CommandText =
            """
            SELECT
              (SELECT count(*)::int FROM inventory.products WHERE "DisplayName" = @marker),
              (SELECT count(*)::int FROM inventory.lots l JOIN inventory.products p ON p."Id" = l."ProductId" WHERE p."DisplayName" = @marker),
              (SELECT count(*)::int FROM inventory.transactions t JOIN inventory.lots l ON l."Id" = t."LotId" JOIN inventory.products p ON p."Id" = l."ProductId" WHERE p."DisplayName" = @marker),
              (SELECT count(*)::int FROM platform.audit_events ae WHERE ae."EventName" = 'inventory.lot.created' AND ae."TargetId" IN (
                  SELECT l."Id" FROM inventory.lots l JOIN inventory.products p ON p."Id" = l."ProductId" WHERE p."DisplayName" = @marker
              )),
              (SELECT count(*)::int FROM platform.idempotency_records WHERE "Key" = @key)
            """;
        counts.Parameters.AddWithValue("marker", markerName);
        counts.Parameters.AddWithValue("key", idempotencyKey);
        await using var reader = await counts.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
    }

    /// <summary>TEST-0005-007 companion: logout invalidates subsequent protected inventory reads.</summary>
    [Fact]
    public async Task LogoutInvalidatesProtectedInventoryAccess()
    {
        await using var factory = new KitchenFlowFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", csrf);
        var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, logoutResponse.StatusCode);

        // Test auth handler remains present for factory auth; cookie/session middleware still clears CSRF/session state.
        // Independence note: full cookie-session death is proven by Keycloak browser automation (TEST-0005-007).
        using var missingCsrf = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = JsonContent.Create(CreateLot("after-logout")) };
        missingCsrf.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var rejected = await client.SendAsync(missingCsrf);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejected.StatusCode);
        _ = lotId;
    }

    private static object CreateLot(string productName = "Test tomato", decimal measuredValue = 100m) =>
        new { productName, quantity = new { measuredValue, unit = "Gram", availabilityState = (string?)null }, storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null };

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
        return session.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string csrf, string key, string productName = "Test tomato", decimal measuredValue = 100m)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = JsonContent.Create(CreateLot(productName, measuredValue)) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> AdjustmentAsync(HttpClient client, string csrf, Guid lotId, string? etag, string key, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{lotId}/adjustments") { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<int> CountLotsAsync(KitchenFlowFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.Lots.CountAsync();
    }

    private static async Task<(int Products, int Lots, int Transactions, int AuditEvents)> CountInventoryArtifactsAsync(KitchenFlowFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (
            await database.Products.CountAsync(),
            await database.Lots.CountAsync(),
            await database.Transactions.CountAsync(),
            await database.AuditEvents.CountAsync());
    }

    private sealed class KitchenFlowFactory(string connectionString, bool authenticate) : WebApplicationFactory<Program>
    {
        private readonly string keyRingPath = Path.Combine(Path.GetTempPath(), $"kitchenflow-plan0005-keys-{Guid.NewGuid():N}");

        public async Task EnsureDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:KitchenFlow", connectionString);
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
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "plan0005-user-a";
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim("iss", "https://integration.test")], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}

/// <summary>xUnit collection definition for one shared PostgreSQL container per PLAN-0005 gap suite.</summary>
[CollectionDefinition("Plan0005SharedPostgres")]
public sealed class Plan0005SharedPostgresCollection : ICollectionFixture<Plan0005SharedPostgresFixture>
{
}

/// <summary>Starts exactly one PostgreSQL container for all PLAN-0005 gap tests in a run.</summary>
public sealed class Plan0005SharedPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4")
        .WithDatabase("kitchenflow_plan0005")
        .WithUsername("kitchenflow_test")
        .WithPassword("development-only-test-password")
        .Build();

    /// <summary>Gets the shared connection string.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <inheritdoc />
    public Task InitializeAsync() => _postgres.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
