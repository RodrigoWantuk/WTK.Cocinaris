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

/// <summary>Integration tests for PLAN-0012 remediation: concurrency, PUT semantics, validation, privacy, and equipment identity.</summary>
public sealed class ProfileRemediationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(TestImages.PostgreSql)
        .WithDatabase("cocinaris_profile_remediation")
        .WithUsername("cocinaris_test")
        .WithPassword("development-only-test-password")
        .Build();

    [Fact]
    public async Task PutProfileClearsOmittedFieldsToAbsent()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new
        {
            displayName = new { action = "confirm", value = "Alex", durability = "durable" },
            language = new { action = "confirm", value = "pt-BR", durability = "durable" }
        });
        var etag = created.Headers.ETag!.Tag;
        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { language = new { action = "confirm", value = "en", durability = "durable" } })
        };
        put.Headers.Add("X-CSRF-TOKEN", csrf);
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await client.SendAsync(put);
        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("absent", profile.GetProperty("displayName").GetProperty("presence").GetString());
        Assert.Equal("confirmed", profile.GetProperty("household").GetProperty("language").GetProperty("presence").GetString());
        Assert.Equal("en", profile.GetProperty("household").GetProperty("language").GetProperty("value").GetString());
    }

    [Fact]
    public async Task PatchProfilePreservesOmittedFields()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new
        {
            displayName = new { action = "confirm", value = "Alex", durability = "durable" },
            language = new { action = "confirm", value = "pt-BR", durability = "durable" }
        });
        var etag = created.Headers.ETag!.Tag;
        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { language = new { action = "confirm", value = "en", durability = "durable" } })
        };
        patch.Headers.Add("X-CSRF-TOKEN", csrf);
        patch.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await client.SendAsync(patch);
        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Alex", profile.GetProperty("displayName").GetProperty("value").GetString());
        Assert.Equal("en", profile.GetProperty("household").GetProperty("language").GetProperty("value").GetString());
    }

    [Fact]
    public async Task InvalidActionAndDurabilityReturnValidationFailed()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        using var invalidAction = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { displayName = new { action = "unknown", value = "Alex", durability = "durable" } })
        };
        invalidAction.Headers.Add("X-CSRF-TOKEN", csrf);
        invalidAction.Headers.TryAddWithoutValidation("If-Match", etag);
        var actionResponse = await client.SendAsync(invalidAction);
        using var invalidDurability = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Alex", durability = "temporary" } })
        };
        invalidDurability.Headers.Add("X-CSRF-TOKEN", csrf);
        invalidDurability.Headers.TryAddWithoutValidation("If-Match", etag);
        var durabilityResponse = await client.SendAsync(invalidDurability);

        await AssertProblemAsync(actionResponse, System.Net.HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(durabilityResponse, System.Net.HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task ConcurrentProfileUpdatesAllowOnlyOneSuccess()
    {
        await using var factory = await CreateFactoryAsync();
        using var seedClient = CreateAuthenticatedClient(factory, "profile-concurrent-update");
        var seedCsrf = await GetCsrfAsync(seedClient);
        var created = await PatchProfileAsync(seedClient, seedCsrf, new { displayName = new { action = "confirm", value = "Seed", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            using var client = CreateAuthenticatedClient(factory, "profile-concurrent-update");
            var csrf = await GetCsrfAsync(client);
            using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
            {
                Content = JsonContent.Create(new { displayName = new { action = "confirm", value = $"Writer-{index}", durability = "durable" } })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await client.SendAsync(request);
        })));

        await AssertConcurrentWinnerAndLoserAsync(responses);
    }

    [Fact]
    public async Task ConcurrentPreferenceUpdatesAllowOnlyOneSuccess()
    {
        await using var factory = await CreateFactoryAsync();
        using var seedClient = CreateAuthenticatedClient(factory, "profile-concurrent-preferences");
        var seedCsrf = await GetCsrfAsync(seedClient);
        var created = await PatchProfileAsync(seedClient, seedCsrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            using var client = CreateAuthenticatedClient(factory, "profile-concurrent-preferences");
            var csrf = await GetCsrfAsync(client);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
            {
                Content = JsonContent.Create(new { entries = new[] { new { action = "add", category = "DietaryPattern", stableCode = $"diet_{index}", note = (string?)null } } })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await client.SendAsync(request);
        })));

        await AssertConcurrentWinnerAndLoserAsync(responses);
    }

    [Fact]
    public async Task ConcurrentEquipmentUpdatesAllowOnlyOneSuccess()
    {
        await using var factory = await CreateFactoryAsync();
        using var seedClient = CreateAuthenticatedClient(factory, "profile-concurrent-equipment");
        var seedCsrf = await GetCsrfAsync(seedClient);
        var created = await PatchProfileAsync(seedClient, seedCsrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            using var client = CreateAuthenticatedClient(factory, "profile-concurrent-equipment");
            var csrf = await GetCsrfAsync(client);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/equipment")
            {
                Content = JsonContent.Create(new { entries = new[] { new { stableCode = $"oven_{index}", customName = (string?)null, capacity = (decimal?)null, capacityUnit = (string?)null, constraintNote = (string?)null } } })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await client.SendAsync(request);
        })));

        await AssertConcurrentWinnerAndLoserAsync(responses);
    }

    [Fact]
    public async Task ConcurrentProfileCreationReturnsControlledConflict()
    {
        await using var factory = await CreateFactoryAsync();
        var subject = "profile-concurrent-create";
        var payload = new { displayName = new { action = "confirm", value = "Race", durability = "durable" } };
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var client = CreateAuthenticatedClient(factory, subject);
            var csrf = await GetCsrfAsync(client);
            using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile") { Content = JsonContent.Create(payload) };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            return await client.SendAsync(request);
        })));

        Assert.Equal(1, responses.Count(item => item.StatusCode == System.Net.HttpStatusCode.Created || item.StatusCode == System.Net.HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(item => item.StatusCode == System.Net.HttpStatusCode.Conflict
            || item.StatusCode == System.Net.HttpStatusCode.PreconditionFailed
            || item.StatusCode == System.Net.HttpStatusCode.PreconditionRequired));
    }

    [Theory]
    [InlineData("allergy", "peanut", "severe", "allergy_entry_added")]
    [InlineData("medicalrestriction", "low_sodium", "doctor", "medical_restriction_added")]
    public async Task PreferenceHistoryRedactsSensitiveCategoriesRegardlessOfCasing(string category, string stableCode, string note, string expectedMarker)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = new[] { new { action = "add", category, stableCode, note } } })
        };
        put.Headers.Add("X-CSRF-TOKEN", csrf);
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await client.SendAsync(put);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var history = await database.ProfileChangeHistoryEntries.AsNoTracking().OrderByDescending(item => item.OccurredAt).FirstAsync();
        var codes = JsonSerializer.Deserialize<string[]>(history.FieldCodesJson) ?? [];

        Assert.Equal([expectedMarker], codes);
        Assert.DoesNotContain(codes, code => code.Contains(stableCode, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(codes, code => code.Contains(note, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(history.FieldCodesJson, stableCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(history.FieldCodesJson, note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(history.FieldCodesJson, $":{stableCode}", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreferenceHistoryRedactsSensitiveAllergyCodes()
    {
        await PreferenceHistoryRedactsSensitiveCategoriesRegardlessOfCasing("Allergy", "peanut_allergy", "severe reaction", "allergy_entry_added");
    }

    [Fact]
    public async Task EquipmentPutPreservesIdentityAndSoftRemovesOmittedItems()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        using var initialPut = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/equipment")
        {
            Content = JsonContent.Create(new
            {
                entries = new object[]
                {
                    new { stableCode = "oven", customName = (string?)"Main oven", capacity = (decimal?)null, capacityUnit = (string?)null, constraintNote = (string?)null },
                    new { stableCode = "blender", customName = (string?)null, capacity = (decimal?)null, capacityUnit = (string?)null, constraintNote = (string?)null }
                }
            })
        };
        initialPut.Headers.Add("X-CSRF-TOKEN", csrf);
        initialPut.Headers.TryAddWithoutValidation("If-Match", etag);
        var initialResponse = await client.SendAsync(initialPut);
        var initialBody = await initialResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ovenId = FindEquipmentEntry(initialBody, "oven").GetProperty("entryId").GetGuid();
        var blenderId = FindEquipmentEntry(initialBody, "blender").GetProperty("entryId").GetGuid();
        var nextEtag = initialResponse.Headers.ETag!.Tag;
        using var replacePut = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/equipment")
        {
            Content = JsonContent.Create(new { entries = new[] { new { stableCode = "oven", customName = "Updated oven", capacity = (decimal?)null, capacityUnit = (string?)null, constraintNote = (string?)null } } })
        };
        replacePut.Headers.Add("X-CSRF-TOKEN", csrf);
        replacePut.Headers.TryAddWithoutValidation("If-Match", nextEtag);
        var replaceResponse = await client.SendAsync(replacePut);
        var replaceBody = await replaceResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ovenId, FindEquipmentEntry(replaceBody, "oven").GetProperty("entryId").GetGuid());
        Assert.Equal("Updated oven", FindEquipmentEntry(replaceBody, "oven").GetProperty("customName").GetString());
        Assert.NotEmpty(replaceResponse.Headers.ETag!.Tag);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var blender = await database.EquipmentEntries.AsNoTracking().SingleAsync(item => item.Id == blenderId);
        Assert.True(blender.IsRemoved);
    }

    [Fact]
    public async Task PreferencesAndEquipmentMutationsReturnVersionAndEtag()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        using var putPreferences = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = new[] { new { action = "add", category = "DietaryPattern", stableCode = "vegetarian", note = (string?)null } } })
        };
        putPreferences.Headers.Add("X-CSRF-TOKEN", csrf);
        putPreferences.Headers.TryAddWithoutValidation("If-Match", etag);
        var preferencesResponse = await client.SendAsync(putPreferences);
        var preferencesBody = await preferencesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var preferencesEtag = preferencesResponse.Headers.ETag!.Tag;
        using var getPreferences = new HttpRequestMessage(HttpMethod.Get, "/api/v1/profile/preferences");
        var getPreferencesResponse = await client.SendAsync(getPreferences);

        Assert.Equal(System.Net.HttpStatusCode.OK, preferencesResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(preferencesBody.GetProperty("version").GetString()));
        Assert.NotEmpty(preferencesEtag);
        Assert.NotEqual(etag, preferencesEtag);
        Assert.Equal(System.Net.HttpStatusCode.OK, getPreferencesResponse.StatusCode);
        Assert.NotEmpty(getPreferencesResponse.Headers.ETag!.Tag);
    }

    private static JsonElement FindEquipmentEntry(JsonElement body, string stableCode)
    {
        foreach (var entry in body.GetProperty("entries").EnumerateArray())
        {
            if (entry.GetProperty("stableCode").GetString() == stableCode)
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"Equipment entry '{stableCode}' was not found.");
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private static HttpClient CreateAuthenticatedClient(CocinarisFactory factory, string subject)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        return client;
    }

    private static async Task AssertConcurrentWinnerAndLoserAsync(HttpResponseMessage[] responses)
    {
        var details = new List<string>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            details.Add($"{(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        Assert.True(
            responses.Count(item => item.StatusCode == System.Net.HttpStatusCode.OK) == 1
            && responses.Count(item => item.StatusCode == System.Net.HttpStatusCode.PreconditionFailed) == 1,
            $"Expected exactly one OK and one PreconditionFailed, got:{Environment.NewLine}{string.Join(Environment.NewLine, details)}");
    }

    private async Task<CocinarisFactory> CreateFactoryAsync()
    {
        var factory = new CocinarisFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
        return session.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PatchProfileAsync(HttpClient client, string csrf, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile") { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, System.Net.HttpStatusCode statusCode, string errorCode)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(errorCode, problem.GetProperty("errorCode").GetString());
    }

    private sealed class CocinarisFactory(string connectionString, bool authenticate) : WebApplicationFactory<Program>
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
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "integration-user";
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim("iss", "https://integration.test")], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
