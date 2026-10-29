using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0029: menus, the menu of the day, cooking from a menu, and the shopping list over HTTP.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task AMenuDrivesTheDayAndTheShoppingListWithoutTouchingThePantry()
    {
        using var client = Client("planner");
        var csrf = await CsrfAsync(client);
        var beans = await CreateLotAsync(client, csrf, "Feijão", 300m);
        var feijoada = await CreateRecipeAsync(client, csrf, "Feijoada", ("feijao", 500m, "g"), ("Laranja", 2m, "unit"));
        var salad = await CreateRecipeAsync(client, csrf, "Salada", ("Alface", 1m, "unit"));
        var start = new DateOnly(2026, 11, 2);

        var key = Guid.NewGuid();
        var created = await PostAsync(client, csrf, "/api/v1/planning/menus", new { name = "Semana", startDate = start, endDate = start.AddDays(6) }, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var menuId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("menuId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, csrf, "/api/v1/planning/menus", new { name = "Semana", startDate = start, endDate = start.AddDays(6) }, key)).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/menus")).EnumerateArray());

        // Two dinners of the feijoada (2 portions each of a 2-portion recipe) and a flexible salad.
        var entryKey = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = feijoada, plannedDate = start, mealType = "dinner" }, entryKey)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = feijoada, plannedDate = start, mealType = "dinner" }, entryKey)).StatusCode);
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = feijoada, plannedDate = start.AddDays(2), mealType = "dinner" }, Guid.NewGuid());
        var menu = await (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = salad, servings = 1 }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>();
        var entries = menu.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal([start.ToString("yyyy-MM-dd"), start.AddDays(2).ToString("yyyy-MM-dd"), null], entries.Select(entry => entry.GetProperty("plannedDate").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = salad, plannedDate = start.AddDays(9) }, Guid.NewGuid())).StatusCode);

        // A draft is not on the home page; once accepted, the day's dinner and the flexible salad are.
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/today?date={start:yyyy-MM-dd}")).EnumerateArray());
        Assert.Equal("accepted", (await (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/acceptance", new { })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        var today = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/today?date={start:yyyy-MM-dd}");
        Assert.Equal(["Feijoada", "Salada"], today.EnumerateArray().Select(item => item.GetProperty("entry").GetProperty("recipeName").GetString()));

        // 1000 g of beans needed, 300 g in the pantry (matched by name, accents ignored): 700 g to buy.
        var shopping = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={start:yyyy-MM-dd}");
        var lines = shopping.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("displayName").GetString()!);
        Assert.Equal(("required", 1000m, 300m, 700m), (lines["feijao"].GetProperty("category").GetString(), lines["feijao"].GetProperty("requiredQuantity").GetDecimal(), lines["feijao"].GetProperty("availableQuantity").GetDecimal(), lines["feijao"].GetProperty("toBuyQuantity").GetDecimal()));
        Assert.Equal(4m, lines["Laranja"].GetProperty("toBuyQuantity").GetDecimal());
        var check = await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/shopping/checks", new { itemKey = lines["Laranja"].GetProperty("itemKey").GetString(), @checked = true });
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/shopping/checks", new { itemKey = lines["Laranja"].GetProperty("itemKey").GetString(), @checked = true })).StatusCode);
        Assert.Equal(300m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{beans}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());

        // Cooking the first dinner from the menu marks it cooking, then cooked; it leaves the day and the list.
        var firstEntry = entries[0].GetProperty("entryId").GetGuid();
        var execution = await (await PostAsync(client, csrf, $"/api/v1/recipes/{feijoada}/executions", new { menuEntryId = firstEntry })).Content.ReadFromJsonAsync<JsonElement>();
        var executionId = execution.GetProperty("executionId").GetGuid();
        Assert.Equal("cooking", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries")[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, csrf, $"/api/v1/recipes/executions/{executionId}/completion", new { consumptions = Array.Empty<object>() }, Guid.NewGuid())).StatusCode);
        menu = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}");
        Assert.Equal(("cooked", executionId), (menu.GetProperty("entries")[0].GetProperty("status").GetString(), menu.GetProperty("entries")[0].GetProperty("executionId").GetGuid()));
        Assert.Equal(["Salada"], (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/today?date={start:yyyy-MM-dd}")).EnumerateArray().Select(item => item.GetProperty("entry").GetProperty("recipeName").GetString()));
        shopping = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={start:yyyy-MM-dd}");
        lines = shopping.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("displayName").GetString()!);
        Assert.Equal((200m, true), (lines["feijao"].GetProperty("toBuyQuantity").GetDecimal(), lines["Laranja"].GetProperty("checked").GetBoolean()));

        // Skipping and removing change one entry only; the period cannot shrink past a dated meal.
        var secondEntry = entries[1].GetProperty("entryId").GetGuid();
        menu = await (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/entries/{secondEntry}", new { plannedDate = start.AddDays(2), mealType = "dinner", servings = 2, status = "skipped" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["cooked", "skipped", "planned"], menu.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("status").GetString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}", new { name = "Semana", startDate = start.AddDays(1), endDate = start.AddDays(6) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, HttpMethod.Delete, $"/api/v1/planning/menus/{menuId}/entries/{firstEntry}", null)).StatusCode);
        Assert.Equal("Completed", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/executions/{executionId}")).GetProperty("status").GetString());

        // Another user sees nothing; an archived menu is read-only.
        using var other = Client("planner-other");
        var otherCsrf = await CsrfAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/planning/menus/{menuId}/shopping")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = salad }, Guid.NewGuid())).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>($"/api/v1/planning/today?date={start:yyyy-MM-dd}")).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/archive", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = salad }, Guid.NewGuid())).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/menus")).EnumerateArray());
    }

    private static async Task<Guid> CreateRecipeAsync(HttpClient client, string csrf, string name, params (string Name, decimal Quantity, string Unit)[] ingredients)
    {
        var response = await PostAsync(client, csrf, "/api/v1/recipes", new
        {
            content = new
            {
                name,
                mealTypes = new[] { "dinner" },
                servings = 2,
                ingredients = ingredients.Select(item => new { displayName = item.Name, quantity = item.Quantity, unit = item.Unit, optional = false }).ToArray(),
                stages = new[] { new { name = "Cook", instructions = "Cook it.", activeMinutes = 10, passiveMinutes = 0 } }
            }
        }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipeId").GetGuid();
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string csrf, HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }
}
