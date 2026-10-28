namespace Cocinaris.Modules.Inventory.Domain;

/// <summary>User-owned product referenced by one or more inventory lots.</summary>
public sealed class Product
{
    private Product(Guid id, Guid ownerUserId, ProductName name, DateTimeOffset now)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        DisplayName = name.Value;
        NormalizedSearchName = name.NormalizedSearchValue;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Gets the internal product identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the internal owner identifier.</summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>Gets the user-visible product name.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Gets the normalized product search value.</summary>
    public string NormalizedSearchName { get; private set; }

    /// <summary>Gets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the UTC instant of the last correction.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets whether the product is soft-deleted.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>Creates a product owned by one internal user.</summary>
    public static Product Create(Guid ownerUserId, ProductName name, DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerUserId, name, now);

    /// <summary>Restores a product loaded by an infrastructure adapter without exposing persistence types to the domain.</summary>
    public static Product Restore(Guid id, Guid ownerUserId, ProductName name, DateTimeOffset createdAt, DateTimeOffset updatedAt, bool isDeleted)
    {
        if (id == Guid.Empty || ownerUserId == Guid.Empty || updatedAt < createdAt)
        {
            throw new ArgumentException("Persisted product identity and timestamps are invalid.");
        }

        var product = new Product(id, ownerUserId, name, createdAt) { UpdatedAt = updatedAt, IsDeleted = isDeleted };
        return product;
    }

    /// <summary>Corrects the user-owned display name while retaining its normalized search representation.</summary>
    public void Rename(ProductName name, DateTimeOffset now)
    {
        DisplayName = name.Value;
        NormalizedSearchName = name.NormalizedSearchValue;
        UpdatedAt = now;
    }
}

/// <summary>Authoritative user-owned inventory lot with quantity, lifecycle, and concurrency state.</summary>
public sealed class InventoryLot
{
    private InventoryLot(
        Guid id,
        Guid ownerUserId,
        Guid productId,
        LotQuantity quantity,
        LotStorage storage,
        PackageState? packageState,
        PrintedExpiration? printedExpiration,
        PrivateNotes? notes,
        DateTimeOffset now)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        ProductId = productId;
        Quantity = quantity;
        Storage = storage;
        PackageState = packageState;
        PrintedExpiration = printedExpiration;
        Notes = notes;
        Version = 1;
        ConcurrencyToken = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Gets the internal lot identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the internal owner identifier.</summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>Gets the referenced user-owned product identifier.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the measured or qualitative quantity mode.</summary>
    public LotQuantity Quantity { get; private set; }

    /// <summary>Gets the required storage location.</summary>
    public LotStorage Storage { get; private set; }

    /// <summary>Gets the optional package state.</summary>
    public PackageState? PackageState { get; private set; }

    /// <summary>Gets optional printed expiration evidence.</summary>
    public PrintedExpiration? PrintedExpiration { get; private set; }

    /// <summary>Gets optional private notes.</summary>
    public PrivateNotes? Notes { get; private set; }

    /// <summary>Gets the internal monotonically increasing concurrency version.</summary>
    public long Version { get; private set; }

    /// <summary>Gets the opaque token that identifies the current externally observable version.</summary>
    public Guid ConcurrencyToken { get; private set; }

    /// <summary>Gets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the UTC instant of the last successful mutation.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the soft-deletion instant, when deleted.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Gets whether the lot is soft-deleted.</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>Creates an active lot with initial version one.</summary>
    public static InventoryLot Create(
        Guid ownerUserId,
        Guid productId,
        LotQuantity quantity,
        LotStorage storage,
        PackageState? packageState,
        PrintedExpiration? printedExpiration,
        PrivateNotes? notes,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerUserId, productId, quantity, storage, packageState, printedExpiration, notes, now);

    /// <summary>Restores a lot for an application command while preserving its concurrency and deletion state.</summary>
    public static InventoryLot Restore(
        Guid id,
        Guid ownerUserId,
        Guid productId,
        LotQuantity quantity,
        LotStorage storage,
        PackageState? packageState,
        PrintedExpiration? printedExpiration,
        PrivateNotes? notes,
        long version,
        Guid concurrencyToken,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt)
    {
        if (id == Guid.Empty || ownerUserId == Guid.Empty || productId == Guid.Empty || version < 1 || concurrencyToken == Guid.Empty || updatedAt < createdAt || deletedAt < createdAt)
        {
            throw new ArgumentException("Persisted inventory lot identity, version, or timestamps are invalid.");
        }

        var lot = new InventoryLot(id, ownerUserId, productId, quantity, storage, packageState, printedExpiration, notes, createdAt)
        {
            Version = version,
            ConcurrencyToken = concurrencyToken,
            UpdatedAt = updatedAt,
            DeletedAt = deletedAt
        };
        return lot;
    }

    /// <summary>Corrects mutable metadata and advances the concurrency version.</summary>
    public void UpdateMetadata(
        LotStorage storage,
        PackageState? packageState,
        PrintedExpiration? printedExpiration,
        PrivateNotes? notes,
        DateTimeOffset now)
    {
        EnsureActive();
        Storage = storage;
        PackageState = packageState;
        PrintedExpiration = printedExpiration;
        Notes = notes;
        Touch(now);
    }

    /// <summary>Applies a measured consume, discard, or resulting-quantity correction.</summary>
    public InventoryTransaction AdjustMeasured(
        InventoryTransactionType type,
        decimal value,
        string? reasonCode,
        string? note,
        Guid? idempotencyKey,
        DateTimeOffset now)
    {
        EnsureActive();
        if (Quantity is not LotQuantity.Measured previous)
        {
            throw new InvalidOperationException("Measured adjustments require a measured quantity.");
        }

        if ((type is InventoryTransactionType.Consume or InventoryTransactionType.Discard or InventoryTransactionType.PreparationInputConsumed && value <= 0m) || (type == InventoryTransactionType.Correct && value < 0m) || decimal.Round(value, 3) != value)
        {
            throw new InvalidOperationException("Adjustment value must be nonnegative for corrections and positive for consumption or discard, with at most three decimal places.");
        }

        var result = type switch
        {
            InventoryTransactionType.Consume or InventoryTransactionType.Discard or InventoryTransactionType.PreparationInputConsumed when value <= previous.Value =>
                previous.Value - value,
            InventoryTransactionType.Consume or InventoryTransactionType.Discard or InventoryTransactionType.PreparationInputConsumed =>
                throw new InvalidOperationException("The adjustment cannot exceed the current measured quantity."),
            InventoryTransactionType.Correct => value,
            _ => throw new InvalidOperationException("The adjustment type is not valid for a measured quantity.")
        };

        var resulting = new LotQuantity.Measured(result, previous.Unit);
        Quantity = resulting;
        Touch(now);
        return InventoryTransaction.Create(Id, OwnerUserId, type, previous, resulting, reasonCode, note, idempotencyKey, now);
    }

    /// <summary>Changes qualitative availability and records immutable history.</summary>
    public InventoryTransaction ChangeAvailability(
        AvailabilityState state,
        string? reasonCode,
        string? note,
        Guid? idempotencyKey,
        DateTimeOffset now)
    {
        EnsureActive();
        if (Quantity is not LotQuantity.Availability previous)
        {
            throw new InvalidOperationException("Availability changes require an availability quantity.");
        }

        var resulting = new LotQuantity.Availability(state);
        Quantity = resulting;
        Touch(now);
        return InventoryTransaction.Create(Id, OwnerUserId, InventoryTransactionType.AvailabilityChanged, previous, resulting, reasonCode, note, idempotencyKey, now);
    }

    /// <summary>Soft-deletes the lot and records immutable deletion history.</summary>
    public InventoryTransaction Delete(string? reasonCode, string? note, DateTimeOffset now)
    {
        EnsureActive();
        DeletedAt = now;
        Touch(now);
        return InventoryTransaction.Create(Id, OwnerUserId, InventoryTransactionType.Deleted, Quantity, Quantity, reasonCode, note, null, now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
        ConcurrencyToken = Guid.NewGuid();
    }

    private void EnsureActive()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The inventory lot is deleted.");
        }
    }
}

/// <summary>Immutable record of an inventory lifecycle transition.</summary>
public sealed class InventoryTransaction
{
    private InventoryTransaction(
        Guid id,
        Guid lotId,
        Guid ownerUserId,
        InventoryTransactionType type,
        LotQuantity? previousQuantity,
        LotQuantity? resultingQuantity,
        string? reasonCode,
        string? note,
        Guid? idempotencyKey,
        DateTimeOffset occurredAt)
    {
        Id = id;
        LotId = lotId;
        OwnerUserId = ownerUserId;
        Type = type;
        PreviousQuantity = previousQuantity;
        ResultingQuantity = resultingQuantity;
        ReasonCode = reasonCode;
        Note = note;
        IdempotencyKey = idempotencyKey;
        OccurredAt = occurredAt;
    }

    /// <summary>Gets the transaction identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the affected lot identifier.</summary>
    public Guid LotId { get; private set; }

    /// <summary>Gets the transaction owner identifier.</summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>Gets the immutable transition type.</summary>
    public InventoryTransactionType Type { get; private set; }

    /// <summary>Gets the quantity before the transition, when applicable.</summary>
    public LotQuantity? PreviousQuantity { get; private set; }

    /// <summary>Gets the quantity after the transition, when applicable.</summary>
    public LotQuantity? ResultingQuantity { get; private set; }

    /// <summary>Gets the stable reason code, when supplied.</summary>
    public string? ReasonCode { get; private set; }

    /// <summary>Gets the optional private note.</summary>
    public string? Note { get; private set; }

    /// <summary>Gets the client idempotency key, when applicable.</summary>
    public Guid? IdempotencyKey { get; private set; }

    /// <summary>Gets the UTC transition instant.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Creates an immutable lifecycle transaction.</summary>
    public static InventoryTransaction Create(
        Guid lotId,
        Guid ownerUserId,
        InventoryTransactionType type,
        LotQuantity? previousQuantity,
        LotQuantity? resultingQuantity,
        string? reasonCode,
        string? note,
        Guid? idempotencyKey,
        DateTimeOffset occurredAt) =>
        new(Guid.NewGuid(), lotId, ownerUserId, type, previousQuantity, resultingQuantity, reasonCode, note, idempotencyKey, occurredAt);
}
