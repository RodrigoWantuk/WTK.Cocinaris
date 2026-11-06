using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Cocinaris.Modules.Planning.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>Asking for more dishes (PLAN-0064, then PLAN-0065): an answer changes what is on screen, and full meals and near-duplicates stay out.</summary>
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
        var repeated = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "dinner" }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repeated.StatusCode);
        Assert.Equal("no_more_options", (await JsonOf(repeated)).GetProperty("errorCode").GetString());
        Assert.Equal(shown, First(await client.GetFromJsonAsync<JsonElement>($"{Plans}/{sessionId}")));

        for (var round = 0; round < ShoppingPlanSession.MaxBatches - 1; round++)
        {
            provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck($"Extra{round}"));
            await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid());
        }

        var exhausted = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "dinner" }, Guid.NewGuid());
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
        var failed = await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "dinner", request = "sopa leve" }, Guid.NewGuid());
        Assert.False(failed.IsSuccessStatusCode);

        // The words belonged to that request only.
        JsonElement? next = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Depois", request => next = request));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "dinner" }, Guid.NewGuid());
        Assert.False(next!.Value.GetProperty("planDeck").TryGetProperty("requestedDish", out _));
    }

}

/// <summary>PLAN-0065 Slice 0: fixes found by reviewing the owner's real session.</summary>
public sealed partial class RecipeExecutionApiTests
{
    /// <summary>A deck of the given dishes, keeping everything else valid.</summary>
    private static Func<AiProviderInvocationRequest, AiProviderInvocationResult> DeckOf(params (string Name, string Format, int Minutes)[] dishes) => request =>
    {
        var content = JsonNode.Parse(FakeAiProvider.BuildDefaultContent(request))!;
        var index = 0;
        foreach (var candidate in content["candidates"]!.AsArray())
        {
            var dish = dishes[Math.Min(index, dishes.Length - 1)];
            candidate!["name"] = index < dishes.Length ? dish.Name : $"{dish.Name} {index} extra {Guid.NewGuid():N}";
            candidate["dishFormat"] = dish.Format;
            candidate["summary"] = "Um prato caseiro e gostoso para o dia a dia, com bastante sabor e pouco trabalho na cozinha.";
            candidate["primaryTechnique"] = $"tecnica{Guid.NewGuid():N}";
            candidate["time"]!["totalMinutes"] = dish.Minutes;
            candidate["time"]!["activeMinutes"] = Math.Min(dish.Minutes, 10);
            index++;
        }

        return AiProviderInvocationResult.Success(content.ToJsonString(), "fake-model", 100, 50);
    };

    private static object SnackPlan(DateOnly today) =>
        new { today, days = 7, mealTypes = new[] { "snack" }, mealsOut = 0, cookingStyle = "daily", priority = "short", prepDate = (DateOnly?)null, prepMinutes = (int?)null, people = 1 };

    private static List<string> QueueNames(JsonElement plan) => plan.GetProperty("queue").EnumerateArray().Select(card => card.GetProperty("name").GetString()!).ToList();

    [Fact]
    public async Task FullMealsAreDroppedFromASnackPlanAndTheModelIsToldWhatASnackIs()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-fit");
        var csrf = await CsrfAsync(client);
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, SnackPlan(new DateOnly(2026, 11, 4)), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();

        JsonElement? first = null;
        JsonElement? second = null;
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, request =>
        {
            using var sent = JsonDocument.Parse(request.Payload);
            first = sent.RootElement.Clone();
            return DeckOf(("Espaguete Agridoce", "massa em molho", 25), ("Macarrão Picante", "macarrão", 35), ("Tigela de Carne", "tigela", 50), ("Frango Agridoce", "salteado em frigideira", 25), ("Escondidinho de Milho", "assadeira cremosa", 45), ("Pipoca com Páprica", "petisco crocante", 10))(request);
        });
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, request =>
        {
            using var sent = JsonDocument.Parse(request.Payload);
            second = sent.RootElement.Clone();
            return DeckOf(("Pão de Queijo", "pão de queijo", 25), ("Tapioca de Coalho", "tapioca", 15), ("Bolinho de Polvilho", "bolinho", 40), ("Mini Sanduíche", "sanduíche", 10), ("Cookies de Aveia", "biscoito", 30), ("Wrap de Atum", "wrap", 15))(request);
        });

        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid()));

        var deck = first!.Value.GetProperty("planDeck");
        Assert.Contains("Never a plated main course", deck.GetProperty("mealGuide").GetProperty("snack").GetString());
        Assert.Contains("tastes, not requirements", deck.GetProperty("rule").GetString());

        // Five of six were the wrong kind of dish, so it asked once more, naming them.
        Assert.False(second!.Value.TryGetProperty("repair", out var repair), repair.ToString());
        var refused = second!.Value.GetProperty("planDeck").GetProperty("rejectedDishes").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("Espaguete Agridoce", refused);
        Assert.Contains("Tigela de Carne", refused);

        var names = QueueNames(plan);
        Assert.Contains("Pipoca com Páprica", names);
        Assert.Contains("Pão de Queijo", names);
        Assert.DoesNotContain(names, name => name is "Espaguete Agridoce" or "Macarrão Picante" or "Tigela de Carne" or "Frango Agridoce" or "Escondidinho de Milho");
    }

    [Fact]
    public async Task NearDuplicatesAreOfferedOnce()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-similar");
        var csrf = await CsrfAsync(client);
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, SnackPlan(new DateOnly(2026, 11, 4)), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();

        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, DeckOf(("Pão de Queijo de Frigideira", "a", 25), ("Pão de queijo de frigideira no air fryer", "b", 30), ("Pão de Queijo de Air Fryer", "c", 30), ("Tapioca de Coalho", "d", 15), ("Bolinho de Polvilho", "e", 40), ("Wrap de Atum", "f", 15)));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid()));

        Assert.Single(QueueNames(plan), name => name.Contains("queijo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnAnswerReplacesWhatWasWaitingButKeepsThePersonsOwnRecipes()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "plan-supersede");
        var csrf = await CsrfAsync(client);
        await CreateRecipeAsync(client, csrf, "Receita Minha", ("Cebola", 1m, "unit"));
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, PlanBody(new DateOnly(2026, 11, 4)), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Antigo"));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { }, Guid.NewGuid());

        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Novo"));
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "dinner", replaceOffers = true }, Guid.NewGuid());

        using var scope = host.Services.CreateScope();
        var names = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ShoppingPlanCards.AsNoTracking()
            .Where(card => card.SessionId == sessionId).Select(card => card.SnapshotJson).ToListAsync();
        Assert.DoesNotContain(names, json => json.Contains("Antigo", StringComparison.Ordinal));
        Assert.Contains(names, json => json.Contains("Novo", StringComparison.Ordinal));
        Assert.Contains(names, json => json.Contains("Receita Minha", StringComparison.Ordinal));

        // The next answer still comes first, although deleting cards lowered their count.
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Deck("Outro"));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "dinner" }, Guid.NewGuid()));
        Assert.StartsWith("Outro", First(plan));
    }

}
