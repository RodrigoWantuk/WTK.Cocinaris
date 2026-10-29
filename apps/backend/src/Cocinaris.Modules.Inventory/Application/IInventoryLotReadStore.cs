namespace Cocinaris.Modules.Inventory.Application;

/// <summary>Owner-scoped read boundary for inventory lots and their immutable history.</summary>
public interface IInventoryLotReadStore
{
    /// <summary>Returns one active lot for the supplied owner, or <see langword="null"/> without disclosing another owner's resource.</summary>
    Task<InventoryLotReadModel?> FindActiveAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken);

    /// <summary>Returns a cursor page that is structurally scoped to one owner.</summary>
    Task<InventoryLotReadPage> ListAsync(InventoryLotReadQuery query, CancellationToken cancellationToken);

    /// <summary>Returns up to <paramref name="limit"/> active, non-depleted lots of one owner, most recently updated first.</summary>
    Task<IReadOnlyList<InventoryLotReadModel>> ListActiveAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken);

    /// <summary>Returns immutable history for a lot only when the owner can observe that lot.</summary>
    Task<IReadOnlyList<InventoryHistoryReadModel>?> GetHistoryAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken);
}

/// <summary>Input for an owner-scoped lot list query.</summary>
public sealed record InventoryLotReadQuery(Guid OwnerUserId, int PageSize, string? Status, string? StorageLocation, string? Search, InventoryLotReadCursor? Cursor);

/// <summary>Sort position used by a trusted application cursor token.</summary>
public sealed record InventoryLotReadCursor(DateTimeOffset UpdatedAt, Guid LotId);

/// <summary>Owner-scoped lot page and its next sort position, if more records exist.</summary>
public sealed record InventoryLotReadPage(IReadOnlyList<InventoryLotReadModel> Items, InventoryLotReadCursor? NextCursor);

/// <summary>Persistence-independent representation of one inventory lot for application reads.</summary>
public sealed record InventoryLotReadModel(Guid LotId, Guid ProductId, string ProductName, decimal? MeasuredValue, string? MeasuredUnit, string? AvailabilityState, string StorageLocation, string? CustomLocation, string? PackageState, DateOnly? PrintedExpirationDate, string? Notes, Guid ConcurrencyToken, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt, InventoryLifecycleView? Lifecycle = null);

/// <summary>
/// Persistence-independent history item. A transaction item has an immutable lifecycle type and
/// quantity snapshots; a metadata-correction item is a safe audit projection with field names
/// only, never private values or notes.
/// </summary>
public sealed record InventoryHistoryReadModel(Guid EntryId, string Kind, string? TransactionType, decimal? PreviousMeasuredValue, string? PreviousMeasuredUnit, string? PreviousAvailabilityState, decimal? ResultingMeasuredValue, string? ResultingMeasuredUnit, string? ResultingAvailabilityState, string? ReasonCode, IReadOnlyList<string>? ChangedFields, DateTimeOffset OccurredAt, InventoryTransitionView? Transition = null);
