namespace Cocinaris.Modules.Ai.Usage;

/// <summary>
/// Configured usage ceilings enforced by <see cref="AiUsageGovernor"/> before any provider is invoked.
/// ADR-0005 requires user, period, concurrency, and global budget limits with explicit disablement.
/// </summary>
public sealed class AiUsageOptions
{
    /// <summary>Gets or sets the maximum total usage units reservable across all owners per UTC calendar day.</summary>
    public int GlobalDailyUnitCeiling { get; set; } = 5_000;

    /// <summary>Gets or sets the maximum usage units reservable by one owner per UTC calendar day.</summary>
    public int UserDailyUnitCeiling { get; set; } = 200;

    /// <summary>Gets or sets the maximum number of concurrently reserved (not yet settled or released) operations per owner.</summary>
    public int UserConcurrencyCeiling { get; set; } = 2;

    /// <summary>
    /// Gets or sets the most the deployment may spend on AI providers per UTC calendar month, in US
    /// dollars (PLAN-0040). Settled and released calls count at their real cost; open reservations
    /// count at their operation's estimate.
    /// </summary>
    public decimal MonthlyCostCeilingUsd { get; set; } = 5m;

    /// <summary>
    /// Gets or sets the most provider tokens (prompt plus completion) the whole deployment may use
    /// in any rolling 24-hour window (owner request, 2026-10-29). Closed calls count at their
    /// reported tokens, failed ones included; open reservations at their operation's estimate.
    /// </summary>
    public int GlobalTokensPer24Hours { get; set; } = 600_000;

    /// <summary>Gets or sets the most provider tokens one owner may use in any rolling 24-hour window.</summary>
    public int UserTokensPer24Hours { get; set; } = 150_000;

    /// <summary>Gets or sets whether the AI Gateway is administratively disabled regardless of remaining budget.</summary>
    public bool Disabled { get; set; }

    /// <summary>Validates that configured ceilings are positive and coherent.</summary>
    public bool IsValid() => GlobalDailyUnitCeiling > 0 && UserDailyUnitCeiling > 0 && UserConcurrencyCeiling > 0 && UserDailyUnitCeiling <= GlobalDailyUnitCeiling && MonthlyCostCeilingUsd > 0
        && UserTokensPer24Hours > 0 && UserTokensPer24Hours <= GlobalTokensPer24Hours;

    /// <summary>Gets the monthly ceiling in micro-dollars.</summary>
    public long MonthlyCostCeilingMicroUsd => (long)(MonthlyCostCeilingUsd * 1_000_000m);
}

/// <summary>Lifecycle state of one usage ledger reservation.</summary>
public enum AiUsageEntryStatus
{
    /// <summary>Units are reserved pending settlement or release.</summary>
    Reserved,
    /// <summary>The operation completed and actual usage was recorded.</summary>
    Settled,
    /// <summary>The reservation was released without producing usable output; no cost is charged.</summary>
    Released
}

/// <summary>Application-facing usage ledger entry.</summary>
/// <remarks>
/// <see cref="ReservedUnits"/> and <see cref="SettledUnits"/> are Cocinaris normalized usage credits
/// (suggest candidates = 3, expand selected = 5 by default), not provider tokens.
/// </remarks>
public sealed record AiUsageEntry(Guid Id, Guid OwnerUserId, string Operation, AiUsageEntryStatus Status, int ReservedUnits, int? SettledUnits, string? Provider, string? Model, string CorrelationId, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt);

/// <summary>Outcome of an attempted usage reservation.</summary>
public enum AiUsageReservationOutcome
{
    /// <summary>The reservation succeeded and the operation may proceed.</summary>
    Reserved,
    /// <summary>The AI Gateway is administratively disabled.</summary>
    Disabled,
    /// <summary>The global daily unit ceiling would be exceeded.</summary>
    GlobalBudgetExhausted,
    /// <summary>The owner's daily unit ceiling would be exceeded.</summary>
    UserBudgetExhausted,
    /// <summary>The owner already has the maximum number of concurrent reservations.</summary>
    ConcurrencyExhausted,
    /// <summary>The deployment's monthly provider spend ceiling would be exceeded.</summary>
    MonthlyCostExhausted,
    /// <summary>The deployment's token limit for the last 24 hours would be exceeded.</summary>
    GlobalTokenWindowExhausted,
    /// <summary>The owner's token limit for the last 24 hours would be exceeded.</summary>
    UserTokenWindowExhausted
}

/// <summary>Typed result of a reservation attempt.</summary>
public sealed record AiUsageReservationResult(AiUsageReservationOutcome Outcome, Guid? ReservationId)
{
    /// <summary>Creates a successful reservation result.</summary>
    public static AiUsageReservationResult Success(Guid reservationId) => new(AiUsageReservationOutcome.Reserved, reservationId);

    /// <summary>Creates a rejected reservation result.</summary>
    public static AiUsageReservationResult Rejected(AiUsageReservationOutcome outcome) => new(outcome, null);
}
