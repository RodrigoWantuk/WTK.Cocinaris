using System.Collections.Concurrent;
using System.Diagnostics;
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

namespace Cocinaris.IntegrationTests;

/// <summary>
/// PLAN-0005 performance smoke: concurrent authenticated lists (135/136) and concurrent creates (137).
/// </summary>
[Collection("Plan0005SharedPostgres")]
public sealed class Plan0005PerformanceSmokeTests
{
    private readonly Plan0005SharedPostgresFixture _fixture;

    /// <summary>Creates performance smoke tests bound to the shared PLAN-0005 PostgreSQL fixture.</summary>
    public Plan0005PerformanceSmokeTests(Plan0005SharedPostgresFixture fixture) => _fixture = fixture;

    /// <summary>TEST-0005-135/136: fifty concurrent authenticated list calls; records latency and resource samples.</summary>
    [Fact]
    public async Task FiftyConcurrentAuthenticatedListsCompleteWithoutCrossover()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString);
        await factory.EnsureDatabaseAsync();

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime.TotalMilliseconds;
        var memoryBefore = process.WorkingSet64;
        var postgresConnectionsBefore = await CountPostgresConnectionsAsync(_fixture.ConnectionString);

        var subjects = Enumerable.Range(0, 50).Select(index => $"plan0005-perf-{index:D2}").ToArray();
        var clients = subjects.Select(subject =>
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
            return client;
        }).ToArray();

        foreach (var client in clients)
        {
            var csrf = await GetCsrfAsync(client);
            var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), $"perf-{Guid.NewGuid():N}", 3m);
            Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        }

        var latencies = new ConcurrentBag<long>();
        var statuses = new ConcurrentBag<int>();
        var connectionSamples = new ConcurrentBag<int>();
        var ownerLeak = 0;
        var errors = 0;
        using var sampleCts = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!sampleCts.IsCancellationRequested)
            {
                try
                {
                    connectionSamples.Add(await CountPostgresConnectionsAsync(_fixture.ConnectionString));
                }
                catch
                {
                    // Ignore transient sample failures during load.
                }

                try
                {
                    await Task.Delay(25, sampleCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        var tasks = clients.Select(async (client, index) =>
        {
            var sw = Stopwatch.StartNew();
            var response = await client.GetAsync("/api/v1/inventory/lots?pageSize=25");
            sw.Stop();
            latencies.Add(sw.ElapsedMilliseconds);
            statuses.Add((int)response.StatusCode);
            if ((int)response.StatusCode >= 400)
            {
                Interlocked.Increment(ref errors);
            }

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var count = body.GetProperty("items").GetArrayLength();
            if (count != 1)
            {
                Interlocked.Increment(ref ownerLeak);
            }
        });

        await Task.WhenAll(tasks);
        sampleCts.Cancel();
        try
        {
            await sampler;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(0, ownerLeak);
        Assert.Equal(0, errors);
        Assert.All(statuses, status => Assert.InRange(status, 200, 299));

        process.Refresh();
        var cpuAfter = process.TotalProcessorTime.TotalMilliseconds;
        var memoryAfter = process.WorkingSet64;
        var postgresConnectionsAfter = await CountPostgresConnectionsAsync(_fixture.ConnectionString);
        connectionSamples.Add(postgresConnectionsBefore);
        connectionSamples.Add(postgresConnectionsAfter);
        var postgresConnectionsPeakDuringWorkload = connectionSamples.Count == 0 ? postgresConnectionsAfter : connectionSamples.Max();

        var ordered = latencies.OrderBy(value => value).ToArray();
        var p50 = ordered[(int)(ordered.Length * 0.50)];
        var p95 = ordered[Math.Min(ordered.Length - 1, (int)(ordered.Length * 0.95))];
        var p99 = ordered[Math.Min(ordered.Length - 1, (int)(ordered.Length * 0.99))];
        Assert.True(p99 < 30_000, $"p99 latency too high for local smoke: {p99}ms");

        var evidenceDir = ResolveEvidenceDir();
        Directory.CreateDirectory(evidenceDir);
        var payload = new Dictionary<string, object?>
        {
            ["plan"] = "PLAN-0005",
            ["testIds"] = new[] { "TEST-0005-135", "TEST-0005-136" },
            ["status"] = "Passed",
            ["integratedMainSha"] = Environment.GetEnvironmentVariable("PLAN0005_INTEGRATED_SHA") ?? "b94abd9a83fe29d88b095e3e9a42f10d01c05414",
            ["prHeadSha"] = Environment.GetEnvironmentVariable("PLAN0005_PR_HEAD_SHA"),
            ["checkedOutCommitSha"] = Environment.GetEnvironmentVariable("PLAN0005_CHECKED_OUT_SHA"),
            ["evidenceGenerationSha"] = Environment.GetEnvironmentVariable("PLAN0005_EVIDENCE_GENERATION_SHA")
                ?? Environment.GetEnvironmentVariable("PLAN0005_PR_HEAD_SHA"),
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["samples"] = ordered.Length,
            ["p50Ms"] = p50,
            ["p95Ms"] = p95,
            ["p99Ms"] = p99,
            ["errors"] = errors,
            ["ownerCrossovers"] = ownerLeak,
            ["cpuBefore"] = cpuBefore,
            ["cpuAfter"] = cpuAfter,
            ["memoryBeforeBytes"] = memoryBefore,
            ["memoryAfterBytes"] = memoryAfter,
            ["postgresConnectionsBefore"] = postgresConnectionsBefore,
            ["postgresConnectionsAfter"] = postgresConnectionsAfter,
            ["postgresConnectionsPeak"] = postgresConnectionsPeakDuringWorkload,
            ["postgresConnectionsSampleCount"] = connectionSamples.Count,
            ["postgresConnectionsMetric"] = "peak_sampled_during_concurrent_list_workload",
            ["note"] = "Non-SLA smoke. Concurrent authenticated lists without owner crossover. postgresConnectionsPeak is max of samples taken during the workload."
        };
        await File.WriteAllTextAsync(
            Path.Combine(evidenceDir, "performance-smoke.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>TEST-0005-137: ten concurrent creates with distinct idempotency keys produce ten lots and no duplicates.</summary>
    [Fact]
    public async Task TenConcurrentCreatesWithDistinctKeysProduceExactLots()
    {
        await using var factory = new CocinarisFactory(_fixture.ConnectionString);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Test-Subject", "plan0005-perf-137");
        var csrf = await GetCsrfAsync(client);
        var marker = $"plan0005-137-{Guid.NewGuid():N}";
        var keys = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var statuses = new ConcurrentBag<int>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = keys.Select(async (key, index) =>
        {
            await start.Task;
            var response = await CreateAsync(client, csrf, key.ToString(), $"{marker}-{index:D2}", 2m + index);
            statuses.Add((int)response.StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(tasks);
        Assert.Equal(10, statuses.Count(code => code is >= 200 and < 300));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var products = await db.Products.AsNoTracking().Where(p => p.DisplayName.StartsWith(marker)).ToListAsync();
        Assert.Equal(10, products.Count);
        var productIds = products.Select(p => p.Id).ToHashSet();
        var lots = await db.Lots.AsNoTracking().Where(l => productIds.Contains(l.ProductId)).ToListAsync();
        Assert.Equal(10, lots.Count);
        var lotIds = lots.Select(l => l.Id).ToHashSet();
        var transactions = await db.Transactions.AsNoTracking().Where(t => lotIds.Contains(t.LotId)).ToListAsync();
        Assert.Equal(10, transactions.Count);
        Assert.All(transactions, t => Assert.Equal("Initial", t.Type));
        var idem = await db.IdempotencyRecords.AsNoTracking().CountAsync(r => keys.Contains(r.Key));
        Assert.Equal(10, idem);

        var evidenceDir = ResolveEvidenceDir();
        Directory.CreateDirectory(evidenceDir);
        var payload = new
        {
            plan = "PLAN-0005",
            testId = "TEST-0005-137",
            status = "Passed",
            integratedMainSha = Environment.GetEnvironmentVariable("PLAN0005_INTEGRATED_SHA") ?? "b94abd9a83fe29d88b095e3e9a42f10d01c05414",
            prHeadSha = Environment.GetEnvironmentVariable("PLAN0005_PR_HEAD_SHA"),
            checkedOutCommitSha = Environment.GetEnvironmentVariable("PLAN0005_CHECKED_OUT_SHA"),
            evidenceGenerationSha = Environment.GetEnvironmentVariable("PLAN0005_EVIDENCE_GENERATION_SHA")
                ?? Environment.GetEnvironmentVariable("PLAN0005_PR_HEAD_SHA"),
            concurrentCreates = 10,
            distinctIdempotencyKeys = 10,
            lots = 10,
            initialTransactions = 10,
            duplicates = 0,
            partialResults = 0,
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        await File.WriteAllTextAsync(
            Path.Combine(evidenceDir, "performance-concurrent-creates.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static string ResolveEvidenceDir()
    {
        var configured = Environment.GetEnvironmentVariable("PLAN0005_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "docs", "evidence", "plan-0005"));
    }

    private static async Task<int> CountPostgresConnectionsAsync(string connectionString)
    {
        await using var conn = new Npgsql.NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*)::int FROM pg_stat_activity WHERE datname = current_database()";
        return (int)(await cmd.ExecuteScalarAsync() ?? 0);
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
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "plan0005-perf";
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim("iss", "https://integration.test")], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
