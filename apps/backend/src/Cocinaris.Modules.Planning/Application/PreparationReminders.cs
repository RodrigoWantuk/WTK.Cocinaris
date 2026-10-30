using Cocinaris.Modules.Inventory.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>A dated meal of a menu in use, checked before its day.</summary>
/// <param name="PlanId">Menu.</param>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="PlannedDate">Day of the meal.</param>
/// <param name="MealType">Meal, if set.</param>
public sealed record ReminderMeal(Guid PlanId, Guid EntryId, string RecipeName, DateOnly PlannedDate, string? MealType);

/// <summary>
/// Something to do before a planned meal (PLAN-0029 Phase 4). <paramref name="Kind"/> is
/// <c>buy</c> (a required ingredient the pantry does not cover), <c>thaw</c> (a reserved lot is
/// frozen), or <c>use_by</c> (a reserved lot's date falls before the meal).
/// </summary>
/// <param name="PlanId">Menu.</param>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="PlannedDate">Day of the meal.</param>
/// <param name="MealType">Meal, if set.</param>
/// <param name="Kind"><c>buy</c>, <c>thaw</c>, or <c>use_by</c>.</param>
/// <param name="ItemName">Ingredient or product name.</param>
/// <param name="Unit">Unit of <paramref name="Quantity"/>, when there is one.</param>
/// <param name="Quantity">Quantity missing (<c>buy</c>) or reserved from the lot (<c>thaw</c>, <c>use_by</c>).</param>
/// <param name="DueDate">Day to act by: the day before the meal for <c>buy</c> and <c>thaw</c> (never before the first day checked), and the lot's date for <c>use_by</c>.</param>
/// <param name="LotId">Pantry lot, for <c>thaw</c> and <c>use_by</c>.</param>
/// <param name="UseByDate">The lot's earliest date, for <c>use_by</c>.</param>
public sealed record PreparationReminderView(Guid PlanId, Guid EntryId, string RecipeName, DateOnly PlannedDate, string? MealType, string Kind, string ItemName, string? Unit, decimal? Quantity, DateOnly DueDate, Guid? LotId, DateOnly? UseByDate);

/// <summary>
/// Pre-meal readiness checks (PLAN-0029 Phase 4). Deterministic and read-only: they are derived
/// from the reservation projection whenever they are read, so a reminder disappears as soon as its
/// cause does (the item was bought, the lot was thawed, the meal was cooked, skipped, or moved).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only meals that reserve ingredients are checked, because only they have lots set aside.</item>
/// <item>A lot is frozen when it is stored in the freezer or its lifecycle state is <c>Frozen</c>;
/// a lot already thawing needs no reminder.</item>
/// <item>A date before the meal is advisory, like every shelf-life signal: the reminder suggests
/// cooking earlier or using another lot; it never blocks the meal.</item>
/// </list>
/// </remarks>
public static class PreparationReminders
{
    private static readonly string[] KindOrder = ["use_by", "thaw", "buy"];

    /// <summary>Builds the reminders, earliest due first.</summary>
    /// <param name="meals">Dated meals to check.</param>
    /// <param name="projection">Reservations of every meal of the menus in use.</param>
    /// <param name="lots">The owner's active lots.</param>
    /// <param name="from">First day checked (the owner's current day).</param>
    public static IReadOnlyList<PreparationReminderView> Build(IReadOnlyList<ReminderMeal> meals, ReservationProjection projection, IReadOnlyList<InventoryLotReadModel> lots, DateOnly from)
    {
        var lotById = lots.ToDictionary(lot => lot.LotId);
        var reminders = new List<PreparationReminderView>();
        foreach (var meal in meals)
        {
            var dayBefore = meal.PlannedDate.AddDays(-1);
            var advance = dayBefore < from ? from : dayBefore;
            PreparationReminderView Reminder(string kind, string name, string? unit, decimal? quantity, DateOnly due, Guid? lotId = null, DateOnly? useBy = null) =>
                new(meal.PlanId, meal.EntryId, meal.RecipeName, meal.PlannedDate, meal.MealType, kind, name, unit, quantity, due, lotId, useBy);

            if (projection.Meals.TryGetValue(meal.EntryId, out var readiness))
            {
                reminders.AddRange(readiness.Missing.Select(item => Reminder("buy", item.Name, item.Unit, item.Quantity, advance)));
            }

            foreach (var (lotId, commitment) in projection.Lots)
            {
                var reserved = commitment.Reservations.Where(item => item.MenuEntryId == meal.EntryId).Sum(item => item.Quantity);
                if (reserved <= 0 || !lotById.TryGetValue(lotId, out var lot))
                {
                    continue;
                }

                if (lot.Lifecycle?.State is not "Thawing" && (lot.StorageLocation == "Freezer" || lot.Lifecycle?.State == "Frozen"))
                {
                    reminders.Add(Reminder("thaw", lot.ProductName, lot.MeasuredUnit, reserved, advance, lot.LotId));
                }

                if (ReservationProjector.EarliestDate(lot) is { } date && date < meal.PlannedDate)
                {
                    reminders.Add(Reminder("use_by", lot.ProductName, lot.MeasuredUnit, reserved, date < from ? from : date, lot.LotId, date));
                }
            }
        }

        return reminders
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.PlannedDate)
            .ThenBy(item => Array.IndexOf(KindOrder, item.Kind))
            .ThenBy(item => item.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
