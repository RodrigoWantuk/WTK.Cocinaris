using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0056 Phase 4: pantry name suggestions while typing.</summary>
public sealed class InventoryNameSuggestionTests
{
    private static InventoryNameSuggestionService Service(params string[] usedNames) =>
        new(new User(), new Names(usedNames), IngredientCatalog.Default);

    [Fact]
    public async Task NamesThePersonUsedComeFirstAndAccentsAndCaseAreIgnored()
    {
        var suggestions = await Service("Requeijão cremoso Vigor", "Arroz Branco Cru").SuggestAsync("REQUEIJAO", "pt", CancellationToken.None);

        Assert.Equal(("Requeijão cremoso Vigor", "pantry"), (suggestions[0].Name, suggestions[0].Source));
        Assert.Contains(suggestions, item => item is { Name: "Requeijão", Source: "catalog" });
    }

    [Fact]
    public async Task AStartMatchRanksBeforeAWordMatchAndTheLanguageIsRespected()
    {
        var suggestions = await Service().SuggestAsync("ceb", "pt", CancellationToken.None);

        Assert.Equal("Cebola", suggestions[0].Name);
        Assert.All(suggestions, item => Assert.Equal("catalog", item.Source));
        Assert.DoesNotContain(suggestions, item => item.Name.Equals("Onion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains((await Service().SuggestAsync("onion", "en", CancellationToken.None)), item => item.Name == "Onion");
        // "verde" is a later word of "cebolinha verde": it still matches, after names that start with the text.
        var words = await Service().SuggestAsync("verde", "pt", CancellationToken.None);
        Assert.Contains(words, item => item.Name == "Cebolinha verde");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData(null)]
    public async Task TooShortTextSuggestsNothing(string? text) =>
        Assert.Empty(await Service("Arroz").SuggestAsync(text, "pt", CancellationToken.None));

    [Fact]
    public async Task AtMostEightDistinctSuggestionsAreReturned()
    {
        var suggestions = await Service("Arroz", "arroz", "ARROZ").SuggestAsync("ar", null, CancellationToken.None);

        Assert.True(suggestions.Count <= InventoryNameSuggestionService.MaximumResults);
        Assert.Equal(suggestions.Count, suggestions.Select(item => IngredientText.Normalize(item.Name)).Distinct().Count());
        Assert.Equal(("Arroz", "pantry"), (suggestions[0].Name, suggestions[0].Source));
    }

    private sealed class User : ICurrentUserAccessor
    {
        public Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InternalUser(Guid.NewGuid(), "https://issuer.test", "subject", DateTimeOffset.UtcNow));
    }

    private sealed class Names(string[] names) : IInventoryProductNameSource
    {
        public Task<IReadOnlyList<string>> RecentAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(names);
    }
}
