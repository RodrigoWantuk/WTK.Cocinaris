using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0042 Phase 1: cook a recipe and reconcile the pantry through the HTTP boundary against PostgreSQL.</summary>
public sealed class RecipeExecutionApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.4")
        .WithDatabase("cocinaris_recipe_execution")
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
    public async Task CookingConsumesConfirmedLotsAndStoresLeftoversAtomically()
    {
        using var client = Client("cook-user");
        var csrf = await CsrfAsync(client);
        var chicken = await CreateLotAsync(client, csrf, "Chicken", 1000m);
        var rice = await CreateLotAsync(client, csrf, "Rice", 300m);
        var recipeId = await SeedRecipeAsync("cook-user", servings: 2, chicken, rice);

        var started = await PostAsync(client, csrf, $"/api/v1/recipes/{recipeId}/executions", new { servings = 4 });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var execution = await started.Content.ReadFromJsonAsync<JsonElement>();
        var executionId = execution.GetProperty("executionId").GetGuid();
        var again = await PostAsync(client, csrf, $"/api/v1/recipes/{recipeId}/executions", new { servings = 4 });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(executionId, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("executionId").GetGuid());

        // Four servings of a two-serving recipe: 600 g chicken (ready) and 400 g rice from a 300 g lot (insufficient).
        var lines = execution.GetProperty("proposal").GetProperty("lines").EnumerateArray().ToDictionary(line => line.GetProperty("ingredientId").GetString()!);
        Assert.Equal(("ready", 600m), (lines["ing-chicken"].GetProperty("status").GetString(), lines["ing-chicken"].GetProperty("proposedQuantity").GetDecimal()));
        Assert.Equal(("insufficient", 300m), (lines["ing-rice"].GetProperty("status").GetString(), lines["ing-rice"].GetProperty("proposedQuantity").GetDecimal()));
        Assert.Equal("not_tracked", lines["ing-salt"].GetProperty("status").GetString());
        var versions = execution.GetProperty("proposal").GetProperty("pantryLots").EnumerateArray().ToDictionary(lot => lot.GetProperty("lotId").GetGuid(), lot => lot.GetProperty("version").GetGuid());

        var key = Guid.NewGuid();
        var body = new
        {
            consumptions = new[]
            {
                new { lotId = chicken, quantity = 600m, unit = "Gram", version = versions[chicken] },
                new { lotId = rice, quantity = 300m, unit = "Gram", version = versions[rice] }
            },
            leftover = new { portions = 2, storageLocation = "Refrigerator", useBy = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3) },
            rating = 5,
            notes = "Less salt next time"
        };
        var completed = await PostAsync(client, csrf, $"/api/v1/recipes/executions/{executionId}/completion", body, key);
        var result = await completed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("Completed", result.GetProperty("status").GetString());
        var batchId = result.GetProperty("inventoryBatchId").GetGuid();

        var chickenLot = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{chicken}");
        Assert.Equal(400m, chickenLot.GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        var batch = await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/preparations/{batchId}");
        Assert.Equal("RecipeExecution", batch.GetProperty("sourceType").GetString());
        var leftover = batch.GetProperty("outputs").EnumerateArray().Single().GetProperty("lot");
        Assert.Equal(("Chicken and rice", 2m, "Refrigerator"), (leftover.GetProperty("productName").GetString(), leftover.GetProperty("quantity").GetProperty("measuredValue").GetDecimal(), leftover.GetProperty("storageLocation").GetString()));

        // A retry with the same key replays; another key is refused; nothing is consumed twice.
        var replay = await PostAsync(client, csrf, $"/api/v1/recipes/executions/{executionId}/completion", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(batchId, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inventoryBatchId").GetGuid());
        var second = await PostAsync(client, csrf, $"/api/v1/recipes/executions/{executionId}/completion", body, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal(400m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{chicken}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/recipes/executions/in-progress")).EnumerateArray());

        // Another user cannot see the execution.
        using var other = Client("other-cook");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/recipes/executions/{executionId}")).StatusCode);
    }

    [Fact]
    public async Task AStaleLotVersionRollsBackTheWholeCompletion()
    {
        using var client = Client("stale-cook");
        var csrf = await CsrfAsync(client);
        var chicken = await CreateLotAsync(client, csrf, "Chicken", 1000m);
        var rice = await CreateLotAsync(client, csrf, "Rice", 300m);
        var recipeId = await SeedRecipeAsync("stale-cook", servings: 2, chicken, rice);
        var execution = await (await PostAsync(client, csrf, $"/api/v1/recipes/{recipeId}/executions", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var executionId = execution.GetProperty("executionId").GetGuid();
        var chickenVersion = execution.GetProperty("proposal").GetProperty("pantryLots").EnumerateArray().Single(lot => lot.GetProperty("lotId").GetGuid() == chicken).GetProperty("version").GetGuid();

        var response = await PostAsync(client, csrf, $"/api/v1/recipes/executions/{executionId}/completion", new
        {
            consumptions = new[]
            {
                new { lotId = chicken, quantity = 300m, unit = "Gram", version = chickenVersion },
                new { lotId = rice, quantity = 200m, unit = "Gram", version = Guid.NewGuid() }
            }
        }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(1000m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{chicken}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal("InProgress", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/executions/{executionId}")).GetProperty("status").GetString());
    }

    private async Task<Guid> SeedRecipeAsync(string subject, int servings, Guid chicken, Guid rice)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = await database.Users.Where(user => user.Subject == subject).Select(user => user.Id).SingleAsync();
        var recipeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var content = JsonSerializer.Serialize(new
        {
            recipe = new
            {
                ingredients = new object[]
                {
                    new { ingredientId = "ing-chicken", sourceType = "inventory", displayName = "Chicken", inventoryItemId = chicken.ToString("n"), requiredQuantity = 300, unit = "g", optional = false },
                    new { ingredientId = "ing-rice", sourceType = "inventory", displayName = "Rice", inventoryItemId = rice.ToString("n"), requiredQuantity = 200, unit = "g", optional = false },
                    new { ingredientId = "ing-salt", sourceType = "assumption", displayName = "Salt", requiredQuantity = 5, unit = "g", optional = false }
                }
            }
        });
        database.Recipes.Add(new RecipeRecord { Id = recipeId, OwnerUserId = owner, CurrentRevisionNumber = 1, CreatedAt = now, UpdatedAt = now });
        database.RecipeRevisions.Add(new RecipeRevisionRecord
        {
            Id = Guid.NewGuid(), RecipeId = recipeId, OwnerUserId = owner, RevisionNumber = 1, Name = "Chicken and rice", MealTypesJson = "[\"lunch\"]", Servings = servings,
            NormalizedRecipeJson = content, ThumbnailVisualJson = "{}", SourceCandidateId = "cand-1", SourceGenerationSessionId = Guid.NewGuid(), CreatedAt = now
        });
        await database.SaveChangesAsync();
        return recipeId;
    }

    private HttpClient Client(string subject)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        return client;
    }

    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("csrfToken").GetString()!;

    private static async Task<Guid> CreateLotAsync(HttpClient client, string csrf, string name, decimal grams)
    {
        var response = await PostAsync(client, csrf, "/api/v1/inventory/lots", new { productName = name, quantity = new { measuredValue = grams, unit = "Gram" }, storageLocation = "Refrigerator" }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotId").GetGuid();
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string csrf, string path, object body, Guid? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (key is { } value)
        {
            request.Headers.Add("Idempotency-Key", value.ToString());
        }

        return await client.SendAsync(request);
    }
}
