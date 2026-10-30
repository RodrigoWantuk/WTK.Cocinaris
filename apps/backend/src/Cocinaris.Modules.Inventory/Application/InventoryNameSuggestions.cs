using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Ingredients;

namespace Cocinaris.Modules.Inventory.Application;

/// <summary>One suggested product name while the person types (PLAN-0056 Phase 4, owner report 1).</summary>
/// <param name="Name">Name as it will be filled in.</param>
/// <param name="Source"><c>pantry</c> (a name the person used before) or <c>catalog</c> (a curated ingredient name).</param>
public sealed record InventoryNameSuggestion(string Name, string Source);

/// <summary>Owner-scoped source of product names the person has used.</summary>
public interface IInventoryProductNameSource
{
    /// <summary>
    /// Up to <paramref name="limit"/> of the owner's product display names, most recently used
    /// first. Filtering happens in memory, because stored search names keep their accents.
    /// </summary>
    Task<IReadOnlyList<string>> RecentAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Suggests product names for the pantry form (PLAN-0056 Phase 4, owner report 1): names the
/// person already used come first, then curated catalog names in their language. Matching ignores
/// case and accents; a name that starts with the text ranks before one with a word that starts
/// with it, which ranks before one that merely contains it.
/// </summary>
public sealed class InventoryNameSuggestionService(ICurrentUserAccessor currentUser, IInventoryProductNameSource products, IngredientCatalog catalog)
{
    /// <summary>Fewest characters before suggesting.</summary>
    public const int MinimumLength = 2;

    /// <summary>Most characters considered.</summary>
    public const int MaximumLength = 60;

    /// <summary>Most suggestions returned.</summary>
    public const int MaximumResults = 8;

    /// <summary>Suggestions for <paramref name="text"/>; empty for text shorter than <see cref="MinimumLength"/>.</summary>
    /// <param name="text">What the person typed.</param>
    /// <param name="language">Interface language (<c>pt</c>, <c>en</c>, <c>es</c>); null offers catalog names in every language.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<InventoryNameSuggestion>> SuggestAsync(string? text, string? language, CancellationToken cancellationToken)
    {
        var fragment = IngredientText.Normalize(text ?? string.Empty);
        if (fragment.Length < MinimumLength)
        {
            return [];
        }

        fragment = fragment.Length > MaximumLength ? fragment[..MaximumLength] : fragment;
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var used = await products.RecentAsync(user.Id, 500, cancellationToken);
        var candidates = used.Select(name => (Name: name, Source: "pantry"))
            .Concat(catalog.Items
                .SelectMany(item => language is not null && item.Names.TryGetValue(language, out var names) ? names : item.Names.Values.SelectMany(values => values))
                .Select(name => (Name: name, Source: "catalog")));
        return candidates
            .Select(candidate => (candidate.Name, candidate.Source, Normalized: IngredientText.Normalize(candidate.Name)))
            .Where(candidate => candidate.Normalized.Contains(fragment, StringComparison.Ordinal))
            .DistinctBy(candidate => candidate.Normalized)
            .OrderBy(candidate => Rank(candidate.Normalized, fragment))
            .ThenBy(candidate => candidate.Source == "pantry" ? 0 : 1)
            .ThenBy(candidate => candidate.Normalized.Length)
            .ThenBy(candidate => candidate.Normalized, StringComparer.Ordinal)
            .Take(MaximumResults)
            .Select(candidate => new InventoryNameSuggestion(Capitalize(candidate.Name, candidate.Source), candidate.Source))
            .ToList();
    }

    private static int Rank(string name, string fragment) =>
        name.StartsWith(fragment, StringComparison.Ordinal) ? 0 : name.Contains(" " + fragment, StringComparison.Ordinal) ? 1 : 2;

    // Catalog names are stored in lower case; a pantry name keeps the person's own spelling.
    private static string Capitalize(string name, string source) =>
        source == "catalog" && name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
}
