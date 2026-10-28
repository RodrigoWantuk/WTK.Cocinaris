using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.Modules.Inventory.Application;

/// <summary>
/// Executes lifecycle transitions for an already authorized, user-owned inventory lot.
/// Persistence adapters load and atomically save the lot and returned immutable transaction;
/// this use case owns the transition choice and domain invocation.
/// </summary>
public sealed class InventoryLotLifecycleUseCase
{
    /// <summary>Applies a validated adjustment and returns its immutable transaction.</summary>
    /// <param name="lot">Authorized active lot loaded by the persistence adapter.</param>
    /// <param name="command">Validated and normalized adjustment command.</param>
    /// <param name="idempotencyKey">Client command key retained in immutable history.</param>
    /// <param name="occurredAt">UTC instant at which the transition occurs.</param>
    /// <returns>The immutable transaction representing the transition.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the lot's current quantity mode rejects the command.</exception>
    public InventoryTransaction ApplyAdjustment(InventoryLot lot, InventoryAdjustmentCommand command, Guid idempotencyKey, DateTimeOffset occurredAt) =>
        command.Type switch
        {
            InventoryTransactionType.Consume => lot.AdjustMeasured(InventoryTransactionType.Consume, command.Value!.Value, command.ReasonCode, command.Note, idempotencyKey, occurredAt),
            InventoryTransactionType.Discard => lot.AdjustMeasured(InventoryTransactionType.Discard, command.Value!.Value, command.ReasonCode, command.Note, idempotencyKey, occurredAt),
            InventoryTransactionType.Correct => lot.AdjustMeasured(InventoryTransactionType.Correct, command.Value!.Value, command.ReasonCode, command.Note, idempotencyKey, occurredAt),
            InventoryTransactionType.AvailabilityChanged => lot.ChangeAvailability(command.AvailabilityState!.Value, command.ReasonCode, command.Note, idempotencyKey, occurredAt),
            _ => throw new InvalidOperationException("The adjustment is invalid for this lot.")
        };

    /// <summary>Soft-deletes an authorized active lot and returns its immutable deletion transaction.</summary>
    /// <param name="lot">Authorized active lot loaded by the persistence adapter.</param>
    /// <param name="occurredAt">UTC instant at which deletion occurs.</param>
    /// <returns>The immutable deletion transaction.</returns>
    public InventoryTransaction Delete(InventoryLot lot, DateTimeOffset occurredAt) => lot.Delete(null, null, occurredAt);
}
