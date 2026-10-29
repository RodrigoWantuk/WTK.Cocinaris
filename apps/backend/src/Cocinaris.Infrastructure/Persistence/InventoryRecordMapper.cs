using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// Single mapping between inventory domain objects and their PostgreSQL records. Every store that
/// loads a lot and writes it back must use it: a store that skipped a column, such as the
/// lifecycle state, would silently reset that column on its next write.
/// </summary>
internal static class InventoryRecordMapper
{
    public static Product ToDomain(ProductRecord item)
    {
        ProductName.TryCreate(item.DisplayName, out var name);
        return Product.Restore(item.Id, item.OwnerUserId, name!, item.CreatedAt, item.UpdatedAt, item.IsDeleted);
    }

    public static InventoryLot ToDomain(LotRecord item)
    {
        var quantity = item.MeasuredValue is { } value ? new LotQuantity.Measured(value, Enum.Parse<CanonicalUnit>(item.MeasuredUnit!), item.QuantityIsEstimate) : LotQuantity.FromAvailability(Enum.Parse<AvailabilityState>(item.AvailabilityState!));
        LotStorage.TryCreate(Enum.Parse<StorageLocation>(item.StorageLocation), item.CustomLocation, out var storage);
        PrivateNotes.TryCreate(item.Notes, out var notes);
        var lifecycle = new LotLifecycle(Enum.Parse<LotLifecycleState>(item.LifecycleState), item.OpenedAt, item.FrozenAt, item.ThawStartedAt, item.ThawedAt, item.ParentLotId);
        return InventoryLot.Restore(item.Id, item.OwnerUserId, item.ProductId, quantity, storage!, item.PackageState is null ? null : Enum.Parse<PackageState>(item.PackageState), item.PrintedExpirationDate is null ? null : new PrintedExpiration(item.PrintedExpirationDate.Value, ExpirationProvenance.UserEntered), notes, item.Version, item.ConcurrencyToken, item.CreatedAt, item.UpdatedAt, item.DeletedAt, lifecycle);
    }

    public static ProductRecord ToRecord(Product item) => new() { Id = item.Id, OwnerUserId = item.OwnerUserId, DisplayName = item.DisplayName, NormalizedSearchName = item.NormalizedSearchName, CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt, IsDeleted = item.IsDeleted };

    public static LotRecord ToRecord(InventoryLot item) => new()
    {
        Id = item.Id,
        OwnerUserId = item.OwnerUserId,
        ProductId = item.ProductId,
        MeasuredValue = item.Quantity is LotQuantity.Measured measured ? measured.Value : null,
        MeasuredUnit = item.Quantity is LotQuantity.Measured measuredUnit ? measuredUnit.Unit.ToString() : null,
        QuantityIsEstimate = item.Quantity is LotQuantity.Measured { IsEstimate: true },
        AvailabilityState = item.Quantity is LotQuantity.Availability availability ? availability.State.ToString() : null,
        StorageLocation = item.Storage.Location.ToString(),
        CustomLocation = item.Storage.CustomLocation,
        PackageState = item.PackageState?.ToString(),
        PrintedExpirationDate = item.PrintedExpiration?.Date,
        ExpirationProvenance = item.PrintedExpiration?.Provenance.ToString(),
        Notes = item.Notes?.Value,
        LifecycleState = item.Lifecycle.State.ToString(),
        OpenedAt = item.Lifecycle.OpenedAt,
        FrozenAt = item.Lifecycle.FrozenAt,
        ThawStartedAt = item.Lifecycle.ThawStartedAt,
        ThawedAt = item.Lifecycle.ThawedAt,
        ParentLotId = item.Lifecycle.ParentLotId,
        Version = item.Version,
        ConcurrencyToken = item.ConcurrencyToken,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        DeletedAt = item.DeletedAt
    };

    public static TransactionRecord ToRecord(InventoryTransaction item) => new()
    {
        Id = item.Id,
        OwnerUserId = item.OwnerUserId,
        LotId = item.LotId,
        Type = item.Type.ToString(),
        PreviousMeasuredValue = item.PreviousQuantity is LotQuantity.Measured previous ? previous.Value : null,
        PreviousMeasuredUnit = item.PreviousQuantity is LotQuantity.Measured previousUnit ? previousUnit.Unit.ToString() : null,
        PreviousAvailabilityState = item.PreviousQuantity is LotQuantity.Availability previousAvailability ? previousAvailability.State.ToString() : null,
        ResultingMeasuredValue = item.ResultingQuantity is LotQuantity.Measured resulting ? resulting.Value : null,
        ResultingMeasuredUnit = item.ResultingQuantity is LotQuantity.Measured resultingUnit ? resultingUnit.Unit.ToString() : null,
        ResultingAvailabilityState = item.ResultingQuantity is LotQuantity.Availability resultingAvailability ? resultingAvailability.State.ToString() : null,
        ReasonCode = item.ReasonCode,
        Note = item.Note,
        IdempotencyKey = item.IdempotencyKey,
        OccurredAt = item.OccurredAt,
        TransitionKind = item.Transition?.Kind.ToString(),
        PreviousLifecycleState = item.Transition?.PreviousState.ToString(),
        ResultingLifecycleState = item.Transition?.ResultingState.ToString(),
        PreviousStorageLocation = item.Transition?.PreviousStorage.ToString(),
        ResultingStorageLocation = item.Transition?.ResultingStorage.ToString(),
        RelatedLotId = item.Transition?.RelatedLotId
    };
}
