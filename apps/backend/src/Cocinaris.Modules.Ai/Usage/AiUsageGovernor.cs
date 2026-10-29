namespace Cocinaris.Modules.Ai.Usage;

/// <summary>
/// Enforces global daily, per-owner daily, and per-owner concurrency usage ceilings and owns the
/// reserve/settle/release lifecycle of the authoritative usage ledger. Deterministic code, not AI
/// output, decides whether an operation may proceed and what it ultimately costs.
/// Ceiling evaluation and insert are performed atomically by <see cref="IAiUsageLedgerStore.TryReserveAsync"/>.
/// </summary>
/// <remarks>The monthly monetary ceiling (PLAN-0040) is evaluated in the same atomic reservation.</remarks>
public sealed class AiUsageGovernor(IAiUsageLedgerStore store, AiUsageOptions options, AiPricingOptions pricing, TimeProvider timeProvider)
{
    /// <summary>
    /// Attempts to reserve Cocinaris usage units for one operation.
    /// When the gateway is not administratively disabled, this method only delegates to
    /// <see cref="IAiUsageLedgerStore.TryReserveAsync"/> — it performs no separate read/check/insert.
    /// </summary>
    public Task<AiUsageReservationResult> ReserveAsync(Guid ownerUserId, string operation, int estimatedUnits, long estimatedCostMicroUsd, int estimatedTokens, string correlationId, CancellationToken cancellationToken)
    {
        if (options.Disabled)
        {
            return Task.FromResult(AiUsageReservationResult.Rejected(AiUsageReservationOutcome.Disabled));
        }

        return store.TryReserveAsync(ownerUserId, operation, estimatedUnits, estimatedCostMicroUsd, estimatedTokens, correlationId, timeProvider.GetUtcNow(), options, cancellationToken);
    }

    /// <summary>Real provider cost of a call in micro-dollars, from the configured prices.</summary>
    public long CostMicroUsd(AiCallUsage usage) => pricing.CostMicroUsd(usage.Model, usage.PromptTokens, usage.CompletionTokens);

    /// <summary>Settles a reservation with Cocinaris usage units and records what the call used at its real price.</summary>
    public Task SettleAsync(Guid reservationId, int settledUnits, AiCallUsage usage, CancellationToken cancellationToken) =>
        store.SettleAsync(reservationId, settledUnits, usage, pricing.CostMicroUsd(usage.Model, usage.PromptTokens, usage.CompletionTokens), timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>Settles a priced-per-call operation (an image) at the cost the provider reported (PLAN-0030).</summary>
    public Task SettleAtCostAsync(Guid reservationId, int settledUnits, AiCallUsage usage, long costMicroUsd, CancellationToken cancellationToken) =>
        store.SettleAsync(reservationId, settledUnits, usage, costMicroUsd, timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>Releases a priced-per-call operation, recording what a billed but rejected result cost.</summary>
    public Task ReleaseAtCostAsync(Guid reservationId, AiCallUsage? usage, long costMicroUsd, CancellationToken cancellationToken) =>
        store.ReleaseAsync(reservationId, usage, costMicroUsd, timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>Releases a reservation without usage credits; provider tokens spent on failed attempts are still costed.</summary>
    public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken, AiCallUsage? usage = null) =>
        store.ReleaseAsync(reservationId, usage, usage is null ? 0 : pricing.CostMicroUsd(usage.Model, usage.PromptTokens, usage.CompletionTokens), timeProvider.GetUtcNow(), cancellationToken);
}
