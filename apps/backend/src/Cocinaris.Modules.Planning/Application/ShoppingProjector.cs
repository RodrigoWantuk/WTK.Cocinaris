using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>One meal still to cook, in the order the projection allocates the pantry.</summary>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name at the revision used.</param>
/// <param name="PlannedDate">Intended day, if any.</param>
/// <param name="Servings">Portions planned.</param>
/// <param name="RecipeServings">Portions the revision is written for.</param>
/// <param name="NormalizedRecipeJson">Revision content.</param>
public sealed record ShoppingMeal(Guid EntryId, string RecipeName, DateOnly? PlannedDate, int Servings, int RecipeServings, string NormalizedRecipeJson);

/// <summary>One meal that needs an item.</summary>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="PlannedDate">Intended day, if any.</param>
/// <param name="Quantity">Quantity this meal needs, in the item's unit.</param>
/// <param name="Optional">Whether the recipe marks the ingredient optional.</param>
/// <param name="MissingQuantity">What this meal still lacks after the pantry is allocated in plan order (PLAN-0029 Phase 4), so a purchase smaller than planned names the meals left short.</param>
public sealed record ShoppingUseView(Guid EntryId, string RecipeName, DateOnly? PlannedDate, decimal Quantity, bool Optional, decimal MissingQuantity = 0);

/// <summary>
/// One shopping line: what the remaining meals need of one product in one unit, what the pantry
/// holds, and what is left to buy.
/// </summary>
/// <param name="ItemKey">Stable key (normalized name and unit) used to remember the check mark.</param>
/// <param name="DisplayName">Name shown.</param>
/// <param name="Unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>.</param>
/// <param name="Category"><c>required</c>, <c>optional</c>, <c>assumed</c> (basics the recipe assumes you have), <c>runs_out</c> (covered, but the plan uses all of it), or <c>available</c>.</param>
/// <param name="RequiredQuantity">Total the remaining meals need.</param>
/// <param name="AvailableQuantity">Total the pantry holds now.</param>
/// <param name="ToBuyQuantity">Required minus what the pantry covers, never negative.</param>
/// <param name="Checked">Whether the owner checked the line off.</param>
/// <param name="Uses">The meals that need it, in plan order.</param>
/// <param name="AvailableIsEstimate">Whether any matched lot holds an estimated amount, so <paramref name="AvailableQuantity"/> is approximate (PLAN-0054).</param>
/// <param name="StoreSection">Store section of the line's catalog ingredient (<see cref="StoreSections"/>, PLAN-0037 Phase 3); <c>other</c> when the name is not a catalog ingredient.</param>
/// <param name="PackageSize">Package size the owner confirmed for this item, if any (PLAN-0029 Phase 4); never assumed.</param>
/// <param name="PackageCount">Whole packages that cover <paramref name="ToBuyQuantity"/>, when a package size is known and something is left to buy.</param>
/// <param name="ExpectedRemainder">What those packages leave after the menu, in <paramref name="Unit"/>, when <paramref name="PackageCount"/> is set.</param>
/// <param name="MarkKey">Key to address this line's marks: its key before any substitution.</param>
/// <param name="Later">Whether the person left it for a later purchase.</param>
/// <param name="SubstituteFor">The ingredient this line replaces, if any.</param>
/// <param name="Place">Where it is bought (<see cref="ShoppingPlaces"/>).</param>
public sealed record ShoppingItemView(string ItemKey, string DisplayName, string Unit, string Category, decimal RequiredQuantity, decimal AvailableQuantity, decimal ToBuyQuantity, bool Checked, IReadOnlyList<ShoppingUseView> Uses, bool AvailableIsEstimate = false, string StoreSection = StoreSections.Other, decimal? PackageSize = null, int? PackageCount = null, decimal? ExpectedRemainder = null, string? MarkKey = null, bool Later = false, string? SubstituteFor = null, string Place = ShoppingPlaces.Market);

/// <summary>
/// Where a shopping line is bought (PLAN-0062). Closed codes; the frontend localizes them. The default
/// comes from the store section until the person moves the line.
/// </summary>
public static class ShoppingPlaces
{
    /// <summary>The supermarket.</summary>
    public const string Market = "market";
    /// <summary>A street market.</summary>
    public const string StreetMarket = "street_market";
    /// <summary>A butcher.</summary>
    public const string Butcher = "butcher";
    /// <summary>A bakery.</summary>
    public const string Bakery = "bakery";

    /// <summary>Accepted codes.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Market, StreetMarket, Butcher, Bakery };

    /// <summary>The default place of a store section.</summary>
    /// <param name="section">Store section.</param>
    public static string OfSection(string section) => section switch
    {
        StoreSections.Produce => StreetMarket,
        StoreSections.ButcherFish => Butcher,
        StoreSections.Bakery => Bakery,
        _ => Market
    };
}

/// <summary>What the person said about a line while shopping (PLAN-0062).</summary>
/// <param name="ItemKey">The line's key before any substitution.</param>
/// <param name="State"><c>later</c>, or null.</param>
/// <param name="SubstituteName">Name of the product bought instead; the recipe does not change.</param>
/// <param name="Place">Where the person buys it, or null for the section's default.</param>
public sealed record ShoppingLineMark(string ItemKey, string? State, string? SubstituteName, string? Place);

/// <summary>
/// Derives a shopping list from the meals still to cook and the current pantry (PLAN-0029).
/// Deterministic and read-only: it never reserves or changes inventory.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Quantities scale like the cooking proposal (<see cref="RecipeIngredientReader.Scale"/>).</item>
/// <item>Pantry lots are matched by catalog ingredient or, failing that, normalized product name
/// (case, accents, and spacing ignored), and by unit (PLAN-0055). An ingredient taken from a specific lot is matched by that lot's product name. Each new
/// lot has its own product, so product identifiers cannot be compared across lots.</item>
/// <item>The pantry is allocated meal by meal in plan order, so an early meal uses stock before a
/// later one, and the later one shows what it still needs.</item>
/// <item>No package size is assumed: the line is the net quantity to buy. When the owner confirmed
/// the package size of an item, the line also says how many packages cover it and what they leave
/// after the menu (PLAN-0029 Phase 4).</item>
/// </list>
/// </remarks>
public static class ShoppingProjector
{
    private static readonly string[] CategoryOrder = ["required", "assumed", "optional", "runs_out", "available"];

    /// <summary>Builds the shopping lines.</summary>
    /// <param name="meals">Meals still to cook, already in plan order.</param>
    /// <param name="lots">The owner's active lots.</param>
    /// <param name="checkedKeys">Item keys the owner checked off.</param>
    /// <param name="matchKeys">Ingredient matching keys (PLAN-0055); names only when null.</param>
    /// <param name="packageSizes">Package sizes the owner confirmed, by item key; none when null.</param>
    /// <param name="marks">What the owner said about lines while shopping, by the line's original key; none when null.</param>
    public static IReadOnlyList<ShoppingItemView> Build(IReadOnlyList<ShoppingMeal> meals, IReadOnlyList<InventoryLotReadModel> lots, IReadOnlySet<string> checkedKeys, IngredientMatchKeys? matchKeys = null, IReadOnlyDictionary<string, PackageSize>? packageSizes = null, IReadOnlyDictionary<string, ShoppingLineMark>? marks = null)
    {
        matchKeys ??= IngredientMatchKeys.NamesOnly;
        marks ??= new Dictionary<string, ShoppingLineMark>();
        var measured = lots.Where(lot => lot.MeasuredValue is > 0 && lot.MeasuredUnit is not null).ToList();
        var lotById = measured.ToDictionary(lot => lot.LotId);
        var stock = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var estimated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lot in measured)
        {
            var key = Key(lot.ProductName, lot.MeasuredUnit!, matchKeys);
            stock[key] = stock.GetValueOrDefault(key) + lot.MeasuredValue!.Value;
            if (lot.QuantityIsEstimate)
            {
                estimated.Add(key);
            }
        }

        var remaining = new Dictionary<string, decimal>(stock, StringComparer.Ordinal);
        var items = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        foreach (var meal in meals)
        {
            var scale = (decimal)meal.Servings / Math.Max(1, meal.RecipeServings);
            foreach (var ingredient in RecipeIngredientReader.Read(meal.NormalizedRecipeJson))
            {
                if (string.IsNullOrWhiteSpace(ingredient.Name))
                {
                    continue;
                }

                var unit = RecipeIngredientReader.ToLotUnit(ingredient.Unit);
                var need = RecipeIngredientReader.Scale(ingredient.Quantity, unit, scale);
                var name = ingredient.LotId is { } lotId && lotById.TryGetValue(lotId, out var linked) && linked.MeasuredUnit == unit
                    ? linked.ProductName
                    : ingredient.Name;
                var key = Key(name, unit, matchKeys);
                var markKey = key;
                string? substituted = null;
                // A substitute takes over the line's identity, so stock, check marks, and package sizes
                // follow the product actually bought. The quantity and the recipe stay as they are.
                if (marks.TryGetValue(key, out var replaced) && !string.IsNullOrWhiteSpace(replaced.SubstituteName))
                {
                    substituted = name.Trim();
                    name = replaced.SubstituteName;
                    key = Key(name, unit, matchKeys);
                }

                if (!items.TryGetValue(key, out var item))
                {
                    items[key] = item = new Accumulator(key, name.Trim(), unit) { MarkKey = markKey, SubstituteFor = substituted };
                }

                var covered = Math.Min(need, remaining.GetValueOrDefault(key));
                remaining[key] = remaining.GetValueOrDefault(key) - covered;
                item.Required += need;
                item.Covered += covered;
                item.HasRequired |= !ingredient.Optional && ingredient.SourceType != "assumption";
                item.HasAssumed |= !ingredient.Optional && ingredient.SourceType == "assumption";
                item.IsBasic |= HouseholdBasics.Is(ingredient.Name);
                item.Uses.Add(new ShoppingUseView(meal.EntryId, meal.RecipeName, meal.PlannedDate, need, ingredient.Optional, need - covered));
            }
        }

        // Salt and water are assumed in every home: never to buy, and never a warning that they run
        // out. A tracked one with stock left stays listed as available (PLAN-0061).
        return items.Values
            .Where(item => !item.IsBasic || (item.Required <= item.Covered && remaining.GetValueOrDefault(item.Key) > 0))
            .Select(item =>
            {
                var toBuy = item.Required - item.Covered;
                var category = toBuy > 0
                    ? item.HasRequired ? "required" : item.HasAssumed ? "assumed" : "optional"
                    : remaining.GetValueOrDefault(item.Key) <= 0 ? "runs_out" : "available";
                var package = packageSizes?.GetValueOrDefault(item.Key);
                var (count, remainder) = package?.Cover(toBuy) ?? (0, 0);
                return new ShoppingItemView(item.Key, item.DisplayName, item.Unit, category, item.Required, stock.GetValueOrDefault(item.Key), toBuy, checkedKeys.Contains(item.Key), item.Uses, estimated.Contains(item.Key),
                    StoreSections.OfMatchKey(item.Key[..item.Key.LastIndexOf('|')], IngredientCatalog.Default),
                    package?.Size,
                    count > 0 ? count : null,
                    count > 0 ? remainder : null,
                    item.MarkKey,
                    marks.GetValueOrDefault(item.MarkKey)?.State == "later",
                    item.SubstituteFor,
                    marks.GetValueOrDefault(item.MarkKey)?.Place ?? ShoppingPlaces.OfSection(StoreSections.OfMatchKey(item.Key[..item.Key.LastIndexOf('|')], IngredientCatalog.Default)));
            })
            .OrderBy(item => Array.IndexOf(CategoryOrder, item.Category))
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Normalizes a product name for matching (see <see cref="RecipeIngredientReader.NormalizeName"/>).</summary>
    public static string NormalizeName(string name) => RecipeIngredientReader.NormalizeName(name);

    /// <summary>
    /// Item key: the catalog ingredient (PLAN-0055) or the normalized name (at most 120
    /// characters), and the canonical unit. A name that resolves after a line was checked off gets
    /// a new key, so that one check mark is lost once; nothing else changes.
    /// </summary>
    public static string Key(string name, string unit, IngredientMatchKeys? matchKeys = null) =>
        (matchKeys ?? IngredientMatchKeys.NamesOnly).Key(name) + "|" + unit;

    private sealed class Accumulator(string key, string displayName, string unit)
    {
        public string Key { get; } = key;
        public string DisplayName { get; } = displayName;
        public string Unit { get; } = unit;
        public decimal Required { get; set; }
        public decimal Covered { get; set; }
        public bool HasRequired { get; set; }
        public bool HasAssumed { get; set; }
        public bool IsBasic { get; set; }
        public string MarkKey { get; init; } = key;
        public string? SubstituteFor { get; init; }
        public List<ShoppingUseView> Uses { get; } = [];
    }
}
