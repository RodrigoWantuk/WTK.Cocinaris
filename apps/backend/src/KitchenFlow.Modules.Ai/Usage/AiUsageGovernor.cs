namespace KitchenFlow.Modules.Ai.Usage;

/// <summary>
/// Enforces global daily, per-owner daily, and per-owner concurrency usage ceilings and owns the
/// reserve/settle/release lifecycle of the authoritative usage ledger. Deterministic code, not AI
/// output, decides whether an operation may proceed and what it ultimately costs.
/// Ceiling evaluation and insert are performed atomically by <see cref="IAiUsageLedgerStore.TryReserveAsync"/>.
/// </summary>
public sealed class AiUsageGovernor(IAiUsageLedgerStore store, AiUsageOptions options, TimeProvider timeProvider)
{
    /// <summary>
    /// Attempts to reserve KitchenFlow usage units for one operation.
    /// When the gateway is not administratively disabled, this method only delegates to
    /// <see cref="IAiUsageLedgerStore.TryReserveAsync"/> — it performs no separate read/check/insert.
    /// </summary>
    public Task<AiUsageReservationResult> ReserveAsync(Guid ownerUserId, string operation, int estimatedUnits, string correlationId, CancellationToken cancellationToken)
    {
        if (options.Disabled)
        {
            return Task.FromResult(AiUsageReservationResult.Rejected(AiUsageReservationOutcome.Disabled));
        }

        return store.TryReserveAsync(ownerUserId, operation, estimatedUnits, correlationId, timeProvider.GetUtcNow(), options, cancellationToken);
    }

    /// <summary>
    /// Settles a reservation with KitchenFlow usage units once an operation completes.
    /// Optional <paramref name="promptTokens"/> and <paramref name="completionTokens"/> record provider token metadata only;
    /// they are not usage credits.
    /// </summary>
    public Task SettleAsync(
        Guid reservationId,
        int settledUnits,
        string provider,
        string model,
        CancellationToken cancellationToken,
        int? promptTokens = null,
        int? completionTokens = null) =>
        store.SettleAsync(reservationId, settledUnits, provider, model, promptTokens, completionTokens, timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>Releases a reservation without cost when an operation fails safely before producing usable output.</summary>
    public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken) =>
        store.ReleaseAsync(reservationId, timeProvider.GetUtcNow(), cancellationToken);
}
