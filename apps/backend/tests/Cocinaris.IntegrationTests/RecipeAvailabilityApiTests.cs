using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0057 (review finding H2): what the pantry covers of a recipe, over HTTP.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task RecipeAvailabilitySaysWhatThePantryCoversWithoutChangingIt()
    {
        using var client = Client("availability-reader");
        var csrf = await CsrfAsync(client);
        await CreateLotAsync(client, csrf, "Arroz", 500m);
        var chicken = await CreateLotAsync(client, csrf, "Peito de frango", 150m);
        var recipe = await CreateRecipeAsync(client, csrf, "Frango com arroz", ("arroz", 300m, "g"), ("Peito de frango", 400m, "g"), ("Cebola", 1m, "unit"));

        var lines = await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/{recipe}/availability");
        var statuses = lines.EnumerateArray().Select(line => (line.GetProperty("status").GetString(), line.GetProperty("missingQuantity").GetDecimal())).ToList();
        Assert.Equal([("available", 0m), ("partial", 250m), ("missing", 1m)], statuses);
        Assert.Equal(150m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{chicken}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());

        using var other = Client("availability-other");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/recipes/{recipe}/availability")).StatusCode);
    }

    [Fact]
    public async Task CookPageListsRecipesByHowMuchOfThemThePantryCovers()
    {
        using var client = Client("readiness-reader");
        var csrf = await CsrfAsync(client);
        await CreateLotAsync(client, csrf, "Arroz", 500m);
        await CreateLotAsync(client, csrf, "Feijão", 400m);
        var none = await CreateRecipeAsync(client, csrf, "Bolo", ("Farinha", 300m, "g"), ("Ovos", 3m, "unit"));
        var full = await CreateRecipeAsync(client, csrf, "Arroz com feijão", ("arroz", 200m, "g"), ("Feijão", 200m, "g"));
        var half = await CreateRecipeAsync(client, csrf, "Risoto", ("Arroz", 300m, "g"), ("Queijo", 100m, "g"));

        var list = (await client.GetFromJsonAsync<JsonElement>("/api/v1/recipes/readiness")).EnumerateArray().ToList();
        Assert.Equal([full, half, none], list.Select(item => item.GetProperty("recipeId").GetGuid()));
        Assert.Equal((2, 0), (list[0].GetProperty("available").GetInt32(), list[0].GetProperty("missing").GetInt32()));
        Assert.Equal((1, 1), (list[1].GetProperty("available").GetInt32(), list[1].GetProperty("missing").GetInt32()));

        // Another person's recipes never appear.
        using var other = Client("readiness-other");
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/v1/recipes/readiness")).EnumerateArray());
    }
}
