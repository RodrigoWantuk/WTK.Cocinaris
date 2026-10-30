using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0029 Phase 4: package sizes on the shopping list and pre-meal reminders over HTTP.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task PackageSizesShapeTheShoppingListAndRemindersFollowTheMenuInUse()
    {
        using var client = Client("package-buyer");
        var csrf = await CsrfAsync(client);
        var chicken = await CreateLotAsync(client, csrf, "Peito de frango", 200m);
        var stew = await CreateRecipeAsync(client, csrf, "Ensopado", ("Peito de frango", 600m, "g"), ("Cebola", 2m, "unit"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var menu = await (await PostAsync(client, csrf, "/api/v1/planning/menus", new { startDate = today, endDate = today.AddDays(6) }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>();
        var menuId = menu.GetProperty("menuId").GetGuid();
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = stew, plannedDate = today.AddDays(1), mealType = "dinner" }, Guid.NewGuid());

        // No package size is assumed until the owner confirms one; then the line counts packages and the remainder.
        JsonElement ChickenLine(JsonElement list) => list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("displayName").GetString() == "Peito de frango");
        var shopping = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={today:yyyy-MM-dd}");
        var line = ChickenLine(shopping);
        Assert.Equal((400m, JsonValueKind.Null), (line.GetProperty("toBuyQuantity").GetDecimal(), line.GetProperty("packageSize").ValueKind));
        Assert.Equal(400m, line.GetProperty("uses")[0].GetProperty("missingQuantity").GetDecimal());
        var itemKey = line.GetProperty("itemKey").GetString();
        var saved = await SendAsync(client, csrf, HttpMethod.Put, "/api/v1/planning/package-sizes", new { itemKey, size = 1000m });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        line = ChickenLine(await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={today:yyyy-MM-dd}"));
        Assert.Equal((1000m, 1, 600m), (line.GetProperty("packageSize").GetDecimal(), line.GetProperty("packageCount").GetInt32(), line.GetProperty("expectedRemainder").GetDecimal()));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, "/api/v1/planning/package-sizes", new { itemKey, size = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, "/api/v1/planning/package-sizes", new { itemKey = "frango", size = 500m })).StatusCode);

        // Sizes are the owner's own: another person's list for the same item has none.
        using var other = Client("package-buyer-other");
        var otherCsrf = await CsrfAsync(other);
        var otherStew = await CreateRecipeAsync(other, otherCsrf, "Ensopado", ("Peito de frango", 600m, "g"));
        var otherMenu = (await (await PostAsync(other, otherCsrf, "/api/v1/planning/menus", new { startDate = today, endDate = today }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("menuId").GetGuid();
        await PostAsync(other, otherCsrf, $"/api/v1/planning/menus/{otherMenu}/entries", new { recipeId = otherStew, plannedDate = today }, Guid.NewGuid());
        Assert.Equal(JsonValueKind.Null, ChickenLine(await other.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{otherMenu}/shopping?from={today:yyyy-MM-dd}")).GetProperty("packageSize").ValueKind);

        // A draft reminds of nothing; once in use, tomorrow's dinner needs the onion and more chicken.
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/preparation?from={today:yyyy-MM-dd}")).GetProperty("reminders").EnumerateArray());
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/acceptance", new { });
        var preparation = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/preparation?from={today:yyyy-MM-dd}&days=2");
        var reminders = preparation.GetProperty("reminders").EnumerateArray().ToList();
        Assert.Equal(["buy:Cebola", "buy:Peito de frango"], reminders.Select(item => $"{item.GetProperty("kind").GetString()}:{item.GetProperty("itemName").GetString()}").Order());
        Assert.All(reminders, item => Assert.Equal((menuId, today), (item.GetProperty("menuId").GetGuid(), DateOnly.Parse(item.GetProperty("dueDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture))));

        // Moving the chicken to the freezer adds a thaw reminder for the reserved 200 g.
        using var current = await client.GetAsync($"/api/v1/inventory/lots/{chicken}");
        using (var move = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{chicken}/transitions") { Content = JsonContent.Create(new { kind = "Freeze" }) })
        {
            move.Headers.Add("X-CSRF-TOKEN", csrf);
            move.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            move.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.Tag);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(move)).StatusCode);
        }

        var thaw = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/preparation?from={today:yyyy-MM-dd}")).GetProperty("reminders").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "thaw");
        Assert.Equal((chicken, 200m), (thaw.GetProperty("lotId").GetGuid(), thaw.GetProperty("quantity").GetDecimal()));
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>($"/api/v1/planning/preparation?from={today:yyyy-MM-dd}")).GetProperty("reminders").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/planning/preparation?from={today:yyyy-MM-dd}&days=8")).StatusCode);

        // Forgetting the size returns the line to the net quantity.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, HttpMethod.Put, "/api/v1/planning/package-sizes", new { itemKey, size = (decimal?)null })).StatusCode);
        Assert.Equal(JsonValueKind.Null, ChickenLine(await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={today:yyyy-MM-dd}")).GetProperty("packageCount").ValueKind);
    }
}
