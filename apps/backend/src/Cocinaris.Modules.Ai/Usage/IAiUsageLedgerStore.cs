namespace Cocinaris.Modules.Ai.Usage;

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
    /// <param name="estimatedUnits">Cocinaris normalized usage credits to reserve (not provider tokens).</param>
    /// <param name="estimatedCostMicroUsd">Estimated provider cost held against the monthly ceiling until the call closes.</param>
    /// <param name="estimatedTokens">Estimated provider tokens held against the rolling 24-hour token windows until the call closes.</param>
    /// <param name="correlationId">Request correlation identifier.</param>
    /// <param name="now">UTC evaluation instant used for day-bound ceilings.</param>
    /// <param name="ceilings">Configured global, per-owner, and concurrency ceilings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exact reservation or rejection outcome; never partially inserts on rejection.</returns>
    Task<AiUsageReservationResult> TryReserveAsync(
        Guid ownerUserId,
        string operation,
        int estimatedUnits,
        long estimatedCostMicroUsd,
        int estimatedTokens,
        string correlationId,
        DateTimeOffset now,
        AiUsageOptions ceilings,
        CancellationToken cancellationToken);

    /// <summary>Sums reserved and settled Cocinaris usage units across all owners for the UTC calendar day containing <paramref name="asOf"/>.</summary>
    Task<int> SumGlobalUnitsForDayAsync(DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>Sums reserved and settled Cocinaris usage units for one owner for the UTC calendar day containing <paramref name="asOf"/>.</summary>
    Task<int> SumOwnerUnitsForDayAsync(Guid ownerUserId, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>Counts one owner's currently open (<see cref="AiUsageEntryStatus.Reserved"/>) reservations.</summary>
    Task<int> CountOpenReservationsAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an open reservation settled with actual Cocinaris usage units, what the call used, and its real cost.
    /// <paramref name="settledUnits"/> are Cocinaris normalized credits (for example suggest=3, expand=5), not provider tokens.
    /// </summary>
    Task SettleAsync(Guid reservationId, int settledUnits, AiCallUsage usage, long costMicroUsd, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an open reservation released without usage credits. A call that reached the provider
    /// still records its usage and real cost against the monthly ceiling.
    /// </summary>
    Task ReleaseAsync(Guid reservationId, AiCallUsage? usage, long costMicroUsd, DateTimeOffset now, CancellationToken cancellationToken);
}
