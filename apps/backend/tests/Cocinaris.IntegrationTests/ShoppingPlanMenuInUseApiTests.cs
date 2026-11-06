using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0065 Slice 4: a menu in use, cooking several dishes ahead, and choosing a dish again.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private static async Task<JsonElement> MenuOf(HttpClient client, Guid menuId) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}");

    // Saved recipes only: ideas in the shared library depend on what other tests left there.
    private static List<Guid> SavedOptionIds(JsonElement plan, string meal) =>
        Group(plan, meal).GetProperty("options").EnumerateArray().Where(card => card.GetProperty("source").GetString() == "saved").Select(card => card.GetProperty("cardId").GetGuid()).ToList();

    private static async Task<(Guid SessionId, Guid MenuId, List<Guid> Options)> AcceptedSnacksAsync(HttpClient client, string csrf, string[] names, int wanted, int chosen, DateOnly? secondDay = null)
    {
        foreach (var name in names)
        {
            await CreateMealRecipeAsync(client, csrf, name, "snack", "Farinha");
        }

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: wanted, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var ids = SavedOptionIds(plan, "snack");
        for (var index = 0; index < chosen; index++)
        {
            await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ids[index]}/decision", new { decision = "accept", mealType = "snack", plannedDate = index == 1 ? secondDay : null });
        }

        Assert.Equal("finished", (await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/acceptance", new { }))).GetProperty("status").GetString());
        return (sessionId, plan.GetProperty("menuId").GetGuid(), ids);
    }

    [Fact]
    public async Task DishesCookedAheadFormTheirOwnRouteWithoutMovingTheDayTheyAreEaten()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "s4-cook-ahead");
        using var other = PlanClient(host, "s4-cook-ahead-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        var (sessionId, menuId, _) = await AcceptedSnacksAsync(client, csrf, ["Bolo Cedo", "Torta Cedo", "Pao Cedo"], 3, 3, CountsToday.AddDays(4));
        // A plan by counts asks for no preparation day, so there is no session until dishes are cooked ahead.
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?menuId={menuId}")).GetProperty("sessionId").ValueKind);

        var entries = (await MenuOf(client, menuId)).GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);
        var dated = entries.Single(entry => entry.GetProperty("plannedDate").ValueKind != JsonValueKind.Null).GetProperty("entryId").GetGuid();
        var undated = entries.First(entry => entry.GetProperty("plannedDate").ValueKind == JsonValueKind.Null).GetProperty("entryId").GetGuid();
        var cookDay = CountsToday.AddDays(1);

        var menu = await JsonOf(await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/cook-day", new { date = cookDay, entryIds = new[] { undated, dated } }));
        var byId = menu.GetProperty("entries").EnumerateArray().ToDictionary(entry => entry.GetProperty("entryId").GetGuid());
        Assert.Equal(cookDay.ToString("yyyy-MM-dd"), byId[dated].GetProperty("cookOnDate").GetString());
        // Cooking ahead never moves the day a dish is eaten.
        Assert.Equal(CountsToday.AddDays(4).ToString("yyyy-MM-dd"), byId[dated].GetProperty("plannedDate").GetString());
        Assert.Equal(JsonValueKind.Null, byId[undated].GetProperty("plannedDate").ValueKind);

        // The route is those two dishes, on that day, from the menu page and from "Hoje".
        Assert.Equal(sessionId, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?menuId={menuId}")).GetProperty("sessionId").GetGuid());
        Assert.Equal(sessionId, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?date={cookDay:yyyy-MM-dd}")).GetProperty("sessionId").GetGuid());
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?date={CountsToday:yyyy-MM-dd}")).GetProperty("sessionId").ValueKind);
        var route = await client.GetFromJsonAsync<JsonElement>($"{Plans}/{sessionId}/route");
        Assert.Equal(cookDay.ToString("yyyy-MM-dd"), route.GetProperty("prepDate").GetString());
        Assert.Equal(new[] { dated, undated }.Order(), route.GetProperty("recipes").EnumerateArray().Select(item => item.GetProperty("entryId").GetGuid()).Order());

        // Sending the day again with one dish takes the day away from the other.
        menu = await JsonOf(await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/cook-day", new { date = cookDay, entryIds = new[] { undated } }));
        byId = menu.GetProperty("entries").EnumerateArray().ToDictionary(entry => entry.GetProperty("entryId").GetGuid());
        Assert.Equal(JsonValueKind.Null, byId[dated].GetProperty("cookOnDate").ValueKind);
        Assert.Single((await client.GetFromJsonAsync<JsonElement>($"{Plans}/{sessionId}/route")).GetProperty("recipes").EnumerateArray());

        // A dish cannot be cooked after the day it is eaten, nor far before the menu starts.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/cook-day", new { date = CountsToday.AddDays(5), entryIds = new[] { dated } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/cook-day", new { date = CountsToday.AddDays(-8), entryIds = new[] { undated } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, otherCsrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/cook-day", new { date = cookDay, entryIds = new[] { undated } })).StatusCode);

        // A draft reserves nothing and is not cooked from.
        var draft = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 1, days: 3), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{draft.GetProperty("menuId").GetGuid()}/cook-day", new { date = cookDay, entryIds = Array.Empty<Guid>() })).StatusCode);
    }

    [Fact]
    public async Task AMenuInUseFillsWhatIsMissingAndSwapsOneDishWithoutMovingTheOthers()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "s4-amend");
        using var other = PlanClient(host, "s4-amend-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        var (sessionId, menuId, _) = await AcceptedSnacksAsync(client, csrf, ["Biscoito Uso", "Bolinho Uso", "Cookie Uso", "Muffin Uso"], 3, 2);

        var link = await client.GetFromJsonAsync<JsonElement>($"{Plans}/menu-link?menuId={menuId}");
        Assert.Equal((sessionId, "finished", 3), (link.GetProperty("sessionId").GetGuid(), link.GetProperty("status").GetString(), link.GetProperty("mealCounts").GetProperty("snack").GetInt32()));
        Assert.Equal(JsonValueKind.Null, (await other.GetFromJsonAsync<JsonElement>($"{Plans}/menu-link?menuId={menuId}")).GetProperty("sessionId").ValueKind);
        var before = (await MenuOf(client, menuId)).GetProperty("entries").EnumerateArray().ToDictionary(entry => entry.GetProperty("entryId").GetGuid(), entry => entry.GetProperty("suggestedOrder").GetInt32());
        Assert.Equal(2, before.Count);

        // Something is missing: the person's own recipes not on the menu are offered at once.
        var plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/amendments", new { menuId, today = CountsToday }));
        Assert.Equal("amending", plan.GetProperty("status").GetString());
        Assert.Equal((3, 2), (Group(plan, "snack").GetProperty("wanted").GetInt32(), Group(plan, "snack").GetProperty("filled").GetInt32()));
        var offered = SavedOptionIds(plan, "snack");
        Assert.Equal(2, offered.Count);
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{offered[0]}/decision", new { decision = "accept", mealType = "snack" }));
        var menu = await MenuOf(client, menuId);
        Assert.Equal("accepted", menu.GetProperty("status").GetString());
        var orders = menu.GetProperty("entries").EnumerateArray().ToDictionary(entry => entry.GetProperty("entryId").GetGuid(), entry => entry.GetProperty("suggestedOrder").GetInt32());
        // The new dish comes after the others, which keep their places.
        Assert.All(before, item => Assert.Equal(item.Value, orders[item.Key]));
        Assert.Equal(before.Values.Max() + 1, orders.Single(item => !before.ContainsKey(item.Key)).Value);

        // Swapping removes exactly that dish, and the new one can take its day.
        var swapped = before.Keys.First();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/amendments", new { menuId, today = CountsToday, replaceEntryId = swapped }));
        Assert.Equal(2, Group(plan, "snack").GetProperty("filled").GetInt32());
        var next = SavedOptionIds(plan, "snack").First();
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{next}/decision", new { decision = "accept", mealType = "snack", plannedDate = CountsToday.AddDays(2) });
        var entries = (await MenuOf(client, menuId)).GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);
        Assert.DoesNotContain(entries, entry => entry.GetProperty("entryId").GetGuid() == swapped);
        Assert.Contains(entries, entry => entry.GetProperty("plannedDate").GetString() == CountsToday.AddDays(2).ToString("yyyy-MM-dd"));

        // Closing keeps the menu in use and drops the options.
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/acceptance", new { }));
        Assert.Equal("finished", plan.GetProperty("status").GetString());
        Assert.Equal("accepted", (await MenuOf(client, menuId)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/distribution", new { })).StatusCode);

        // Only the owner's menu in use opens; a draft is changed on its board.
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"{Plans}/amendments", new { menuId, today = CountsToday })).StatusCode);
        var draft = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 1, days: 3), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync(client, csrf, $"{Plans}/amendments", new { menuId = draft.GetProperty("menuId").GetGuid(), today = CountsToday })).StatusCode);
        // A skipped dish cannot be swapped.
        var cooked = entries[0].GetProperty("entryId").GetGuid();
        await SendAsync(client, csrf, HttpMethod.Put, $"/api/v1/planning/menus/{menuId}/entries/{cooked}", new { plannedDate = (DateOnly?)null, mealType = "snack", servings = 1, status = "skipped" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync(client, csrf, $"{Plans}/amendments", new { menuId, today = CountsToday, replaceEntryId = cooked })).StatusCode);
    }

    [Fact]
    public async Task AMenuBuiltByHandGetsAPlanThatWantsWhatItHas()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "s4-hand");
        var csrf = await CsrfAsync(client);
        var soup = await CreateRecipeAsync(client, csrf, "Sopa Feita", ("Batata", 300m, "g"));
        var start = CountsToday;
        var menuId = (await JsonOf(await PostAsync(client, csrf, "/api/v1/planning/menus", new { name = "Mão", startDate = start, endDate = start.AddDays(2) }, Guid.NewGuid()))).GetProperty("menuId").GetGuid();
        var entryId = (await JsonOf(await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = soup, plannedDate = start, mealType = "dinner" }, Guid.NewGuid()))).GetProperty("entries")[0].GetProperty("entryId").GetGuid();
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/acceptance", new { });
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/menu-link?menuId={menuId}")).GetProperty("sessionId").ValueKind);

        var plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/amendments", new { menuId, today = start, replaceEntryId = entryId }));
        Assert.Equal((1, 0), (Group(plan, "dinner").GetProperty("wanted").GetInt32(), Group(plan, "dinner").GetProperty("filled").GetInt32()));
        Assert.Empty((await MenuOf(client, menuId)).GetProperty("entries").EnumerateArray());
        Assert.Equal("amending", (await client.GetFromJsonAsync<JsonElement>($"{Plans}/menu-link?menuId={menuId}")).GetProperty("status").GetString());
        // Opening it again reads the menu again and makes no second plan.
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/amendments", new { menuId, today = start }));
        Assert.Equal(1, Group(plan, "dinner").GetProperty("wanted").GetInt32());
    }
}
