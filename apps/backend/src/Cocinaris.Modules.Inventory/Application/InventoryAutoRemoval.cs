namespace Cocinaris.Modules.Inventory.Application;

/// <summary>An active lot whose auto-removal day is at most the given cutoff.</summary>
/// <param name="OwnerUserId">Owner.</param>
/// <param name="LotId">Lot.</param>
/// <param name="AutoRemoveOn">Day after which it is removed.</param>
public sealed record InventoryAutoRemovalCandidate(Guid OwnerUserId, Guid LotId, DateOnly AutoRemoveOn);

/// <summary>Finds active lots that may be due for automatic removal (PLAN-0056).</summary>
public interface IInventoryAutoRemovalSource
{
    /// <summary>Up to <paramref name="limit"/> active lots whose removal day is before <paramref name="before"/>, oldest day first.</summary>
    Task<IReadOnlyList<InventoryAutoRemovalCandidate>> FindDueAsync(DateOnly before, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Removes a lot whose auto-removal day has passed in the owner's calendar (PLAN-0056, owner
/// report 2). The removal is the ordinary lifecycle deletion with reason <c>auto_remove_date</c>,
/// saved with optimistic concurrency and an audit event, so it appears in the lot's history like
/// any other deletion. A lot that changed meanwhile is left for the next run.
/// </summary>
public sealed class InventoryAutoRemovalService(IInventoryLotWriteStore writeStore, TimeProvider timeProvider)
{
    /// <summary>Reason code recorded on the deletion transaction.</summary>
    public const string ReasonCode = "auto_remove_date";

    /// <summary>Removes the lot when its day is before <paramref name="ownerToday"/>; returns whether it was removed.</summary>
    /// <param name="ownerUserId">Owner of the lot.</param>
    /// <param name="lotId">Lot.</param>
    /// <param name="ownerToday">Today in the owner's time zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> RemoveIfDueAsync(Guid ownerUserId, Guid lotId, DateOnly ownerToday, CancellationToken cancellationToken)
    {
        var state = await writeStore.LoadActiveAsync(ownerUserId, lotId, cancellationToken);
        // "Depois do dia tal": the lot stays through its day and is removed from the next day on.
        if (state is null || state.Lot.AutoRemoveOn is not { } day || day >= ownerToday)
        {
            return false;
        }

        var expectedVersion = state.Lot.Version;
        var transaction = state.Lot.Delete(ReasonCode, null, timeProvider.GetUtcNow());
        var outcome = await writeStore.SaveMutationAsync(
            new InventoryLotMutationWrite(ownerUserId, state.Lot, state.Product, expectedVersion, transaction, "inventory.lot.auto_removed", $"{{\"reason\":\"{ReasonCode}\",\"autoRemoveOn\":\"{day:yyyy-MM-dd}\"}}", "inventory-auto-removal", null),
            cancellationToken);
        return outcome == InventoryWriteOutcome.Saved;
    }
}
