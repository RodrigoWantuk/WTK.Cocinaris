using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0062 Slice 4: a meal with no day says by when to cook it, and can be frozen or left as it is.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task AMealWithNoDaySaysByWhenToCookItAndCanBeLeftAsItIs()
    {
        using var client = Client("cook-by");
        using var other = Client("cook-by-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fish = await JsonOf(await PostAsync(client, csrf, "/api/v1/inventory/lots", new { productName = "Peixe branco", quantity = new { measuredValue = 300m, unit = "Gram" }, storageLocation = "Refrigerator", printedExpirationDate = today.AddDays(1) }, Guid.NewGuid()));
        var fishId = fish.GetProperty("lotId").GetGuid();
        var stew = await CreateRecipeAsync(client, csrf, "Moqueca", ("Peixe branco", 300m, "g"));
        var menuId = (await JsonOf(await PostAsync(client, csrf, "/api/v1/planning/menus", new { startDate = today, endDate = today.AddDays(6) }, Guid.NewGuid()))).GetProperty("menuId").GetGuid();
        var entryId = (await JsonOf(await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = stew, mealType = "dinner" }, Guid.NewGuid()))).GetProperty("entries")[0].GetProperty("entryId").GetGuid();
        var preparation = $"/api/v1/planning/preparation?from={today:yyyy-MM-dd}&days=2";
        List<JsonElement> CookBy(JsonElement view) => view.GetProperty("reminders").EnumerateArray().Where(item => item.GetProperty("kind").GetString() == "cook_by").ToList();

        // A draft reserves nothing, so it says nothing.
        Assert.Empty(CookBy(await client.GetFromJsonAsync<JsonElement>(preparation)));
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/acceptance", new { });

        // The fish is printed for tomorrow: the dish with no day should be cooked by then, starting today.
        var reminder = Assert.Single(CookBy(await client.GetFromJsonAsync<JsonElement>(preparation)));
        Assert.Equal(
            (entryId, "Peixe branco", today.AddDays(1).ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), fishId),
            (reminder.GetProperty("entryId").GetGuid(), reminder.GetProperty("itemName").GetString(), reminder.GetProperty("useByDate").GetString(), reminder.GetProperty("dueDate").GetString(), reminder.GetProperty("lotId").GetGuid()));
        // With the storage table off, nothing claims the fish can be frozen.
        Assert.Empty(reminder.GetProperty("freezeLots").EnumerateArray());

        // "Deixar como está" hides it for that day; only the owner's meal and only this kind are accepted.
        var dismissal = "/api/v1/planning/reminders/dismissal";
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, dismissal, new { entryId, kind = "buy", date = today.AddDays(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, otherCsrf, HttpMethod.Put, dismissal, new { entryId, kind = "cook_by", date = today.AddDays(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, csrf, HttpMethod.Put, dismissal, new { entryId, kind = "cook_by", date = today.AddDays(1) })).StatusCode);
        Assert.Empty(CookBy(await client.GetFromJsonAsync<JsonElement>(preparation)));

        // A dismissal of another day does not hide it, and the same request twice changes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, csrf, HttpMethod.Put, dismissal, new { entryId, kind = "cook_by", date = today })).StatusCode);
        Assert.Single(CookBy(await client.GetFromJsonAsync<JsonElement>(preparation)));
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, csrf, HttpMethod.Put, dismissal, new { entryId, kind = "cook_by", date = today })).StatusCode);
        Assert.Single(CookBy(await client.GetFromJsonAsync<JsonElement>(preparation)));

        // "Congelar hoje": once the reserved fish is frozen, it waits, and the reminder goes. The meal
        // reserves the whole lot, so the whole lot is frozen; a part would be a split.
        using var current = await client.GetAsync($"/api/v1/inventory/lots/{fishId}");
        using (var freeze = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{fishId}/transitions") { Content = JsonContent.Create(new { kind = "Freeze" }) })
        {
            freeze.Headers.Add("X-CSRF-TOKEN", csrf);
            freeze.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            freeze.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.Tag);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(freeze)).StatusCode);
        }

        Assert.Empty(CookBy(await client.GetFromJsonAsync<JsonElement>(preparation)));
        // Another owner's day stays empty throughout.
        Assert.Empty(CookBy(await other.GetFromJsonAsync<JsonElement>(preparation)));
    }
}
