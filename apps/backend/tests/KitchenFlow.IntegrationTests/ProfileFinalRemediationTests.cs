using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using KitchenFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KitchenFlow.IntegrationTests;

/// <summary>Final PLAN-0012 remediation: missing-profile version semantics, owner-bound ETags, and unique equipment identity/order.</summary>
public sealed class ProfileFinalRemediationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4")
        .WithDatabase("kitchenflow_profile_final")
        .WithUsername("kitchenflow_test")
        .WithPassword("development-only-test-password")
        .Build();

    [Fact]
    public async Task MissingProfileGetOmitsSyntheticVersionAndEtag()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var response = await client.GetAsync("/api/v1/profile");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("profileExists").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("createdAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
        Assert.Null(response.Headers.ETag);
        Assert.Equal("absent", body.GetProperty("displayName").GetProperty("presence").GetString());

        var second = await client.GetAsync("/api/v1/profile");
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, secondBody.GetProperty("createdAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, secondBody.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task ProfileCreateUpdateAndStaleEtagFollowPreconditionRules()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);

        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var etag = created.Headers.ETag!.Tag;
        Assert.True(createdBody.GetProperty("profileExists").GetBoolean());
        Assert.Equal(etag.Trim('"'), createdBody.GetProperty("version").GetString());
        Assert.NotEqual(JsonValueKind.Null, createdBody.GetProperty("createdAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, createdBody.GetProperty("updatedAt").ValueKind);
        var createdAt = createdBody.GetProperty("createdAt").GetDateTimeOffset();
        var createdUpdatedAt = createdBody.GetProperty("updatedAt").GetDateTimeOffset();

        var get = await client.GetAsync("/api/v1/profile");
        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(getBody.GetProperty("profileExists").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, getBody.GetProperty("version").ValueKind);
        Assert.Equal(get.Headers.ETag!.Tag.Trim('"'), getBody.GetProperty("version").GetString());
        Assert.Equal(createdAt, getBody.GetProperty("createdAt").GetDateTimeOffset());

        using var missingMatch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "NoMatch", durability = "durable" } })
        };
        missingMatch.Headers.Add("X-CSRF-TOKEN", csrf);
        await AssertProblemAsync(await client.SendAsync(missingMatch), System.Net.HttpStatusCode.PreconditionRequired, "precondition_required");

        using var valid = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Updated", durability = "durable" } })
        };
        valid.Headers.Add("X-CSRF-TOKEN", csrf);
        valid.Headers.TryAddWithoutValidation("If-Match", etag);
        var updated = await client.SendAsync(valid);
        Assert.Equal(System.Net.HttpStatusCode.OK, updated.StatusCode);
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated", updatedBody.GetProperty("displayName").GetProperty("value").GetString());
        Assert.Equal(createdAt, updatedBody.GetProperty("createdAt").GetDateTimeOffset());
        Assert.True(updatedBody.GetProperty("updatedAt").GetDateTimeOffset() >= createdUpdatedAt);

        using var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Stale", durability = "durable" } })
        };
        stale.Headers.Add("X-CSRF-TOKEN", csrf);
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        await AssertProblemAsync(await client.SendAsync(stale), System.Net.HttpStatusCode.PreconditionFailed, "precondition_failed");
    }

    [Fact]
    public async Task ForeignOwnerEtagCannotMutateAnotherUsersProfile()
    {
        await using var factory = await CreateFactoryAsync();
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        other.DefaultRequestHeaders.Add("X-Test-Subject", "profile-final-user-b");
        var ownerCsrf = await GetCsrfAsync(owner);
        var otherCsrf = await GetCsrfAsync(other);

        var created = await PatchProfileAsync(owner, ownerCsrf, new { displayName = new { action = "confirm", value = "Owner", durability = "durable" } });
        var ownerEtag = created.Headers.ETag!.Tag;
        await PatchProfileAsync(other, otherCsrf, new { displayName = new { action = "confirm", value = "Other", durability = "durable" } });

        using var steal = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Stolen", durability = "durable" } })
        };
        steal.Headers.Add("X-CSRF-TOKEN", otherCsrf);
        steal.Headers.TryAddWithoutValidation("If-Match", ownerEtag);
        await AssertProblemAsync(await other.SendAsync(steal), System.Net.HttpStatusCode.PreconditionFailed, "precondition_failed");

        var ownerProfile = await owner.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        var otherProfile = await other.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        Assert.Equal("Owner", ownerProfile.GetProperty("displayName").GetProperty("value").GetString());
        Assert.Equal("Other", otherProfile.GetProperty("displayName").GetProperty("value").GetString());
    }

    [Fact]
    public async Task EquipmentHistoryPersistsCanonicalStableCodesOnly()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;
        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/equipment")
        {
            Content = new StringContent("""{"entries":[{"stableCode":" blender "},{"stableCode":"oven"}]}""", System.Text.Encoding.UTF8, "application/json")
        };
        put.Headers.Add("X-CSRF-TOKEN", csrf);
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await client.SendAsync(put);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var history = await database.ProfileChangeHistoryEntries.AsNoTracking().OrderByDescending(item => item.OccurredAt).FirstAsync();
        var codes = System.Text.Json.JsonSerializer.Deserialize<string[]>(history.FieldCodesJson) ?? [];
        Assert.Equal(["blender", "oven"], codes);
        Assert.DoesNotContain(history.FieldCodesJson, " blender ", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"displayName":{"value":"Alex"}}""")]
    [InlineData("""{"displayName":{"action":null,"value":"Alex","durability":"durable"}}""")]
    [InlineData("""{"displayName":{"action":"confirm","value":"Alex","durability":null}}""")]
    [InlineData("""{"displayName":{"action":"","value":"Alex","durability":""}}""")]
    public async Task MalformedDisplayNamePayloadsReturnValidationFailed(string payload)
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AdultDeclarationValidationErrorsUseNestedPaths()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new
            {
                adultDeclaration = new
                {
                    adultDeclared = true,
                    termsVersion = "",
                    privacyVersion = new string('p', 33)
                }
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.GetProperty("errorCode").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("adultDeclaration.termsVersion", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("adultDeclaration.privacyVersion", out _));
        Assert.False(problem.GetProperty("errors").TryGetProperty("termsVersion", out _));
        Assert.False(problem.GetProperty("errors").TryGetProperty("privacyVersion", out _));
    }

    [Fact]
    public async Task DuplicateEquipmentStableCodesAreRejectedIncludingNormalizedDuplicates()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;

        using var duplicate = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/equipment")
        {
            Content = new StringContent("""{"entries":[{"stableCode":"oven"},{"stableCode":" oven ","customName":"Second"}]}""", System.Text.Encoding.UTF8, "application/json")
        };
        duplicate.Headers.Add("X-CSRF-TOKEN", csrf);
        duplicate.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await client.SendAsync(duplicate);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.GetProperty("errorCode").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("entries", out _));
    }

    [Fact]
    public async Task EquipmentArrayOrderIsCanonicalAndDeterministicWithIdentityPreserve()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await PatchProfileAsync(client, csrf, new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } });
        var etag = created.Headers.ETag!.Tag;

        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/equipment")
        {
            Content = new StringContent("""{"entries":[{"stableCode":"oven","customName":"Main"},{"stableCode":"air-fryer"},{"stableCode":"blender"}]}""", System.Text.Encoding.UTF8, "application/json")
        };
        put.Headers.Add("X-CSRF-TOKEN", csrf);
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        var first = await client.SendAsync(put);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var ovenId = FindEquipmentEntry(firstBody, "oven").GetProperty("entryId").GetGuid();
        Assert.Equal(["oven", "air-fryer", "blender"], firstBody.GetProperty("entries").EnumerateArray().Select(item => item.GetProperty("stableCode").GetString()).ToList());
        Assert.Equal([0, 1, 2], firstBody.GetProperty("entries").EnumerateArray().Select(item => item.GetProperty("sortOrder").GetInt32()).ToList());

        using var reorder = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/equipment")
        {
            Content = new StringContent("""{"entries":[{"stableCode":"blender"},{"stableCode":"oven","customName":"Main"}]}""", System.Text.Encoding.UTF8, "application/json")
        };
        reorder.Headers.Add("X-CSRF-TOKEN", csrf);
        reorder.Headers.TryAddWithoutValidation("If-Match", first.Headers.ETag!.Tag);
        var second = await client.SendAsync(reorder);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["blender", "oven"], secondBody.GetProperty("entries").EnumerateArray().Select(item => item.GetProperty("stableCode").GetString()).ToList());
        Assert.Equal([0, 1], secondBody.GetProperty("entries").EnumerateArray().Select(item => item.GetProperty("sortOrder").GetInt32()).ToList());
        Assert.Equal(ovenId, FindEquipmentEntry(secondBody, "oven").GetProperty("entryId").GetGuid());
        Assert.DoesNotContain(secondBody.GetProperty("entries").EnumerateArray(), item => item.GetProperty("stableCode").GetString() == "air-fryer");
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateEquipmentRowsAndAllowsSameCodeAcrossOwners()
    {
        await using var factory = await CreateFactoryAsync();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        database.Users.Add(new KitchenFlow.Modules.Identity.InternalUser(ownerA, "https://integration.test", $"db-a-{ownerA:N}", now));
        database.Users.Add(new KitchenFlow.Modules.Identity.InternalUser(ownerB, "https://integration.test", $"db-b-{ownerB:N}", now));
        await database.SaveChangesAsync();

        static UserProfileRecord CreateProfile(Guid ownerUserId, DateTimeOffset timestamp) => new()
        {
            OwnerUserId = ownerUserId,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            DisplayNamePresence = "Absent",
            DefaultAdultCountPresence = "Absent",
            DefaultChildCountPresence = "Absent",
            DefaultServingCountPresence = "Absent",
            LanguagePresence = "Absent",
            RegionPresence = "Absent",
            CurrencyPresence = "Absent",
            MeasurementSystemPresence = "Absent",
            TimeZonePresence = "Absent",
            PlanningCadencePresence = "Absent",
            ShoppingCadencePresence = "Absent",
            OverallSkillPresence = "Absent",
            ConfidencePresence = "Absent",
            PreferredInstructionDetailPresence = "Absent",
            OrdinaryPrepMinutesPresence = "Absent",
            ExceptionalPrepMinutesPresence = "Absent",
            EffortTolerancePresence = "Absent",
            CleanupTolerancePresence = "Absent",
            RepeatMealPreferencePresence = "Absent",
            ReheatingPreferencePresence = "Absent",
            LeftoverPreferencePresence = "Absent",
            FreezingPreferencePresence = "Absent"
        };

        database.UserProfiles.Add(CreateProfile(ownerA, now));
        database.UserProfiles.Add(CreateProfile(ownerB, now));
        await database.SaveChangesAsync();

        database.EquipmentEntries.Add(new EquipmentEntryRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerA,
            StableCode = "oven",
            IsRemoved = false,
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        });
        database.EquipmentEntries.Add(new EquipmentEntryRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerB,
            StableCode = "oven",
            IsRemoved = false,
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        });
        await database.SaveChangesAsync();

        database.EquipmentEntries.Add(new EquipmentEntryRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerA,
            StableCode = "oven",
            IsRemoved = true,
            SortOrder = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        Assert.IsType<PostgresException>(exception.InnerException);
    }

    [Fact]
    public async Task GeneratedOpenApiDocumentsMissingProfileAndEquipmentContracts()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:KitchenFlow", "Host=127.0.0.1;Database=kitchenflow_contract_test;Username=test;Password=test"));
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var profileSchema = document.GetProperty("components").GetProperty("schemas").GetProperty("ProfileResponse").GetProperty("properties");
        Assert.Contains("boolean", SchemaTypes(profileSchema.GetProperty("profileExists")));
        Assert.Contains("null", SchemaTypes(profileSchema.GetProperty("version")));
        Assert.Contains("null", SchemaTypes(profileSchema.GetProperty("createdAt")));
        Assert.Contains("null", SchemaTypes(profileSchema.GetProperty("updatedAt")));
        Assert.Contains(document.GetProperty("components").GetProperty("schemas").GetProperty("ProfileResponse").GetProperty("required").EnumerateArray().Select(item => item.GetString()), value => value == "profileExists");

        var equipmentItem = document.GetProperty("components").GetProperty("schemas").GetProperty("EquipmentItemDto").GetProperty("properties");
        Assert.False(equipmentItem.TryGetProperty("sortOrder", out _));
        Assert.True(document.GetProperty("components").GetProperty("schemas").GetProperty("EquipmentResponse").GetProperty("properties").TryGetProperty("sortOrder", out _));

        var getProfile = document.GetProperty("paths").GetProperty("/api/v1/profile").GetProperty("get");
        Assert.Contains(getProfile.GetProperty("responses").GetProperty("200").GetProperty("headers").EnumerateObject(), header => header.Name == "ETag");
        Assert.Contains("omit", getProfile.GetProperty("responses").GetProperty("200").GetProperty("description").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private async Task<KitchenFlowFactory> CreateFactoryAsync()
    {
        var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
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

    private sealed class KitchenFlowFactory(string connectionString, bool authenticate) : WebApplicationFactory<Program>
    {
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
