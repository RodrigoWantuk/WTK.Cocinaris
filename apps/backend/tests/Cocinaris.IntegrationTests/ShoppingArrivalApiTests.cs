using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0062 Slice 2: line marks and "Cheguei do mercado" over HTTP.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private static async Task<(Guid MenuId, JsonElement Line)> MenuWithLineAsync(HttpClient client, string csrf, DateOnly day, string ingredient)
    {
        var recipe = await CreateRecipeAsync(client, csrf, $"Prato {ingredient}", (ingredient, 600m, "g"));
        var menuId = (await JsonOf(await PostAsync(client, csrf, "/api/v1/planning/menus", new { startDate = day, endDate = day.AddDays(6) }, Guid.NewGuid()))).GetProperty("menuId").GetGuid();
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = recipe, plannedDate = day.AddDays(4) }, Guid.NewGuid());
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/acceptance", new { });
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={day:yyyy-MM-dd}");
        return (menuId, list.GetProperty("items")[0]);
    }

    [Fact]
    public async Task AnArrivalWithTheFlagOffStoresWholeAndARetryNeverCountsTwice()
    {
        using var client = Client("arrival-off");
        var csrf = await CsrfAsync(client);
        var day = new DateOnly(2026, 11, 4);
        var (menuId, line) = await MenuWithLineAsync(client, csrf, day, "Peito de frango");

        var plan = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/arrival?from={day:yyyy-MM-dd}");
        Assert.False(plan.GetProperty("guidanceEnabled").GetBoolean());
        var proposed = plan.GetProperty("lines")[0];
        Assert.Equal(600m, proposed.GetProperty("boughtQuantity").GetDecimal());
        Assert.True(proposed.GetProperty("needsRealQuantity").GetBoolean());
        var portion = proposed.GetProperty("portions").EnumerateArray().Single();
        Assert.NotEqual("Freezer", portion.GetProperty("location").GetString());

        // Another quantity recomputes without changing anything.
        var preview = await JsonOf(await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/arrival/preview", new { from = day, quantities = new[] { new { itemKey = line.GetProperty("itemKey").GetString(), boughtQuantity = 1000m } } }));
        Assert.Equal(1000m, preview.GetProperty("lines")[0].GetProperty("boughtQuantity").GetDecimal());

        var key = Guid.NewGuid();
        var body = new { lines = new[] { new { itemKey = line.GetProperty("itemKey").GetString(), displayName = "Peito de frango", unit = "Gram", portions = new[] { new { location = "Refrigerator", quantity = 300m, count = 1, notes = (string?)"Para: Sopa" }, new { location = "Freezer", quantity = 150m, count = 2, notes = (string?)null } } } } };
        var recorded = await JsonOf(await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/arrival", body, key));
        Assert.Empty(recorded.GetProperty("failed").EnumerateArray());
        Assert.Equal(3, recorded.GetProperty("recorded").GetInt32());
        recorded = await JsonOf(await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/arrival", body, key));
        Assert.Equal(3, recorded.GetProperty("recorded").GetInt32());

        var lots = (await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?pageSize=50")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, lots.Count);
        var frozen = lots.Where(lot => lot.GetProperty("storageLocation").GetString() == "Freezer").ToList();
        Assert.Equal(2, frozen.Count);
        Assert.All(frozen, lot => Assert.Equal("Frozen", lot.GetProperty("lifecycle").GetProperty("state").GetString()));
        Assert.Contains(lots, lot => lot.GetProperty("notes").GetString() == "Para: Sopa");

        // The list recalculates: the purchase covers the menu.
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={day:yyyy-MM-dd}");
        Assert.Equal(0m, list.GetProperty("items")[0].GetProperty("toBuyQuantity").GetDecimal());

        // A frozen portion reserved for a dated meal raises the thaw reminder.
        var reminders = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/preparation?from={day.AddDays(3):yyyy-MM-dd}&days=2");
        Assert.Contains(reminders.GetProperty("reminders").EnumerateArray(), item => item.GetProperty("kind").GetString() == "thaw");

        using var other = Client("arrival-other");
        var otherCsrf = await CsrfAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/planning/menus/{menuId}/arrival?from={day:yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"/api/v1/planning/menus/{menuId}/arrival", body, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task WithTheFlagOnAMealFarAheadIsFrozenAndAnUnfitLotFailsAlone()
    {
        await using var host = _factory.WithWebHostBuilder(builder => builder.UseSetting("Planning:StorageGuidance:Enabled", "true"));
        using var client = PlanClient(host, "arrival-on");
        var csrf = await CsrfAsync(client);
        var day = new DateOnly(2026, 11, 4);
        var (menuId, line) = await MenuWithLineAsync(client, csrf, day, "Peito de frango");

        var plan = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/arrival?from={day:yyyy-MM-dd}");
        Assert.True(plan.GetProperty("guidanceEnabled").GetBoolean());
        Assert.Equal("Freezer", plan.GetProperty("lines")[0].GetProperty("portions")[0].GetProperty("location").GetString());
        Assert.Equal(1, plan.GetProperty("containerCount").GetInt32());

        var body = new { lines = new[]
        {
            new { itemKey = line.GetProperty("itemKey").GetString()!, displayName = "Peito de frango", unit = "Gram", portions = new[] { new { location = "Refrigerator", quantity = 100m, count = 1 } } },
            new { itemKey = "bad|Gram", displayName = new string('x', 500), unit = "Gram", portions = new[] { new { location = "Pantry", quantity = 100m, count = 1 } } }
        } };
        var recorded = await JsonOf(await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/arrival", body, Guid.NewGuid()));
        Assert.Equal(1, recorded.GetProperty("recorded").GetInt32());
        Assert.Equal("bad|Gram", recorded.GetProperty("failed")[0].GetProperty("itemKey").GetString());
    }

    [Fact]
    public async Task MarksMoveALineLaterSubstituteItAndTheGuardRefusesABlockedOne()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "marks");
        var csrf = await CsrfAsync(client);
        await SetVegetarianAsync(client, csrf);
        var day = new DateOnly(2026, 11, 4);
        var (menuId, line) = await MenuWithLineAsync(client, csrf, day, "Abobrinha");
        var markKey = line.GetProperty("markKey").GetString();
        var path = $"/api/v1/planning/menus/{menuId}/shopping/marks";

        var blocked = await SendAsync(client, csrf, HttpMethod.Put, path, new { itemKey = markKey, substituteName = "Frango" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Equal("restriction_blocked", (await JsonOf(blocked)).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, path, new { itemKey = markKey, place = "moon" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, HttpMethod.Put, path, new { itemKey = markKey, substituteName = "Chuchu", state = "later", place = "butcher" })).StatusCode);
        var marked = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={day:yyyy-MM-dd}")).GetProperty("items")[0];
        Assert.Equal(("Chuchu", "Abobrinha", true, "butcher", markKey), (marked.GetProperty("displayName").GetString(), marked.GetProperty("substituteFor").GetString(), marked.GetProperty("later").GetBoolean(), marked.GetProperty("place").GetString(), marked.GetProperty("markKey").GetString()));

        // A line left for later is not proposed as bought.
        var arrival = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/arrival?from={day:yyyy-MM-dd}");
        Assert.Empty(arrival.GetProperty("lines").EnumerateArray());

        // Empty marks remove it, and another owner cannot touch it.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, HttpMethod.Put, path, new { itemKey = markKey })).StatusCode);
        var restored = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={day:yyyy-MM-dd}")).GetProperty("items")[0];
        Assert.Equal("Abobrinha", restored.GetProperty("displayName").GetString());
        using var other = PlanClient(host, "marks-other");
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, await CsrfAsync(other), HttpMethod.Put, path, new { itemKey = markKey, state = "later" })).StatusCode);
    }
}
