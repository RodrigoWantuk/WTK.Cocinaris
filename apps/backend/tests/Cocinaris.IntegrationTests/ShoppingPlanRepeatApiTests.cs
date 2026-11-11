using System.Net;
using System.Text.Json;
using Cocinaris.Modules.Ai.Abstractions;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0065 Slice 6: "Repetir" a chosen dish, cooked once for more meals or again each time.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private static Task<HttpResponseMessage> RepeatAsync(HttpClient client, string csrf, Guid sessionId, Guid cardId, string? mode, int meals) =>
        SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/repeats", new { mode, meals });

    [Fact]
    public async Task RepeatingAChosenDishFillsMoreMealsEitherWay()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "s6-repeat");
        using var other = PlanClient(host, "s6-repeat-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        await CreateMealRecipeAsync(client, csrf, "Pão de Queijo", "snack", "Polvilho");
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 7, days: 14), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var cardId = OptionIds(plan, "snack").Single();

        // Only a chosen dish repeats.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RepeatAsync(client, csrf, sessionId, cardId, "again", 2)).StatusCode);
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" });

        // Cooked again each time: one meal per cooking, past the four meals one cooking can cover.
        plan = await JsonOf(await RepeatAsync(client, csrf, sessionId, cardId, "again", 6));
        Assert.Equal(6, Group(plan, "snack").GetProperty("filled").GetInt32());
        Assert.Equal(("again", 6), (plan.GetProperty("accepted")[0].GetProperty("mode").GetString(), plan.GetProperty("accepted")[0].GetProperty("repeats").GetInt32()));
        var entries = await EntriesAsync(client, menuId);
        Assert.Equal(6, entries.GetArrayLength());
        Assert.All(entries.EnumerateArray(), entry => Assert.Equal((1, 1), (entry.GetProperty("mealsCovered").GetInt32(), entry.GetProperty("servings").GetInt32())));

        // The value is the whole state, so sending it again adds nothing.
        await RepeatAsync(client, csrf, sessionId, cardId, "again", 6);
        Assert.Equal(6, (await EntriesAsync(client, menuId)).GetArrayLength());

        // Never past what the meal asked for, and never a count without a way to cook it.
        Assert.Equal(HttpStatusCode.BadRequest, (await RepeatAsync(client, csrf, sessionId, cardId, "again", 8)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RepeatAsync(client, csrf, sessionId, cardId, null, 2)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RepeatAsync(client, csrf, sessionId, cardId, "twice", 2)).StatusCode);

        // Switching to one cooking leaves one meal on the menu that covers three.
        plan = await JsonOf(await RepeatAsync(client, csrf, sessionId, cardId, "yield", 3));
        Assert.Equal(3, Group(plan, "snack").GetProperty("filled").GetInt32());
        entries = await EntriesAsync(client, menuId);
        Assert.Equal((1, 3, 3), (entries.GetArrayLength(), entries[0].GetProperty("mealsCovered").GetInt32(), entries[0].GetProperty("servings").GetInt32()));
        // One cooking covers at most four meals.
        Assert.Equal(HttpStatusCode.BadRequest, (await RepeatAsync(client, csrf, sessionId, cardId, "yield", 5)).StatusCode);

        // And back: every meal of the period from one dish, then the undo's earlier value.
        plan = await JsonOf(await RepeatAsync(client, csrf, sessionId, cardId, "again", 7));
        Assert.Equal(7, Group(plan, "snack").GetProperty("filled").GetInt32());
        entries = await EntriesAsync(client, menuId);
        Assert.Equal(7, entries.GetArrayLength());
        Assert.All(entries.EnumerateArray(), entry => Assert.Equal(1, entry.GetProperty("mealsCovered").GetInt32()));
        plan = await JsonOf(await RepeatAsync(client, csrf, sessionId, cardId, null, 1));
        Assert.Equal(1, Group(plan, "snack").GetProperty("filled").GetInt32());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("accepted")[0].GetProperty("mode").ValueKind);
        Assert.Equal(1, (await EntriesAsync(client, menuId)).GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await RepeatAsync(other, otherCsrf, sessionId, cardId, "again", 2)).StatusCode);
    }

    [Fact]
    public async Task ADishStillBeingPreparedGetsItsRepeatsWhenItsRecipeArrives()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "s6-pending");
        var csrf = await CsrfAsync(client);
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 5, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks("Repete"));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack" }, Guid.NewGuid()));
        var cardId = OptionIds(plan, "snack")[0];
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" });

        plan = await JsonOf(await RepeatAsync(client, csrf, sessionId, cardId, "again", 3));
        Assert.Equal(3, Group(plan, "snack").GetProperty("filled").GetInt32());
        Assert.Equal(0, (await EntriesAsync(client, menuId)).GetArrayLength());

        var expanded = await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/expansion", new { }, Guid.NewGuid());
        Assert.True(expanded.IsSuccessStatusCode, await expanded.Content.ReadAsStringAsync());
        var entries = await EntriesAsync(client, menuId);
        Assert.Equal(3, entries.GetArrayLength());
        Assert.Single(entries.EnumerateArray().Select(entry => entry.GetProperty("recipeId").GetGuid()).Distinct());

        // Undoing the choice takes every cooking off the menu, including those past the fourth key.
        plan = await JsonOf(await RepeatAsync(client, csrf, sessionId, cardId, "again", 5));
        Assert.Equal(5, (await EntriesAsync(client, menuId)).GetArrayLength());
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/removal", new { });
        Assert.Equal(0, (await EntriesAsync(client, menuId)).GetArrayLength());
    }
}
