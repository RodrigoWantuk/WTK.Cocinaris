namespace Cocinaris.Modules.Inventory.Application;

/// <summary>Advisory attention state of a lot, ordered from most to least urgent for listing.</summary>
public enum InventoryAttentionState
{
    /// <summary>The evidence suggests the food is probably no longer good; the user decides.</summary>
    LikelyDiscard,
    /// <summary>The user should look at, smell, or otherwise check the food before using it.</summary>
    CheckCondition,
    /// <summary>The food should be used today or tomorrow.</summary>
    UsePriority,
    /// <summary>The food should be used within a few days.</summary>
    UseSoon,
    /// <summary>The available evidence does not support a reliable estimate.</summary>
    EstimateUncertain,
    /// <summary>No attention is needed.</summary>
    Normal
}

/// <summary>Strongest evidence used for an attention result, following the documented evidence priority.</summary>
public enum InventoryAttentionEvidence
{
    /// <summary>The date printed on the package, entered by the user.</summary>
    PrintedDate,
    /// <summary>The shelf-life date recorded for a prepared component.</summary>
    PreparedRecord,
    /// <summary>A curated Cocinaris rule (for example, thawed food in the refrigerator).</summary>
    CuratedRule,
    /// <summary>No evidence is available.</summary>
    Unknown
}

/// <summary>
/// Lifecycle and shelf-life facts of one lot that the advisor evaluates. <c>KeepsWhenOpened</c> is
/// the curated catalog fact that the product is a dry, shelf-stable food that stays usable once
/// opened (PLAN-0056, <see cref="Cocinaris.Modules.Ingredients.IngredientCatalog.KeepsWhenOpened"/>).
/// </summary>
public sealed record InventoryAttentionFacts(
    DateOnly? PrintedExpirationDate,
    DateOnly? PreparedShelfLifeDate,
    string StorageLocation,
    string? PackageState,
    string LifecycleState,
    DateTimeOffset? FrozenAt,
    DateTimeOffset? ThawStartedAt,
    DateTimeOffset? ThawedAt,
    bool KeepsWhenOpened = false);

/// <summary>
/// Advisory, explainable attention result. <see cref="Reasons"/> are stable codes that clients
/// localize; <see cref="EstimatedUseBy"/> is a calendar date in the user's time zone, or null.
/// </summary>
public sealed record InventoryAttention(InventoryAttentionState State, DateOnly? EstimatedUseBy, InventoryAttentionEvidence Evidence, IReadOnlyList<string> Reasons);

/// <summary>
/// Deterministic shelf-life advisor (PLAN-0037 Phase 2). It never blocks a user choice and never
/// claims safety: every state is advice with its reasons. Evidence follows the accepted priority
/// (printed or user-recorded dates before curated rules before unknown), and a weaker source never
/// replaces a stronger one. The curated rules are deliberately generic until products carry a
/// culinary category (Phase 3).
/// </summary>
public static class InventoryAttentionAdvisor
{
    /// <summary>Thawed food in the refrigerator: use within this many days of thawing (common food-safety guidance for raw meat and fish).</summary>
    public const int ThawedRefrigeratorDays = 2;

    /// <summary>Frozen food keeps safely but loses quality; suggest a check after this many days.</summary>
    public const int FrozenQualityCheckDays = 180;

    /// <summary>Days within which a dated item counts as "use soon".</summary>
    public const int UseSoonDays = 4;

    /// <summary>Days past a date after which the item is "likely discard" instead of "check condition".</summary>
    public const int LikelyDiscardAfterDays = 7;

    /// <summary>Evaluates one lot.</summary>
    /// <param name="facts">Lot facts.</param>
    /// <param name="today">The current calendar date in the user's time zone.</param>
    /// <param name="timeZone">The user's time zone, used to turn transition instants into calendar dates.</param>
    /// <returns>The advisory attention result.</returns>
    public static InventoryAttention Evaluate(InventoryAttentionFacts facts, DateOnly today, TimeZoneInfo timeZone)
    {
        DateOnly ToDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
        var inRefrigerator = facts.StorageLocation == "Refrigerator";

        if (facts.StorageLocation == "Freezer")
        {
            // Freezing stops the printed-date clock, so a frozen lot is judged on quality only.
            return facts.FrozenAt is { } frozenAt && today.DayNumber - ToDate(frozenAt).DayNumber > FrozenQualityCheckDays
                ? new(InventoryAttentionState.CheckCondition, null, InventoryAttentionEvidence.CuratedRule, ["frozen_long_time"])
                : new(InventoryAttentionState.Normal, null, InventoryAttentionEvidence.CuratedRule, ["frozen"]);
        }

        if (facts.LifecycleState is "Thawing" or "Thawed")
        {
            if (!inRefrigerator)
            {
                return new(InventoryAttentionState.CheckCondition, null, InventoryAttentionEvidence.CuratedRule, [facts.LifecycleState == "Thawing" ? "thawing_outside_refrigerator" : "thawed_outside_refrigerator"]);
            }

            var thawStart = facts.ThawedAt ?? facts.ThawStartedAt;
            if (thawStart is { } started)
            {
                var thawEstimate = ToDate(started).AddDays(ThawedRefrigeratorDays);
                // A printed date earlier than the thaw rule stays the stronger evidence.
                return facts.PrintedExpirationDate is { } printed && printed < thawEstimate
                    ? Classify(printed, today, InventoryAttentionEvidence.PrintedDate, "printed_date")
                    : Classify(thawEstimate, today, InventoryAttentionEvidence.CuratedRule, "thawed_in_refrigerator");
            }
        }

        var opened = facts.PackageState == "Opened";
        if (facts.PreparedShelfLifeDate is { } prepared)
        {
            return Classify(prepared, today, InventoryAttentionEvidence.PreparedRecord, "prepared_date");
        }

        if (facts.PrintedExpirationDate is { } date)
        {
            var result = Classify(date, today, InventoryAttentionEvidence.PrintedDate, "printed_date");
            // A printed date describes the sealed package; once opened it no longer supports a
            // confident "fine for now", except for dry foods that keep once opened.
            return opened && !KeepsInPantry(facts) && result.State == InventoryAttentionState.Normal
                ? result with { State = InventoryAttentionState.EstimateUncertain, Reasons = ["printed_date", "opened_after_printed_date"] }
                : result;
        }

        // Owner report 2026-10-30: an opened bag of raw rice asked for attention forever. Dry,
        // shelf-stable foods kept in the pantry stay usable for months once opened (curated list).
        if (opened && KeepsInPantry(facts))
        {
            return new(InventoryAttentionState.Normal, null, InventoryAttentionEvidence.CuratedRule, ["keeps_when_opened"]);
        }

        return opened
            ? new(InventoryAttentionState.EstimateUncertain, null, InventoryAttentionEvidence.Unknown, ["opened_without_date"])
            : new(InventoryAttentionState.Normal, null, InventoryAttentionEvidence.Unknown, ["no_date"]);
    }

    private static bool KeepsInPantry(InventoryAttentionFacts facts) => facts.KeepsWhenOpened && facts.StorageLocation == "Pantry";

    private static InventoryAttention Classify(DateOnly estimate, DateOnly today, InventoryAttentionEvidence evidence, string source)
    {
        var days = estimate.DayNumber - today.DayNumber;
        return days switch
        {
            < -LikelyDiscardAfterDays => new(InventoryAttentionState.LikelyDiscard, estimate, evidence, [source, "date_passed_long_ago"]),
            < 0 => new(InventoryAttentionState.CheckCondition, estimate, evidence, [source, "date_passed"]),
            <= 1 => new(InventoryAttentionState.UsePriority, estimate, evidence, [source, "date_today_or_tomorrow"]),
            <= UseSoonDays => new(InventoryAttentionState.UseSoon, estimate, evidence, [source, "date_soon"]),
            _ => new(InventoryAttentionState.Normal, estimate, evidence, [source])
        };
    }
}
