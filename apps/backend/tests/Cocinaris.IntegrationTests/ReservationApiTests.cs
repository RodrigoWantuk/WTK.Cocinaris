using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0029 Phase 3: reservations, protections, readiness, and their effect on cooking and shopping over HTTP.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task AcceptedMealsReservePantryStockAndProtectionsAreNeverProposed()
    {
        using var client = Client("reserver");
        var csrf = await CsrfAsync(client);
        var rice = await CreateLotAsync(client, csrf, "Arroz", 500m);
        var risotto = await CreateRecipeAsync(client, csrf, "Risoto", ("arroz", 300m, "g"));
        var pudding = await CreateRecipeAsync(client, csrf, "Arroz doce", ("Arroz", 300m, "g"));
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var menu = await (await PostAsync(client, csrf, "/api/v1/planning/menus", new { startDate = day, endDate = day.AddDays(6) }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>();
        var menuId = menu.GetProperty("menuId").GetGuid();
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = risotto, plannedDate = day }, Guid.NewGuid());
        menu = await (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = pudding, plannedDate = day.AddDays(1) }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>();

        // A draft reserves nothing, but shows what accepting it would mean.
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/reservations")).GetProperty("lots").EnumerateArray());
        Assert.Equal(["ready", "missing"], menu.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("readiness").GetProperty("readiness").GetString()));

        menu = await (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/acceptance", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var entries = menu.GetProperty("entries").EnumerateArray().ToList();
        var risottoEntry = entries[0].GetProperty("entryId").GetGuid();
        var puddingEntry = entries[1].GetProperty("entryId").GetGuid();
        Assert.Equal(100m, entries[1].GetProperty("readiness").GetProperty("missing")[0].GetProperty("quantity").GetDecimal());
        var lot = (await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/reservations")).GetProperty("lots").EnumerateArray().Single();
        Assert.Equal((rice, 500m, 0m, 500m, 0m), (lot.GetProperty("lotId").GetGuid(), lot.GetProperty("quantity").GetDecimal(), lot.GetProperty("protectedQuantity").GetDecimal(), lot.GetProperty("reservedQuantity").GetDecimal(), lot.GetProperty("freeQuantity").GetDecimal()));

        // Keeping at least 100 g leaves 400 g to reserve: the risotto is covered, the pudding lacks 200 g.
        var protectedView = await (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/protections/{rice}", new { kind = "minimum", minimumQuantity = 100m })).Content.ReadFromJsonAsync<JsonElement>();
        lot = protectedView.GetProperty("lots").EnumerateArray().Single();
        Assert.Equal(("minimum", 100m, 400m), (lot.GetProperty("protection").GetProperty("kind").GetString(), lot.GetProperty("protectedQuantity").GetDecimal(), lot.GetProperty("reservedQuantity").GetDecimal()));
        Assert.Equal(200m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries")[1].GetProperty("readiness").GetProperty("missing")[0].GetProperty("quantity").GetDecimal());

        // Cooking something off the menu sees both reservations and the protection; cooking the risotto from its meal sees only the other meal.
        var offMenu = await (await PostAsync(client, csrf, $"/api/v1/recipes/{pudding}/executions", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var pantryLot = offMenu.GetProperty("proposal").GetProperty("pantryLots").EnumerateArray().Single(item => item.GetProperty("lotId").GetGuid() == rice);
        Assert.Equal((100m, 400m), (pantryLot.GetProperty("protectedQuantity").GetDecimal(), pantryLot.GetProperty("reservedQuantity").GetDecimal()));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, csrf, $"/api/v1/recipes/executions/{offMenu.GetProperty("executionId").GetGuid()}/abandonment", new { })).StatusCode);
        var fromMeal = await (await PostAsync(client, csrf, $"/api/v1/recipes/{risotto}/executions", new { menuEntryId = risottoEntry })).Content.ReadFromJsonAsync<JsonElement>();
        pantryLot = fromMeal.GetProperty("proposal").GetProperty("pantryLots").EnumerateArray().Single(item => item.GetProperty("lotId").GetGuid() == rice);
        Assert.Equal(100m, pantryLot.GetProperty("reservedQuantity").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, csrf, $"/api/v1/recipes/executions/{fromMeal.GetProperty("executionId").GetGuid()}/abandonment", new { })).StatusCode);

        // Releasing the pudding's reservations keeps the meal planned but stops it reserving.
        menu = await (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/entries/{puddingEntry}", new { plannedDate = day.AddDays(1), servings = 2, status = "planned", reserveIngredients = false })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((false, JsonValueKind.Null), (menu.GetProperty("entries")[1].GetProperty("reserveIngredients").GetBoolean(), menu.GetProperty("entries")[1].GetProperty("readiness").ValueKind));
        Assert.Equal(300m, (await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/reservations")).GetProperty("lots")[0].GetProperty("reservedQuantity").GetDecimal());

        // Another menu's shopping list counts only what is neither protected nor reserved: 500 - 100 - 300 = 100 g.
        var draft = await (await PostAsync(client, csrf, "/api/v1/planning/menus", new { startDate = day, endDate = day }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>();
        var draftId = draft.GetProperty("menuId").GetGuid();
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{draftId}/entries", new { recipeId = risotto, plannedDate = day }, Guid.NewGuid());
        var shopping = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{draftId}/shopping?from={day:yyyy-MM-dd}");
        var line = shopping.GetProperty("items").EnumerateArray().Single();
        Assert.Equal((100m, 200m), (line.GetProperty("availableQuantity").GetDecimal(), line.GetProperty("toBuyQuantity").GetDecimal()));
        Assert.Equal(500m, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/lots/{rice}")).GetProperty("quantity").GetProperty("measuredValue").GetDecimal());

        // Protections are owner-scoped and removable.
        using var other = Client("reserver-other");
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, await CsrfAsync(other), HttpMethod.Put, $"/api/v1/planning/protections/{rice}", new { kind = "whole" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/protections/{rice}", new { kind = "whole", minimumQuantity = 5m })).StatusCode);
        var removed = await (await SendAsync(client, csrf, HttpMethod.Delete, $"/api/v1/planning/protections/{rice}", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("lots")[0].GetProperty("protection").ValueKind);
    }
}
