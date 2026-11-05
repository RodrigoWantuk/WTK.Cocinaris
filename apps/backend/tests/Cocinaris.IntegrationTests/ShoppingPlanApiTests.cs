using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0062: the whole shopping plan journey over HTTP, with the fake provider.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private const string Plans = "/api/v1/planning/shopping-plans";

    private static object PlanBody(DateOnly today, string style = "daily", string? anchor = null, int people = 2) =>
        new { today, days = 3, mealTypes = new[] { "dinner" }, mealsOut = 0, cookingStyle = style, priority = "short", prepDate = style == "daily" ? (DateOnly?)null : today, prepMinutes = style == "daily" ? (int?)null : 120, anchorIngredient = anchor, people };

    private (WebApplicationFactory<Program> Host, FakeAiProvider Provider) PlanHost()
    {
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: true);
        return (_factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider))), provider);
    }

    private static HttpClient PlanClient(WebApplicationFactory<Program> host, string subject)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        return client;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task APlanGoesFromCardsToAListWithoutReservingUntilItIsAccepted()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-journey");
        var csrf = await CsrfAsync(client);
        var today = new DateOnly(2026, 11, 4);
        await CreateLotAsync(client, csrf, "Tomate", 1000m);

        var start = await client.GetFromJsonAsync<JsonElement>($"{Plans}/start");
        Assert.Equal(JsonValueKind.Null, start.GetProperty("current").ValueKind);

        var key = Guid.NewGuid();
        var created = await PostAsync(client, csrf, Plans, PlanBody(today), key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plan = await JsonOf(created);
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        Assert.Equal(3, plan.GetProperty("progress").GetProperty("mealsTotal").GetInt32());
        Assert.True(plan.GetProperty("needsBatch").GetBoolean());

        // The same key replays; a second plan is refused and names the one in progress.
        Assert.Equal(sessionId, (await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(today), key))).GetProperty("sessionId").GetGuid());
        var second = await PostAsync(client, csrf, Plans, PlanBody(today), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("plan_in_progress", (await JsonOf(second)).GetProperty("errorCode").GetString());

        // A batch adds six cards; repeating its key does not call the provider again.
        var batchKey = Guid.NewGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, batchKey));
        Assert.Equal(5, plan.GetProperty("queue").GetArrayLength());
        var names = plan.GetProperty("queue").EnumerateArray().Select(card => card.GetProperty("name").GetString()).ToList();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, batchKey));
        Assert.Equal(names, plan.GetProperty("queue").EnumerateArray().Select(card => card.GetProperty("name").GetString()));

        // Tomato is at home, so a card with it counts it as reused.
        var withTomato = plan.GetProperty("queue").EnumerateArray().First(card => card.GetProperty("ingredients").EnumerateArray().Any(item => item.GetProperty("name").GetString() == "Tomate"));
        Assert.Contains("Tomate", withTomato.GetProperty("reusedNames").EnumerateArray().Select(item => item.GetString()));

        // "Yes, cook again": two meals from one card. Expansion puts them on the menu.
        var cardId = plan.GetProperty("queue")[0].GetProperty("cardId").GetGuid();
        var decision = new { decision = "accept", mode = "again", repeats = 2 };
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", decision));
        Assert.Equal(2, plan.GetProperty("progress").GetProperty("mealsFilled").GetInt32());
        Assert.True(plan.GetProperty("progress").GetProperty("listItemCount").GetInt32() > 0);
        Assert.Equal("pending", plan.GetProperty("accepted")[0].GetProperty("expansionState").GetString());

        // Repeating the decision changes nothing, and review waits for the recipe.
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", decision));
        Assert.Equal(2, plan.GetProperty("progress").GetProperty("mealsFilled").GetInt32());
        var waiting = await PostAsync(client, csrf, $"{Plans}/{sessionId}/review", new { });
        Assert.Equal("expansions_pending", (await JsonOf(waiting)).GetProperty("errorCode").GetString());

        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/expansion", new { }));
        Assert.Equal("ready", plan.GetProperty("accepted")[0].GetProperty("expansionState").GetString());
        var menu = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}");
        Assert.Equal(2, menu.GetProperty("entries").GetArrayLength());

        // Retrying the expansion creates neither a recipe nor a meal.
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/expansion", new { });
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries").GetArrayLength());

        // A refused card takes a reason. Undo restores it, once.
        var refused = plan.GetProperty("queue")[0].GetProperty("cardId").GetGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{refused}/decision", new { decision = "reject" }));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{refused}/reason", new { reason = "tired_of" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, csrf, HttpMethod.Put, $"{Plans}/{sessionId}/cards/{refused}/reason", new { reason = "because" })).StatusCode);
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/undo", new { }));
        Assert.Contains(refused, plan.GetProperty("queue").EnumerateArray().Select(card => card.GetProperty("cardId").GetGuid()));
        Assert.False(plan.GetProperty("canUndo").GetBoolean());

        // A saved-free accept of one more meal fills the plan.
        var last = plan.GetProperty("queue")[0].GetProperty("cardId").GetGuid();
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{last}/decision", new { decision = "accept" });
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{last}/expansion", new { }));
        Assert.Equal(3, plan.GetProperty("progress").GetProperty("mealsFilled").GetInt32());
        Assert.Equal(0, plan.GetProperty("queue").GetArrayLength());

        // Drafts reserve nothing and stay off "Hoje"; the list already works.
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/reservations?from={today:yyyy-MM-dd}")).GetProperty("lots").EnumerateArray());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/today?date={today:yyyy-MM-dd}")).EnumerateArray());
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={today:yyyy-MM-dd}");
        Assert.True(list.GetProperty("items").GetArrayLength() > 0);

        // Review sets an order. Accepting reserves, and "Hoje" shows only the next flexible meal.
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/review", new { }));
        Assert.Equal("reviewing", plan.GetProperty("status").GetString());
        menu = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}");
        Assert.Equal([1, 2, 3], menu.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("suggestedOrder").GetInt32()).Order());
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/acceptance", new { }));
        Assert.Equal("finished", plan.GetProperty("status").GetString());
        Assert.Equal("accepted", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("status").GetString());
        var day = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/today?date={today:yyyy-MM-dd}");
        Assert.Equal(1, day.GetArrayLength());
        Assert.Equal(1, day[0].GetProperty("entry").GetProperty("suggestedOrder").GetInt32());

        // The next plan offers the last answers as defaults and starts fresh.
        start = await client.GetFromJsonAsync<JsonElement>($"{Plans}/start");
        Assert.Equal("short", start.GetProperty("lastSetup").GetProperty("priority").GetString());
        Assert.Equal(JsonValueKind.Null, start.GetProperty("current").ValueKind);
    }

    [Fact]
    public async Task RestrictedDishesNeverBecomeCardsAndAnotherOwnerSeesNothing()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-guard");
        var csrf = await CsrfAsync(client);
        var today = new DateOnly(2026, 11, 4);
        await SetVegetarianAsync(client, csrf);

        string? payload = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, request =>
        {
            payload = request.Payload;
            return AiProviderInvocationResult.Success(FakeAiProvider.BuildDefaultContent(request), "fake-model", 100, 50);
        });
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(today, anchor: "abóbora"), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid()));

        // The fake's chicken dishes are dropped, whatever the model returned.
        var names = plan.GetProperty("queue").EnumerateArray().Select(card => card.GetProperty("name").GetString()!).ToList();
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, name => name.Contains("frango", StringComparison.OrdinalIgnoreCase));

        // The request carried the restriction, and the typed anchor stays in its own untrusted field.
        using var sent = JsonDocument.Parse(payload!);
        Assert.Contains("vegetarian", sent.RootElement.GetProperty("userPresets").GetProperty("restrictions").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("abóbora", sent.RootElement.GetProperty("planDeck").GetProperty("anchorIngredient").GetProperty("text").GetString());

        using var other = PlanClient(host, "plan-guard-other");
        var otherCsrf = await CsrfAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Plans}/{sessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"{Plans}/{sessionId}/undo", new { })).StatusCode);
        var card = plan.GetProperty("queue")[0].GetProperty("cardId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"{Plans}/{sessionId}/cards/{card}/decision", new { decision = "accept" })).StatusCode);

        // Discarding archives the menu and frees the person to start again.
        var menuId = plan.GetProperty("menuId").GetGuid();
        Assert.Equal("abandoned", (await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/abandon", new { }))).GetProperty("status").GetString());
        Assert.Equal("archived", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, csrf, Plans, PlanBody(today), Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task SavedRecipesWorkWithoutAiAndAFailedBatchIsGivenBack()
    {
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: false);
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider)));
        using var client = PlanClient(host, "plan-saved");
        var csrf = await CsrfAsync(client);
        var today = new DateOnly(2026, 11, 4);
        var recipe = await CreateRecipeAsync(client, csrf, "Arroz da casa", ("Arroz", 200m, "g"));

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(today), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var card = plan.GetProperty("queue").EnumerateArray().Single();
        Assert.Equal("saved", card.GetProperty("source").GetString());
        Assert.Equal(1, card.GetProperty("newItemCount").GetInt32());

        // The provider is down: the request fails, and the plan still works.
        var failed = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid());
        Assert.False(failed.IsSuccessStatusCode);
        plan = await client.GetFromJsonAsync<JsonElement>($"{Plans}/{sessionId}");
        Assert.True(plan.GetProperty("needsBatch").GetBoolean());

        // "Yields 2": one entry, double portions, two meals. No expansion is needed.
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{card.GetProperty("cardId").GetGuid()}/decision", new { decision = "accept", mode = "yield", repeats = 2 }));
        Assert.Equal("ready", plan.GetProperty("accepted")[0].GetProperty("expansionState").GetString());
        var entry = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries").EnumerateArray().Single();
        Assert.Equal((recipe, 4, 2), (entry.GetProperty("recipeId").GetGuid(), entry.GetProperty("servings").GetInt32(), entry.GetProperty("mealsCovered").GetInt32()));

        // Undo removes the meal; the saved recipe is untouched.
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/undo", new { });
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries").EnumerateArray());

        // Too many portions for one cooking, and bad answers, are refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{card.GetProperty("cardId").GetGuid()}/decision", new { decision = "accept", repeats = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, Plans, new { today, days = 99, mealTypes = new[] { "dinner" }, cookingStyle = "daily", priority = "short" }, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task AFinishedPlanCanBeRepeatedWithoutDeletedOrNowRestrictedDishes()
    {
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: false);
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider)));
        using var client = PlanClient(host, "plan-repeat");
        var csrf = await CsrfAsync(client);
        var firstDay = new DateOnly(2026, 11, 4);
        var body = new { today = firstDay, days = 7, mealTypes = new[] { "dinner" }, mealsOut = 0, cookingStyle = "mix", priority = "short", prepDate = firstDay, prepMinutes = 120, people = 2 };
        await CreateRecipeAsync(client, csrf, "Arroz", ("Arroz", 200m, "g"));
        await CreateRecipeAsync(client, csrf, "Feijao", ("Feijao", 200m, "g"));
        var deleted = await CreateRecipeAsync(client, csrf, "Sopa", ("Cenoura", 200m, "g"));
        await CreateRecipeAsync(client, csrf, "Frango assado", ("Frango", 200m, "g"));
        await CreateRecipeAsync(client, csrf, "Lentilha", ("Lentilha", 200m, "g"));

        var plan = await JsonOf(await PostAsync(client, csrf, Plans, body, Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var firstMenu = plan.GetProperty("menuId").GetGuid();
        var cards = plan.GetProperty("queue").EnumerateArray().ToDictionary(card => card.GetProperty("name").GetString()!, card => card.GetProperty("cardId").GetGuid());
        string Decide(string name) => $"{Plans}/{sessionId}/cards/{cards[name]}/decision";
        await PostAsync(client, csrf, Decide("Arroz"), new { decision = "accept", mode = "yield", repeats = 2 });
        await PostAsync(client, csrf, Decide("Feijao"), new { decision = "accept", mode = "again", repeats = 2 });
        await PostAsync(client, csrf, Decide("Sopa"), new { decision = "accept" });
        await PostAsync(client, csrf, Decide("Frango assado"), new { decision = "accept" });

        // Not accepted yet: nothing to repeat, and an open plan's menu cannot be repeated.
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"{Plans}/start")).GetProperty("recent").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, csrf, Plans, new { today = firstDay, fromMenuId = firstMenu }, Guid.NewGuid())).StatusCode);
        Assert.True((await PostAsync(client, csrf, $"{Plans}/{sessionId}/acceptance", new { })).IsSuccessStatusCode);

        var recent = (await client.GetFromJsonAsync<JsonElement>($"{Plans}/start")).GetProperty("recent").EnumerateArray().Single();
        Assert.Equal(firstMenu, recent.GetProperty("menuId").GetGuid());
        Assert.Equal(4, recent.GetProperty("dishes").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, csrf, $"/api/v1/recipes/{deleted}/archive", new { archived = true })).StatusCode);
        await SetVegetarianAsync(client, csrf);

        // Another owner cannot repeat this plan or learn that it exists.
        using var stranger = PlanClient(host, "plan-repeat-stranger");
        var strangerCsrf = await CsrfAsync(stranger);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(stranger, strangerCsrf, Plans, new { today = firstDay, fromMenuId = firstMenu }, Guid.NewGuid())).StatusCode);

        // A week later: the same answers and the new period. The deleted dish and the one that now
        // conflicts with a restriction are gone, and no AI was needed.
        var later = firstDay.AddDays(7);
        var key = Guid.NewGuid();
        var created = await PostAsync(client, csrf, Plans, new { today = later, fromMenuId = firstMenu }, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var again = await JsonOf(created);
        var newMenu = again.GetProperty("menuId").GetGuid();
        Assert.NotEqual(firstMenu, newMenu);
        Assert.Equal(later, again.GetProperty("setup").GetProperty("prepDate").Deserialize<DateOnly>());
        Assert.Equal(7, again.GetProperty("setup").GetProperty("days").GetInt32());
        var accepted = again.GetProperty("accepted").EnumerateArray().ToDictionary(card => card.GetProperty("name").GetString()!);
        Assert.Equal(["Arroz", "Feijao"], accepted.Keys.Order());
        Assert.Equal(("yield", 2), (accepted["Arroz"].GetProperty("mode").GetString(), accepted["Arroz"].GetProperty("repeats").GetInt32()));
        Assert.Equal(("again", 2), (accepted["Feijao"].GetProperty("mode").GetString(), accepted["Feijao"].GetProperty("repeats").GetInt32()));
        Assert.Equal(4, again.GetProperty("progress").GetProperty("mealsFilled").GetInt32());
        Assert.Equal(["Lentilha"], again.GetProperty("queue").EnumerateArray().Select(card => card.GetProperty("name").GetString()!));

        // Still a draft that reserves nothing, with the meals in the new period.
        var menu = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{newMenu}");
        Assert.Equal("draft", menu.GetProperty("status").GetString());
        var entries = menu.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, entry => entry.GetProperty("mealsCovered").GetInt32() == 2 && entry.GetProperty("servings").GetInt32() == 4 && entry.GetProperty("plannedDate").Deserialize<DateOnly>() == later);

        // A retry finds the same plan and adds no meal.
        Assert.Equal(again.GetProperty("sessionId").GetGuid(), (await JsonOf(await PostAsync(client, csrf, Plans, new { today = later, fromMenuId = firstMenu }, key))).GetProperty("sessionId").GetGuid());
        Assert.Equal(3, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{newMenu}")).GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task ABasePreparedAheadCoversTheDishesThatUseIt()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-base");
        var csrf = await CsrfAsync(client);
        var today = new DateOnly(2026, 11, 4);
        var day = today.ToString("yyyy-MM-dd");
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(today, "mix"), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid()));

        // The base stays out of sight until a dish that uses it is chosen.
        Assert.DoesNotContain(plan.GetProperty("queue").EnumerateArray(), card => card.GetProperty("component").ValueKind == JsonValueKind.Object);
        var cards = plan.GetProperty("queue").EnumerateArray().ToList();
        var dish = cards.First(card => card.GetProperty("ingredients").EnumerateArray().Any(item => item.GetProperty("name").GetString() == "Frango desfiado"));
        var dishId = dish.GetProperty("cardId").GetGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{dishId}/decision", new { decision = "accept" }));
        var offered = plan.GetProperty("queue")[0];
        Assert.Equal("Frango desfiado", offered.GetProperty("component").GetProperty("outputName").GetString());
        Assert.Equal(700m, offered.GetProperty("component").GetProperty("outputQuantity").GetDecimal());
        Assert.Equal("Gram", offered.GetProperty("component").GetProperty("outputUnit").GetString());
        var baseId = offered.GetProperty("cardId").GetGuid();

        // A base is prepared once, fills no meal, and takes its item off the estimate.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{baseId}/decision", new { decision = "accept", mode = "yield", repeats = 2 })).StatusCode);
        var filled = plan.GetProperty("progress").GetProperty("mealsFilled").GetInt32();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{baseId}/decision", new { decision = "accept" }));
        Assert.Equal(filled, plan.GetProperty("progress").GetProperty("mealsFilled").GetInt32());
        Assert.Contains(plan.GetProperty("accepted").EnumerateArray(), card => card.GetProperty("isComponent").GetBoolean());

        foreach (var id in new[] { dishId, baseId })
        {
            plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{id}/expansion", new { }));
        }

        var menu = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}");
        var component = menu.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("kind").GetString() == "component");
        Assert.Equal(("Frango desfiado", 700m, "Gram"), (component.GetProperty("producesName").GetString(), component.GetProperty("producesQuantity").GetDecimal(), component.GetProperty("producesUnit").GetString()));
        Assert.Equal(today, component.GetProperty("plannedDate").Deserialize<DateOnly>());
        Assert.Equal(JsonValueKind.Null, component.GetProperty("mealType").ValueKind);

        // It is cooked first, and the dish's item comes from it instead of the store.
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/review", new { });
        menu = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}");
        Assert.Equal(1, menu.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("kind").GetString() == "component").GetProperty("suggestedOrder").GetInt32());
        JsonElement Line(JsonElement list, string name) => list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("displayName").GetString() == name);
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={day}");
        Assert.Equal(("from_prep", 0m), (Line(list, "Frango desfiado").GetProperty("category").GetString(), Line(list, "Frango desfiado").GetProperty("toBuyQuantity").GetDecimal()));

        Assert.True((await PostAsync(client, csrf, $"{Plans}/{sessionId}/acceptance", new { })).IsSuccessStatusCode);
        var entryId = component.GetProperty("entryId").GetGuid();
        var recipeId = component.GetProperty("recipeId").GetGuid();

        // Nothing tells the person to buy what the base will make.
        var prep = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/preparation?from={day}&days=7");
        Assert.DoesNotContain(prep.GetProperty("reminders").EnumerateArray(), item => item.GetProperty("kind").GetString() == "buy" && item.GetProperty("itemName").GetString() == "Frango desfiado");

        var execution = await JsonOf(await PostAsync(client, csrf, $"/api/v1/recipes/{recipeId}/executions", new { menuEntryId = entryId }));
        var executionId = execution.GetProperty("executionId").GetGuid();
        var proposed = execution.GetProperty("plan");
        Assert.True(proposed.GetProperty("isComponent").GetBoolean());
        Assert.Equal(700m, proposed.GetProperty("proposed").EnumerateArray().Sum(item => item.GetProperty("quantity").GetDecimal()));
        Assert.All(proposed.GetProperty("proposed").EnumerateArray(), item => Assert.Equal("Refrigerator", item.GetProperty("storageLocation").GetString()));

        // Bad outputs are refused and change nothing: too many, the wrong unit, or with a leftover.
        var completion = $"/api/v1/recipes/executions/{executionId}/completion";
        var part = new { quantity = 350m, unit = "Gram", storageLocation = "Refrigerator" };
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, completion, new { storedOutputs = Enumerable.Repeat(part, 5) }, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, completion, new { storedOutputs = new[] { new { quantity = 2m, unit = "Unit", storageLocation = "Refrigerator" } } }, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, completion, new { storedOutputs = new[] { part }, leftover = new { portions = 1, storageLocation = "Refrigerator" } }, Guid.NewGuid())).StatusCode);

        // A stale lot rolls everything back: the execution stays open and no portion appears.
        var chicken = await CreateLotAsync(client, csrf, "Peito de frango", 800m);
        var lots = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?pageSize=50");
        Assert.Single(lots.GetProperty("items").EnumerateArray());
        var stale = await PostAsync(client, csrf, completion, new { consumptions = new[] { new { lotId = chicken, quantity = 100m, unit = "Gram", version = Guid.NewGuid() } }, storedOutputs = new[] { part, part } }, Guid.NewGuid());
        Assert.False(stale.IsSuccessStatusCode);
        Assert.Equal("InProgress", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/recipes/executions/{executionId}")).GetProperty("status").GetString());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?pageSize=50")).GetProperty("items").EnumerateArray());

        // Two portions in one batch, named for what the base makes, frozen or not as chosen.
        var key = Guid.NewGuid();
        var body = new { storedOutputs = new[] { part, new { quantity = 350m, unit = "Gram", storageLocation = "Freezer" } } };
        var done = await PostAsync(client, csrf, completion, body, key);
        Assert.True(done.IsSuccessStatusCode, await done.Content.ReadAsStringAsync());
        Assert.True((await PostAsync(client, csrf, completion, body, key)).IsSuccessStatusCode);
        var stored = (await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?pageSize=50")).GetProperty("items").EnumerateArray().Where(lot => lot.GetProperty("productName").GetString() == "Frango desfiado").ToList();
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, lot => lot.GetProperty("storageLocation").GetString() == "Freezer");

        // The base left the plan, and its real lots now cover the dish.
        list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}/shopping?from={day}");
        Assert.Equal(0m, Line(list, "Frango desfiado").GetProperty("toBuyQuantity").GetDecimal());
        Assert.NotEqual("from_prep", Line(list, "Frango desfiado").GetProperty("category").GetString());
        Assert.Equal(700m, Line(list, "Frango desfiado").GetProperty("availableQuantity").GetDecimal());
    }

    [Fact]
    public async Task ADishThatCoversSeveralMealsStoresPortionsAndRemindsUntilEaten()
    {
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: false);
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider)));
        using var client = PlanClient(host, "plan-portions");
        var csrf = await CsrfAsync(client);
        var today = new DateOnly(2026, 11, 4);
        var recipe = await CreateRecipeAsync(client, csrf, "Lasanha", ("Massa", 200m, "g"));
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(today, "mix"), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var card = plan.GetProperty("queue")[0].GetProperty("cardId").GetGuid();
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{card}/decision", new { decision = "accept", mode = "yield", repeats = 3 });
        Assert.True((await PostAsync(client, csrf, $"{Plans}/{sessionId}/acceptance", new { })).IsSuccessStatusCode);
        var entry = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/menus/{menuId}")).GetProperty("entries")[0];

        var execution = await JsonOf(await PostAsync(client, csrf, $"/api/v1/recipes/{recipe}/executions", new { servings = 6, menuEntryId = entry.GetProperty("entryId").GetGuid() }));
        var proposed = execution.GetProperty("plan");
        Assert.False(proposed.GetProperty("isComponent").GetBoolean());
        // With the table off, every proposal is the refrigerator and no date is claimed.
        Assert.Equal([2m, 2m], proposed.GetProperty("proposed").EnumerateArray().Select(item => item.GetProperty("quantity").GetDecimal()));
        Assert.All(proposed.GetProperty("proposed").EnumerateArray(), item => Assert.Equal("Refrigerator", item.GetProperty("storageLocation").GetString()));

        var completion = $"/api/v1/recipes/executions/{execution.GetProperty("executionId").GetGuid()}/completion";
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, completion, new { storedOutputs = new[] { new { quantity = 7m, unit = "Unit", storageLocation = "Freezer" } } }, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, completion, new { storedOutputs = new[] { new { quantity = 1.5m, unit = "Unit", storageLocation = "Freezer" } } }, Guid.NewGuid())).StatusCode);
        var parts = new[] { new { quantity = 2m, unit = "Unit", storageLocation = "Refrigerator" }, new { quantity = 2m, unit = "Unit", storageLocation = "Freezer" } };
        Assert.True((await PostAsync(client, csrf, completion, new { storedOutputs = parts }, Guid.NewGuid())).IsSuccessStatusCode);

        async Task<List<JsonElement>> Ready() => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/planning/preparation?from={today:yyyy-MM-dd}&days=2")).GetProperty("reminders").EnumerateArray().Where(item => item.GetProperty("kind").GetString() == "ready_portions").ToList();
        var ready = await Ready();
        Assert.Equal(2, ready.Count);
        Assert.All(ready, item => Assert.Equal("Lasanha", item.GetProperty("itemName").GetString()));

        // Eating the last portions of a lot makes its reminder go away by itself.
        var lotId = ready[0].GetProperty("lotId").GetGuid();
        var lot = await client.GetAsync($"/api/v1/inventory/lots/{lotId}");
        var etag = lot.Headers.ETag!.Tag;
        using var adjust = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/lots/{lotId}/adjustments") { Content = JsonContent.Create(new { type = "Consume", value = 2m, reasonCode = "ui_consume" }) };
        adjust.Headers.Add("X-CSRF-TOKEN", csrf);
        adjust.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        adjust.Headers.TryAddWithoutValidation("If-Match", etag);
        var eaten = await client.SendAsync(adjust);
        Assert.True(eaten.IsSuccessStatusCode, await eaten.Content.ReadAsStringAsync());
        Assert.Single(await Ready());
    }

    [Fact]
    public async Task ThePreparationRouteExistsOnlyAfterAcceptanceAndResumesWhereItStopped()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-route");
        var csrf = await CsrfAsync(client);
        var today = new DateOnly(2026, 11, 4);
        await CreateRecipeAsync(client, csrf, "Feijão da rota", ("Feijão", 200m, "g"));
        await CreateRecipeAsync(client, csrf, "Arroz da rota", ("Arroz", 200m, "g"));
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(today, "mix"), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        var menuId = plan.GetProperty("menuId").GetGuid();
        var route = $"{Plans}/{sessionId}/route";
        foreach (var card in plan.GetProperty("queue").EnumerateArray().Select(item => item.GetProperty("cardId").GetGuid()).ToList())
        {
            await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{card}/decision", new { decision = "accept", mode = "yield", repeats = 2 });
        }

        // A draft reserves nothing, so it cannot be cooked yet.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?menuId={menuId}")).GetProperty("sessionId").ValueKind);
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/acceptance", new { });

        var view = await client.GetFromJsonAsync<JsonElement>(route);
        Assert.Equal(2, view.GetProperty("recipes").GetArrayLength());
        Assert.Equal(120, view.GetProperty("budgetMinutes").GetInt32());
        var tasks = view.GetProperty("tasks").EnumerateArray().ToList();
        Assert.NotEmpty(tasks);
        Assert.Equal(tasks.Max(task => task.GetProperty("endMinute").GetInt32()), view.GetProperty("totalMinutes").GetInt32());
        Assert.Empty(view.GetProperty("progress").GetProperty("done").EnumerateArray());
        Assert.Equal(sessionId, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?menuId={menuId}")).GetProperty("sessionId").GetGuid());
        Assert.Equal(sessionId, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?date=2026-11-04")).GetProperty("sessionId").GetGuid());
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?date=2026-11-05")).GetProperty("sessionId").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Plans}/route-link")).StatusCode);

        async Task<HttpResponseMessage> SaveAsync(HttpClient caller, string token, object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{route}/progress") { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-TOKEN", token);
            return await caller.SendAsync(request);
        }

        // Progress survives a reload, and a key no longer on the route is dropped.
        var first = tasks[0].GetProperty("taskKey").GetString()!;
        var endsAt = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var saved = await SaveAsync(client, csrf, new { done = new[] { first, "gone:x" }, skipped = Array.Empty<string>(), timers = new[] { new { taskKey = first, endsAt } } });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var progress = (await client.GetFromJsonAsync<JsonElement>(route)).GetProperty("progress");
        Assert.Equal([first], progress.GetProperty("done").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(endsAt, progress.GetProperty("timers")[0].GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(client, csrf, new { done = new[] { first } })).StatusCode);

        // Another owner sees nothing and changes nothing.
        using var other = PlanClient(host, "plan-route-other");
        var otherCsrf = await CsrfAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SaveAsync(other, otherCsrf, new { done = Array.Empty<string>(), skipped = Array.Empty<string>(), timers = Array.Empty<object>() })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await other.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?menuId={menuId}")).GetProperty("sessionId").ValueKind);

        // Cooking a dish marks its tasks done; when all are cooked, nothing is left to open.
        foreach (var recipe in view.GetProperty("recipes").EnumerateArray())
        {
            var execution = await JsonOf(await PostAsync(client, csrf, $"/api/v1/recipes/{recipe.GetProperty("recipeId").GetGuid()}/executions", new { servings = recipe.GetProperty("servings").GetInt32(), menuEntryId = recipe.GetProperty("entryId").GetGuid() }));
            var completed = await PostAsync(client, csrf, $"/api/v1/recipes/executions/{execution.GetProperty("executionId").GetGuid()}/completion", new { consumptions = Array.Empty<object>() }, Guid.NewGuid());
            Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync());
        }

        view = await client.GetFromJsonAsync<JsonElement>(route);
        Assert.All(view.GetProperty("recipes").EnumerateArray(), recipe => Assert.Equal("cooked", recipe.GetProperty("status").GetString()));
        Assert.Equal(tasks.Count, view.GetProperty("progress").GetProperty("done").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>($"{Plans}/route-link?menuId={menuId}")).GetProperty("sessionId").ValueKind);
    }

    private static async Task SetVegetarianAsync(HttpClient client, string csrf)
    {
        using var bootstrap = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/profile") { Content = JsonContent.Create(new { displayName = new { action = "confirm", value = "Alex", durability = "durable" } }) };
        bootstrap.Headers.Add("X-CSRF-TOKEN", csrf);
        var created = await client.SendAsync(bootstrap);
        using var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/profile/preferences")
        {
            Content = JsonContent.Create(new { entries = new[] { new { action = "add", category = "DietaryPattern", stableCode = "vegetarian", note = (string?)null } } })
        };
        put.Headers.Add("X-CSRF-TOKEN", csrf);
        put.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.Tag);
        var response = await client.SendAsync(put);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
