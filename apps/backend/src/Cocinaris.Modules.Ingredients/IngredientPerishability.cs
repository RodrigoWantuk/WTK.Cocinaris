namespace Cocinaris.Modules.Ingredients;

/// <summary>
/// How soon a catalog ingredient usually needs cooking, used to order a shopping plan's dishes and
/// to score cards (PLAN-0062). This sets an order; it is not a safety statement and never produces a
/// date or a use-by claim.
/// </summary>
public static class IngredientPerishability
{
    /// <summary>Rank of anything that does not press for early cooking, including names outside the catalog.</summary>
    public const int NotPerishable = 9;

    private const string IngredientPrefix = "ingredient:";

    private static readonly HashSet<string> GroundMeat = new(StringComparer.Ordinal) { "ground-beef", "ground-pork", "ground-chicken" };

    /// <summary>Rank of a catalog item: lower ranks are cooked first.</summary>
    /// <param name="item">Catalog item, or null for a name outside the catalog.</param>
    public static int Rank(IngredientCatalogItem? item)
    {
        if (item is null)
        {
            return NotPerishable;
        }

        if (GroundMeat.Contains(item.Slug))
        {
            return 1;
        }

        return item.Category switch
        {
            "fish" or "seafood" => 1,
            "poultry" or "meat" => 2,
            "herb" => 3,
            "vegetable" or "fruit" or "dairy" or "bakery" => 4,
            "egg" => 5,
            _ => NotPerishable
        };
    }

    /// <summary>Catalog item of a matching key (<c>ingredient:&lt;slug&gt;</c>), or null for a plain name.</summary>
    /// <param name="matchKey">Key from <see cref="IngredientMatchKeys.Key"/>, without the unit.</param>
    /// <param name="catalog">Catalog.</param>
    public static IngredientCatalogItem? ItemOfKey(string matchKey, IngredientCatalog catalog) =>
        matchKey.StartsWith(IngredientPrefix, StringComparison.Ordinal) && catalog.BySlug.TryGetValue(matchKey[IngredientPrefix.Length..], out var item) ? item : null;

    /// <summary>Whether an item counts as perishable when scoring cards.</summary>
    /// <param name="item">Catalog item, or null for a name outside the catalog, which counts as not perishable.</param>
    public static bool IsPerishable(IngredientCatalogItem? item) => Rank(item) < NotPerishable;
}
