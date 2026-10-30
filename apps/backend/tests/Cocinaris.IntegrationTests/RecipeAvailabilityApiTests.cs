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
}
