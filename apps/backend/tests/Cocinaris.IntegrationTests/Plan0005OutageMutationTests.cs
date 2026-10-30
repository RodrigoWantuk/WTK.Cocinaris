using System.Diagnostics;
using System.Net;
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
/// PLAN-0005 TEST-0005-132: authenticated read/mutation during PostgreSQL outage must fail safely without corruption.
/// </summary>
[Collection("Plan0005OutagePostgres")]
public sealed class Plan0005OutageMutationTests
{
    private readonly Plan0005OutagePostgresFixture _fixture;

    /// <summary>Creates outage mutation tests bound to a dedicated PostgreSQL container.</summary>
    public Plan0005OutageMutationTests(Plan0005OutagePostgresFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Authenticates, creates a known lot, pauses PostgreSQL, asserts safe failures for read/mutation,
    /// restores PostgreSQL, proves integrity, and retries the mutation at most once.
    /// </summary>
    [Fact]
    public async Task AuthenticatedMutationDuringOutageFailsSafelyWithoutCorruption()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("X-Test-Subject", "plan0005-outage");
        var csrf = await GetCsrfAsync(client);
        var createKey = Guid.NewGuid().ToString();
        var created = await CreateAsync(client, csrf, createKey, "outage-known-lot", 12m);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var lotId = createdBody.GetProperty("lotId").GetGuid();
        var etag = created.Headers.ETag?.Tag ?? createdBody.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(etag));

        var mutationKey = Guid.NewGuid();
        var baseline = await SnapshotLotAsync(factory, lotId, mutationKey);

        OutageAttemptResult readDuring;
        OutageAttemptResult mutateDuring;
        await _fixture.StopPostgresAsync();
        try
        {
            readDuring = await AssertSafeOutageFailureAsync(() => client.GetAsync($"/api/v1/inventory/lots/{lotId}"));

            using var mutate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{lotId}/adjustments")
            {
                Content = JsonContent.Create(new
                {
                    type = "Consume",
                    value = 1m,
                    availabilityState = (string?)null,
                    reasonCode = "plan0005-outage",
                    note = (string?)null
                })
            };
            mutate.Headers.Add("X-CSRF-TOKEN", csrf);
            mutate.Headers.TryAddWithoutValidation("If-Match", etag);
            mutate.Headers.Add("Idempotency-Key", mutationKey.ToString());
            mutateDuring = await AssertSafeOutageFailureAsync(() => client.SendAsync(mutate));
            mutateDuring = mutateDuring with { IdempotencyKeyMarker = "mutation-key-present" };
        }
        finally
        {
            await _fixture.StartPostgresAsync();
            await WaitForPostgresAsync(_fixture.ConnectionString, TimeSpan.FromSeconds(60));
            await factory.EnsureDatabaseAsync();
        }

        var afterRestore = await SnapshotLotAsync(factory, lotId, mutationKey);
        Assert.Equal(baseline.Quantity, afterRestore.Quantity);
        Assert.Equal(baseline.Version, afterRestore.Version);
        Assert.Equal(baseline.TransactionCount, afterRestore.TransactionCount);
        Assert.Equal(baseline.AuditCount, afterRestore.AuditCount);
        Assert.Equal(0, afterRestore.IdempotencyCount);

        csrf = await GetCsrfAsync(client);
        var detail = await client.GetAsync($"/api/v1/inventory/lots/{lotId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var freshEtag = detail.Headers.ETag?.Tag;
        using var retry = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{lotId}/adjustments")
        {
            Content = JsonContent.Create(new
            {
                type = "Consume",
                value = 1m,
                availabilityState = (string?)null,
                reasonCode = "plan0005-outage-retry",
                note = (string?)null
            })
        };
        retry.Headers.Add("X-CSRF-TOKEN", csrf);
        retry.Headers.TryAddWithoutValidation("If-Match", freshEtag);
        retry.Headers.Add("Idempotency-Key", mutationKey.ToString());
        var retryResponse = await client.SendAsync(retry);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var afterFirstRetry = await SnapshotLotAsync(factory, lotId, mutationKey);
        Assert.Equal(baseline.Quantity - 1m, afterFirstRetry.Quantity);
        Assert.Equal(baseline.TransactionCount + 1, afterFirstRetry.TransactionCount);
        Assert.Equal(1, afterFirstRetry.IdempotencyCount);

        var replayEtag = retryResponse.Headers.ETag?.Tag ?? freshEtag!;
        var replay = await client.SendAsync(CloneAdjustment(csrf, lotId, replayEtag, mutationKey));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var afterRetry = await SnapshotLotAsync(factory, lotId, mutationKey);
        Assert.Equal(afterFirstRetry.Quantity, afterRetry.Quantity);
        Assert.Equal(afterFirstRetry.TransactionCount, afterRetry.TransactionCount);
        Assert.Equal(1, afterRetry.IdempotencyCount);

        var evidenceDir = ResolveEvidenceDir();
        Directory.CreateDirectory(evidenceDir);
        var payload = new
        {
            plan = "PLAN-0005",
            testId = "TEST-0005-132",
            status = "Passed",
            integratedMainSha = Environment.GetEnvironmentVariable("PLAN0005_INTEGRATED_SHA") ?? "b94abd9a83fe29d88b095e3e9a42f10d01c05414",
            prHeadSha = Environment.GetEnvironmentVariable("PLAN0005_PR_HEAD_SHA"),
            checkedOutCommitSha = Environment.GetEnvironmentVariable("PLAN0005_CHECKED_OUT_SHA") ?? TryGitHead(),
            evidenceGenerationSha = Environment.GetEnvironmentVariable("PLAN0005_EVIDENCE_GENERATION_SHA")
                ?? Environment.GetEnvironmentVariable("PLAN0005_PR_HEAD_SHA")
                ?? TryGitHead(),
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            baseline = ToJsonSnapshot(baseline),
            duringOutage = new
            {
                read = new
                {
                    resultType = readDuring.ResultType,
                    statusCode = readDuring.StatusCode,
                    safeBodyValidated = readDuring.SafeBodyValidated
                },
                mutation = new
                {
                    resultType = mutateDuring.ResultType,
                    statusCode = mutateDuring.StatusCode,
                    safeBodyValidated = mutateDuring.SafeBodyValidated,
                    idempotencyKey = mutateDuring.IdempotencyKeyMarker
                }
            },
            afterRestore = ToJsonSnapshot(afterRestore),
            afterRetry = new
            {
                quantity = afterRetry.Quantity,
                version = afterRetry.Version,
                transactionCount = afterRetry.TransactionCount,
                auditCount = afterRetry.AuditCount,
                idempotencyCount = afterRetry.IdempotencyCount,
                replayProducedAdditionalEffect = false
            },
            note = "Authenticated read and mutation during PostgreSQL pause returned safe errors; lot intact after restore; idempotency absent until successful retry; replay produced no additional effect."
        };
        await File.WriteAllTextAsync(
            Path.Combine(evidenceDir, "outage-mutation-recovery.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static object ToJsonSnapshot(OutageStateSnapshot snap) => new
    {
        quantity = snap.Quantity,
        version = snap.Version,
        transactionCount = snap.TransactionCount,
        auditCount = snap.AuditCount,
        idempotencyCount = snap.IdempotencyCount
    };

    private static string ResolveEvidenceDir()
    {
        var configured = Environment.GetEnvironmentVariable("PLAN0005_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "docs", "evidence", "plan-0005"));
    }

    private static HttpRequestMessage CloneAdjustment(string csrf, Guid lotId, string etag, Guid key)
    {
        var clone = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{lotId}/adjustments")
        {
            Content = JsonContent.Create(new
            {
                type = "Consume",
                value = 1m,
                availabilityState = (string?)null,
                reasonCode = "plan0005-outage-retry",
                note = (string?)null
            })
        };
        clone.Headers.Add("X-CSRF-TOKEN", csrf);
        clone.Headers.TryAddWithoutValidation("If-Match", etag);
        clone.Headers.Add("Idempotency-Key", key.ToString());
        return clone;
    }

    private static async Task<OutageAttemptResult> AssertSafeOutageFailureAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            using var response = await send();
            Assert.True((int)response.StatusCode >= 400, $"Expected failure status, got {(int)response.StatusCode}");
            var body = await response.Content.ReadAsStringAsync();
            AssertSafeErrorBody(body);
            return new OutageAttemptResult("http_error", (int)response.StatusCode, true, null);
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            Assert.DoesNotContain("Bearer ", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", ex.Message, StringComparison.OrdinalIgnoreCase);
            return new OutageAttemptResult("timeout", null, true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            Assert.DoesNotContain("Bearer ", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", ex.Message, StringComparison.OrdinalIgnoreCase);
            return new OutageAttemptResult("connection_error", null, true, null);
        }
    }

    private static void AssertSafeErrorBody(string body)
    {
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at Cocinaris", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer ", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<OutageStateSnapshot> SnapshotLotAsync(CocinarisFactory factory, Guid lotId, Guid mutationKey)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lot = await db.Lots.AsNoTracking().SingleAsync(l => l.Id == lotId);
        var tx = await db.Transactions.AsNoTracking().CountAsync(t => t.LotId == lotId);
        var audit = await db.AuditEvents.AsNoTracking().CountAsync(a => a.TargetId == lotId);
        var idem = await db.IdempotencyRecords.AsNoTracking().CountAsync(r => r.Key == mutationKey);
        return new OutageStateSnapshot(lot.MeasuredValue ?? 0m, lot.Version, tx, audit, idem);
    }

    private static async Task WaitForPostgresAsync(string connectionString, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var conn = new Npgsql.NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(1000);
            }
        }

        throw new InvalidOperationException($"PostgreSQL did not become reachable within {timeout}.", last);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
        return session.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string csrf, string key, string productName, decimal measuredValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots")
        {
            Content = JsonContent.Create(new
            {
                productName,
                quantity = new { measuredValue, unit = "Gram", availabilityState = (string?)null },
                storageLocation = "Pantry",
                customLocation = (string?)null,
                packageState = (string?)null,
                printedExpirationDate = (DateOnly?)null,
                notes = (string?)null
            })
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static string? TryGitHead()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            using var p = Process.Start(psi);
            return p?.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Named lot/history snapshot used for JSON evidence (not a ValueTuple).</summary>
    private sealed record OutageStateSnapshot(
        decimal Quantity,
        long Version,
        int TransactionCount,
        int AuditCount,
        int IdempotencyCount);

    /// <summary>Sanitized outage attempt outcome for evidence.</summary>
    private sealed record OutageAttemptResult(
        string ResultType,
        int? StatusCode,
        bool SafeBodyValidated,
        string? IdempotencyKeyMarker);

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
                services.PostConfigure<OpenIdConnectOptions>("oidc", options =>
                {
                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        AuthorizationEndpoint = "https://identity.integration.test/authorize",
                        Issuer = "https://identity.integration.test"
                    };
                    options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
                });
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
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "plan0005-outage";
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim("iss", "https://integration.test")], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}

/// <summary>Collection for a dedicated outage PostgreSQL container.</summary>
[CollectionDefinition("Plan0005OutagePostgres")]
public sealed class Plan0005OutagePostgresCollection : ICollectionFixture<Plan0005OutagePostgresFixture>
{
}

/// <summary>PostgreSQL container that can be paused and unpaused for outage injection.</summary>
public sealed class Plan0005OutagePostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(TestImages.PostgreSql)
        .WithDatabase("cocinaris_plan0005_outage")
        .WithUsername("cocinaris_test")
        .WithPassword("development-only-test-password")
        .Build();

    /// <summary>Gets the connection string for the outage fixture container.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <inheritdoc />
    public Task InitializeAsync() => _postgres.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    /// <summary>Pauses the PostgreSQL container to simulate an outage without changing the mapped port.</summary>
    public Task StopPostgresAsync() => _postgres.PauseAsync();

    /// <summary>Unpauses the PostgreSQL container after an outage.</summary>
    public Task StartPostgresAsync() => _postgres.UnpauseAsync();
}
