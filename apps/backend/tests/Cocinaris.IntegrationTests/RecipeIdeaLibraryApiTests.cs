using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Cocinaris.Modules.Recipes.Ideas;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0055 Phase 2: the shared recipe-idea library over HTTP against PostgreSQL.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task IdeasWrittenForOnePersonAreServedToAnotherWithoutAnyAiCall()
    {
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: true);
        await using var host = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recipes:Ideas:NoveltyShare", "0");
            builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider));
        });
        HttpClient HostClient(string subject)
        {
            var created = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            created.DefaultRequestHeaders.Add("X-Test-Subject", subject);
            created.DefaultRequestHeaders.Add("Accept-Language", "en");
            return created;
        }

        // Person A names the pantry in Portuguese and chooses each of the three AI suggestions once.
        using var first = HostClient("idea-writer");
        var firstCsrf = await CsrfAsync(first);
        var writerChicken = await CreateLotAsync(first, firstCsrf, "Peito de frango", 600m);
        await CreateLotAsync(first, firstCsrf, "Arroz", 800m);
        await CreateLotAsync(first, firstCsrf, "Cebola", 300m);
        foreach (var candidateId in new[] { "c1", "c2", "c3" })
        {
            // The fake's third summary says "pantry staples", which the privacy guard rejects (by design).
            provider.Enqueue(AiOperationRegistry.SuggestCandidates, request => AiProviderInvocationResult.Success(FakeAiProvider.BuildDefaultContent(request).Replace("pantry staples", "basic staples"), "fake-model", 100, 50));
            provider.Enqueue(AiOperationRegistry.ExpandSelected, request => AiProviderInvocationResult.Success(GenericExpansion(FakeAiProvider.BuildDefaultContent(request)), "fake-model", 100, 50));
            var session = await RequestSuggestionsAsync(first, firstCsrf);
            Assert.All(session.Candidates, id => Assert.DoesNotContain(RecipeIdeaLibrary.CandidatePrefix, id));
            var selected = await PostAsync(first, firstCsrf, $"/api/v1/recipes/generation-sessions/{session.SessionId}/selection", new { candidateId }, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Created, selected.StatusCode);
        }

        int AiCalls(ApplicationDbContext database, string subject) => database.AiUsageLedgerEntries.Count(entry =>
            entry.Operation.StartsWith("recipe.") && database.Users.Any(user => user.Id == entry.OwnerUserId && user.Subject == subject));
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ideas = await database.RecipeIdeas.AsNoTracking().ToListAsync();
            Assert.Equal(3, ideas.Count);
            Assert.Equal(6, AiCalls(database, "idea-writer"));
            // Generic by construction: catalog names in the output language, no lot ids, no words A typed.
            Assert.All(ideas, idea => Assert.Equal(("en", 0), (idea.Language, idea.Restrictions.Length)));
            Assert.All(ideas, idea => Assert.DoesNotContain("Peito", idea.ContentJson));
            Assert.All(ideas, idea => Assert.DoesNotContain(writerChicken.ToString("n"), idea.ContentJson));
            Assert.Contains(ideas, idea => idea.ContentJson.Contains("\"chicken breast\"", StringComparison.Ordinal));
            // Someone with a restriction the ideas were not generated under never receives them.
            var store = scope.ServiceProvider.GetRequiredService<IRecipeIdeaStore>();
            Assert.Empty(await store.FindCandidatesAsync(new RecipeIdeaQuery("en", null, null, ["peanut_allergy"], null, ["chicken-breast", "rice", "onion"], [], 40), CancellationToken.None));
        }

        // Person B names the same foods in English: suggestions and the recipe come from the library.
        using var second = HostClient("idea-reader");
        var secondCsrf = await CsrfAsync(second);
        var readerChicken = await CreateLotAsync(second, secondCsrf, "Chicken breast", 700m);
        await CreateLotAsync(second, secondCsrf, "Rice", 900m);
        await CreateLotAsync(second, secondCsrf, "Onion", 400m);
        var served = await RequestSuggestionsAsync(second, secondCsrf);
        Assert.Equal(3, served.Candidates.Count);
        Assert.All(served.Candidates, id => Assert.StartsWith(RecipeIdeaLibrary.CandidatePrefix, id));

        var chosen = await PostAsync(second, secondCsrf, $"/api/v1/recipes/generation-sessions/{served.SessionId}/selection", new { candidateId = served.Candidates[0] }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, chosen.StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, AiCalls(database, "idea-reader"));
            var reader = await database.Users.Where(user => user.Subject == "idea-reader").Select(user => user.Id).SingleAsync();
            var revision = await database.RecipeRevisions.AsNoTracking().SingleAsync(item => item.OwnerUserId == reader);
            // The recipe uses B's own lot, so cooking it proposes and consumes B's chicken.
            Assert.Contains(readerChicken.ToString("n"), revision.NormalizedRecipeJson);
            Assert.StartsWith(RecipeIdeaLibrary.CandidatePrefix, revision.SourceCandidateId);
            Assert.Equal(1, await database.RecipeIdeas.SumAsync(idea => idea.TimesSelected));
        }
    }

    private async Task<(Guid SessionId, List<string> Candidates, JsonElement Body)> RequestSuggestionsAsync(HttpClient client, string csrf)
    {
        var response = await PostAsync(client, csrf, "/api/v1/recipes/generation-sessions", new { }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("sessionId").GetGuid(), body.GetProperty("candidates").EnumerateArray().Select(item => item.GetProperty("candidateId").GetString()!).ToList(), body);
    }

    /// <summary>
    /// The fake expansion with no equipment (the test person declares none) and, for the
    /// pantry-free third candidate, rice as an ingredient to buy, so every dish has a main ingredient.
    /// </summary>
    private static string GenericExpansion(string content)
    {
        var root = JsonNode.Parse(content)!;
        var recipe = root["recipe"]!;
        recipe["equipment"] = new JsonArray();
        var ingredients = recipe["ingredients"]!.AsArray();
        if (ingredients.All(item => item!["sourceType"]!.GetValue<string>() == "assumption"))
        {
            ingredients.Add(new JsonObject { ["ingredientId"] = "ing-rice", ["sourceType"] = "additional", ["displayName"] = "Rice", ["requiredQuantity"] = 200, ["unit"] = "g", ["optional"] = false });
            recipe["stages"]![0]!["usesIngredientIds"]!.AsArray().Add("ing-rice");
        }

        return root.ToJsonString();
    }
}
