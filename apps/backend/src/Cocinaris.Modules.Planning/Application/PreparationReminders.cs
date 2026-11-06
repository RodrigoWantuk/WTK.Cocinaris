using Cocinaris.Modules.Inventory.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>A dated meal of a menu in use, checked before its day.</summary>
/// <param name="PlanId">Menu.</param>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="PlannedDate">Day of the meal.</param>
/// <param name="MealType">Meal, if set.</param>
public sealed record ReminderMeal(Guid PlanId, Guid EntryId, string RecipeName, DateOnly PlannedDate, string? MealType);

/// <summary>A meal of a menu in use with no day, eaten "quando quiser" (PLAN-0062 Slice 4, PLAN-0065).</summary>
/// <param name="PlanId">Menu.</param>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="MealType">Meal, if set.</param>
public sealed record FlexibleReminderMeal(Guid PlanId, Guid EntryId, string RecipeName, string? MealType);

/// <summary>Pantry quantity reserved for a meal that can be frozen today (PLAN-0062 Slice 4).</summary>
/// <param name="LotId">Pantry lot.</param>
/// <param name="Quantity">Quantity the meal reserves from it.</param>
/// <param name="Unit">Unit of the quantity.</param>
public sealed record PreparationReminderLot(Guid LotId, decimal Quantity, string? Unit);

/// <summary>
/// Something to do before a planned meal (PLAN-0029 Phase 4). <paramref name="Kind"/> is
/// <c>buy</c> (a required ingredient the pantry does not cover), <c>thaw</c> (a reserved lot is
/// frozen), <c>use_by</c> (a reserved lot's date falls before the meal), or <c>ready_portions</c>
/// (portions cooked ahead are waiting to be eaten, PLAN-0062), or <c>cook_by</c> (a meal with no day
/// should be cooked by a day, because of what it reserves, PLAN-0062 Slice 4).
/// </summary>
/// <param name="PlanId">Menu.</param>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="PlannedDate">Day of the meal; for <c>cook_by</c>, whose meal has no day, the day to cook it by.</param>
/// <param name="MealType">Meal, if set.</param>
/// <param name="Kind"><c>buy</c>, <c>thaw</c>, <c>use_by</c>, <c>ready_portions</c>, or <c>cook_by</c>.</param>
/// <param name="ItemName">Ingredient or product name.</param>
/// <param name="Unit">Unit of <paramref name="Quantity"/>, when there is one.</param>
/// <param name="Quantity">Quantity missing (<c>buy</c>) or reserved from the lot (<c>thaw</c>, <c>use_by</c>).</param>
/// <param name="DueDate">Day to act by: the day before the meal for <c>buy</c> and <c>thaw</c> (never before the first day checked), and the lot's date for <c>use_by</c>.</param>
/// <param name="LotId">Pantry lot, for <c>thaw</c> and <c>use_by</c>.</param>
/// <param name="UseByDate">The lot's earliest date, for <c>use_by</c>; the day to cook by, for <c>cook_by</c>.</param>
/// <param name="FreezeLots">For <c>cook_by</c>: what the meal reserves that the storage table says can be frozen; empty without the table.</param>
public sealed record PreparationReminderView(Guid PlanId, Guid EntryId, string RecipeName, DateOnly PlannedDate, string? MealType, string Kind, string ItemName, string? Unit, decimal? Quantity, DateOnly DueDate, Guid? LotId, DateOnly? UseByDate, IReadOnlyList<PreparationReminderLot>? FreezeLots = null);

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

    /// <summary>Portions a cooking stored for later meals, until they are eaten. Derived, so each disappears once its lot is used up.</summary>
    /// <param name="lotBatches">The preparation batch of each output lot.</param>
    /// <param name="cooked">Menu entries that covered several meals, by the batch their cooking made.</param>
    /// <param name="lots">The owner's active lots.</param>
    /// <param name="today">The owner's current day.</param>
    public static IReadOnlyList<PreparationReminderView> ReadyPortions(IReadOnlyDictionary<Guid, Guid> lotBatches, IReadOnlyDictionary<Guid, Domain.MenuEntry> cooked, IReadOnlyList<InventoryLotReadModel> lots, DateOnly today) =>
        lots.Where(lot => lot.MeasuredUnit == "Unit" && lot.MeasuredValue is > 0 && lotBatches.TryGetValue(lot.LotId, out var batch) && cooked.ContainsKey(batch))
            .Select(lot =>
            {
                var entry = cooked[lotBatches[lot.LotId]];
                return new PreparationReminderView(entry.MenuPlanId, entry.Id, lot.ProductName, entry.PlannedDate ?? today, entry.MealType, "ready_portions", lot.ProductName, lot.MeasuredUnit, lot.MeasuredValue, today, lot.LotId, null);
            })
            .ToList();

    /// <summary>
    /// "Faça até": for each meal with no day, the earliest day any lot it reserves should be used by,
    /// from the lot's advisory attention (a printed or recorded date, or the owner-reviewed storage
    /// table). Frozen lots wait, so they set no day. The reminder is due the day before and on the day,
    /// and never earlier than <paramref name="from"/>. It never blocks anything, and a meal with no
    /// such day gets none. A dismissal holds until the day changes.
    /// </summary>
    /// <param name="meals">Meals with no day, of menus in use.</param>
    /// <param name="projection">Reservations of every meal of the menus in use.</param>
    /// <param name="lots">The owner's active lots.</param>
    /// <param name="useBy">A lot's advisory use-by day, or null when nothing says.</param>
    /// <param name="freezable">Whether the storage table says a lot can be frozen as bought.</param>
    /// <param name="dismissed">The day each meal's reminder named when the owner left it as it was.</param>
    /// <param name="from">First day checked (the owner's current day).</param>
    /// <param name="end">First day not checked.</param>
    public static IReadOnlyList<PreparationReminderView> CookBy(IReadOnlyList<FlexibleReminderMeal> meals, ReservationProjection projection, IReadOnlyList<InventoryLotReadModel> lots, Func<InventoryLotReadModel, DateOnly?> useBy, Func<InventoryLotReadModel, bool> freezable, IReadOnlyDictionary<Guid, DateOnly> dismissed, DateOnly from, DateOnly end)
    {
        var lotById = lots.ToDictionary(lot => lot.LotId);
        var reminders = new List<PreparationReminderView>();
        foreach (var meal in meals)
        {
            var reserved = projection.Lots
                .Select(pair => (Lot: lotById.GetValueOrDefault(pair.Key), Quantity: pair.Value.Reservations.Where(item => item.MenuEntryId == meal.EntryId).Sum(item => item.Quantity)))
                .Where(item => item.Lot is not null && item.Quantity > 0 && !IsFrozen(item.Lot))
                .Select(item => (Lot: item.Lot!, item.Quantity, Day: useBy(item.Lot!)))
                .ToList();
            var limit = reserved.Where(item => item.Day is not null).OrderBy(item => item.Day).ThenBy(item => item.Lot.ProductName, StringComparer.Ordinal).FirstOrDefault();
            if (limit.Day is not { } day || (dismissed.TryGetValue(meal.EntryId, out var left) && left == day))
            {
                continue;
            }

            var due = day.AddDays(-1) < from ? from : day.AddDays(-1);
            if (due >= end)
            {
                continue;
            }

            var freeze = reserved.Where(item => freezable(item.Lot)).Select(item => new PreparationReminderLot(item.Lot.LotId, item.Quantity, item.Lot.MeasuredUnit)).ToList();
            reminders.Add(new PreparationReminderView(meal.PlanId, meal.EntryId, meal.RecipeName, day, meal.MealType, "cook_by", limit.Lot.ProductName, limit.Lot.MeasuredUnit, limit.Quantity, due, limit.Lot.LotId, day, freeze));
        }

        return reminders;
    }

    // A thawing lot is counting down again, so its own estimate applies.
    private static bool IsFrozen(InventoryLotReadModel lot) =>
        lot.Lifecycle?.State is not "Thawing" && (lot.StorageLocation == "Freezer" || lot.Lifecycle?.State == "Frozen");

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
