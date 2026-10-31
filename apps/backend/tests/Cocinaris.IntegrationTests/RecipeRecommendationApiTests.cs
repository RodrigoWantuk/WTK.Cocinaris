using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0021: "Para você agora" recommendations over HTTP.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task ForYouNowRanksSavedRecipesByPantryDatesTimeAndMeal()
    {
        using var client = Client("recommend-reader");
        var csrf = await CsrfAsync(client);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PostAsync(client, csrf, "/api/v1/inventory/lots", new { productName = "Creme de leite", quantity = new { measuredValue = 200, unit = "Gram" }, storageLocation = "Refrigerator", printedExpirationDate = today.AddDays(1) }, Guid.NewGuid());
        await CreateLotAsync(client, csrf, "Arroz", 500m);
        var creamy = await RecipeWithTimeAsync(client, csrf, "Strogonoff", "dinner", 25, ("Creme de leite", 200m, "g"), ("Cogumelos", 100m, "g"));
        var rice = await RecipeWithTimeAsync(client, csrf, "Arroz simples", "lunch", 20, ("Arroz", 200m, "g"));
        var slow = await RecipeWithTimeAsync(client, csrf, "Feijoada", "lunch", 180, ("Feijão", 500m, "g"));

        var all = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/recommendations?today={today:yyyy-MM-dd}&mealType=lunch")).EnumerateArray().ToList();
        var codes = all.ToDictionary(item => item.GetProperty("recipeId").GetGuid(), item => item.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetProperty("code").GetString()).ToList());
        // The rice is fully covered and fits lunch; the stroganoff uses the cream that is due tomorrow.
        Assert.Equal(rice, all[0].GetProperty("recipeId").GetGuid());
        Assert.Contains("have_all", codes[rice]);
        Assert.Contains("fits_meal", codes[rice]);
        Assert.Equal("uses_soon", codes[creamy][0]);
        Assert.Equal("Creme de leite", all.Single(item => item.GetProperty("recipeId").GetGuid() == creamy).GetProperty("reasons")[0].GetProperty("item").GetString());
        Assert.Equal(20, all[0].GetProperty("totalMinutes").GetInt32());

        // A time chosen now is a hard limit: the three-hour feijoada is left out.
        var quick = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/recommendations?today={today:yyyy-MM-dd}&maxMinutes=30")).EnumerateArray().Select(item => item.GetProperty("recipeId").GetGuid()).ToList();
        Assert.DoesNotContain(slow, quick);
        Assert.Equal(2, quick.Count);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/recipes/recommendations?today={today:yyyy-MM-dd}&mealType=brunch")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/recipes/recommendations")).StatusCode);
        using var other = Client("recommend-other");
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/recommendations?today={today:yyyy-MM-dd}")).EnumerateArray());
    }

    private static async Task<Guid> RecipeWithTimeAsync(HttpClient client, string csrf, string name, string mealType, int minutes, params (string Name, decimal Quantity, string Unit)[] ingredients)
    {
        var response = await PostAsync(client, csrf, "/api/v1/recipes", new
        {
            content = new
            {
                name,
                mealTypes = new[] { mealType },
                servings = 2,
                ingredients = ingredients.Select(item => new { displayName = item.Name, quantity = item.Quantity, unit = item.Unit, optional = false }).ToArray(),
                stages = new[] { new { name = "Cozinhar", instructions = "Cozinhe.", activeMinutes = minutes, passiveMinutes = 0 } }
            }
        }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipeId").GetGuid();
    }
}
