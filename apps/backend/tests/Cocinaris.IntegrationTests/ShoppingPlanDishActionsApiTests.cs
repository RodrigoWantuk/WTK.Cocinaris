using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Modules.Ai.Abstractions;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0065 Slice 3: acting on chosen dishes, "Completar", and "Distribuir nos dias".</summary>
public sealed partial class RecipeExecutionApiTests
{
    private static List<Guid> OptionIds(JsonElement plan, string meal) =>
        Group(plan, meal).GetProperty("options").EnumerateArray().Select(card => card.GetProperty("cardId").GetGuid()).ToList();

    private static async Task<JsonElement> EntriesAsync(HttpClient client, Guid menuId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries");

    [Fact]
    public async Task CompletarChoosesWhatFitsAndNamesWhatDidNot()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "s3-complete");
        using var other = PlanClient(host, "s3-complete-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        foreach (var name in new[] { "Biscoito Um", "Biscoito Dois", "Biscoito Tres", "Biscoito Quatro" })
        {
            await CreateMealRecipeAsync(client, csrf, name, "snack", "Farinha");
        }

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 3, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var ids = OptionIds(plan, "snack");
        Assert.Equal(4, ids.Count);
        // The person chose one while the proposal was open, so only two of the three still fit.
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ids[0]}/decision", new { decision = "accept", mealType = "snack" });

        var missing = Guid.NewGuid();
        var body = new { choices = new[] { ids[1], ids[2], ids[3], missing }.Select(id => new { cardId = id, mealType = "snack" }).ToArray() };
        var result = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/choices", body));
        Assert.Equal([ids[3], missing], result.GetProperty("failed").EnumerateArray().Select(item => item.GetGuid()));
        Assert.Equal(3, Group(result.GetProperty("plan"), "snack").GetProperty("filled").GetInt32());
        Assert.Equal(3, (await EntriesAsync(client, menuId)).GetArrayLength());

        // Sending it again chooses nothing twice.
        result = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/choices", body));
        Assert.Equal(3, Group(result.GetProperty("plan"), "snack").GetProperty("filled").GetInt32());
        Assert.Equal(3, (await EntriesAsync(client, menuId)).GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/choices", new { choices = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/choices", new { choices = new[] { new { cardId = ids[1], mealType = "snack" }, new { cardId = ids[1], mealType = "snack" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"{Plans}/{sessionId}/choices", body)).StatusCode);
    }

    [Fact]
    public async Task ADishCanFeedMorePeopleThanThePlanWithinOneCooking()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "s3-people");
        var csrf = await CsrfAsync(client);
        await CreateMealRecipeAsync(client, csrf, "Torta Salgada", "snack", "Farinha");
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 4, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var cardId = OptionIds(plan, "snack").Single();

        // Only a chosen dish has people of its own.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/people", new { people = 3 })).StatusCode);
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" });

        plan = await JsonOf(await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/people", new { people = 3 }));
        Assert.Equal(3, plan.GetProperty("accepted")[0].GetProperty("people").GetInt32());
        Assert.Equal(1, plan.GetProperty("setup").GetProperty("people").GetInt32());
        Assert.Equal(3, (await EntriesAsync(client, menuId))[0].GetProperty("servings").GetInt32());

        // "Rende mais" scales the dish's own people, not the plan's.
        await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/yield", new { meals = 2 });
        Assert.Equal(6, (await EntriesAsync(client, menuId))[0].GetProperty("servings").GetInt32());

        // One cooking makes at most 24 portions, whichever of the two changes.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/people", new { people = 13 })).StatusCode);
        plan = await JsonOf(await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/people", new { people = 12 }));
        Assert.Equal(24, (await EntriesAsync(client, menuId))[0].GetProperty("servings").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/yield", new { meals = 3 })).StatusCode);
    }

    [Fact]
    public async Task DistributingGivesUndatedDishesDaysAcrossThePeriodAndCanBeUndone()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "s3-spread");
        var csrf = await CsrfAsync(client);
        foreach (var name in new[] { "Bolo Um", "Bolo Dois", "Bolo Tres" })
        {
            await CreateMealRecipeAsync(client, csrf, name, "snack", "Farinha");
        }

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 4, days: 6), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var saved = OptionIds(plan, "snack");
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{saved[0]}/decision", new { decision = "accept", mealType = "snack", plannedDate = CountsToday.AddDays(5) });
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{saved[1]}/decision", new { decision = "accept", mealType = "snack" });
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{saved[2]}/decision", new { decision = "accept", mealType = "snack" });

        // A dish whose recipe is still being prepared has nothing to order by yet.
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks("Fila"));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack" }, Guid.NewGuid()));
        var ai = Group(plan, "snack").GetProperty("options")[0].GetProperty("cardId").GetGuid();
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ai}/decision", new { decision = "accept", mealType = "snack" });
        var waiting = await PostAsync(client, csrf, $"{Plans}/{sessionId}/distribution", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, waiting.StatusCode);
        Assert.Equal("expansions_pending", Code(await JsonOf(waiting)));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ai}/removal", new { });

        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/distribution", new { }));
        var days = (await EntriesAsync(client, menuId)).EnumerateArray().Select(entry => DateOnly.Parse(entry.GetProperty("plannedDate").GetString()!)).Order().ToList();
        // Two undated snacks over six days fall three days apart; the day the person gave stays.
        Assert.Equal([CountsToday, CountsToday.AddDays(3), CountsToday.AddDays(5)], days);
        Assert.All(plan.GetProperty("accepted").EnumerateArray(), card => Assert.NotEqual(JsonValueKind.Null, card.GetProperty("plannedDate").ValueKind));

        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/distribution", new { clear = true }));
        Assert.All((await EntriesAsync(client, menuId)).EnumerateArray(), entry => Assert.Equal(JsonValueKind.Null, entry.GetProperty("plannedDate").ValueKind));
        Assert.All(plan.GetProperty("accepted").EnumerateArray(), card => Assert.Equal(JsonValueKind.Null, card.GetProperty("plannedDate").ValueKind));
    }
}
