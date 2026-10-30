using System.Net.Http.Json;
using System.Text.Json;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0056 Phase 4: pantry name suggestions over HTTP, scoped to the owner.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task NameSuggestionsOfferTheOwnersNamesFirstAndNeverAnotherPersons()
    {
        using var owner = Client("namer");
        var csrf = await CsrfAsync(owner);
        await CreateLotAsync(owner, csrf, "Requeijão da Fazenda Feliz", 200m);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/inventory/name-suggestions?q=requeijao");
        request.Headers.Add("Accept-Language", "pt-BR");

        var mine = await (await owner.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(("Requeijão da Fazenda Feliz", "pantry"), (mine[0].GetProperty("name").GetString(), mine[0].GetProperty("source").GetString()));
        Assert.Contains(mine.EnumerateArray(), item => item.GetProperty("name").GetString() == "Requeijão");

        using var other = Client("namer-neighbor");
        var theirs = await other.GetFromJsonAsync<JsonElement>("/api/v1/inventory/name-suggestions?q=requeijao");
        Assert.DoesNotContain(theirs.EnumerateArray(), item => item.GetProperty("name").GetString()!.Contains("Fazenda", StringComparison.Ordinal));
        Assert.All(theirs.EnumerateArray(), item => Assert.Equal("catalog", item.GetProperty("source").GetString()));
    }
}
