using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>A later use of what is being cooked.</summary>
/// <param name="Quantity">Quantity it needs.</param>
/// <param name="InDays">Days from now until that use.</param>
public sealed record OutputUse(decimal Quantity, int InDays);

/// <summary>
/// Proposes how to store what a cooking makes (PLAN-0062). Pure and advisory: the person may change
/// everything. Without curated guidance it proposes the refrigerator only and never claims a date,
/// and the location comes from the curated table, never from model text.
/// </summary>
public static class StoredOutputAdvisor
{
    /// <summary>Most portions stored from one cooking.</summary>
    public const int MaxOutputs = 4;

    /// <summary>Portions of a dish that covers several meals: one is eaten now, and each other meal is stored.</summary>
    /// <param name="servings">Portions cooked.</param>
    /// <param name="mealsCovered">Meals this cooking covers.</param>
    /// <param name="inDays">Days until each later meal, in order.</param>
    /// <param name="freezeCompatible">Whether the recipe says the dish freezes.</param>
    /// <param name="cookedFridgeDays">Curated days a cooked dish keeps; null when guidance is off.</param>
    public static IReadOnlyList<ExecutionOutputProposal> ForDish(int servings, int mealsCovered, IReadOnlyList<int> inDays, bool freezeCompatible, int? cookedFridgeDays)
    {
        var perMeal = Math.Max(1, servings / Math.Max(1, mealsCovered));
        return inDays.Take(Math.Min(MaxOutputs, mealsCovered - 1))
            .Select(days => new ExecutionOutputProposal(perMeal, "Unit", freezeCompatible && cookedFridgeDays is { } limit && days > limit ? "Freezer" : "Refrigerator"))
            .ToList();
    }

    /// <summary>Measured portions of a base: one per later use, with what is left in the last.</summary>
    /// <param name="quantity">Quantity made.</param>
    /// <param name="unit">Its unit.</param>
    /// <param name="uses">Later uses, in order.</param>
    /// <param name="rule">Curated rule for the base; null when guidance is off or has none.</param>
    public static IReadOnlyList<ExecutionOutputProposal> ForComponent(decimal quantity, string unit, IReadOnlyList<OutputUse> uses, StorageRule? rule)
    {
        string Location(int days) => rule is { Freezable: true, FridgeDays: { } limit } && days > limit ? "Freezer" : "Refrigerator";
        var parts = new List<ExecutionOutputProposal>();
        var left = quantity;
        foreach (var use in uses.Take(MaxOutputs - 1))
        {
            var part = Math.Min(use.Quantity, left);
            if (part <= 0)
            {
                break;
            }

            parts.Add(new ExecutionOutputProposal(part, unit, Location(use.InDays)));
            left -= part;
        }

        if (left > 0)
        {
            parts.Add(new ExecutionOutputProposal(left, unit, parts.Count == 0 ? "Refrigerator" : parts[^1].StorageLocation));
        }

        return parts;
    }
}
