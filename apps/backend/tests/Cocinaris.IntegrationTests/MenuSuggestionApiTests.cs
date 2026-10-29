using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0029 Phase 2: AI suggestions for a menu slot over HTTP, with the fake provider.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task MenuSlotSuggestionsSeeOnlyFreeStockAndThePlannedMeals()
    {
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: true);
        // The shared host has no AI provider; this test's host uses the fake one.
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider)));
        HttpClient HostClient(string subject)
        {
            var created = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            created.DefaultRequestHeaders.Add("X-Test-Subject", subject);
            return created;
        }

        using var client = HostClient("menu-ai");
        var csrf = await CsrfAsync(client);
        await CreateLotAsync(client, csrf, "Arroz", 500m);
        var risotto = await CreateRecipeAsync(client, csrf, "Risoto", ("Arroz", 300m, "g"));
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var menuId = (await (await PostAsync(client, csrf, "/api/v1/planning/menus", new { startDate = day, endDate = day.AddDays(6) }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("menuId").GetGuid();
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId = risotto, plannedDate = day, mealType = "dinner" }, Guid.NewGuid());
        await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/acceptance", new { });

        string? payload = null;
        provider.Enqueue(AiOperationRegistry.SuggestCandidates, request =>
        {
            payload = request.Payload;
            return AiProviderInvocationResult.Success(FakeAiProvider.BuildDefaultContent(request), "fake-model", 100, 50);
        });

        var response = await PostAsync(client, csrf, "/api/v1/recipes/generation-sessions", new { menuPlanning = new { menuId, plannedDate = day.AddDays(1), mealType = "lunch", servings = 3 } }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        var candidates = session.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal(3, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal(("lunch", 3), (candidate.GetProperty("targetMealType").GetString(), candidate.GetProperty("servings").GetInt32())));

        // The provider saw the slot, the meal already planned, and only the 200 g the risotto does not reserve.
        using var sent = JsonDocument.Parse(payload!);
        var execution = sent.RootElement.GetProperty("executionContext");
        Assert.Equal(("menu_planning", day.AddDays(1).ToString("yyyy-MM-dd"), "lunch"), (execution.GetProperty("executionMode").GetString(), execution.GetProperty("plannedDate").GetString(), execution.GetProperty("targetMealType").GetString()));
        Assert.Equal(["Risoto"], execution.GetProperty("alreadyPlannedRecipeNames").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(200m, sent.RootElement.GetProperty("availabilitySnapshot").GetProperty("items").EnumerateArray().Single().GetProperty("quantity").GetDecimal());
        Assert.Contains("planned_purchase_reuse", sent.RootElement.GetProperty("candidatePolicy").GetProperty("recommendedStrategySet").EnumerateArray().Select(item => item.GetString()));

        // Choosing a candidate saves a recipe; the frontend then adds it to the slot.
        var selected = await PostAsync(client, csrf, $"/api/v1/recipes/generation-sessions/{session.GetProperty("sessionId").GetGuid()}/selection", new { candidateId = candidates[0].GetProperty("candidateId").GetString() }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, selected.StatusCode);
        var recipeId = (await selected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipeId").GetGuid();
        var menu = await (await PostAsync(client, csrf, $"/api/v1/planning/menus/{menuId}/entries", new { recipeId, plannedDate = day.AddDays(1), mealType = "lunch", servings = 3 }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, menu.GetProperty("entries").GetArrayLength());

        // A slot outside the period is refused, and another owner's menu is not found.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, csrf, "/api/v1/recipes/generation-sessions", new { menuPlanning = new { menuId, plannedDate = day.AddDays(10), servings = 2 } }, Guid.NewGuid())).StatusCode);
        using var other = HostClient("menu-ai-other");
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, await CsrfAsync(other), "/api/v1/recipes/generation-sessions", new { menuPlanning = new { menuId, plannedDate = day, servings = 2 } }, Guid.NewGuid())).StatusCode);
    }
}
