using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0065 Slice 1: a plan is how many of each meal, within a period, with or without days.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private static readonly DateOnly CountsToday = new(2026, 11, 4);

    private static object CountsPlan(int breakfast = 0, int lunch = 0, int dinner = 0, int snack = 0, int days = 14, DateOnly? startDate = null) =>
        new { today = CountsToday, days, mealCounts = new { breakfast, lunch, dinner, snack }, startDate, people = 1 };

    /// <summary>Six recognizable snacks with new names, optionally keeping the request the model received.</summary>
    private static Func<AiProviderInvocationRequest, AiProviderInvocationResult> Snacks(string tag, Action<JsonElement>? onRequest = null) => request =>
    {
        if (onRequest is not null)
        {
            using var sent = JsonDocument.Parse(request.Payload);
            onRequest(sent.RootElement.Clone());
        }

        return DeckOf(($"Bolinho {tag} Um", "bolinho", 25), ($"Tapioca {tag} Dois", "tapioca", 15), ($"Sanduíche {tag} Três", "sanduíche", 10), ($"Cookie {tag} Quatro", "cookie", 30), ($"Pipoca {tag} Cinco", "pipoca", 10), ($"Muffin {tag} Seis", "muffin", 35))(request);
    };

    private static JsonElement Group(JsonElement plan, string meal) =>
        plan.GetProperty("groups").EnumerateArray().Single(group => group.GetProperty("mealType").GetString() == meal);

    private static List<string> OptionNames(JsonElement plan, string meal) =>
        Group(plan, meal).GetProperty("options").EnumerateArray().Select(card => card.GetProperty("name").GetString()!).ToList();

    private static string Code(JsonElement problem) => problem.GetProperty("errorCode").GetString()!;

    /// <summary>A saved recipe for the given meal; saved recipes are offered only for their own meals.</summary>
    private static async Task CreateMealRecipeAsync(HttpClient client, string csrf, string name, string meal, string ingredient)
    {
        var response = await PostAsync(client, csrf, "/api/v1/recipes", new
        {
            content = new
            {
                name,
                mealTypes = new[] { meal },
                servings = 2,
                ingredients = new[] { new { displayName = ingredient, quantity = 200m, unit = "g", optional = false } },
                stages = new[] { new { name = "Cook", instructions = "Cook it.", activeMinutes = 10, passiveMinutes = 0 } }
            }
        }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<(JsonElement Plan, Guid SessionId)> StartSnacksAsync(HttpClient client, string csrf, FakeAiProvider provider, int snack = 7)
    {
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: snack), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks("Base"));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack" }, Guid.NewGuid()));
        return (plan, sessionId);
    }

    [Fact]
    public async Task OnlySnacksWithNoDaysIsAValidPlan()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-snacks");
        var csrf = await CsrfAsync(client);
        await CreateLotAsync(client, csrf, "Tomate", 1000m);

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        Assert.Equal(7, plan.GetProperty("progress").GetProperty("mealsTotal").GetInt32());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("setup").GetProperty("prepDate").ValueKind);
        Assert.Equal(CountsToday.AddDays(13), DateOnly.Parse(plan.GetProperty("setup").GetProperty("endDate").GetString()!));

        JsonElement? sent = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks("Lanche", request => sent = request));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack" }, Guid.NewGuid()));

        // A snack needs no base and no session, so the plain deck is what is asked for.
        Assert.Equal("recipe.suggest_plan_deck.v1", sent!.Value.GetProperty("operation").GetString());
        var deck = sent.Value.GetProperty("planDeck");
        Assert.Equal(["snack"], deck.GetProperty("mealTypes").EnumerateArray().Select(item => item.GetString()!));
        Assert.False(deck.GetProperty("timing").GetProperty("fixedDays").GetBoolean());
        Assert.Equal(14, deck.GetProperty("timing").GetProperty("windowDays").GetInt32());
        // The pantry starts off, and what the model cannot see it cannot build on.
        Assert.Empty(sent.Value.GetProperty("availabilitySnapshot").GetProperty("items").EnumerateArray());

        var snack = Group(plan, "snack");
        Assert.Equal((7, 0, 6), (snack.GetProperty("wanted").GetInt32(), snack.GetProperty("filled").GetInt32(), snack.GetProperty("options").GetArrayLength()));

        var cardId = snack.GetProperty("options")[0].GetProperty("cardId").GetGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" }));
        Assert.Equal(1, Group(plan, "snack").GetProperty("filled").GetInt32());
        var expanded = await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/expansion", new { }, Guid.NewGuid());
        Assert.True(expanded.IsSuccessStatusCode, await expanded.Content.ReadAsStringAsync());

        // No day was given, so the dish waits for any day of the period.
        var menu = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{plan.GetProperty("menuId").GetGuid()}");
        Assert.Equal(JsonValueKind.Null, menu.GetProperty("entries")[0].GetProperty("plannedDate").ValueKind);
        Assert.Equal("snack", menu.GetProperty("entries")[0].GetProperty("mealType").GetString());
    }

    [Fact]
    public async Task ThreeLunchesAndTwoDinnersAreCountedApart()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-mains");
        var csrf = await CsrfAsync(client);
        await CreateRecipeAsync(client, csrf, "Prato Um", ("Cebola", 1m, "unit"));
        await CreateRecipeAsync(client, csrf, "Prato Dois", ("Alho", 1m, "unit"));
        await CreateRecipeAsync(client, csrf, "Prato Tres", ("Couve", 1m, "unit"));

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(lunch: 3, dinner: 1, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        Assert.Equal(4, plan.GetProperty("progress").GetProperty("mealsTotal").GetInt32());
        Assert.Equal(3, Group(plan, "lunch").GetProperty("wanted").GetInt32());
        Assert.Equal(1, Group(plan, "dinner").GetProperty("wanted").GetInt32());

        // Lunch and dinner share their options, because either dish serves either meal.
        Assert.Equal(OptionNames(plan, "lunch"), OptionNames(plan, "dinner"));
        var ids = Group(plan, "dinner").GetProperty("options").EnumerateArray().Select(card => card.GetProperty("cardId").GetGuid()).ToList();

        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ids[0]}/decision", new { decision = "accept", mealType = "dinner" }));
        Assert.Equal((0, 1), (Group(plan, "lunch").GetProperty("filled").GetInt32(), Group(plan, "dinner").GetProperty("filled").GetInt32()));

        // Dinner is full: asking for one more says so, and a snack is not a meal of this plan.
        var full = await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ids[1]}/decision", new { decision = "accept", mealType = "dinner" });
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
        var wrong = await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ids[1]}/decision", new { decision = "accept", mealType = "snack" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        // With no meal named, the dish takes whichever of the two has room.
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ids[1]}/decision", new { decision = "accept" }));
        Assert.Equal(1, Group(plan, "lunch").GetProperty("filled").GetInt32());
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(41, 0, 0, 0)]
    [InlineData(20, 21, 0, 0)]
    [InlineData(-1, 2, 0, 0)]
    public async Task CountsOutOfRangeAreRefused(int breakfast, int lunch, int dinner, int snack)
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, $"counts-range-{breakfast}-{lunch}");
        var csrf = await CsrfAsync(client);
        var response = await PostAsync(client, csrf, Plans, CountsPlan(breakfast, lunch, dinner, snack), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheStartOfThePeriodIsThePersonsChoiceButNotThePast()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-start");
        var csrf = await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, startDate: CountsToday.AddDays(-1)), Guid.NewGuid())).StatusCode);

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, days: 3, startDate: CountsToday.AddDays(2)), Guid.NewGuid()));
        Assert.Equal(CountsToday.AddDays(2), DateOnly.Parse(plan.GetProperty("setup").GetProperty("startDate").GetString()!));
        Assert.Equal(CountsToday.AddDays(4), DateOnly.Parse(plan.GetProperty("setup").GetProperty("endDate").GetString()!));
    }

    [Fact]
    public async Task ASpecificDishComesFirstAndSkipsTheMealRules()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-request");
        var csrf = await CsrfAsync(client);
        var (plan, sessionId) = await StartSnacksAsync(client, csrf, provider);
        var before = OptionNames(plan, "snack");

        JsonElement? sent = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, request =>
        {
            using var document = JsonDocument.Parse(request.Payload);
            sent = document.RootElement.Clone();
            // A plated dish on a snack plan: the person asked for it, so it stays.
            return DeckOf(("Espaguete da Avó", "massa em molho", 40), ("Talharim da Avó", "massa em molho", 45))(request);
        });
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack", request = "espaguete da minha avó" }, Guid.NewGuid()));

        var requested = sent!.Value.GetProperty("planDeck").GetProperty("requestedDish");
        Assert.Equal("espaguete da minha avó", requested.GetProperty("text").GetString());
        Assert.Contains("untrusted", requested.GetProperty("treatment").GetString());

        var options = Group(plan, "snack").GetProperty("options");
        Assert.Equal("Espaguete da Avó", options[0].GetProperty("name").GetString());
        Assert.Equal("espaguete da minha avó", options[0].GetProperty("requestText").GetString());

        // A request adds; it takes nothing off the screen.
        using var scope = host.Services.CreateScope();
        var snapshots = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ShoppingPlanCards.AsNoTracking()
            .Where(card => card.SessionId == sessionId).Select(card => card.SnapshotJson).ToListAsync();
        Assert.All(before, name => Assert.Contains(snapshots, json => json.Contains(name, StringComparison.Ordinal)));

        // The words are for this one request: the next asks without them.
        JsonElement? next = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks("Depois", request => next = request));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack" }, Guid.NewGuid());
        Assert.False(next!.Value.GetProperty("planDeck").TryGetProperty("requestedDish", out _));
    }

    [Fact]
    public async Task ARequestThatBreaksARestrictionIsRefusedByName()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-blocked");
        var csrf = await CsrfAsync(client);
        await SetVegetarianAsync(client, csrf);
        var (_, sessionId) = await StartSnacksAsync(client, csrf, provider);

        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, request =>
        {
            var content = JsonNode.Parse(FakeAiProvider.BuildDefaultContent(request))!;
            foreach (var candidate in content["candidates"]!.AsArray())
            {
                candidate!["summary"] = "Um salgado caseiro e gostoso para o dia a dia, com bastante sabor e pouco trabalho na cozinha.";
                candidate["additionalIngredients"] = new JsonArray(new JsonObject { ["name"] = "Peito de frango", ["requiredQuantity"] = 300, ["unit"] = "g", ["optional"] = false });
            }

            return AiProviderInvocationResult.Success(content.ToJsonString(), "fake-model", 100, 50);
        });
        var response = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack", request = "coxinha de frango" }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await JsonOf(response);
        Assert.Equal("request_blocked", Code(problem));
        Assert.Equal("vegetarian", problem.GetProperty("errors").GetProperty("restriction")[0].GetString());
    }

    [Fact]
    public async Task ARequestedDishNeverReachesTheSharedLibrary()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-private");
        var csrf = await CsrfAsync(client);
        var (_, sessionId) = await StartSnacksAsync(client, csrf, provider);

        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks("Pedido"));
        var plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack", request = "bolinho da vizinha Ana" }, Guid.NewGuid()));
        var cardId = Group(plan, "snack").GetProperty("options")[0].GetProperty("cardId").GetGuid();
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" });
        var expanded = await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/expansion", new { }, Guid.NewGuid());
        Assert.True(expanded.IsSuccessStatusCode, await expanded.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RecipeIdeas.AnyAsync());
    }

    [Fact]
    public async Task RequestsAreBoundedAndComparedOnlyWithThisPlansDishes()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-bounds");
        using var other = PlanClient(host, "counts-bounds-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        var (plan, sessionId) = await StartSnacksAsync(client, csrf, provider);
        var (otherPlan, _) = await StartSnacksAsync(other, otherCsrf, provider);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack", request = new string('a', 121) }, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "lunch" }, Guid.NewGuid())).StatusCode);
        var foreignCard = Group(otherPlan, "snack").GetProperty("options")[0].GetProperty("cardId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack", likeCardId = foreignCard }, Guid.NewGuid())).StatusCode);

        var like = Group(plan, "snack").GetProperty("options")[0];
        JsonElement? sent = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks("Parecido", request => sent = request));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack", likeCardId = like.GetProperty("cardId").GetGuid() }, Guid.NewGuid());
        var direction = sent!.Value.GetProperty("planDeck").GetProperty("direction");
        Assert.Equal(("similar", like.GetProperty("name").GetString()), (direction.GetProperty("mode").GetString(), direction.GetProperty("referenceDish").GetString()));
        // What is on screen is not offered again.
        Assert.Contains(like.GetProperty("name").GetString(), sent.Value.GetProperty("planDeck").GetProperty("rejectedDishes").EnumerateArray().Select(item => item.GetString()));

        // Another person's plan does not exist for this one, whatever the route.
        var card = like.GetProperty("cardId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, otherCsrf, HttpMethod.Put, $"{Plans}/{sessionId}/preferences", new { usePreferences = false, usePantry = true, goal = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, otherCsrf, HttpMethod.Put, $"{Plans}/{sessionId}/meal-counts", new { breakfast = 0, lunch = 0, dinner = 0, snack = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, otherCsrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{card}/day", new { plannedDate = CountsToday })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, otherCsrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{card}/yield", new { meals = 2 })).StatusCode);
    }

    [Fact]
    public async Task TheSwitchesChangeWhatTheModelSeesButNeverTheRestrictions()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-switches");
        var csrf = await CsrfAsync(client);
        await SetTastesAsync(client, csrf);
        await CreateLotAsync(client, csrf, "Tomate", 1000m);
        var (plan, sessionId) = await StartSnacksAsync(client, csrf, provider);
        Assert.True(plan.GetProperty("preferences").GetProperty("usePreferences").GetBoolean());
        Assert.False(plan.GetProperty("preferences").GetProperty("usePantry").GetBoolean());

        async Task<JsonElement> AskAsync(string tag)
        {
            JsonElement? sent = null;
            provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks(tag, request => sent = request));
            var response = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack", replaceOffers = true }, Guid.NewGuid());
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return sent!.Value;
        }

        var withTastes = await AskAsync("Gosto");
        Assert.Contains("spicy_food", withTastes.GetProperty("userPresets").GetProperty("preferences").EnumerateArray().Select(item => item.GetString()));
        Assert.Empty(withTastes.GetProperty("availabilitySnapshot").GetProperty("items").EnumerateArray());

        plan = await JsonOf(await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/preferences", new { usePreferences = false, usePantry = true, goal = "short" }));
        Assert.Equal((false, true, "short"), (plan.GetProperty("preferences").GetProperty("usePreferences").GetBoolean(), plan.GetProperty("preferences").GetProperty("usePantry").GetBoolean(), plan.GetProperty("preferences").GetProperty("goal").GetString()));

        var withoutTastes = await AskAsync("Semgosto");
        Assert.Empty(withoutTastes.GetProperty("userPresets").GetProperty("preferences").EnumerateArray());
        Assert.Contains("cilantro", withoutTastes.GetProperty("userPresets").GetProperty("restrictions").EnumerateArray().Select(item => item.GetString()));
        Assert.NotEmpty(withoutTastes.GetProperty("availabilitySnapshot").GetProperty("items").EnumerateArray());
        Assert.Equal("short", withoutTastes.GetProperty("planDeck").GetProperty("priority").GetString());

        // A switch replaced what was on offer, and only the person's newest answer is left.
        plan = await JsonOf(await client.GetAsync($"{Plans}/{sessionId}"));
        Assert.All(OptionNames(plan, "snack"), name => Assert.Contains("Semgosto", name, StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/preferences", new { usePreferences = true, usePantry = true, goal = "cheapest" })).StatusCode);

        // The answers stay on the plan and start the next one, without ever touching the profile.
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/abandon", new { });
        var start = await client.GetFromJsonAsync<JsonElement>($"{Plans}/start");
        Assert.True(start.GetProperty("lastPreferences").GetProperty("usePantry").GetBoolean());
        var next = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 2), Guid.NewGuid()));
        Assert.Equal((false, true), (next.GetProperty("preferences").GetProperty("usePreferences").GetBoolean(), next.GetProperty("preferences").GetProperty("usePantry").GetBoolean()));
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/profile/preferences");
        Assert.Contains(profile.GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("stableCode").GetString() == "spicy_food");
    }

    [Fact]
    public async Task TheCountsCanChangeButNeverBelowWhatIsChosen()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-change");
        var csrf = await CsrfAsync(client);
        var (plan, sessionId) = await StartSnacksAsync(client, csrf, provider, snack: 2);
        var ids = Group(plan, "snack").GetProperty("options").EnumerateArray().Select(card => card.GetProperty("cardId").GetGuid()).ToList();
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ids[0]}/decision", new { decision = "accept", mealType = "snack" });
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{ids[1]}/decision", new { decision = "accept", mealType = "snack" });

        var below = await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/meal-counts", new { breakfast = 0, lunch = 3, dinner = 0, snack = 1 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, below.StatusCode);
        Assert.Equal("meal_count_below_chosen", Code(await JsonOf(below)));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/meal-counts", new { breakfast = 0, lunch = 0, dinner = 0, snack = 0 })).StatusCode);

        plan = await JsonOf(await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/meal-counts", new { breakfast = 0, lunch = 3, dinner = 0, snack = 2 }));
        Assert.Equal(5, plan.GetProperty("progress").GetProperty("mealsTotal").GetInt32());
        Assert.Equal(3, Group(plan, "lunch").GetProperty("wanted").GetInt32());
        Assert.Equal(["lunch", "snack"], plan.GetProperty("setup").GetProperty("mealTypes").EnumerateArray().Select(item => item.GetString()!));

        // The change is saved, not just answered.
        plan = await JsonOf(await client.GetAsync($"{Plans}/{sessionId}"));
        Assert.Equal(5, plan.GetProperty("progress").GetProperty("mealsTotal").GetInt32());
    }

    [Fact]
    public async Task ADishCanHaveADayAndCoverSeveralMealsAfterBeingChosen()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-day");
        var csrf = await CsrfAsync(client);
        await CreateMealRecipeAsync(client, csrf, "Bolo Salgado", "snack", "Farinha");
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 4, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var cardId = Group(plan, "snack").GetProperty("options").EnumerateArray().Single(card => card.GetProperty("name").GetString() == "Bolo Salgado").GetProperty("cardId").GetGuid();

        // A dish not chosen yet has neither.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/day", new { plannedDate = CountsToday })).StatusCode);

        // A day outside the period is refused when choosing, and the choice itself keeps its day.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", plannedDate = CountsToday.AddDays(7) })).StatusCode);
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", plannedDate = CountsToday.AddDays(1) }));
        async Task<JsonElement> EntryAsync() => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries")[0];
        Assert.Equal(CountsToday.AddDays(1), DateOnly.Parse((await EntryAsync()).GetProperty("plannedDate").GetString()!));

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/day", new { plannedDate = CountsToday.AddDays(7) })).StatusCode);
        await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/day", new { plannedDate = CountsToday.AddDays(3) });
        Assert.Equal(CountsToday.AddDays(3), DateOnly.Parse((await EntryAsync()).GetProperty("plannedDate").GetString()!));
        await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/day", new { plannedDate = (DateOnly?)null });
        Assert.Equal(JsonValueKind.Null, (await EntryAsync()).GetProperty("plannedDate").ValueKind);

        // One cooking covers three snacks: fuller, and scaled to feed them.
        plan = await JsonOf(await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/yield", new { meals = 3 }));
        Assert.Equal(3, Group(plan, "snack").GetProperty("filled").GetInt32());
        var entry = await EntryAsync();
        Assert.Equal((3, 3), (entry.GetProperty("mealsCovered").GetInt32(), entry.GetProperty("servings").GetInt32()));
        var accepted = plan.GetProperty("accepted")[0];
        Assert.Equal(("yield", 3), (accepted.GetProperty("mode").GetString(), accepted.GetProperty("repeats").GetInt32()));

        // Up to what the plan wants, and back down.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/yield", new { meals = 5 })).StatusCode);
        plan = await JsonOf(await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{cardId}/yield", new { meals = 1 }));
        Assert.Equal(1, Group(plan, "snack").GetProperty("filled").GetInt32());
        entry = await EntryAsync();
        Assert.Equal((1, 1), (entry.GetProperty("mealsCovered").GetInt32(), entry.GetProperty("servings").GetInt32()));
    }

    [Fact]
    public async Task UndoForgetsTheDayAndRepeatingADecisionChangesNothing()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-undo");
        var csrf = await CsrfAsync(client);
        await CreateMealRecipeAsync(client, csrf, "Pão Doce", "snack", "Farinha");
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var cardId = Group(plan, "snack").GetProperty("options").EnumerateArray().Single(card => card.GetProperty("name").GetString() == "Pão Doce").GetProperty("cardId").GetGuid();
        var body = new { decision = "accept", plannedDate = CountsToday.AddDays(2) };

        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", body);
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", body));
        Assert.Equal(1, Group(plan, "snack").GetProperty("filled").GetInt32());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries").GetArrayLength());

        await PostAsync(client, csrf, $"{Plans}/{sessionId}/undo", new { });
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept" });
        var entry = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries")[0];
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("plannedDate").ValueKind);
    }

    [Fact]
    public async Task TodayShowsTheNextDishOfEachMealTypeOfAnAcceptedPlan()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-today");
        var csrf = await CsrfAsync(client);
        await CreateMealRecipeAsync(client, csrf, "Almoco Um", "lunch", "Arroz");
        await CreateMealRecipeAsync(client, csrf, "Almoco Dois", "lunch", "Feijao");
        await CreateMealRecipeAsync(client, csrf, "Lanche Um", "snack", "Farinha");
        await CreateMealRecipeAsync(client, csrf, "Lanche Dois", "snack", "Fuba");

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(lunch: 2, snack: 2, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var cards = plan.GetProperty("groups").EnumerateArray().SelectMany(group => group.GetProperty("options").EnumerateArray())
            .GroupBy(card => card.GetProperty("cardId").GetGuid()).Select(group => group.First()).ToList();
        foreach (var card in cards)
        {
            var response = await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{card.GetProperty("cardId").GetGuid()}/decision", new { decision = "accept" });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        Assert.Equal(4, cards.Count);
        var reviewed = await PostAsync(client, csrf, $"{Plans}/{sessionId}/review", new { });
        Assert.True(reviewed.IsSuccessStatusCode, await reviewed.Content.ReadAsStringAsync());
        var accepted = await PostAsync(client, csrf, $"{Plans}/{sessionId}/acceptance", new { });
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());

        var day = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/today?date={CountsToday:yyyy-MM-dd}");
        // One lunch and one snack: the next of each, not all four.
        Assert.Equal(["lunch", "snack"], day.EnumerateArray().Select(item => item.GetProperty("entry").GetProperty("mealType").GetString()!).Order());
    }

    [Fact]
    public async Task ASavedRecipeFoundByNameCanFillAnyMealButNeverBreaksARestriction()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "counts-choose");
        using var other = PlanClient(host, "counts-choose-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        await SetVegetarianAsync(client, csrf);
        // Saved as a dinner, chosen as a snack: it is the person's own recipe, so no meal rule applies.
        var mine = await CreateRecipeAsync(client, csrf, "Escondidinho da Casa", ("Mandioca", 500m, "g"));
        var meat = await CreateRecipeAsync(client, csrf, "Frango Assado", ("Peito de frango", 500m, "g"));
        var theirs = await CreateRecipeAsync(other, otherCsrf, "Receita Alheia", ("Arroz", 200m, "g"));
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();

        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/recipes/{mine}/choice", new { mealType = "snack" }));
        Assert.Equal(1, Group(plan, "snack").GetProperty("filled").GetInt32());
        var chosen = plan.GetProperty("accepted")[0];
        Assert.Equal(("Escondidinho da Casa", "snack"), (chosen.GetProperty("name").GetString(), chosen.GetProperty("mealType").GetString()));
        Assert.True(plan.GetProperty("canUndo").GetBoolean());

        var blocked = await PostAsync(client, csrf, $"{Plans}/{sessionId}/recipes/{meat}/choice", new { mealType = "snack" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        var problem = await JsonOf(blocked);
        Assert.Equal(("request_blocked", "vegetarian"), (Code(problem), problem.GetProperty("errors").GetProperty("restriction")[0].GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/recipes/{theirs}/choice", new { mealType = "snack" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/recipes/{mine}/choice", new { mealType = "lunch" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"{Plans}/{sessionId}/recipes/{theirs}/choice", new { mealType = "snack" })).StatusCode);
    }

    /// <summary>A taste and a dislike, which travel apart: only the taste is optional.</summary>
    private static async Task SetTastesAsync(HttpClient client, string csrf)
    {
        using var bootstrap = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile") { Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } }) };
        bootstrap.Headers.Add("X-CSRF-TOKEN", csrf);
        var created = await client.SendAsync(bootstrap);
        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new
            {
                entries = new[]
                {
                    new { action = "add", category = "Preference", stableCode = "spicy_food", note = (string?)null },
                    new { action = "add", category = "Dislike", stableCode = "cilantro", note = (string?)null }
                }
            })
        };
        put.Headers.Add("X-CSRF-TOKEN", csrf);
        put.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);
        var response = await client.SendAsync(put);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
