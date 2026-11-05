using Cocinaris.Modules.Ingredients;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>One meal that needs part of a purchase.</summary>
/// <param name="RecipeName">Dish.</param>
/// <param name="Quantity">What this meal still lacks, in the line's unit.</param>
/// <param name="ExpectedDay">The day it is likely to be cooked.</param>
public sealed record ArrivalUse(string RecipeName, decimal Quantity, DateOnly ExpectedDay);

/// <summary>A part of a purchase to store together.</summary>
/// <param name="Location"><c>Pantry</c>, <c>Refrigerator</c>, or <c>Freezer</c>.</param>
/// <param name="Quantity">Quantity of one portion.</param>
/// <param name="Count">Portions of this size.</param>
/// <param name="Dish">The dish it is for, when it has one use.</param>
/// <param name="UseFirst">Whether it will not keep until its meal and cannot be frozen, so it should be used first.</param>
public sealed record ArrivalPortion(string Location, decimal Quantity, int Count, string? Dish, bool UseFirst);

/// <summary>
/// Decides where a purchase goes and how to divide it (PLAN-0062). Pure and advisory. Advice comes only
/// from the curated rule it receives, never from model text. With no rule, a purchase stays whole in the
/// location its store section suggests, and nothing mentions freezing or a date.
/// </summary>
public static class ArrivalAdvisor
{
    private const int MaxSurplusPortions = 6;

    /// <summary>Divides one purchased line.</summary>
    /// <param name="bought">Quantity bought.</param>
    /// <param name="unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>.</param>
    /// <param name="section">Store section.</param>
    /// <param name="rule">Curated rule, or null.</param>
    /// <param name="perishable">Whether the catalog counts it as perishable.</param>
    /// <param name="hasFreezer">Whether the person can freeze.</param>
    /// <param name="today">The owner's day.</param>
    /// <param name="uses">Meals that need it, in plan order.</param>
    public static IReadOnlyList<ArrivalPortion> Advise(decimal bought, string unit, string section, StorageRule? rule, bool perishable, bool hasFreezer, DateOnly today, IReadOnlyList<ArrivalUse> uses)
    {
        if (rule is null)
        {
            return [new ArrivalPortion(DefaultLocation(section), bought, 1, SingleDish(uses), false)];
        }

        if (rule.FridgeDays is null || rule.Location == "Pantry")
        {
            return [new ArrivalPortion(rule.Location, bought, 1, SingleDish(uses), false)];
        }

        var canFreeze = rule.Freezable && hasFreezer;
        var groups = new List<Group>();
        var left = bought;
        foreach (var use in uses.Where(use => use.Quantity > 0))
        {
            if (left <= 0)
            {
                break;
            }

            var quantity = Math.Min(left, use.Quantity);
            left -= quantity;
            var keeps = use.ExpectedDay.DayNumber - today.DayNumber <= rule.FridgeDays;
            var location = keeps || !canFreeze ? "Refrigerator" : "Freezer";
            var existing = groups.FirstOrDefault(group => group.Location == location && group.Day == use.ExpectedDay);
            if (existing is null)
            {
                groups.Add(new Group(location, use.ExpectedDay, quantity, [use.RecipeName], !keeps && !canFreeze));
            }
            else
            {
                existing.Quantity += quantity;
                existing.Dishes.Add(use.RecipeName);
            }
        }

        var portions = groups.Select(group => new ArrivalPortion(group.Location, group.Quantity, 1, group.Dishes.Distinct(StringComparer.Ordinal).Count() == 1 ? group.Dishes[0] : null, group.UseFirst)).ToList();
        if (left > 0)
        {
            portions.AddRange(Surplus(left, unit, canFreeze && perishable, uses, rule));
        }

        return portions.Count > 0 ? portions : [new ArrivalPortion(rule.Location, bought, 1, null, false)];
    }

    /// <summary>Default location for a store section: the rule the list already uses when recording a purchase.</summary>
    /// <param name="section">Store section.</param>
    public static string DefaultLocation(string section) => section is StoreSections.ButcherFish or StoreSections.DairyEggs ? "Refrigerator" : "Pantry";

    private static IEnumerable<ArrivalPortion> Surplus(decimal surplus, string unit, bool freeze, IReadOnlyList<ArrivalUse> uses, StorageRule rule)
    {
        if (!freeze)
        {
            yield return new ArrivalPortion(rule.Location, surplus, 1, null, false);
            yield break;
        }

        var largest = uses.Where(use => use.Quantity > 0).Select(use => use.Quantity).DefaultIfEmpty(surplus).Max();
        var count = (int)Math.Min(MaxSurplusPortions, Math.Ceiling(surplus / largest));
        var exact = surplus / count;
        // Half an egg is not a portion: a unit line that does not divide evenly stays together.
        if (unit == "Unit" && exact != Math.Floor(exact))
        {
            yield return new ArrivalPortion("Freezer", surplus, 1, null, false);
            yield break;
        }

        // Whole grams read better than "233.333 g"; the last portion takes the rest, so the total is exact.
        var size = Math.Floor(exact);
        var rest = surplus - size * count;
        if (rest == 0)
        {
            yield return new ArrivalPortion("Freezer", size, count, null, false);
            yield break;
        }

        if (count > 1)
        {
            yield return new ArrivalPortion("Freezer", size, count - 1, null, false);
        }

        yield return new ArrivalPortion("Freezer", size + rest, 1, null, false);
    }

    private static string? SingleDish(IReadOnlyList<ArrivalUse> uses)
    {
        var dishes = uses.Where(use => use.Quantity > 0).Select(use => use.RecipeName).Distinct(StringComparer.Ordinal).ToList();
        return dishes.Count == 1 ? dishes[0] : null;
    }

    private sealed class Group(string location, DateOnly day, decimal quantity, List<string> dishes, bool useFirst)
    {
        public string Location { get; } = location;
        public DateOnly Day { get; } = day;
        public decimal Quantity { get; set; } = quantity;
        public List<string> Dishes { get; } = dishes;
        public bool UseFirst { get; } = useFirst;
    }
}
