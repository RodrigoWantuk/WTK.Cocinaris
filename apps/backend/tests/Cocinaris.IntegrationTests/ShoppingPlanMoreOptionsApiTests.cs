using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0064: "Quero outras opções" must change what is on screen and follow the answers.</summary>
public sealed partial class RecipeExecutionApiTests
{
    /// <summary>A valid deck whose dishes are all new, because the plan drops repeats by name and by format, technique, and ingredients.</summary>
    private static Func<AiProviderInvocationRequest, AiProviderInvocationResult> Deck(string tag, Action<JsonElement>? onRequest = null) => request =>
    {
        if (onRequest is not null)
        {
            using var sent = JsonDocument.Parse(request.Payload);
            onRequest(sent.RootElement.Clone());
        }

        var content = JsonNode.Parse(FakeAiProvider.BuildDefaultContent(request))!;
        var index = 0;
        foreach (var candidate in content["candidates"]!.AsArray())
        {
            candidate!["name"] = $"{tag} {index}";
            candidate["primaryTechnique"] = $"{tag.ToLowerInvariant()}{index}";
            index++;
        }

        return AiProviderInvocationResult.Success(content.ToJsonString(), "fake-model", 100, 50);
    };

    private static string First(JsonElement plan) => plan.GetProperty("queue")[0].GetProperty("name").GetString()!;

    [Fact]
    public async Task AskingForOtherOptionsPutsTheAnswerOnScreenAndKeepsFollowingIt()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-more");
        var csrf = await CsrfAsync(client);
        await SetVegetarianAsync(client, csrf);
        await CreateLotAsync(client, csrf, "Tomate", 1000m);
        var today = new DateOnly(2026, 11, 4);

        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Primeiro"));
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(today), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid()));
        var onScreen = plan.GetProperty("queue")[0];
        Assert.StartsWith("Primeiro", onScreen.GetProperty("name").GetString());

        JsonElement? sent = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Segundo", request => sent = request));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new
        {
            pantry = "free",
            similarity = "different",
            referenceCardId = onScreen.GetProperty("cardId").GetGuid(),
            hint = "  algo bem leve ",
        }, Guid.NewGuid()));

        // The defect: new dishes used to join the old queue, so the same dish stayed on screen.
        Assert.StartsWith("Segundo", First(plan));

        // The model cannot build on what it cannot see, and restrictions still hold.
        var deck = sent!.Value.GetProperty("planDeck");
        Assert.Empty(sent.Value.GetProperty("availabilitySnapshot").GetProperty("items").EnumerateArray());
        Assert.Empty(deck.GetProperty("shoppingListIngredients").EnumerateArray());
        Assert.Contains("not to plan around", deck.GetProperty("rule").GetString());
        Assert.Equal("different", deck.GetProperty("direction").GetProperty("mode").GetString());
        Assert.Equal(onScreen.GetProperty("name").GetString(), deck.GetProperty("direction").GetProperty("referenceDish").GetString());
        Assert.Equal("algo bem leve", deck.GetProperty("personHint").GetProperty("text").GetString());
        Assert.Contains("untrusted", deck.GetProperty("personHint").GetProperty("treatment").GetString());
        Assert.Contains("vegetarian", sent.Value.GetProperty("userPresets").GetProperty("restrictions").EnumerateArray().Select(item => item.GetString()));

        // The next batch the page asks for by itself follows the same answers.
        JsonElement? later = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Terceiro", request => later = request));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid());
        Assert.Empty(later!.Value.GetProperty("availabilitySnapshot").GetProperty("items").EnumerateArray());
        Assert.Equal("algo bem leve", later.Value.GetProperty("planDeck").GetProperty("personHint").GetProperty("text").GetString());
    }

    [Fact]
    public async Task StickingToThePantrySendsItAndAsksForFewNewIngredients()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-more-use");
        var csrf = await CsrfAsync(client);
        await CreateLotAsync(client, csrf, "Tomate", 1000m);
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Primeiro"));
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(new DateOnly(2026, 11, 4)), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();

        JsonElement? sent = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Segundo", request => sent = request));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { pantry = "use" }, Guid.NewGuid());

        Assert.NotEmpty(sent!.Value.GetProperty("availabilitySnapshot").GetProperty("items").EnumerateArray());
        Assert.Contains("as few new ingredients", sent.Value.GetProperty("planDeck").GetProperty("rule").GetString());
        Assert.False(sent.Value.GetProperty("planDeck").TryGetProperty("direction", out _));
    }

    [Fact]
    public async Task ThePersonIsToldWhenNothingNewCameAndWhenOptionsRunOut()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-more-none");
        var csrf = await CsrfAsync(client);
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Primeiro"));
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(new DateOnly(2026, 11, 4)), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid()));
        var shown = First(plan);

        // Every dish repeats one already on the deck.
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Primeiro"));
        var repeated = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { pantry = "free" }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repeated.StatusCode);
        Assert.Equal("no_more_options", (await JsonOf(repeated)).GetProperty("errorCode").GetString());
        Assert.Equal(shown, First(await client.GetFromJsonAsync<JsonElement>($"{Plans}/{sessionId}")));

        for (var round = 0; round < 5; round++)
        {
            provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck($"Extra{round}"));
            await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid());
        }

        var exhausted = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { pantry = "free" }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exhausted.StatusCode);
        Assert.Equal("no_more_options", (await JsonOf(exhausted)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AFailedRequestChangesNothing()
    {
        // No default answers: a retry after the failure must fail too.
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: false);
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider)));
        using var client = PlanClient(host, "plan-more-fail");
        var csrf = await CsrfAsync(client);
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Primeiro"));
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(new DateOnly(2026, 11, 4)), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid()));

        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, AiProviderInvocationResult.Failure(AiProviderFailureKind.Unavailable));
        var failed = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { pantry = "free", hint = "leve" }, Guid.NewGuid());
        Assert.False(failed.IsSuccessStatusCode);

        JsonElement? next = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Depois", request => next = request));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid());
        Assert.False(next!.Value.GetProperty("planDeck").TryGetProperty("personHint", out _));
    }

    [Fact]
    public async Task AnswersAreValidatedAndAnotherOwnersDishCannotEnterThePrompt()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-more-valid");
        using var other = PlanClient(host, "plan-more-valid-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        var today = new DateOnly(2026, 11, 4);
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Meu"));
        var mine = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(today), Guid.NewGuid()));
        var sessionId = mine.GetProperty("sessionId").GetGuid();
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Alheio"));
        var theirs = await JsonOf(await PostAsync(other, otherCsrf, Plans, PlanBody(today), Guid.NewGuid()));
        theirs = await JsonOf(await PostAsync(other, otherCsrf, $"{Plans}/{theirs.GetProperty("sessionId").GetGuid()}/batches", new { }, Guid.NewGuid()));
        var foreign = theirs.GetProperty("queue")[0].GetProperty("cardId").GetGuid();
        async Task<HttpStatusCode> AskAsync(object body) => (await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", body, Guid.NewGuid())).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await AskAsync(new { pantry = "maybe" }));
        Assert.Equal(HttpStatusCode.BadRequest, await AskAsync(new { similarity = "odd" }));
        Assert.Equal(HttpStatusCode.BadRequest, await AskAsync(new { hint = new string('a', 41) }));
        Assert.Equal(HttpStatusCode.BadRequest, await AskAsync(new { similarity = "different" }));
        Assert.Equal(HttpStatusCode.BadRequest, await AskAsync(new { similarity = "different", referenceCardId = foreign }));
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"{Plans}/{sessionId}/batches", new { pantry = "free" }, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task ADishMadeFromATypedHintNeverReachesTheSharedLibrary()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-more-private");
        var csrf = await CsrfAsync(client);
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(new DateOnly(2026, 11, 4)), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Comdica"));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { hint = "receita da vovó" }, Guid.NewGuid()));
        var card = plan.GetProperty("queue")[0];
        Assert.StartsWith("Comdica", card.GetProperty("name").GetString());
        var cardId = card.GetProperty("cardId").GetGuid();

        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept" });
        var expanded = await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/expansion", new { }, Guid.NewGuid());
        Assert.True(expanded.IsSuccessStatusCode, await expanded.Content.ReadAsStringAsync());

        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await database.RecipeIdeas.AnyAsync());
    }
}
