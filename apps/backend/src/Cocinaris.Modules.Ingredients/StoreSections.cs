namespace Cocinaris.Modules.Ingredients;

/// <summary>
/// Store sections a shopping list is grouped by (PLAN-0037 Phase 3), derived from the curated
/// catalog category of an ingredient. Codes are stable and localized by the frontend.
/// </summary>
public static class StoreSections
{
    /// <summary>Fresh fruit, vegetables, and herbs.</summary>
    public const string Produce = "produce";

    /// <summary>Meat, poultry, fish, and seafood.</summary>
    public const string ButcherFish = "butcher_fish";

    /// <summary>Dairy and eggs.</summary>
    public const string DairyEggs = "dairy_eggs";

    /// <summary>Bread and bakery goods.</summary>
    public const string Bakery = "bakery";

    /// <summary>Dry goods, grains, pasta, baking, oils, condiments, spices, and nuts.</summary>
    public const string DryGoods = "dry_goods";

    /// <summary>Drinks.</summary>
    public const string Drinks = "drinks";

    /// <summary>Anything without a catalog ingredient.</summary>
    public const string Other = "other";

    /// <summary>Sections in the order a shopping list shows them.</summary>
    public static readonly IReadOnlyList<string> Order = [Produce, ButcherFish, DairyEggs, Bakery, DryGoods, Drinks, Other];

    private static readonly IReadOnlyDictionary<string, string> ByCategory = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["vegetable"] = Produce,
        ["fruit"] = Produce,
        ["herb"] = Produce,
        ["poultry"] = ButcherFish,
        ["meat"] = ButcherFish,
        ["fish"] = ButcherFish,
        ["seafood"] = ButcherFish,
        ["dairy"] = DairyEggs,
        ["egg"] = DairyEggs,
        ["bakery"] = Bakery,
        ["grain"] = DryGoods,
        ["legume"] = DryGoods,
        ["pasta"] = DryGoods,
        ["baking"] = DryGoods,
        ["sweetener"] = DryGoods,
        ["oil"] = DryGoods,
        ["condiment"] = DryGoods,
        ["spice"] = DryGoods,
        ["nut"] = DryGoods,
        ["beverage"] = Drinks
    };

    /// <summary>Section of a catalog category; <see cref="Other"/> for an unknown one.</summary>
    public static string OfCategory(string category) => ByCategory.TryGetValue(category, out var section) ? section : Other;

    /// <summary>Section of a matching key (<c>ingredient:&lt;slug&gt;</c> or a plain name); <see cref="Other"/> when it is not a catalog ingredient.</summary>
    public static string OfMatchKey(string key, IngredientCatalog catalog)
    {
        const string prefix = "ingredient:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Other;
        }

        return catalog.BySlug.TryGetValue(key[prefix.Length..], out var item) ? OfCategory(item.Category) : Other;
    }
}
