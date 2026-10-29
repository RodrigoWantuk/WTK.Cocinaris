using Cocinaris.Modules.Inventory.Application;
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
public sealed record ShoppingUseView(Guid EntryId, string RecipeName, DateOnly? PlannedDate, decimal Quantity, bool Optional);

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
public sealed record ShoppingItemView(string ItemKey, string DisplayName, string Unit, string Category, decimal RequiredQuantity, decimal AvailableQuantity, decimal ToBuyQuantity, bool Checked, IReadOnlyList<ShoppingUseView> Uses, bool AvailableIsEstimate = false);

/// <summary>
/// Derives a shopping list from the meals still to cook and the current pantry (PLAN-0029).
/// Deterministic and read-only: it never reserves or changes inventory.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Quantities scale like the cooking proposal (<see cref="RecipeIngredientReader.Scale"/>).</item>
/// <item>Pantry lots are matched by normalized product name (case, accents, and spacing ignored) and
/// unit. An ingredient taken from a specific lot is matched by that lot's product name. Each new
/// lot has its own product, so product identifiers cannot be compared across lots.</item>
/// <item>The pantry is allocated meal by meal in plan order, so an early meal uses stock before a
/// later one, and the later one shows what it still needs.</item>
/// <item>No package size is assumed: the line is the net quantity to buy.</item>
/// </list>
/// </remarks>
public static class ShoppingProjector
{
    private static readonly string[] CategoryOrder = ["required", "assumed", "optional", "runs_out", "available"];

    /// <summary>Builds the shopping lines.</summary>
    /// <param name="meals">Meals still to cook, already in plan order.</param>
    /// <param name="lots">The owner's active lots.</param>
    /// <param name="checkedKeys">Item keys the owner checked off.</param>
    public static IReadOnlyList<ShoppingItemView> Build(IReadOnlyList<ShoppingMeal> meals, IReadOnlyList<InventoryLotReadModel> lots, IReadOnlySet<string> checkedKeys)
    {
        var measured = lots.Where(lot => lot.MeasuredValue is > 0 && lot.MeasuredUnit is not null).ToList();
        var lotById = measured.ToDictionary(lot => lot.LotId);
        var stock = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var estimated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lot in measured)
        {
            var key = Key(lot.ProductName, lot.MeasuredUnit!);
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
                var key = Key(name, unit);
                if (!items.TryGetValue(key, out var item))
                {
                    items[key] = item = new Accumulator(key, name.Trim(), unit);
                }

                var covered = Math.Min(need, remaining.GetValueOrDefault(key));
                remaining[key] = remaining.GetValueOrDefault(key) - covered;
                item.Required += need;
                item.Covered += covered;
                item.HasRequired |= !ingredient.Optional && ingredient.SourceType != "assumption";
                item.HasAssumed |= !ingredient.Optional && ingredient.SourceType == "assumption";
                item.Uses.Add(new ShoppingUseView(meal.EntryId, meal.RecipeName, meal.PlannedDate, need, ingredient.Optional));
            }
        }

        return items.Values
            .Select(item =>
            {
                var toBuy = item.Required - item.Covered;
                var category = toBuy > 0
                    ? item.HasRequired ? "required" : item.HasAssumed ? "assumed" : "optional"
                    : remaining.GetValueOrDefault(item.Key) <= 0 ? "runs_out" : "available";
                return new ShoppingItemView(item.Key, item.DisplayName, item.Unit, category, item.Required, stock.GetValueOrDefault(item.Key), toBuy, checkedKeys.Contains(item.Key), item.Uses, estimated.Contains(item.Key));
            })
            .OrderBy(item => Array.IndexOf(CategoryOrder, item.Category))
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Normalizes a product name for matching (see <see cref="RecipeIngredientReader.NormalizeName"/>).</summary>
    public static string NormalizeName(string name) => RecipeIngredientReader.NormalizeName(name);

    /// <summary>Item key: normalized name (at most 120 characters) and canonical unit.</summary>
    public static string Key(string name, string unit)
    {
        var normalized = NormalizeName(name);
        return (normalized.Length > 120 ? normalized[..120] : normalized) + "|" + unit;
    }

    private sealed class Accumulator(string key, string displayName, string unit)
    {
        public string Key { get; } = key;
        public string DisplayName { get; } = displayName;
        public string Unit { get; } = unit;
        public decimal Required { get; set; }
        public decimal Covered { get; set; }
        public bool HasRequired { get; set; }
        public bool HasAssumed { get; set; }
        public List<ShoppingUseView> Uses { get; } = [];
    }
}
