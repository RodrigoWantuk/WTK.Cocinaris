using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0042 Phase 2: ingredient adjustments recorded while cooking.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task AdjustmentsMadeWhileCookingDriveTheProposalAndAreKeptWithTheSession()
    {
        using var client = Client("adjusting-cook");
        var csrf = await CsrfAsync(client);
        var chicken = await CreateLotAsync(client, csrf, "Chicken", 1000m);
        var rice = await CreateLotAsync(client, csrf, "Rice", 300m);
        var tofu = await CreateLotAsync(client, csrf, "Tofu", 400m);
        var recipeId = await SeedRecipeAsync("adjusting-cook", servings: 2, chicken, rice);
        var execution = await (await PostAsync(client, csrf, $"/api/v1/recipes/{recipeId}/executions", new { servings = 2 })).Content.ReadFromJsonAsync<JsonElement>();
        var executionId = execution.GetProperty("executionId").GetGuid();

        // Tofu instead of chicken (250 g this time), and no rice.
        var adjustments = new
        {
            adjustments = new object[]
            {
                new { ingredientId = "ing-chicken", kind = "substitute", substituteName = "Tofu", quantity = 250m },
                new { ingredientId = "ing-rice", kind = "skip" }
            }
        };
        var adjusted = await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/recipes/executions/{executionId}/adjustments", adjustments);
        Assert.Equal(HttpStatusCode.OK, adjusted.StatusCode);
        var view = await adjusted.Content.ReadFromJsonAsync<JsonElement>();
        var lines = view.GetProperty("proposal").GetProperty("lines").EnumerateArray().ToDictionary(line => line.GetProperty("ingredientId").GetString()!);
        Assert.Equal(("Tofu", "Chicken", tofu, 250m), (lines["ing-chicken"].GetProperty("displayName").GetString(), lines["ing-chicken"].GetProperty("originalName").GetString(), lines["ing-chicken"].GetProperty("lotId").GetGuid(), lines["ing-chicken"].GetProperty("proposedQuantity").GetDecimal()));
        Assert.Equal(("skipped", "skip"), (lines["ing-rice"].GetProperty("status").GetString(), lines["ing-rice"].GetProperty("adjustment").GetString()));
        Assert.Equal(2, view.GetProperty("adjustments").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/recipes/executions/{executionId}/adjustments", new { adjustments = new[] { new { ingredientId = "ing-unknown", kind = "skip" } } })).StatusCode);
        using var other = Client("adjusting-neighbor");
        var otherCsrf = await CsrfAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, otherCsrf, HttpMethod.Put, $"/api/v1/recipes/executions/{executionId}/adjustments", adjustments)).StatusCode);

        // Completing consumes the tofu, and the session keeps its adjustments for the recipe editor.
        var versions = view.GetProperty("proposal").GetProperty("pantryLots").EnumerateArray().ToDictionary(lot => lot.GetProperty("lotId").GetGuid(), lot => lot.GetProperty("version").GetGuid());
        var completed = await PostAsync(client, csrf, $"/api/v1/recipes/executions/{executionId}/completion", new { consumptions = new[] { new { lotId = tofu, quantity = 250m, unit = "Gram", version = versions[tofu] } } }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(150m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{tofu}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        Assert.Equal(300m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{rice}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());
        var finished = await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/executions/{executionId}");
        Assert.Equal("Tofu", finished.GetProperty("adjustments").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "substitute").GetProperty("substituteName").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/recipes/executions/{executionId}/adjustments", new { adjustments = Array.Empty<object>() })).StatusCode);
    }
}
