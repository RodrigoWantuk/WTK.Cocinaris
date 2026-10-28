using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.Modules.Inventory.Application;

/// <summary>Explicit owner-scoped write boundary for inventory lots and their immutable side effects.</summary>
public interface IInventoryLotWriteStore
{
    /// <summary>Finds an owner-scoped idempotency record for semantic replay without exposing persistence entities.</summary>
    Task<InventoryIdempotencyRead?> FindIdempotencyAsync(Guid ownerUserId, string scope, Guid key, CancellationToken cancellationToken);

    /// <summary>Persists a new product, lot, initial immutable transaction, audit event, and idempotency response atomically.</summary>
    Task<InventoryWriteOutcome> SaveCreatedAsync(InventoryLotCreationWrite write, CancellationToken cancellationToken);

    /// <summary>Loads an active lot and its product for an authorized mutation without exposing persistence records.</summary>
    Task<InventoryLotMutationState?> LoadActiveAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken);

    /// <summary>Persists a domain mutation, immutable transaction, audit record, and optional idempotency response atomically.</summary>
    Task<InventoryWriteOutcome> SaveMutationAsync(InventoryLotMutationWrite write, CancellationToken cancellationToken);
}

/// <summary>Domain state loaded for one owner-scoped mutation.</summary>
public sealed record InventoryLotMutationState(InventoryLot Lot, Product Product);

/// <summary>Atomic persistence request for a newly created product and inventory lot.</summary>
public sealed record InventoryLotCreationWrite(Guid OwnerUserId, Product Product, InventoryLot Lot, InventoryTransaction InitialTransaction, string CorrelationId, InventoryIdempotencyWrite Idempotency);

/// <summary>
/// Atomic persistence request for an already-authorized inventory mutation. Audit metadata is a
/// privacy-safe, pre-serialized projection containing no request body, private note, credential,
/// or token value.
/// </summary>
/// A partial lifecycle transition also supplies the new derived lot and its creation transaction,
/// which are inserted in the same database transaction as the source update.
public sealed record InventoryLotMutationWrite(Guid OwnerUserId, InventoryLot Lot, Product Product, long ExpectedVersion, InventoryTransaction? Transaction, string AuditEventName, string AuditMetadataJson, string CorrelationId, InventoryIdempotencyWrite? Idempotency, InventoryLot? DerivedLot = null, InventoryTransaction? DerivedTransaction = null);

/// <summary>
/// Completed, typed idempotency response persisted with a mutation. Persistence serialization and
/// outer transport mapping remain outside the Inventory module.
/// </summary>
public sealed record InventoryIdempotencyWrite(Guid Key, string Scope, string RequestHash, InventoryApplicationSuccess Success, InventoryLotView Response, DateTimeOffset CreatedAt);

/// <summary>Replayable idempotency state loaded from authoritative PostgreSQL storage.</summary>
public sealed record InventoryIdempotencyRead(string RequestHash, InventoryApplicationSuccess? Success, InventoryLotView? Response, DateTimeOffset? CompletedAt);

/// <summary>Outcome of an optimistic inventory persistence operation.</summary>
public enum InventoryWriteOutcome
{
    /// <summary>The mutation and all immutable side effects were persisted.</summary>
    Saved,
    /// <summary>The persisted lot version no longer matched the expected version.</summary>
    ConcurrencyConflict,
    /// <summary>A concurrent request owns the supplied idempotency key.</summary>
    IdempotencyConflict
}
