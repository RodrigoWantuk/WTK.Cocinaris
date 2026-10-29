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
        DateTimeOffset now,
        LotLifecycle lifecycle)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        ProductId = productId;
        Quantity = quantity;
        Storage = storage;
        PackageState = packageState;
        PrintedExpiration = printedExpiration;
        Notes = notes;
        Lifecycle = lifecycle;
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

    /// <summary>Gets the current lifecycle state, transition timestamps, and parent lot.</summary>
    public LotLifecycle Lifecycle { get; private set; }

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

    /// <summary>Creates an active lot with initial version one and, unless supplied, the <see cref="LotLifecycle.AsAcquired"/> lifecycle.</summary>
    public static InventoryLot Create(
        Guid ownerUserId,
        Guid productId,
        LotQuantity quantity,
        LotStorage storage,
        PackageState? packageState,
        PrintedExpiration? printedExpiration,
        PrivateNotes? notes,
        DateTimeOffset now,
        LotLifecycle? lifecycle = null) =>
        new(Guid.NewGuid(), ownerUserId, productId, quantity, storage, packageState, printedExpiration, notes, now, lifecycle ?? LotLifecycle.AsAcquired);

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
        DateTimeOffset? deletedAt,
        LotLifecycle? lifecycle = null)
    {
        if (id == Guid.Empty || ownerUserId == Guid.Empty || productId == Guid.Empty || version < 1 || concurrencyToken == Guid.Empty || updatedAt < createdAt || deletedAt < createdAt)
        {
            throw new ArgumentException("Persisted inventory lot identity, version, or timestamps are invalid.");
        }

        var lot = new InventoryLot(id, ownerUserId, productId, quantity, storage, packageState, printedExpiration, notes, createdAt, lifecycle ?? LotLifecycle.AsAcquired)
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
    /// <remarks>
    /// Consuming or discarding more than an estimated quantity empties the lot: the estimate was the
    /// owner's guess, and the real use is better evidence. Against an exact quantity it fails. A
    /// correction replaces the value and states whether the new value is an estimate.
    /// </remarks>
    /// <param name="type">Consume, Discard, PreparationInputConsumed, or Correct.</param>
    /// <param name="value">Quantity used, or the resulting quantity for a correction.</param>
    /// <param name="reasonCode">Non-localized reason retained in history.</param>
    /// <param name="note">Optional private note retained in history.</param>
    /// <param name="idempotencyKey">Client command key retained in history.</param>
    /// <param name="now">UTC instant of the adjustment.</param>
    /// <param name="correctionIsEstimate">For a correction, whether the resulting value is an estimate; ignored otherwise.</param>
    /// <returns>The immutable transaction.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the lot is not measured or the value is invalid.</exception>
    public InventoryTransaction AdjustMeasured(
        InventoryTransactionType type,
        decimal value,
        string? reasonCode,
        string? note,
        Guid? idempotencyKey,
        DateTimeOffset now,
        bool correctionIsEstimate = false)
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
            InventoryTransactionType.Consume or InventoryTransactionType.Discard or InventoryTransactionType.PreparationInputConsumed when previous.IsEstimate =>
                0m,
            InventoryTransactionType.Consume or InventoryTransactionType.Discard or InventoryTransactionType.PreparationInputConsumed =>
                throw new InvalidOperationException("The adjustment cannot exceed the current measured quantity."),
            InventoryTransactionType.Correct => value,
            _ => throw new InvalidOperationException("The adjustment type is not valid for a measured quantity.")
        };

        var resulting = new LotQuantity.Measured(result, previous.Unit, type == InventoryTransactionType.Correct ? correctionIsEstimate : previous.IsEstimate);
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

    /// <summary>Applies a lifecycle transition to the whole lot and records it.</summary>
    /// <param name="kind">Requested transition.</param>
    /// <param name="targetStorage">Destination storage; required for <see cref="LotTransitionKind.Move"/>, optional otherwise.</param>
    /// <param name="idempotencyKey">Client command key retained in immutable history.</param>
    /// <param name="now">UTC transition instant.</param>
    /// <returns>The immutable <see cref="InventoryTransactionType.LifecycleTransition"/> transaction; quantity is unchanged.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the transition is not allowed for the lot's current state.</exception>
    public InventoryTransaction Transition(LotTransitionKind kind, LotStorage? targetStorage, Guid? idempotencyKey, DateTimeOffset now)
    {
        EnsureActive();
        var (storage, lifecycle, packageState) = PlanTransition(kind, targetStorage, now);
        var detail = new LotTransitionDetail(kind, Lifecycle.State, lifecycle.State, Storage.Location, storage.Location, null);
        Storage = storage;
        Lifecycle = lifecycle;
        PackageState = packageState;
        Touch(now);
        return InventoryTransaction.Create(Id, OwnerUserId, InventoryTransactionType.LifecycleTransition, Quantity, Quantity, null, null, idempotencyKey, now, detail);
    }

    /// <summary>
    /// Moves part of a measured lot into a new derived lot that takes the transition's resulting
    /// state. The source keeps the exact remainder and its own state, so quantity is conserved.
    /// </summary>
    /// <param name="kind"><see cref="LotTransitionKind.Move"/>, <see cref="LotTransitionKind.Freeze"/>, or <see cref="LotTransitionKind.StartThaw"/>.</param>
    /// <param name="quantity">Quantity to split off, in the lot's unit; positive, below the current quantity, at most three decimals.</param>
    /// <param name="targetStorage">Destination storage, with the same rules as <see cref="Transition"/>.</param>
    /// <param name="idempotencyKey">Client command key retained in both immutable histories.</param>
    /// <param name="now">UTC transition instant.</param>
    /// <returns>The derived lot and the source and derived transactions.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the lot is not measured, the quantity is invalid, or the transition is not allowed.</exception>
    public (InventoryLot Derived, InventoryTransaction SourceTransaction, InventoryTransaction DerivedTransaction) SplitTransition(LotTransitionKind kind, decimal quantity, LotStorage? targetStorage, Guid? idempotencyKey, DateTimeOffset now)
    {
        EnsureActive();
        if (kind is not (LotTransitionKind.Move or LotTransitionKind.Freeze or LotTransitionKind.StartThaw))
        {
            throw new InvalidOperationException("Only move, freeze, and start-thaw transitions can apply to part of a lot.");
        }

        if (Quantity is not LotQuantity.Measured previous)
        {
            throw new InvalidOperationException("A partial transition requires a measured quantity.");
        }

        if (quantity <= 0m || quantity >= previous.Value || decimal.Round(quantity, 3) != quantity)
        {
            throw new InvalidOperationException("A partial quantity must be positive, below the current quantity, and have at most three decimal places.");
        }

        var (storage, lifecycle, packageState) = PlanTransition(kind, targetStorage, now);
        var remaining = new LotQuantity.Measured(previous.Value - quantity, previous.Unit, previous.IsEstimate);
        var derived = Create(OwnerUserId, ProductId, new LotQuantity.Measured(quantity, previous.Unit, previous.IsEstimate), storage, packageState, PrintedExpiration, Notes, now, lifecycle with { ParentLotId = Id });
        Quantity = remaining;
        Touch(now);
        var sourceTransaction = InventoryTransaction.Create(Id, OwnerUserId, InventoryTransactionType.TransitionSplit, previous, remaining, null, null, idempotencyKey, now, new LotTransitionDetail(kind, Lifecycle.State, Lifecycle.State, Storage.Location, Storage.Location, derived.Id));
        var derivedTransaction = InventoryTransaction.Create(derived.Id, OwnerUserId, InventoryTransactionType.TransitionDerived, null, derived.Quantity, null, null, idempotencyKey, now, new LotTransitionDetail(kind, Lifecycle.State, lifecycle.State, Storage.Location, storage.Location, Id));
        return (derived, sourceTransaction, derivedTransaction);
    }

    // Shared by whole and partial transitions so both enforce identical rules. These rules check
    // physical consistency only (a frozen lot leaves the freezer by thawing); food-safety advice,
    // such as about refreezing, is advisory and is not enforced here.
    private (LotStorage Storage, LotLifecycle Lifecycle, PackageState? PackageState) PlanTransition(LotTransitionKind kind, LotStorage? target, DateTimeOffset now)
    {
        var inFreezer = Storage.Location == StorageLocation.Freezer;
        if (target?.Location == StorageLocation.Freezer && kind != LotTransitionKind.Freeze)
        {
            throw new InvalidOperationException("Use the freeze transition to put a lot in the freezer.");
        }

        switch (kind)
        {
            case LotTransitionKind.Open:
                if (PackageState == Domain.PackageState.Opened)
                {
                    throw new InvalidOperationException("The package is already open.");
                }

                if (inFreezer && target is not null)
                {
                    throw new InvalidOperationException("Use the start-thaw transition to take a lot out of the freezer.");
                }

                return (target ?? Storage, Lifecycle with { OpenedAt = now }, Domain.PackageState.Opened);
            case LotTransitionKind.Move:
                if (target is null || target == Storage)
                {
                    throw new InvalidOperationException("A move requires a different storage location.");
                }

                if (inFreezer)
                {
                    throw new InvalidOperationException("Use the start-thaw transition to take a lot out of the freezer.");
                }

                return (target, Lifecycle, PackageState);
            case LotTransitionKind.Freeze:
                if (inFreezer)
                {
                    throw new InvalidOperationException("The lot is already in the freezer.");
                }

                LotStorage.TryCreate(StorageLocation.Freezer, null, out var freezer);
                return (freezer!, Lifecycle with { State = LotLifecycleState.Frozen, FrozenAt = now, ThawStartedAt = null, ThawedAt = null }, PackageState);
            case LotTransitionKind.StartThaw:
                if (!inFreezer)
                {
                    throw new InvalidOperationException("Only a lot in the freezer can start thawing.");
                }

                LotStorage.TryCreate(StorageLocation.Refrigerator, null, out var refrigerator);
                return (target ?? refrigerator!, Lifecycle with { State = LotLifecycleState.Thawing, ThawStartedAt = now, ThawedAt = null }, PackageState);
            case LotTransitionKind.CompleteThaw:
                if (Lifecycle.State != LotLifecycleState.Thawing)
                {
                    throw new InvalidOperationException("Only a thawing lot can finish thawing.");
                }

                return (target ?? Storage, Lifecycle with { State = LotLifecycleState.Thawed, ThawedAt = now }, PackageState);
            default:
                throw new InvalidOperationException("The transition is not supported.");
        }
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
        DateTimeOffset occurredAt,
        LotTransitionDetail? transition)
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
        Transition = transition;
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

    /// <summary>Gets the lifecycle facts of a transition transaction; <see langword="null"/> for quantity-only transactions.</summary>
    public LotTransitionDetail? Transition { get; private set; }

    /// <summary>Creates an immutable lifecycle transaction; transition types also carry their lifecycle facts.</summary>
    public static InventoryTransaction Create(
        Guid lotId,
        Guid ownerUserId,
        InventoryTransactionType type,
        LotQuantity? previousQuantity,
        LotQuantity? resultingQuantity,
        string? reasonCode,
        string? note,
        Guid? idempotencyKey,
        DateTimeOffset occurredAt,
        LotTransitionDetail? transition = null) =>
        new(Guid.NewGuid(), lotId, ownerUserId, type, previousQuantity, resultingQuantity, reasonCode, note, idempotencyKey, occurredAt, transition);
}
