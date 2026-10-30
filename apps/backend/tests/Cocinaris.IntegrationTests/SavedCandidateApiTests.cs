using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0056 Phase 3: save candidates for later and cook them on demand, over HTTP.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task SavedCandidatesAreKeptPerOwnerAndCookedLaterAgainstTheCurrentPantry()
    {
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: true);
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(provider)));
        HttpClient HostClient(string subject)
        {
            var created = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            created.DefaultRequestHeaders.Add("X-Test-Subject", subject);
            return created;
        }

        using var owner = HostClient("saver");
        var csrf = await CsrfAsync(owner);
        await CreateLotAsync(owner, csrf, "Arroz", 500m);
        var onion = await CreateLotAsync(owner, csrf, "Cebola", 300m);
        var session = await (await PostAsync(owner, csrf, "/api/v1/recipes/generation-sessions", new { }, Guid.NewGuid())).Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = session.GetProperty("sessionId").GetGuid();

        // Two candidates of one session can be kept; saving one again returns the same entry.
        var first = await PostAsync(owner, csrf, $"/api/v1/recipes/generation-sessions/{sessionId}/saved-candidates", new { candidateId = "c1" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var savedId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("savedId").GetGuid();
        await PostAsync(owner, csrf, $"/api/v1/recipes/generation-sessions/{sessionId}/saved-candidates", new { candidateId = "c2" });
        var again = await PostAsync(owner, csrf, $"/api/v1/recipes/generation-sessions/{sessionId}/saved-candidates", new { candidateId = "c1" });
        Assert.Equal(savedId, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("savedId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(owner, csrf, $"/api/v1/recipes/generation-sessions/{sessionId}/saved-candidates", new { candidateId = "c9" })).StatusCode);
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/recipes/saved-candidates");
        Assert.Equal(2, list.GetArrayLength());
        Assert.Contains(list.EnumerateArray(), item => item.GetProperty("candidate").GetProperty("candidateId").GetString() == "c1");

        // Another person sees none of them and cannot cook them.
        using var other = HostClient("saver-neighbor");
        var otherCsrf = await CsrfAsync(other);
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/v1/recipes/saved-candidates")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(other, otherCsrf, $"/api/v1/recipes/saved-candidates/{savedId}/recipe", new { }, Guid.NewGuid())).StatusCode);

        // Later the onion is gone: the saved candidate is cooked against the current pantry.
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync($"""UPDATE inventory.lots SET "DeletedAt" = now() WHERE "Id" = {onion}""");
        }

        var key = Guid.NewGuid();
        var cooked = await PostAsync(owner, csrf, $"/api/v1/recipes/saved-candidates/{savedId}/recipe", new { }, key);
        Assert.Equal(HttpStatusCode.Created, cooked.StatusCode);
        var recipe = await cooked.Content.ReadFromJsonAsync<JsonElement>();
        var content = recipe.GetProperty("normalizedRecipeJson").GetString()!;
        Assert.DoesNotContain(onion.ToString("n"), content);
        Assert.Contains("\"sourceType\":\"additional\",\"displayName\":\"Cebola\"", content.Replace(" ", string.Empty));
        var replay = await PostAsync(owner, csrf, $"/api/v1/recipes/saved-candidates/{savedId}/recipe", new { }, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(recipe.GetProperty("recipeId").GetGuid(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipeId").GetGuid());

        // The cooked one leaves the saved list; the other stays and can be removed.
        var remaining = await owner.GetFromJsonAsync<JsonElement>("/api/v1/recipes/saved-candidates");
        Assert.Equal("c2", Assert.Single(remaining.EnumerateArray()).GetProperty("candidate").GetProperty("candidateId").GetString());
        var otherSaved = remaining[0].GetProperty("savedId").GetGuid();
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/recipes/saved-candidates/{otherSaved}");
        delete.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(delete)).StatusCode);
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/v1/recipes/saved-candidates")).GetArrayLength());
    }
}
