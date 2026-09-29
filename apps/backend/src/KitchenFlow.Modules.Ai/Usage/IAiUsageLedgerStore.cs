namespace KitchenFlow.Modules.Ai.Usage;

/// <summary>Persistence port for the authoritative AI usage ledger.</summary>
public interface IAiUsageLedgerStore
{
    /// <summary>
    /// Atomically evaluates usage ceilings and, when allowed, inserts one reserved ledger row.
    /// PostgreSQL implementations must serialize concurrent reservations with
    /// <c>pg_advisory_xact_lock</c> inside a transaction so count/sum/insert cannot race.
    /// </summary>
    /// <param name="ownerUserId">Authoritative owner of the reservation.</param>
    /// <param name="operation">Registered AI Gateway operation name.</param>
    /// <param name="estimatedUnits">KitchenFlow normalized usage credits to reserve (not provider tokens).</param>
    /// <param name="correlationId">Request correlation identifier.</param>
    /// <param name="now">UTC evaluation instant used for day-bound ceilings.</param>
    /// <param name="ceilings">Configured global, per-owner, and concurrency ceilings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exact reservation or rejection outcome; never partially inserts on rejection.</returns>
    Task<AiUsageReservationResult> TryReserveAsync(
        Guid ownerUserId,
        string operation,
        int estimatedUnits,
        string correlationId,
        DateTimeOffset now,
        AiUsageOptions ceilings,
        CancellationToken cancellationToken);

    /// <summary>Sums reserved and settled KitchenFlow usage units across all owners for the UTC calendar day containing <paramref name="asOf"/>.</summary>
    Task<int> SumGlobalUnitsForDayAsync(DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>Sums reserved and settled KitchenFlow usage units for one owner for the UTC calendar day containing <paramref name="asOf"/>.</summary>
    Task<int> SumOwnerUnitsForDayAsync(Guid ownerUserId, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>Counts one owner's currently open (<see cref="AiUsageEntryStatus.Reserved"/>) reservations.</summary>
    Task<int> CountOpenReservationsAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an open reservation settled with actual KitchenFlow usage units and optional provider token metadata.
    /// <paramref name="settledUnits"/> are KitchenFlow normalized credits (for example suggest=3, expand=5), not provider tokens.
    /// </summary>
    Task SettleAsync(
        Guid reservationId,
        int settledUnits,
        string provider,
        string model,
        int? promptTokens,
        int? completionTokens,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Marks an open reservation released without cost.</summary>
    Task ReleaseAsync(Guid reservationId, DateTimeOffset now, CancellationToken cancellationToken);
}
