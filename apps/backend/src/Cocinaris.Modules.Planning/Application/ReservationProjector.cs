using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>One meal that reserves pantry quantity, in the order reservations are granted.</summary>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="PlannedDate">Day, or null when flexible.</param>
/// <param name="Servings">Portions planned.</param>
/// <param name="RecipeServings">Portions the revision is written for.</param>
/// <param name="NormalizedRecipeJson">Revision content.</param>
public sealed record ReservingMeal(Guid EntryId, string RecipeName, DateOnly? PlannedDate, int Servings, int RecipeServings, string NormalizedRecipeJson);

/// <summary>An ingredient a meal still lacks after reservations.</summary>
/// <param name="Name">Ingredient name.</param>
/// <param name="Unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>.</param>
/// <param name="Quantity">Quantity missing.</param>
public sealed record MissingIngredientView(string Name, string Unit, decimal Quantity);

/// <summary>
/// Whether a meal's pantry ingredients are covered: <c>ready</c> when every required ingredient
/// tracked in the pantry is reserved, <c>missing</c> otherwise. Optional ingredients and basics the
/// recipe assumes are never reported missing.
/// </summary>
public sealed record MealReadinessView(Guid EntryId, string Readiness, IReadOnlyList<MissingIngredientView> Missing);

/// <summary>Commitments of the owner's lots and readiness of each reserving meal.</summary>
public sealed record ReservationProjection(IReadOnlyDictionary<Guid, LotCommitment> Lots, IReadOnlyDictionary<Guid, MealReadinessView> Meals);

/// <summary>
/// Derives menu reservations deterministically (PLAN-0029 Phase 3). Reservations are not stored:
/// they are recomputed from accepted menus, the pantry, and protections whenever they are read, so
/// they can never drift from either. They never change physical quantity.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Protected quantity is set aside first and is never reserved.</item>
/// <item>Meals are served in the given order (day, then meal); each takes what is still free.</item>
/// <item>For each ingredient the lot the recipe was written for comes first, then other lots with
/// the same normalized name and unit, earliest printed or prepared date first, then oldest.</item>
/// <item>Quantities scale like the cooking proposal (<see cref="RecipeIngredientReader"/>).</item>
/// </list>
/// </remarks>
public static class ReservationProjector
{
    /// <summary>Builds the projection.</summary>
    /// <param name="meals">Reserving meals, already in priority order.</param>
    /// <param name="lots">The owner's active lots.</param>
    /// <param name="protections">The owner's lot protections.</param>
    public static ReservationProjection Build(IReadOnlyList<ReservingMeal> meals, IReadOnlyList<InventoryLotReadModel> lots, IReadOnlyList<LotProtection> protections)
    {
        var measured = lots.Where(lot => lot.MeasuredValue is > 0 && lot.MeasuredUnit is not null).ToList();
        var byId = measured.ToDictionary(lot => lot.LotId);
        var protectionByLot = protections.ToDictionary(item => item.LotId);
        var protectedQuantity = measured.ToDictionary(lot => lot.LotId, lot => protectionByLot.TryGetValue(lot.LotId, out var rule) ? rule.ProtectedOf(lot.MeasuredValue!.Value) : 0m);
        var free = measured.ToDictionary(lot => lot.LotId, lot => lot.MeasuredValue!.Value - protectedQuantity[lot.LotId]);
        var byKey = measured
            .GroupBy(lot => ShoppingProjector.Key(lot.ProductName, lot.MeasuredUnit!))
            .ToDictionary(group => group.Key, group => group
                .OrderBy(lot => EarliestDate(lot) ?? DateOnly.MaxValue)
                .ThenBy(lot => lot.CreatedAt)
                .ToList());
        var reservations = new Dictionary<Guid, List<LotReservationView>>();
        var readiness = new Dictionary<Guid, MealReadinessView>();

        foreach (var meal in meals)
        {
            var scale = (decimal)meal.Servings / Math.Max(1, meal.RecipeServings);
            var missing = new List<MissingIngredientView>();
            foreach (var ingredient in RecipeIngredientReader.Read(meal.NormalizedRecipeJson))
            {
                if (string.IsNullOrWhiteSpace(ingredient.Name))
                {
                    continue;
                }

                var unit = RecipeIngredientReader.ToLotUnit(ingredient.Unit);
                var need = RecipeIngredientReader.Scale(ingredient.Quantity, unit, scale);
                var linked = ingredient.LotId is { } lotId && byId.TryGetValue(lotId, out var lot) && lot.MeasuredUnit == unit ? lot : null;
                var key = ShoppingProjector.Key(linked?.ProductName ?? ingredient.Name, unit);
                var candidates = (linked is null ? Enumerable.Empty<InventoryLotReadModel>() : [linked])
                    .Concat(byKey.GetValueOrDefault(key) ?? [])
                    .DistinctBy(item => item.LotId);
                foreach (var candidate in candidates)
                {
                    if (need <= 0)
                    {
                        break;
                    }

                    var take = Math.Min(need, free[candidate.LotId]);
                    if (take <= 0)
                    {
                        continue;
                    }

                    free[candidate.LotId] -= take;
                    need -= take;
                    if (!reservations.TryGetValue(candidate.LotId, out var list))
                    {
                        reservations[candidate.LotId] = list = [];
                    }

                    list.Add(new LotReservationView(meal.EntryId, meal.RecipeName, meal.PlannedDate, take));
                }

                if (need > 0 && !ingredient.Optional && ingredient.SourceType != "assumption")
                {
                    missing.Add(new MissingIngredientView((linked?.ProductName ?? ingredient.Name).Trim(), unit, need));
                }
            }

            readiness[meal.EntryId] = new MealReadinessView(meal.EntryId, missing.Count == 0 ? "ready" : "missing", missing);
        }

        var commitments = measured
            .Where(lot => protectedQuantity[lot.LotId] > 0 || reservations.ContainsKey(lot.LotId))
            .ToDictionary(lot => lot.LotId, lot => new LotCommitment(lot.LotId, protectedQuantity[lot.LotId], reservations.GetValueOrDefault(lot.LotId) ?? []));
        return new ReservationProjection(commitments, readiness);
    }

    private static DateOnly? EarliestDate(InventoryLotReadModel lot)
    {
        var prepared = lot.Lifecycle?.PreparedShelfLifeDate;
        return (lot.PrintedExpirationDate, prepared) switch
        {
            ({ } printed, { } shelf) => printed < shelf ? printed : shelf,
            ({ } printed, null) => printed,
            (null, { } shelf) => shelf,
            _ => null
        };
    }
}
