using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Identity;
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
using Npgsql;
using Testcontainers.PostgreSql;

namespace Cocinaris.IntegrationTests;

/// <summary>
/// PLAN-0027 independent remediation retest for F-0026-01/02/03 and focused non-regression.
/// These tests do not mutate production code; they only assert candidate behavior.
/// </summary>
[Collection("Plan0027SharedPostgres")]
public sealed class Plan0027IndependentPreparedComponentRemediationRetestTests
{
    private readonly Plan0027SharedPostgresFixture _fixture;

    /// <summary>Creates PLAN-0027 independent tests bound to one shared PostgreSQL fixture.</summary>
    public Plan0027IndependentPreparedComponentRemediationRetestTests(Plan0027SharedPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task F002601_ConcurrentSameKeyPreparationReplaysWithoutFalse412AcrossFiftyIterations()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        var false412 = 0;
        var batchIds = new List<Guid>();

        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var client = factory.CreateClient(ClientOptions());
            client.DefaultRequestHeaders.Add("X-Test-Subject", $"plan0027-f01-{iteration}-{Guid.NewGuid():N}");
            var csrf = await GetCsrfAsync(client);
            var parent = await CreateLotAsync(client, csrf, $"F01 parent {iteration}", 20m);
            var key = Guid.NewGuid().ToString();
            var body = MeasuredPreparationBody(parent.LotId, parent.Version, 5m, 5m, DateTimeOffset.UtcNow.AddMinutes(-1));
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<HttpResponseMessage> SendAsync()
            {
                await gate.Task;
                return await SendPreparationAsync(client, csrf, key, body);
            }

            var firstTask = SendAsync();
            var secondTask = SendAsync();
            gate.SetResult();
            var responses = await Task.WhenAll(firstTask, secondTask);
            foreach (var response in responses)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    false412++;
                }
            }

            Assert.All(responses, response => Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode));
            var bodies = await Task.WhenAll(responses.Select(async response => await response.Content.ReadFromJsonAsync<JsonElement>()));
            Assert.Single(bodies.Select(item => item.GetProperty("batchId").GetGuid()).Distinct());
            batchIds.Add(bodies[0].GetProperty("batchId").GetGuid());
            Assert.Equal(15m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        }

        Assert.Equal(0, false412);
        Assert.Equal(50, batchIds.Distinct().Count());
    }

    [Fact]
    public async Task F002601_SameKeyDifferentSemanticCommandConflictsWithoutSecondConsumption()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(ClientOptions());
        client.DefaultRequestHeaders.Add("X-Test-Subject", $"plan0027-reuse-{Guid.NewGuid():N}");
        var csrf = await GetCsrfAsync(client);
        var parent = await CreateLotAsync(client, csrf, "Reuse parent", 30m);
        var key = Guid.NewGuid().ToString();
        var firstBody = MeasuredPreparationBody(parent.LotId, parent.Version, 4m, 4m, DateTimeOffset.UtcNow.AddMinutes(-1));
        using var first = await SendPreparationAsync(client, csrf, key, firstBody);
        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);

        var mismatched = MeasuredPreparationBody(parent.LotId, parent.Version, 5m, 5m, DateTimeOffset.UtcNow.AddMinutes(-1));
        using var conflict = await SendPreparationAsync(client, csrf, key, mismatched);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency_key_reused", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Equal(26m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
    }

    [Fact]
    public async Task F002601_DifferentKeysSameVersionsYieldExactlyOneWinner()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(ClientOptions());
        client.DefaultRequestHeaders.Add("X-Test-Subject", $"plan0027-diffkeys-{Guid.NewGuid():N}");
        var csrf = await GetCsrfAsync(client);
        var parent = await CreateLotAsync(client, csrf, "Diff key parent", 20m);
        var body = MeasuredPreparationBody(parent.LotId, parent.Version, 5m, 5m, DateTimeOffset.UtcNow.AddMinutes(-1));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> SendAsync(string key)
        {
            await gate.Task;
            return await SendPreparationAsync(client, csrf, key, body);
        }

        var firstTask = SendAsync(Guid.NewGuid().ToString());
        var secondTask = SendAsync(Guid.NewGuid().ToString());
        gate.SetResult();
        var responses = await Task.WhenAll(firstTask, secondTask);
        Assert.Single(responses, response => response.StatusCode == System.Net.HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed);
        Assert.Equal(15m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
    }

    [Fact]
    public async Task F002602_DeclaredYieldConstraintRejectsInvalidAndAcceptsValidModes()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var definition = await database.Database.SqlQueryRaw<string>("""
            SELECT pg_get_constraintdef(oid) AS "Value"
            FROM pg_constraint
            WHERE conname = 'ck_preparation_batches_declared_yield'
            """).SingleAsync();
        Assert.Contains("IS TRUE", definition, StringComparison.OrdinalIgnoreCase);

        var ownerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        database.Users.Add(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"subject-{ownerId}", now));
        database.Products.Add(new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Yield product", NormalizedSearchName = "YIELD PRODUCT", CreatedAt = now, UpdatedAt = now, IsDeleted = false });
        await database.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var invalid in new (decimal? Value, string? Unit, string? Availability)[]
                 {
                     (null, null, null), (1m, null, null), (null, "Gram", null), (1m, "Gram", "Available"),
                     (0m, "Gram", null), (-1m, "Gram", null), (1m, "Cup", null), (null, null, "Maybe")
                 })
        {
            await AssertConstraintRejectedAsync(connection, ownerId, productId, invalid.Value, invalid.Unit, invalid.Availability);
        }

        foreach (var valid in new (decimal? Value, string? Unit, string? Availability)[]
                 {
                     (1m, "Gram", null), (2m, "Milliliter", null), (3m, "Unit", null),
                     (null, null, "Available"), (null, null, "Low"), (null, null, "Unavailable")
                 })
        {
            await AssertConstraintAcceptedAsync(connection, ownerId, productId, valid.Value, valid.Unit, valid.Availability);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(49, false)]
    [InlineData(50, false)]
    [InlineData(51, true)]
    [InlineData(55, true)]
    public async Task F002603_ConsumedByProvenanceReportsIndependentTruncationFlags(int relatedBatchCount, bool expectedTruncated)
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(ClientOptions());
        client.DefaultRequestHeaders.Add("X-Test-Subject", $"plan0027-prov-{relatedBatchCount}-{Guid.NewGuid():N}");
        var csrf = await GetCsrfAsync(client);
        var parent = await CreateLotAsync(client, csrf, $"Prov parent {relatedBatchCount}", Math.Max(relatedBatchCount + 5, 10));
        var currentVersion = parent.Version;
        var preparedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        Guid? firstOutputLotId = null;
        for (var index = 0; index < relatedBatchCount; index++)
        {
            var body = MeasuredPreparationBody(parent.LotId, currentVersion, 1m, 1m, preparedAt.AddSeconds(index % 3 == 0 ? 0 : index));
            using var response = await SendPreparationAsync(client, csrf, Guid.NewGuid().ToString(), body);
            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            firstOutputLotId ??= created.GetProperty("outputs")[0].GetProperty("lot").GetProperty("lotId").GetGuid();
            currentVersion = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}")).GetProperty("version").GetString()!;
        }

        var provenance = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}/provenance");
        Assert.Equal(Math.Min(relatedBatchCount, 50), provenance.GetProperty("consumedBy").GetArrayLength());
        Assert.Equal(expectedTruncated, provenance.GetProperty("consumedByTruncated").GetBoolean());
        Assert.Equal(0, provenance.GetProperty("producedBy").GetArrayLength());
        Assert.False(provenance.GetProperty("producedByTruncated").GetBoolean());
        var ids = provenance.GetProperty("consumedBy").EnumerateArray().Select(item => item.GetProperty("batchId").GetGuid()).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
        var repeated = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}/provenance");
        Assert.Equal(ids, repeated.GetProperty("consumedBy").EnumerateArray().Select(item => item.GetProperty("batchId").GetGuid()).ToList());

        if (relatedBatchCount > 0 && firstOutputLotId is Guid outputLotId)
        {
            var outputProvenance = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{outputLotId}/provenance");
            Assert.Equal(1, outputProvenance.GetProperty("producedBy").GetArrayLength());
            Assert.False(outputProvenance.GetProperty("producedByTruncated").GetBoolean());
            Assert.False(outputProvenance.GetProperty("consumedByTruncated").GetBoolean());
        }
    }

    [Fact]
    public async Task F002603_ProducedByTruncationIsIndependentWhenConsumedByIsTruncated()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(ClientOptions());
        client.DefaultRequestHeaders.Add("X-Test-Subject", $"plan0027-produced-{Guid.NewGuid():N}");
        var csrf = await GetCsrfAsync(client);
        var parent = await CreateLotAsync(client, csrf, "Produced independence parent", 80m);
        var currentVersion = parent.Version;
        Guid? outputLotId = null;
        for (var index = 0; index < 55; index++)
        {
            using var response = await SendPreparationAsync(client, csrf, Guid.NewGuid().ToString(), MeasuredPreparationBody(parent.LotId, currentVersion, 1m, 1m, DateTimeOffset.UtcNow.AddMinutes(-20).AddSeconds(index)));
            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            outputLotId ??= body.GetProperty("outputs")[0].GetProperty("lot").GetProperty("lotId").GetGuid();
            currentVersion = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}")).GetProperty("version").GetString()!;
        }

        var parentProvenance = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}/provenance");
        Assert.True(parentProvenance.GetProperty("consumedByTruncated").GetBoolean());
        Assert.False(parentProvenance.GetProperty("producedByTruncated").GetBoolean());

        var outputProvenance = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{outputLotId}/provenance");
        Assert.False(outputProvenance.GetProperty("consumedByTruncated").GetBoolean());
        Assert.False(outputProvenance.GetProperty("producedByTruncated").GetBoolean());
        Assert.Equal(1, outputProvenance.GetProperty("producedBy").GetArrayLength());
    }

    [Fact]
    public async Task NonRegression_ImmutableMeasuredYieldAfterOutputMutation()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(ClientOptions());
        client.DefaultRequestHeaders.Add("X-Test-Subject", $"plan0027-yield-{Guid.NewGuid():N}");
        var csrf = await GetCsrfAsync(client);
        var parent = await CreateLotAsync(client, csrf, "Immutable yield parent", 100m);
        var key = Guid.NewGuid().ToString();
        var body = MeasuredPreparationBody(parent.LotId, parent.Version, 60m, 60m, DateTimeOffset.UtcNow.AddMinutes(-2));
        using var create = await SendPreparationAsync(client, csrf, key, body);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = created.GetProperty("batchId").GetGuid();
        var outputLotId = created.GetProperty("outputs")[0].GetProperty("lot").GetProperty("lotId").GetGuid();
        var outputVersion = created.GetProperty("outputs")[0].GetProperty("lot").GetProperty("version").GetString()!;
        using var consume = await AdjustAsync(client, csrf, outputLotId, Quote(outputVersion), Guid.NewGuid().ToString(), 20m);
        Assert.Equal(System.Net.HttpStatusCode.OK, consume.StatusCode);
        using var replay = await SendPreparationAsync(client, csrf, key, body);
        var batch = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/preparations/{batchId}");
        var provenance = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parent.LotId}/provenance");
        Assert.Equal(60m, created.GetProperty("declaredYield").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(60m, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("declaredYield").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(60m, batch.GetProperty("declaredYield").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(60m, provenance.GetProperty("consumedBy")[0].GetProperty("declaredYield").GetProperty("measuredValue").GetDecimal());
    }

    [Fact]
    public async Task NonRegression_ForeignPreparationRemainsNondisclosing()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var owner = factory.CreateClient(ClientOptions());
        using var stranger = factory.CreateClient(ClientOptions());
        stranger.DefaultRequestHeaders.Add("X-Test-Subject", "plan0027-user-b");
        var ownerCsrf = await GetCsrfAsync(owner);
        var strangerCsrf = await GetCsrfAsync(stranger);
        var parent = await CreateLotAsync(owner, ownerCsrf, "Private parent", 20m);
        using var created = await SendPreparationAsync(owner, ownerCsrf, Guid.NewGuid().ToString(), MeasuredPreparationBody(parent.LotId, parent.Version, 5m, 5m, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = body.GetProperty("batchId").GetGuid();
        using var foreignBatch = await stranger.GetAsync($"/api/v1/inventory/preparations/{batchId}");
        using var foreignProvenance = await stranger.GetAsync($"/api/v1/inventory/lots/{parent.LotId}/provenance");
        using var foreignPrepare = await SendPreparationAsync(stranger, strangerCsrf, Guid.NewGuid().ToString(), MeasuredPreparationBody(parent.LotId, parent.Version, 1m, 1m, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, foreignBatch.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, foreignProvenance.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, foreignPrepare.StatusCode);
    }

    [Fact]
    public async Task NonRegression_AdjustmentSameKeyReplayFiftyIterations()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString, authenticate: true);
        await factory.EnsureDatabaseAsync();
        for (var iteration = 0; iteration < 50; iteration++)
        {
            using var client = factory.CreateClient(ClientOptions());
            client.DefaultRequestHeaders.Add("X-Test-Subject", $"plan0027-adj-{iteration}-{Guid.NewGuid():N}");
            var csrf = await GetCsrfAsync(client);
            var created = await CreateLotAsync(client, csrf, $"Adjust {iteration}", 100m);
            var key = Guid.NewGuid().ToString();
            var payload = new { type = "Consume", value = 3m, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null };
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<HttpResponseMessage> SendAsync()
            {
                await gate.Task;
                return await AdjustmentAsync(client, csrf, created.LotId, Quote(created.Version), key, payload);
            }

            var firstTask = SendAsync();
            var secondTask = SendAsync();
            gate.SetResult();
            var responses = await Task.WhenAll(firstTask, secondTask);
            Assert.All(responses, response => Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(97m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{created.LotId}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
            var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{created.LotId}/history");
            Assert.Equal(1, history.EnumerateArray().Count(entry => entry.GetProperty("type").GetString() == "Consume"));
        }
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true };

    private static object MeasuredPreparationBody(Guid lotId, string version, decimal yield, decimal output, DateTimeOffset preparedAt) => new
    {
        outputProduct = new { productId = (Guid?)null, productName = "Prepared component" },
        declaredYield = new { measuredValue = yield, unit = "Gram", availabilityState = (string?)null },
        inputs = new[] { new { lotId, quantity = new { measuredValue = yield, unit = "Gram", availabilityState = (string?)null }, version } },
        outputs = new[] { new { quantity = new { measuredValue = output, unit = "Gram", availabilityState = (string?)null }, storageLocation = "Refrigerator", customLocation = (string?)null, packageState = "Opened", shelfLifeEvidence = new { date = (DateOnly?)null, source = "Unknown", confidence = "Unknown", conditions = (string?)null } } },
        preparedAt
    };

    private static string Quote(string version) => version.StartsWith('"') ? version : $"\"{version}\"";

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
        return session.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<(Guid LotId, string Version)> CreateLotAsync(HttpClient client, string csrf, string productName, decimal measuredValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots")
        {
            Content = JsonContent.Create(new { productName, quantity = new { measuredValue, unit = "Gram", availabilityState = (string?)null }, storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("lotId").GetGuid(), body.GetProperty("version").GetString()!);
    }

    private static async Task<HttpResponseMessage> SendPreparationAsync(HttpClient client, string csrf, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/preparations") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> AdjustAsync(HttpClient client, string csrf, Guid lotId, string etag, string key, decimal value) =>
        await AdjustmentAsync(client, csrf, lotId, etag, key, new { type = "Consume", value, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null });

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

    private static async Task AssertConstraintRejectedAsync(NpgsqlConnection connection, Guid ownerId, Guid productId, decimal? value, string? unit, string? availability)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO inventory.preparation_batches ("Id", "OwnerUserId", "OutputProductId", "DeclaredYieldMeasuredValue", "DeclaredYieldMeasuredUnit", "DeclaredYieldAvailabilityState", "SourceType", "PreparedAt", "CreatedAt")
            VALUES (@id, @owner, @product, @value, @unit, @availability, 'ManualPreparation', NOW(), NOW())
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("owner", ownerId);
        command.Parameters.AddWithValue("product", productId);
        command.Parameters.AddWithValue("value", (object?)value ?? DBNull.Value);
        command.Parameters.AddWithValue("unit", (object?)unit ?? DBNull.Value);
        command.Parameters.AddWithValue("availability", (object?)availability ?? DBNull.Value);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private static async Task AssertConstraintAcceptedAsync(NpgsqlConnection connection, Guid ownerId, Guid productId, decimal? value, string? unit, string? availability)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO inventory.preparation_batches ("Id", "OwnerUserId", "OutputProductId", "DeclaredYieldMeasuredValue", "DeclaredYieldMeasuredUnit", "DeclaredYieldAvailabilityState", "SourceType", "PreparedAt", "CreatedAt")
            VALUES (@id, @owner, @product, @value, @unit, @availability, 'ManualPreparation', NOW(), NOW())
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("owner", ownerId);
        command.Parameters.AddWithValue("product", productId);
        command.Parameters.AddWithValue("value", (object?)value ?? DBNull.Value);
        command.Parameters.AddWithValue("unit", (object?)unit ?? DBNull.Value);
        command.Parameters.AddWithValue("availability", (object?)availability ?? DBNull.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed class CocinarisFactory(string connectionString, bool authenticate) : WebApplicationFactory<Program>
    {
        private readonly string keyRingPath = Path.Combine(Path.GetTempPath(), $"cocinaris-plan0027-keys-{Guid.NewGuid():N}");

        public async Task EnsureDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Cocinaris", connectionString);
            builder.UseSetting("COCINARIS_SESSION_KEYRING_PATH", keyRingPath);
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
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "plan0027-user-a";
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, subject), new Claim("sub", subject), new Claim("iss", "https://integration.test")],
                TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}

/// <summary>xUnit collection definition for one shared PostgreSQL container per PLAN-0027 suite.</summary>
[CollectionDefinition("Plan0027SharedPostgres")]
public sealed class Plan0027SharedPostgresCollection : ICollectionFixture<Plan0027SharedPostgresFixture>
{
}

/// <summary>Starts exactly one PostgreSQL container for all PLAN-0027 independent tests in a run.</summary>
public sealed class Plan0027SharedPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(TestImages.PostgreSql)
        .WithDatabase("cocinaris_plan0027")
        .WithUsername("cocinaris_test")
        .WithPassword("development-only-test-password")
        .Build();

    /// <summary>Gets the shared connection string.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <inheritdoc />
    public Task InitializeAsync() => _postgres.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
