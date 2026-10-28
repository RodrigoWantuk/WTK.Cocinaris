using System.Security.Claims;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using KitchenFlow.Api.Services;
using KitchenFlow.Infrastructure.Persistence;
using KitchenFlow.Modules.Identity;
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

public sealed class ApiAuthenticationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4")
        .WithDatabase("kitchenflow_api_test")
        .WithUsername("kitchenflow_test")
        .WithPassword("development-only-test-password")
        .Build();

    [Fact]
    public async Task ProtectedSessionReturnsUnauthorizedWithoutAuthentication()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: false);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync("/api/v1/session");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("authentication_required", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AuthenticatedSessionProvisionsInternalUserAndReturnsCsrfToken()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync("/api/v1/session");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("csrfToken", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticatedUserCannotReadAnotherUsersLot()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        var lotId = await factory.SeedOtherUsersLotAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync($"/api/v1/inventory/lots/{lotId}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostgreSqlRejectsOrphanedLotsAndNegativeMeasuredQuantities()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        database.Lots.Add(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 1m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();

        database.Users.Add(new InternalUser(ownerId, "https://integration.test", $"constraint-{ownerId:N}", now));
        database.Products.Add(new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Constraint tomato", NormalizedSearchName = "CONSTRAINT TOMATO", CreatedAt = now, UpdatedAt = now });
        await database.SaveChangesAsync();
        database.Lots.Add(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = ownerId, ProductId = productId, MeasuredValue = -1m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now });

        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task MutationRequiresCsrfToken()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = JsonContent.Create(CreateLot()) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FrameworkBindingFailuresUseSafeProblemDetails()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = new StringContent("{", Encoding.UTF8, "application/json") };
        malformedRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        malformedRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        using var unsupportedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = new StringContent("{}", Encoding.UTF8, "text/plain") };
        unsupportedRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        unsupportedRequest.Headers.Add("X-CSRF-TOKEN", csrf);

        var malformed = await client.SendAsync(malformedRequest);
        var unsupported = await client.SendAsync(unsupportedRequest);
        var queryBinding = await client.GetAsync("/api/v1/inventory/lots?pageSize=not-a-number");

        await AssertProblemAsync(malformed, System.Net.HttpStatusCode.BadRequest, "malformed_request");
        await AssertProblemAsync(unsupported, System.Net.HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
        await AssertProblemAsync(queryBinding, System.Net.HttpStatusCode.BadRequest, "malformed_request");
    }

    [Fact]
    public async Task EveryCookieAuthenticatedMutationRejectsMissingCsrf()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        using var update = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/inventory/lots/{lotId}") { Content = JsonContent.Create(new { storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null }) };
        update.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);
        using var adjustment = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{lotId}/adjustments") { Content = JsonContent.Create(new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "test", note = (string?)null }) };
        adjustment.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);
        adjustment.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/inventory/lots/{lotId}");
        delete.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");

        var responses = new[]
        {
            await client.SendAsync(update),
            await client.SendAsync(adjustment),
            await client.SendAsync(delete),
            await client.SendAsync(logout)
        };

        foreach (var response in responses)
        {
            await AssertProblemAsync(response, System.Net.HttpStatusCode.BadRequest, "validation_failed");
        }
    }

    [Theory]
    [InlineData("/inventory/lots")]
    [InlineData("https://attacker.example")]
    [InlineData("//attacker.example")]
    [InlineData("/%2f%2fattacker.example")]
    [InlineData("/%252f%252fattacker.example")]
    [InlineData("/safe%5cattacker.example")]
    [InlineData("/signin-oidc")]
    [InlineData("/bad%zz")]
    [InlineData("/safe/../api/v1/auth/login")]
    public async Task LoginEndpointNeverRedirectsDirectlyToUntrustedReturnUrl(string returnUrl)
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var response = await client.PostAsync($"/api/v1/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}", null);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.True(response.Headers.Location!.IsAbsoluteUri);
        Assert.DoesNotContain("attacker.example", response.Headers.Location.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionUnexpectedFailureReturnsRedactedProblemDetails()
    {
        const string privateConnectionValue = "private-connection-value";
        await using var factory = new KitchenFlowFactory($"Host=127.0.0.1;Port=1;Database=kitchenflow;Username=kitchenflow;Password={privateConnectionValue};Timeout=1;Command Timeout=1", authenticate: true, environment: "Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var readiness = await client.GetAsync("/health/ready");
        var response = await client.GetAsync("/api/v1/session");
        var body = await response.Content.ReadAsStringAsync();

        await AssertProblemAsync(readiness, System.Net.HttpStatusCode.ServiceUnavailable, "service_unavailable");
        await AssertProblemAsync(response, System.Net.HttpStatusCode.InternalServerError, "unexpected_error");
        Assert.DoesNotContain(privateConnectionValue, body, StringComparison.Ordinal);
        Assert.DoesNotContain("integration-user", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MutationRateLimitReturnsProblemDetails()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 61; attempt++)
        {
            response?.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = new StringContent("{", Encoding.UTF8, "application/json") };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            response = await client.SendAsync(request);
        }

        using (response)
        {
            await AssertProblemAsync(response!, System.Net.HttpStatusCode.TooManyRequests, "rate_limit_exceeded");
        }
    }

    [Fact]
    public async Task CreateReplayIsIdempotentAndProducesOneLot()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var key = Guid.NewGuid().ToString();

        var first = await CreateAsync(client, csrf, key);
        var second = await CreateAsync(client, csrf, key);
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots");

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(first.Headers.ETag!.Tag, second.Headers.ETag!.Tag);
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task CreateReplayCanonicalizesEquivalentWhitespaceAndDecimalScale()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var key = Guid.NewGuid().ToString();

        var first = await CreateAsync(client, csrf, key, "Canonical tomato", 100m);
        var second = await CreateAsync(client, csrf, key, "  Canonical tomato  ", 100.0m);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?search=Canonical%20tomato");

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task LotRepresentationUsesAnOpaqueVersionTokenThatMatchesItsEtag()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);

        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var version = body.GetProperty("version").GetString();

        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        Assert.False(long.TryParse(version, out _));
        Assert.Equal($"\"{version}\"", created.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task ConcurrentCreateWithSameIdempotencyKeyReplaysTheWinningResponse()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(CreateAsync(client, csrf, key, "Concurrent tomato"), CreateAsync(client, csrf, key, "Concurrent tomato"));
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?search=Concurrent%20tomato");

        Assert.All(responses, response => Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode));
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ConcurrentCreateWithSameKeyAndDifferentPayloadReturnsKeyReuseConflict()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(CreateAsync(client, csrf, key, "Concurrent tomato A"), CreateAsync(client, csrf, key, "Concurrent tomato B"));

        Assert.Contains(responses, response => response.StatusCode == System.Net.HttpStatusCode.Created);
        Assert.Contains(responses, response => response.StatusCode == System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateRequiresCurrentEtagAndRejectsStaleVersion()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var createdLot = await created.Content.ReadFromJsonAsync<JsonElement>();
        var lotId = createdLot.GetProperty("lotId").GetGuid();

        var missing = await UpdateAsync(client, csrf, lotId, null);
        var malformed = await UpdateAsync(client, csrf, lotId, "\"not-a-protected-version\"");
        var valid = await UpdateAsync(client, csrf, lotId, created.Headers.ETag!.Tag);
        var stale = await UpdateAsync(client, csrf, lotId, created.Headers.ETag!.Tag);

        Assert.Equal((System.Net.HttpStatusCode)428, missing.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, malformed.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task AdjustmentAndDeleteEnforceMissingMalformedStaleAndValidEtag()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var adjustedLot = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Adjustment ETag lot");
        var adjustedLotId = (await adjustedLot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var adjustmentMissing = await AdjustmentAsync(client, csrf, adjustedLotId, null, Guid.NewGuid().ToString(), new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "test", note = (string?)null });
        var adjustmentMalformed = await AdjustmentAsync(client, csrf, adjustedLotId, "\"malformed\"", Guid.NewGuid().ToString(), new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "test", note = (string?)null });
        var adjustmentValid = await AdjustAsync(client, csrf, adjustedLotId, adjustedLot.Headers.ETag!.Tag, Guid.NewGuid().ToString(), 1m);
        var adjustmentStale = await AdjustAsync(client, csrf, adjustedLotId, adjustedLot.Headers.ETag!.Tag, Guid.NewGuid().ToString(), 1m);

        var deletedLot = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Delete ETag lot");
        var deletedLotId = (await deletedLot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var deleteMissing = await DeleteAsync(client, csrf, deletedLotId, null);
        var deleteMalformed = await DeleteAsync(client, csrf, deletedLotId, "\"malformed\"");
        var corrected = await UpdateAsync(client, csrf, deletedLotId, deletedLot.Headers.ETag!.Tag);
        var deleteStale = await DeleteAsync(client, csrf, deletedLotId, deletedLot.Headers.ETag!.Tag);
        var deleteValid = await DeleteAsync(client, csrf, deletedLotId, corrected.Headers.ETag!.Tag);

        Assert.Equal((System.Net.HttpStatusCode)428, adjustmentMissing.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, adjustmentMalformed.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, adjustmentValid.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, adjustmentStale.StatusCode);
        Assert.Equal((System.Net.HttpStatusCode)428, deleteMissing.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, deleteMalformed.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, deleteStale.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, deleteValid.StatusCode);
    }

    [Fact]
    public async Task AdjustmentReplayDoesNotDuplicateHistory()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var etag = created.Headers.ETag!.Tag;
        var key = Guid.NewGuid().ToString();

        var first = await AdjustAsync(client, csrf, lotId, etag, key, 25m);
        var second = await AdjustAsync(client, csrf, lotId, etag, key, 25m);
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");

        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(first.Headers.ETag!.Tag, second.Headers.ETag!.Tag);
        Assert.Equal(2, history.GetArrayLength());
    }

    [Fact]
    public async Task ConcurrentAdjustmentWithSameKeyReplaysTheWinningResponse()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(AdjustAsync(client, csrf, lotId, created.Headers.ETag!.Tag, key, 25m), AdjustAsync(client, csrf, lotId, created.Headers.ETag!.Tag, key, 25m));
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");

        Assert.All(responses, response => Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(2, history.GetArrayLength());
    }

    [Fact]
    public async Task ConcurrentAdjustmentWithSameKeyAndDifferentPayloadReturnsReuseConflictWithoutDuplicateHistory()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(
            AdjustAsync(client, csrf, lotId, created.Headers.ETag!.Tag, key, 25m),
            AdjustAsync(client, csrf, lotId, created.Headers.ETag!.Tag, key, 20m));
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");

        Assert.Contains(responses, response => response.StatusCode == System.Net.HttpStatusCode.OK);
        var conflict = Assert.Single(responses, response => response.StatusCode == System.Net.HttpStatusCode.Conflict);
        Assert.Equal("idempotency_key_reused", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Equal(2, history.GetArrayLength());
    }

    [Fact]
    public async Task AdjustmentKeyReuseWithDifferentPayloadReturnsConflict()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var etag = created.Headers.ETag!.Tag;
        var key = Guid.NewGuid().ToString();

        await AdjustAsync(client, csrf, lotId, etag, key, 25m);
        var reused = await AdjustAsync(client, csrf, lotId, etag, key, 20m);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, reused.StatusCode);
    }

    [Fact]
    public async Task DeleteSoftDeletesLotAndRetainsHistory()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var deleted = await DeleteAsync(client, csrf, lotId, created.Headers.ETag!.Tag);
        var get = await client.GetAsync($"/api/v1/inventory/lots/{lotId}");
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal(100m, history[0].GetProperty("previousQuantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(100m, history[0].GetProperty("resultingQuantity").GetProperty("measuredValue").GetDecimal());
    }

    [Fact]
    public async Task VersionEtagIsBoundToItsInventoryLot()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var first = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "First version-bound lot");
        var second = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Second version-bound lot");
        var secondLotId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();

        var rejected = await UpdateAsync(client, csrf, secondLotId, first.Headers.ETag!.Tag);

        Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        Assert.Equal("precondition_failed", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task MetadataCorrectionRemainsVisibleAfterDeletionAndHistoryIsDeterministicallyOrdered()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "History ordering lot");
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var corrected = await UpdateAsync(client, csrf, lotId, created.Headers.ETag!.Tag);
        var deleted = await DeleteAsync(client, csrf, lotId, corrected.Headers.ETag!.Tag);

        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");
        var entries = history.EnumerateArray().ToArray();

        Assert.Equal(System.Net.HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(3, entries.Length);
        Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "MetadataCorrection");
        for (var index = 1; index < entries.Length; index++)
        {
            var previousTime = entries[index - 1].GetProperty("occurredAt").GetDateTimeOffset();
            var currentTime = entries[index].GetProperty("occurredAt").GetDateTimeOffset();
            Assert.True(previousTime >= currentTime);
            if (previousTime == currentTime)
            {
                Assert.True(entries[index - 1].GetProperty("entryId").GetGuid().CompareTo(entries[index].GetProperty("entryId").GetGuid()) > 0);
            }
        }
    }

    [Fact]
    public async Task HistoryIsOwnerScopedAfterSoftDeletion()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        other.DefaultRequestHeaders.Add("X-Test-Subject", "history-user-b");
        var ownerCsrf = await GetCsrfAsync(owner);
        await GetCsrfAsync(other);
        var created = await CreateAsync(owner, ownerCsrf, Guid.NewGuid().ToString(), "Private history lot");
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        await DeleteAsync(owner, ownerCsrf, lotId, created.Headers.ETag!.Tag);

        var response = await other.GetAsync($"/api/v1/inventory/lots/{lotId}/history");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListUsesTamperEvidentCursorAndPreservesFilters()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var search = $"cursor-probe-{Guid.NewGuid():N}";

        await CreateAsync(client, csrf, Guid.NewGuid().ToString(), $"{search}-one");
        await CreateAsync(client, csrf, Guid.NewGuid().ToString(), $"{search}-two");
        var first = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots?pageSize=1&storageLocation=Pantry&search={search}");
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots?pageSize=1&storageLocation=Pantry&search={search}&cursor={Uri.EscapeDataString(cursor!)}");
        var tampered = await client.GetAsync($"/api/v1/inventory/lots?cursor={Uri.EscapeDataString(cursor!)}x");
        var problem = await tampered.Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotNull(cursor);
        Assert.Equal(1, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.NotEqual(first.GetProperty("items")[0].GetProperty("lotId").GetGuid(), second.GetProperty("items")[0].GetProperty("lotId").GetGuid());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, tampered.StatusCode);
        Assert.Equal("invalid_cursor", problem.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task CreateRejectsNoncanonicalMeasuredQuantity()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = JsonContent.Create(new { productName = "Invalid unit", quantity = new { measuredValue = 1.2345m, unit = "Pound", availabilityState = (string?)null }, storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((System.Net.HttpStatusCode)422, response.StatusCode);
        Assert.Equal("domain_rule_violated", problem.GetProperty("errorCode").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("quantity", out _));
    }

    [Fact]
    public async Task UpdateCanCorrectProductDisplayName()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/inventory/lots/{lotId}") { Content = JsonContent.Create(new { productName = "Corrected tomato", storageLocation = "Other", customLocation = "Cellar shelf", packageState = "Unknown", printedExpirationDate = (DateOnly?)null, notes = " trimmed note " }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);

        var updated = await client.SendAsync(request);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Corrected tomato", body.GetProperty("productName").GetString());
        Assert.Equal("Other", body.GetProperty("storageLocation").GetString());
        Assert.Equal("Cellar shelf", body.GetProperty("customLocation").GetString());
        Assert.Equal("trimmed note", body.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task MetadataCorrectionAppearsAsSafeHistoryAuditProjection()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        using var correction = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/inventory/lots/{lotId}") { Content = JsonContent.Create(new { storageLocation = "Refrigerator", customLocation = (string?)null, packageState = "Opened", printedExpirationDate = (DateOnly?)null, notes = "private correction note" }) };
        correction.Headers.Add("X-CSRF-TOKEN", csrf);
        correction.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);

        var updated = await client.SendAsync(correction);
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");
        var projection = history.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "MetadataCorrection");

        Assert.Equal(System.Net.HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(JsonValueKind.Null, projection.GetProperty("type").ValueKind);
        var changedFields = projection.GetProperty("changedFields").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(["notes", "packageState", "storageLocation"], changedFields.OrderBy(item => item, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("private correction note", projection.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdenticalMetadataCorrectionDoesNotAdvanceVersionOrCreateHistory()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/inventory/lots/{lotId}") { Content = JsonContent.Create(new { storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);
        var update = await client.SendAsync(request);
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");

        Assert.Equal(System.Net.HttpStatusCode.OK, update.StatusCode);
        Assert.NotNull(update.Headers.ETag);
        Assert.Single(history.EnumerateArray());
    }

    [Fact]
    public async Task AuthenticatedUserCannotMutateAnotherUsersLot()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        other.DefaultRequestHeaders.Add("X-Test-Subject", "integration-user-b");
        var ownerCsrf = await GetCsrfAsync(owner);
        var otherCsrf = await GetCsrfAsync(other);
        var created = await CreateAsync(owner, ownerCsrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();

        var update = await UpdateAsync(other, otherCsrf, lotId, created.Headers.ETag!.Tag);
        var adjustment = await AdjustAsync(other, otherCsrf, lotId, created.Headers.ETag!.Tag, Guid.NewGuid().ToString(), 1m);
        var delete = await DeleteAsync(other, otherCsrf, lotId, created.Headers.ETag!.Tag);
        var history = await other.GetAsync($"/api/v1/inventory/lots/{lotId}/history");
        var list = await other.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, adjustment.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, history.StatusCode);
        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    /// PLAN-0018 #26: foreign/nonexistent lot mutations must not surface 412/428 from If-Match evaluation.
    /// </summary>
    [Fact]
    public async Task ForeignAndNonexistentLotMutationsAreNondisclosingForPreconditionVariants()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        other.DefaultRequestHeaders.Add("X-Test-Subject", "plan0018-user-b");
        var ownerCsrf = await GetCsrfAsync(owner);
        var otherCsrf = await GetCsrfAsync(other);
        var created = await CreateAsync(owner, ownerCsrf, Guid.NewGuid().ToString(), "PLAN-0018 isolation lot");
        var foreignLotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        var ownerEtag = created.Headers.ETag!.Tag;
        var missingLotId = Guid.NewGuid();

        async Task<(System.Net.HttpStatusCode Status, string ErrorCode, string? Detail)> CaptureProblemAsync(HttpResponseMessage response)
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (response.StatusCode, problem.GetProperty("errorCode").GetString()!, problem.GetProperty("detail").GetString());
        }

        async Task<List<(string Name, System.Net.HttpStatusCode Status, string ErrorCode, string? Detail)>> ProbeAsync(Guid lotId)
        {
            var detail = await other.GetAsync($"/api/v1/inventory/lots/{lotId}");
            var fabricated = await AdjustmentAsync(other, otherCsrf, lotId, "\"v1\"", Guid.NewGuid().ToString(), new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null });
            var missingIfMatch = await AdjustmentAsync(other, otherCsrf, lotId, null, Guid.NewGuid().ToString(), new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null });
            var ownerEtagAdjust = await AdjustmentAsync(other, otherCsrf, lotId, ownerEtag, Guid.NewGuid().ToString(), new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null });
            var availability = await AdjustmentAsync(other, otherCsrf, lotId, "\"v1\"", Guid.NewGuid().ToString(), new { type = "AvailabilityChanged", value = (decimal?)null, availabilityState = "Low", reasonCode = "checked", note = (string?)null });
            var updateMissing = await UpdateAsync(other, otherCsrf, lotId, null);
            var updateFabricated = await UpdateAsync(other, otherCsrf, lotId, "\"v1\"");
            var deleteMissing = await DeleteAsync(other, otherCsrf, lotId, null);
            var deleteFabricated = await DeleteAsync(other, otherCsrf, lotId, "\"v1\"");
            var history = await other.GetAsync($"/api/v1/inventory/lots/{lotId}/history");
            var invalidCsrf = await AdjustmentAsync(other, "not-a-csrf-token", lotId, "\"v1\"", Guid.NewGuid().ToString(), new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null });

            var results = new List<(string, System.Net.HttpStatusCode, string, string?)>();
            async Task AddAsync(string name, HttpResponseMessage response)
            {
                var captured = await CaptureProblemAsync(response);
                results.Add((name, captured.Status, captured.ErrorCode, captured.Detail));
            }

            await AddAsync("detail", detail);
            await AddAsync("adjust_fabricated", fabricated);
            await AddAsync("adjust_missing_if_match", missingIfMatch);
            await AddAsync("adjust_owner_etag", ownerEtagAdjust);
            await AddAsync("adjust_availability", availability);
            await AddAsync("update_missing_if_match", updateMissing);
            await AddAsync("update_fabricated", updateFabricated);
            await AddAsync("delete_missing_if_match", deleteMissing);
            await AddAsync("delete_fabricated", deleteFabricated);
            await AddAsync("history", history);

            // CSRF failure is request-auth, not resource disclosure — still must not return 412 for foreign lots.
            Assert.NotEqual(System.Net.HttpStatusCode.PreconditionFailed, invalidCsrf.StatusCode);
            Assert.NotEqual(System.Net.HttpStatusCode.PreconditionRequired, invalidCsrf.StatusCode);
            Assert.True(
                invalidCsrf.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized,
                $"Unexpected CSRF status for foreign/missing lot: {(int)invalidCsrf.StatusCode}");

            foreach (var item in results)
            {
                Assert.Equal(System.Net.HttpStatusCode.NotFound, item.Item2);
                Assert.Equal("resource_not_found", item.Item3);
            }

            return results;
        }

        var foreignProbe = await ProbeAsync(foreignLotId);
        var missingProbe = await ProbeAsync(missingLotId);
        Assert.Equal(foreignProbe.Count, missingProbe.Count);
        for (var i = 0; i < foreignProbe.Count; i++)
        {
            Assert.Equal(foreignProbe[i].Name, missingProbe[i].Name);
            Assert.Equal(foreignProbe[i].Status, missingProbe[i].Status);
            Assert.Equal(foreignProbe[i].ErrorCode, missingProbe[i].ErrorCode);
            Assert.Equal(foreignProbe[i].Detail, missingProbe[i].Detail);
        }

        // Owner concurrency semantics remain intact after the isolation ordering fix.
        var ownerMissing = await AdjustmentAsync(owner, ownerCsrf, foreignLotId, null, Guid.NewGuid().ToString(), new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null });
        var ownerStale = await AdjustmentAsync(owner, ownerCsrf, foreignLotId, "\"v1\"", Guid.NewGuid().ToString(), new { type = "Consume", value = 1m, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null });
        var ownerOk = await AdjustAsync(owner, ownerCsrf, foreignLotId, ownerEtag, Guid.NewGuid().ToString(), 1m);
        await AssertProblemAsync(ownerMissing, System.Net.HttpStatusCode.PreconditionRequired, "precondition_required");
        await AssertProblemAsync(ownerStale, System.Net.HttpStatusCode.PreconditionFailed, "precondition_failed");
        Assert.Equal(System.Net.HttpStatusCode.OK, ownerOk.StatusCode);
    }

    [Fact]
    public async Task ListSeparatesActiveDepletedAndDeletedLotsAcrossFilters()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var active = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Active pantry lot");
        var depleted = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Depleted pantry lot", 1m);
        var depletedId = (await depleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        await AdjustAsync(client, csrf, depletedId, depleted.Headers.ETag!.Tag, Guid.NewGuid().ToString(), 1m);
        var deleted = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Deleted pantry lot");
        var deletedId = (await deleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
        await DeleteAsync(client, csrf, deletedId, deleted.Headers.ETag!.Tag);

        var defaultList = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots");
        var activeList = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?status=active&storageLocation=Pantry&search=Active");
        var depletedList = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?status=depleted&storageLocation=Pantry");
        var deletedList = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?status=deleted&storageLocation=Pantry");

        Assert.Single(defaultList.GetProperty("items").EnumerateArray());
        Assert.Equal((await active.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid(), activeList.GetProperty("items")[0].GetProperty("lotId").GetGuid());
        Assert.Single(depletedList.GetProperty("items").EnumerateArray());
        Assert.Equal(depletedId, depletedList.GetProperty("items")[0].GetProperty("lotId").GetGuid());
        Assert.Single(deletedList.GetProperty("items").EnumerateArray());
        Assert.Equal(deletedId, deletedList.GetProperty("items")[0].GetProperty("lotId").GetGuid());
    }

    [Fact]
    public async Task MaximumCanonicalDecimalRoundTripsWithoutPrecisionLoss()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);

        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Maximum decimal lot", 999_999_999_999_999.999m);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var read = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{body.GetProperty("lotId").GetGuid()}");

        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(999_999_999_999_999.999m, body.GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(999_999_999_999_999.999m, read.GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
    }

    [Fact]
    public async Task LogoutSucceedsOnlyWithIssuedCsrfToken()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task ConsumeCannotReduceMeasuredLotBelowZero()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();

        var rejected = await AdjustAsync(client, csrf, lotId, created.Headers.ETag!.Tag, Guid.NewGuid().ToString(), 101m);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");

        Assert.Equal((System.Net.HttpStatusCode)422, rejected.StatusCode);
        Assert.Equal("domain_rule_violated", problem.GetProperty("errorCode").GetString());
        Assert.Equal("The adjustment cannot exceed the current measured quantity.", problem.GetProperty("detail").GetString());
        Assert.Equal(1, history.GetArrayLength());
    }

    [Fact]
    public async Task AdjustmentValidationReturnsFieldErrorsAndTraceIdentifier()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();

        var rejected = await AdjustmentAsync(client, csrf, lotId, created.Headers.ETag!.Tag, Guid.NewGuid().ToString(), new { type = "Consume", value = 0m, availabilityState = "Low", reasonCode = "", note = (string?)null });
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((System.Net.HttpStatusCode)422, rejected.StatusCode);
        Assert.Equal("domain_rule_violated", problem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.True(problem.GetProperty("errors").TryGetProperty("value", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("availabilityState", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("reasonCode", out _));
    }

    [Fact]
    public async Task CorrectRecordsPreviousAndResultingMeasuredQuantity()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAsync(client, csrf, Guid.NewGuid().ToString());
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();

        var corrected = await AdjustmentAsync(client, csrf, lotId, created.Headers.ETag!.Tag, Guid.NewGuid().ToString(), new { type = "Correct", value = 75m, availabilityState = (string?)null, reasonCode = "counted", note = (string?)null });
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");

        Assert.Equal(System.Net.HttpStatusCode.OK, corrected.StatusCode);
        Assert.Equal(75m, (await corrected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal("Correct", history[0].GetProperty("type").GetString());
        Assert.Equal(100m, history[0].GetProperty("previousQuantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(75m, history[0].GetProperty("resultingQuantity").GetProperty("measuredValue").GetDecimal());
    }

    [Fact]
    public async Task AvailabilityChangeRequiresAndRecordsReason()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        var created = await CreateAvailabilityAsync(client, csrf, Guid.NewGuid().ToString(), "Low");
        var lotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();

        var changed = await AdjustmentAsync(client, csrf, lotId, created.Headers.ETag!.Tag, Guid.NewGuid().ToString(), new { type = "AvailabilityChanged", value = (decimal?)null, availabilityState = "Available", reasonCode = "checked", note = (string?)null });
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{lotId}/history");

        Assert.Equal(System.Net.HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("Available", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quantity").GetProperty("availabilityState").GetString());
        Assert.Equal("AvailabilityChanged", history[0].GetProperty("type").GetString());
        Assert.Equal("checked", history[0].GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task ProfilePatchCreatesProgressiveProfileAndSessionProjectionStaysSafe()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile")
        {
            Content = JsonContent.Create(new
            {
                displayName = new { action = "confirm", value = "Alex", durability = "durable" },
                language = new { action = "confirm", value = "pt-BR", durability = "durable" },
                timeZone = new { action = "confirm", value = "America/Sao_Paulo", durability = "durable" },
                adultDeclaration = new { adultDeclared = true, termsVersion = "2026-07-31", privacyVersion = "2026-07-31" }
            })
        };
        patch.Headers.Add("X-CSRF-TOKEN", csrf);
        var created = await client.SendAsync(patch);
        var profile = await created.Content.ReadFromJsonAsync<JsonElement>();
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");

        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Alex", profile.GetProperty("displayName").GetProperty("value").GetString());
        Assert.Equal("Alex", session.GetProperty("displayName").GetString());
        Assert.True(session.GetProperty("profileExists").GetBoolean());
        Assert.Equal("Declared", session.GetProperty("adultDeclarationState").GetString());
        Assert.False(session.TryGetProperty("preferences", out _));
        Assert.False(session.TryGetProperty("allergies", out _));
    }

    [Fact]
    public async Task ProfileUpdateRequiresIfMatchAfterCreate()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile") { Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "First", durability = "durable" } }) };
        patch.Headers.Add("X-CSRF-TOKEN", csrf);
        var created = await client.SendAsync(patch);
        var etag = created.Headers.ETag!.Tag;
        using var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile") { Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Second", durability = "durable" } }) };
        stale.Headers.Add("X-CSRF-TOKEN", csrf);
        stale.Headers.TryAddWithoutValidation("If-Match", "\"invalid\"");
        var conflict = await client.SendAsync(stale);
        using var valid = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile") { Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Second", durability = "durable" } }) };
        valid.Headers.Add("X-CSRF-TOKEN", csrf);
        valid.Headers.TryAddWithoutValidation("If-Match", etag);
        var updated = await client.SendAsync(valid);

        await AssertProblemAsync(conflict, System.Net.HttpStatusCode.PreconditionFailed, "precondition_failed");
        Assert.Equal(System.Net.HttpStatusCode.OK, updated.StatusCode);
    }

    [Fact]
    public async Task ExplicitPreferenceCommandsPersistAllergyCodes()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var bootstrap = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile") { Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } }) };
        bootstrap.Headers.Add("X-CSRF-TOKEN", csrf);
        var created = await client.SendAsync(bootstrap);
        var etag = created.Headers.ETag!.Tag;
        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = new[] { new { action = "add", category = "Allergy", stableCode = "peanut_allergy", note = (string?)null } } })
        };
        put.Headers.Add("X-CSRF-TOKEN", csrf);
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await client.SendAsync(put);
        var preferences = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Allergy", preferences.GetProperty("entries")[0].GetProperty("category").GetString());
        Assert.Equal("peanut_allergy", preferences.GetProperty("entries")[0].GetProperty("stableCode").GetString());
    }

    [Fact]
    public async Task PreparationCreatesOwnedLotsConsumesParentAndReplaysTheAuthoritativeResponse()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var parentCreate = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), "Dry beans", 100m);
        var parentBody = await parentCreate.Content.ReadFromJsonAsync<JsonElement>();
        var parentLotId = parentBody.GetProperty("lotId").GetGuid();
        var parentVersion = parentCreate.Headers.ETag!.Tag;
        var key = Guid.NewGuid().ToString();
        var requestBody = new
        {
            outputProduct = new { productId = (Guid?)null, productName = "Cooked beans" },
            declaredYield = new { measuredValue = 60m, unit = "Gram", availabilityState = (string?)null },
            inputs = new[] { new { lotId = parentLotId, quantity = new { measuredValue = 60m, unit = "Gram", availabilityState = (string?)null }, version = parentVersion } },
            outputs = new[] { new { quantity = new { measuredValue = 60m, unit = "Gram", availabilityState = (string?)null }, storageLocation = "Refrigerator", customLocation = (string?)null, packageState = "Opened", shelfLifeEvidence = new { date = (DateOnly?)null, source = "Unknown", confidence = "Unknown", conditions = (string?)null } } },
            preparedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/preparations") { Content = JsonContent.Create(requestBody) };
        firstRequest.Headers.Add("Idempotency-Key", key);
        firstRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        using var first = await client.SendAsync(firstRequest);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = firstBody.GetProperty("batchId").GetGuid();
        var outputLot = firstBody.GetProperty("outputs")[0].GetProperty("lot");
        var outputLotId = outputLot.GetProperty("lotId").GetGuid();
        var outputVersion = outputLot.GetProperty("version").GetString()!;
        using var consumeOutput = await AdjustAsync(client, csrf, outputLotId, outputVersion, Guid.NewGuid().ToString(), 20m);
        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/preparations") { Content = JsonContent.Create(requestBody) };
        replayRequest.Headers.Add("Idempotency-Key", key);
        replayRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        using var replay = await client.SendAsync(replayRequest);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        var parentAfter = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parentLotId}");
        var provenance = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parentLotId}/provenance");
        var batch = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/preparations/{batchId}");

        Assert.Equal(System.Net.HttpStatusCode.Created, parentCreate.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, consumeOutput.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(batchId, replayBody.GetProperty("batchId").GetGuid());
        Assert.Equal(60m, firstBody.GetProperty("declaredYield").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(60m, replayBody.GetProperty("declaredYield").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(60m, firstBody.GetProperty("outputs")[0].GetProperty("lot").GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(40m, parentAfter.GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(batchId, provenance.GetProperty("consumedBy")[0].GetProperty("batchId").GetGuid());
        Assert.Equal(60m, provenance.GetProperty("consumedBy")[0].GetProperty("declaredYield").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(batchId, batch.GetProperty("batchId").GetGuid());
        Assert.Equal(60m, batch.GetProperty("declaredYield").GetProperty("measuredValue").GetDecimal());
    }

    [Fact]
    public async Task ConcurrentSameKeyPreparationReplaysTheWinnerForFiftySynchronizedIterations()
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batchCountBefore = await database.PreparationBatches.CountAsync();
        var inputTransactionCountBefore = await database.Transactions.CountAsync(item => item.Type == "PreparationInputConsumed");

        for (var group = 0; group < 5; group++)
        {
            // The real rate limiter remains enabled. Isolated test hosts keep the 50 synchronized
            // duplicate-delivery iterations below the production fixed-window mutation bound.
            await using var iterationFactory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
            await iterationFactory.EnsureDatabaseAsync();
            for (var offset = 0; offset < 10; offset++)
            {
                var iteration = group * 10 + offset;
                using var client = iterationFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
                var csrf = await GetCsrfAsync(client);
                using var parentCreate = await CreateAsync(client, csrf, Guid.NewGuid().ToString(), $"Concurrent preparation parent {iteration}", 10m);
                Assert.Equal(System.Net.HttpStatusCode.Created, parentCreate.StatusCode);
                var parent = await parentCreate.Content.ReadFromJsonAsync<JsonElement>();
                var parentLotId = parent.GetProperty("lotId").GetGuid();
                var parentVersion = parentCreate.Headers.ETag!.Tag;
                var key = Guid.NewGuid().ToString();
                var body = new
                {
                    outputProduct = new { productId = (Guid?)null, productName = $"Concurrent prepared output {iteration}" },
                    declaredYield = new { measuredValue = 3m, unit = "Gram", availabilityState = (string?)null },
                    inputs = new[] { new { lotId = parentLotId, quantity = new { measuredValue = 3m, unit = "Gram", availabilityState = (string?)null }, version = parentVersion } },
                    outputs = new[] { new { quantity = new { measuredValue = 3m, unit = "Gram", availabilityState = (string?)null }, storageLocation = "Refrigerator", customLocation = (string?)null, packageState = "Opened", shelfLifeEvidence = new { date = (DateOnly?)null, source = "Unknown", confidence = "Unknown", conditions = (string?)null } } },
                    preparedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                };
                var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await SendPreparationAsync(client, csrf, key, body);
                });
                var second = Task.Run(async () =>
                {
                    await barrier.Task;
                    return await SendPreparationAsync(client, csrf, key, body);
                });
                barrier.SetResult();
                var responses = await Task.WhenAll(first, second);
                try
                {
                    Assert.All(responses, response => Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode));
                    var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<JsonElement>()));
                    Assert.Single(bodies.Select(item => item.GetProperty("batchId").GetGuid()).Distinct());
                }
                finally
                {
                    foreach (var response in responses)
                    {
                        response.Dispose();
                    }
                }

                var parentAfter = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{parentLotId}");
                Assert.Equal(7m, parentAfter.GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
            }
        }

        Assert.Equal(batchCountBefore + 50, await database.PreparationBatches.CountAsync());
        Assert.Equal(inputTransactionCountBefore + 50, await database.Transactions.CountAsync(item => item.Type == "PreparationInputConsumed"));
    }

    [Theory]
    [InlineData("{\"outputProduct\":{\"productName\":\"Prepared\"},\"declaredYield\":{\"measuredValue\":1,\"unit\":\"Gram\"},\"inputs\":[null],\"outputs\":[]}", "inputs")]
    [InlineData("{\"outputProduct\":{\"productName\":\"Prepared\"},\"declaredYield\":{\"measuredValue\":1,\"unit\":\"Gram\"},\"inputs\":[],\"outputs\":[null]}", "outputs")]
    [InlineData("{\"outputProduct\":{\"productName\":\"Prepared\"},\"declaredYield\":{\"measuredValue\":1,\"unit\":\"Gram\"},\"inputs\":null,\"outputs\":[]}", "inputs")]
    [InlineData("{\"outputProduct\":{\"productName\":\"Prepared\"},\"declaredYield\":{\"measuredValue\":1,\"unit\":\"Gram\"},\"inputs\":[],\"outputs\":null}", "outputs")]
    public async Task PreparationNullCollectionsOrItemsReturnValidationProblem(string body, string field)
    {
        await using var factory = new KitchenFlowFactory(_postgres.GetConnectionString(), authenticate: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/preparations") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.GetProperty("errorCode").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    private static object CreateLot(string productName = "Test tomato", decimal measuredValue = 100m) => new { productName, quantity = new { measuredValue, unit = "Gram", availabilityState = (string?)null }, storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null };

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

    private static async Task<HttpResponseMessage> SendPreparationAsync(HttpClient client, string csrf, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/preparations") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CreateAvailabilityAsync(HttpClient client, string csrf, string key, string availabilityState)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/lots") { Content = JsonContent.Create(new { productName = "Availability tomato", quantity = new { measuredValue = (decimal?)null, unit = (string?)null, availabilityState }, storageLocation = "Pantry", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null }) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UpdateAsync(HttpClient client, string csrf, Guid lotId, string? etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/inventory/lots/{lotId}") { Content = JsonContent.Create(new { storageLocation = "Refrigerator", customLocation = (string?)null, packageState = (string?)null, printedExpirationDate = (DateOnly?)null, notes = (string?)null }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> AdjustAsync(HttpClient client, string csrf, Guid lotId, string etag, string key, decimal value)
    {
        return await AdjustmentAsync(client, csrf, lotId, etag, key, new { type = "Consume", value, availabilityState = (string?)null, reasonCode = "meal", note = (string?)null });
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

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string csrf, Guid lotId, string? etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/inventory/lots/{lotId}");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }
        return await client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, System.Net.HttpStatusCode expectedStatus, string expectedCode)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedCode, problem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private sealed class KitchenFlowFactory(string connectionString, bool authenticate, string environment = "Development") : WebApplicationFactory<Program>
    {
        private readonly string keyRingPath = Path.Combine(Path.GetTempPath(), $"kitchenflow-integration-keys-{Guid.NewGuid():N}");

        public async Task EnsureDatabaseAsync()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        }

        public async Task<Guid> SeedOtherUsersLotAsync()
        {
            using var scope = Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ownerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var lotId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            database.Users.Add(new InternalUser(ownerId, "https://integration.test", $"seeded-{ownerId:N}", now));
            database.Products.Add(new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Private tomato", NormalizedSearchName = "PRIVATE TOMATO", CreatedAt = now, UpdatedAt = now });
            database.Lots.Add(new LotRecord { Id = lotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 100m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now });
            await database.SaveChangesAsync();
            return lotId;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:KitchenFlow", connectionString);
            if (!string.Equals(environment, "Development", StringComparison.Ordinal))
            {
                builder.UseSetting("KITCHENFLOW_OIDC_AUTHORITY", "https://identity.integration.test/realms/kitchenflow");
                builder.UseSetting("KITCHENFLOW_OIDC_CLIENT_ID", "kitchenflow-backend");
                builder.UseSetting("KITCHENFLOW_OIDC_CLIENT_SECRET", "valid-secret-value");
                builder.UseSetting("KITCHENFLOW_SESSION_KEYRING_PATH", keyRingPath);
            }
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
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "integration-user";
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim("iss", "https://integration.test")], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
