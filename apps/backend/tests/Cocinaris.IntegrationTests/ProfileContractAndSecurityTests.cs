using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Profiles.Application;
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

/// <summary>OpenAPI truthfulness, owner isolation, CSRF, and empty-profile semantics for profile endpoints.</summary>
public sealed class ProfileContractAndSecurityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4")
        .WithDatabase("cocinaris_profile_contract")
        .WithUsername("cocinaris_test")
        .WithPassword("development-only-test-password")
        .Build();

    [Fact]
    public async Task GeneratedOpenApiMatchesProfileRuntimeContracts()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Cocinaris", "Host=127.0.0.1;Database=cocinaris_contract_test;Username=test;Password=test"));
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var paths = document.GetProperty("paths");

        Assert.Equal(["absent", "confirm", "remove"], EnumValues(schemas.GetProperty("ProfileFieldAction")));
        Assert.Equal(["durable"], EnumValues(schemas.GetProperty("ProfileFieldDurability")));
        Assert.Equal(["add", "remove", "update"], EnumValues(schemas.GetProperty("PreferenceCommandAction")));

        foreach (var mutationSchema in schemas.EnumerateObject().Where(schema => schema.Name.StartsWith("FieldMutationDto", StringComparison.Ordinal)))
        {
            Assert.Equal("#/components/schemas/ProfileFieldAction", mutationSchema.Value.GetProperty("properties").GetProperty("action").GetProperty("$ref").GetString());
            Assert.Equal("#/components/schemas/ProfileFieldDurability", mutationSchema.Value.GetProperty("properties").GetProperty("durability").GetProperty("$ref").GetString());
        }

        Assert.Equal("#/components/schemas/PreferenceCommandAction", schemas.GetProperty("PreferenceCommandDto").GetProperty("properties").GetProperty("action").GetProperty("$ref").GetString());
        Assert.Contains("null", SchemaTypes(schemas.GetProperty("PreferencesCollectionResponse").GetProperty("properties").GetProperty("version")));
        Assert.Contains("null", SchemaTypes(schemas.GetProperty("EquipmentCollectionResponse").GetProperty("properties").GetProperty("version")));

        AssertCollectionOperation(paths, "/api/v1/profile/preferences", "get", csrf: false, ifMatch: false, etagStatus: "200", "PreferencesCollectionResponse");
        AssertCollectionOperation(paths, "/api/v1/profile/preferences", "put", csrf: true, ifMatch: true, etagStatus: "200", "PreferencesCollectionResponse");
        AssertCollectionOperation(paths, "/api/v1/profile/equipment", "get", csrf: false, ifMatch: false, etagStatus: "200", "EquipmentCollectionResponse");
        AssertCollectionOperation(paths, "/api/v1/profile/equipment", "put", csrf: true, ifMatch: true, etagStatus: "200", "EquipmentCollectionResponse");

        foreach (var method in new[] { "put", "patch" })
        {
            var operation = paths.GetProperty("/api/v1/profile").GetProperty(method);
            var headers = HeaderNames(operation);
            Assert.Contains("X-CSRF-TOKEN", headers);
            Assert.Contains("If-Match", headers);
            Assert.Contains(operation.GetProperty("responses").GetProperty("200").GetProperty("headers").EnumerateObject(), header => header.Name == "ETag");
            foreach (var status in new[] { "400", "401", "403", "409", "412", "428" })
            {
                Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _), $"Expected {status} on profile {method}");
            }
        }

        Assert.Contains(paths.GetProperty("/api/v1/profile").GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("headers").EnumerateObject(), header => header.Name == "ETag");
        Assert.DoesNotContain("If-None-Match", HeaderNames(paths.GetProperty("/api/v1/profile").GetProperty("get")));
        Assert.DoesNotContain("If-None-Match", HeaderNames(paths.GetProperty("/api/v1/profile/preferences").GetProperty("get")));
    }

    [Fact]
    public async Task MissingProfileCollectionsReturnNullVersionWithoutEtag()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

        var preferences = await client.GetAsync("/api/v1/profile/preferences");
        var equipment = await client.GetAsync("/api/v1/profile/equipment");
        var preferencesBody = await preferences.Content.ReadFromJsonAsync<JsonElement>();
        var equipmentBody = await equipment.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.OK, preferences.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, equipment.StatusCode);
        Assert.Equal(JsonValueKind.Null, preferencesBody.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, equipmentBody.GetProperty("version").ValueKind);
        Assert.Empty(preferencesBody.GetProperty("entries").EnumerateArray());
        Assert.Empty(equipmentBody.GetProperty("entries").EnumerateArray());
        Assert.Null(preferences.Headers.ETag);
        Assert.Null(equipment.Headers.ETag);
    }

    [Fact]
    public async Task FirstCreateAndFirstCollectionPutUsePreconditionRules()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);

        using var preferencesCreate = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = new[] { new { action = "add", category = "DietaryPattern", stableCode = "vegetarian", note = (string?)null } } })
        };
        preferencesCreate.Headers.Add("X-CSRF-TOKEN", csrf);
        var createdByPreferences = await client.SendAsync(preferencesCreate);
        var createdBody = await createdByPreferences.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(System.Net.HttpStatusCode.OK, createdByPreferences.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, createdBody.GetProperty("version").ValueKind);
        Assert.NotNull(createdByPreferences.Headers.ETag);
        Assert.Single(createdBody.GetProperty("entries").EnumerateArray());

        using var preferencesMissingMatch = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = Array.Empty<object>() })
        };
        preferencesMissingMatch.Headers.Add("X-CSRF-TOKEN", csrf);
        var blocked = await client.SendAsync(preferencesMissingMatch);
        await AssertProblemAsync(blocked, System.Net.HttpStatusCode.PreconditionRequired, "precondition_required");

        using var preferencesUpdate = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = new[] { new { action = "add", category = "DietaryPattern", stableCode = "vegan", note = (string?)null } } })
        };
        preferencesUpdate.Headers.Add("X-CSRF-TOKEN", csrf);
        preferencesUpdate.Headers.TryAddWithoutValidation("If-Match", createdByPreferences.Headers.ETag!.Tag);
        var preferencesResponse = await client.SendAsync(preferencesUpdate);
        var preferencesBody = await preferencesResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.OK, preferencesResponse.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, preferencesBody.GetProperty("version").ValueKind);
        Assert.NotNull(preferencesResponse.Headers.ETag);
        Assert.Contains(preferencesBody.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("stableCode").GetString() == "vegan");
    }

    [Fact]
    public async Task AuthenticatedUserCannotReadOrMutateAnotherUsersProfileResources()
    {
        await using var factory = await CreateFactoryAsync();
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        other.DefaultRequestHeaders.Add("X-Test-Subject", "profile-user-b");
        var ownerCsrf = await GetCsrfAsync(owner);
        var otherCsrf = await GetCsrfAsync(other);

        var created = await PatchProfileAsync(owner, ownerCsrf, new
        {
            displayName = new { action = "confirm", value = "Owner", durability = "durable" },
            language = new { action = "confirm", value = "en", durability = "durable" }
        });
        var etag = created.Headers.ETag!.Tag;
        using var putPreferences = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = new[] { new { action = "add", category = "DietaryPattern", stableCode = "vegetarian", note = (string?)null } } })
        };
        putPreferences.Headers.Add("X-CSRF-TOKEN", ownerCsrf);
        putPreferences.Headers.TryAddWithoutValidation("If-Match", etag);
        var preferencesResponse = await owner.SendAsync(putPreferences);
        var ownerPreferencesEtag = preferencesResponse.Headers.ETag!.Tag;

        var otherProfile = await other.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        var otherPreferences = await other.GetAsync("/api/v1/profile/preferences");
        var otherEquipment = await other.GetAsync("/api/v1/profile/equipment");
        var otherPreferencesBody = await otherPreferences.Content.ReadFromJsonAsync<JsonElement>();
        var otherEquipmentBody = await otherEquipment.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("absent", otherProfile.GetProperty("displayName").GetProperty("presence").GetString());
        Assert.Equal(JsonValueKind.Null, otherPreferencesBody.GetProperty("version").ValueKind);
        Assert.Empty(otherPreferencesBody.GetProperty("entries").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, otherEquipmentBody.GetProperty("version").ValueKind);
        Assert.Null(otherPreferences.Headers.ETag);
        Assert.Null(otherEquipment.Headers.ETag);

        var otherCreated = await PatchProfileAsync(other, otherCsrf, new { displayName = new { action = "confirm", value = "Other", durability = "durable" } });
        Assert.Equal(System.Net.HttpStatusCode.Created, otherCreated.StatusCode);

        using var steal = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Stolen", durability = "durable" } })
        };
        steal.Headers.Add("X-CSRF-TOKEN", otherCsrf);
        steal.Headers.TryAddWithoutValidation("If-Match", etag);
        var stealResponse = await other.SendAsync(steal);
        using var stealPreferences = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = Array.Empty<object>() })
        };
        stealPreferences.Headers.Add("X-CSRF-TOKEN", otherCsrf);
        stealPreferences.Headers.TryAddWithoutValidation("If-Match", ownerPreferencesEtag);
        var stealPreferencesResponse = await other.SendAsync(stealPreferences);

        await AssertProblemAsync(stealResponse, System.Net.HttpStatusCode.PreconditionFailed, "precondition_failed");
        await AssertProblemAsync(stealPreferencesResponse, System.Net.HttpStatusCode.PreconditionFailed, "precondition_failed");

        var ownerProfile = await owner.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        var ownerPreferences = await owner.GetFromJsonAsync<JsonElement>("/api/v1/profile/preferences");
        Assert.Equal("Owner", ownerProfile.GetProperty("displayName").GetProperty("value").GetString());
        Assert.Contains(ownerPreferences.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("stableCode").GetString() == "vegetarian");
        var otherAfter = await other.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        Assert.Equal("Other", otherAfter.GetProperty("displayName").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("PUT", "/api/v1/profile")]
    [InlineData("PATCH", "/api/v1/profile")]
    [InlineData("PUT", "/api/v1/profile/preferences")]
    [InlineData("PUT", "/api/v1/profile/equipment")]
    public async Task ProfileMutationsRejectMissingAndInvalidCsrfWithoutMutation(string method, string path)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/profile");

        HttpContent content = path.EndsWith("/preferences", StringComparison.Ordinal)
            ? JsonContent.Create(new { entries = new[] { new { action = "add", category = "DietaryPattern", stableCode = "vegan", note = (string?)null } } })
            : path.EndsWith("/equipment", StringComparison.Ordinal)
                ? JsonContent.Create(new { entries = new[] { new { stableCode = "oven", customName = (string?)null, capacity = (decimal?)null, capacityUnit = (string?)null, constraintNote = (string?)null } } })
                : JsonContent.Create(new { displayName = new { action = "confirm", value = "Mutated", durability = "durable" } });

        using var missing = new HttpRequestMessage(new HttpMethod(method), path) { Content = content };
        missing.Headers.TryAddWithoutValidation("If-Match", etag);
        var missingResponse = await client.SendAsync(missing);

        using var invalid = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = path.EndsWith("/preferences", StringComparison.Ordinal)
                ? JsonContent.Create(new { entries = new[] { new { action = "add", category = "DietaryPattern", stableCode = "vegan", note = (string?)null } } })
                : path.EndsWith("/equipment", StringComparison.Ordinal)
                    ? JsonContent.Create(new { entries = new[] { new { stableCode = "oven", customName = (string?)null, capacity = (decimal?)null, capacityUnit = (string?)null, constraintNote = (string?)null } } })
                    : JsonContent.Create(new { displayName = new { action = "confirm", value = "Mutated", durability = "durable" } })
        };
        invalid.Headers.Add("X-CSRF-TOKEN", "not-the-session-token");
        invalid.Headers.TryAddWithoutValidation("If-Match", etag);
        var invalidResponse = await client.SendAsync(invalid);

        await AssertProblemAsync(missingResponse, System.Net.HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblemAsync(invalidResponse, System.Net.HttpStatusCode.BadRequest, "validation_failed");

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        Assert.Equal(before.GetProperty("displayName").GetProperty("value").GetString(), after.GetProperty("displayName").GetProperty("value").GetString());
        Assert.Equal(before.GetProperty("version").GetString(), after.GetProperty("version").GetString());
    }

    [Fact]
    public void GetProfileRecordsReadMetricNotMutationMetric()
    {
        var observed = new List<(string Instrument, string Operation)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Cocinaris.Profiles")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            string? operation = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation")
                {
                    operation = tag.Value?.ToString();
                }
            }

            if (operation is not null)
            {
                observed.Add((instrument.Name, operation));
            }
        });
        listener.Start();

        var metrics = new ProfileMetrics();
        metrics.RecordRead("get");
        metrics.RecordMutation("patch");

        Assert.Contains(observed, item => item.Instrument == "profile_reads_total" && item.Operation == "get");
        Assert.Contains(observed, item => item.Instrument == "profile_mutations_total" && item.Operation == "patch");
        Assert.DoesNotContain(observed, item => item.Instrument == "profile_mutations_total" && item.Operation == "get");
    }

    [Fact]
    public async Task GetProfileHttpPathDoesNotRecordMutationMetric()
    {
        var observedMutations = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Cocinaris.Profiles" && instrument.Name == "profile_mutations_total")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "operation")
                {
                    observedMutations.Add(tag.Value?.ToString() ?? string.Empty);
                }
            }
        });
        listener.Start();

        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var response = await client.GetAsync("/api/v1/profile");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("get", observedMutations);
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

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

    private static void AssertCollectionOperation(JsonElement paths, string path, string method, bool csrf, bool ifMatch, string etagStatus, string schemaName)
    {
        var operation = paths.GetProperty(path).GetProperty(method);
        var headers = HeaderNames(operation);
        Assert.Equal(csrf, headers.Contains("X-CSRF-TOKEN"));
        Assert.Equal(ifMatch, headers.Contains("If-Match"));
        Assert.DoesNotContain("If-None-Match", headers);
        Assert.Equal($"#/components/schemas/{schemaName}", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Contains(operation.GetProperty("responses").GetProperty(etagStatus).GetProperty("headers").EnumerateObject(), header => header.Name == "ETag");
        if (method is "put" or "patch")
        {
            foreach (var status in new[] { "400", "401", "409", "412", "428" })
            {
                Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _), $"Expected {status} on {path} {method}");
            }
        }
    }

    private static HashSet<string> HeaderNames(JsonElement operation) =>
        operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Where(parameter => parameter.GetProperty("in").GetString() == "header").Select(parameter => parameter.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];

    private static string[] EnumValues(JsonElement schema) =>
        schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> SchemaTypes(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in type.EnumerateArray())
                {
                    yield return item.GetString()!;
                }
            }
            else
            {
                yield return type.GetString()!;
            }
        }

        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            foreach (var option in anyOf.EnumerateArray())
            {
                foreach (var nested in SchemaTypes(option))
                {
                    yield return nested;
                }
            }
        }
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
