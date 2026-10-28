using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.UnitTests;

public sealed class InventoryLifecycleTransitionTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddDays(2);

    [Fact]
    public void NewLotsAreAsAcquiredAndPreparedLotsArePrepared()
    {
        Assert.Equal(LotLifecycleState.AsAcquired, Lot(StorageLocation.Pantry).Lifecycle.State);
        var prepared = InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), new LotQuantity.Measured(1m, CanonicalUnit.Unit), Storage(StorageLocation.Refrigerator), null, null, null, Created, LotLifecycle.Prepared);
        Assert.Equal(LotLifecycleState.Prepared, prepared.Lifecycle.State);
    }

    [Fact]
    public void OpenMarksThePackageOpenedAndCanMoveItInTheSameStep()
    {
        var lot = Lot(StorageLocation.Pantry, PackageState.Sealed);
        var version = lot.Version;

        var transaction = lot.Transition(LotTransitionKind.Open, Storage(StorageLocation.Refrigerator), Guid.NewGuid(), Later);

        Assert.Equal(PackageState.Opened, lot.PackageState);
        Assert.Equal(Later, lot.Lifecycle.OpenedAt);
        Assert.Equal(StorageLocation.Refrigerator, lot.Storage.Location);
        Assert.Equal(version + 1, lot.Version);
        Assert.Equal(InventoryTransactionType.LifecycleTransition, transaction.Type);
        Assert.Equal(new LotTransitionDetail(LotTransitionKind.Open, LotLifecycleState.AsAcquired, LotLifecycleState.AsAcquired, StorageLocation.Pantry, StorageLocation.Refrigerator, null), transaction.Transition);
        Assert.Equal(transaction.PreviousQuantity, transaction.ResultingQuantity);
    }

    [Fact]
    public void OpenRejectsAnAlreadyOpenedPackage()
    {
        var lot = Lot(StorageLocation.Pantry, PackageState.Opened);
        Assert.Throws<InvalidOperationException>(() => lot.Transition(LotTransitionKind.Open, null, null, Later));
    }

    [Fact]
    public void MoveRequiresADifferentNonFreezerLocationAndALotOutsideTheFreezer()
    {
        var lot = Lot(StorageLocation.Pantry);
        Assert.Throws<InvalidOperationException>(() => lot.Transition(LotTransitionKind.Move, null, null, Later));
        Assert.Throws<InvalidOperationException>(() => lot.Transition(LotTransitionKind.Move, Storage(StorageLocation.Pantry), null, Later));
        Assert.Throws<InvalidOperationException>(() => lot.Transition(LotTransitionKind.Move, Storage(StorageLocation.Freezer), null, Later));

        lot.Transition(LotTransitionKind.Move, Storage(StorageLocation.Refrigerator), null, Later);
        Assert.Equal(StorageLocation.Refrigerator, lot.Storage.Location);

        var frozen = Lot(StorageLocation.Freezer);
        Assert.Throws<InvalidOperationException>(() => frozen.Transition(LotTransitionKind.Move, Storage(StorageLocation.Pantry), null, Later));
    }

    [Fact]
    public void FreezeMovesToTheFreezerAndClearsAPreviousThaw()
    {
        var lot = Lot(StorageLocation.Freezer);
        lot.Transition(LotTransitionKind.StartThaw, null, null, Created.AddHours(1));
        lot.Transition(LotTransitionKind.CompleteThaw, null, null, Created.AddHours(10));

        // Refreezing is allowed: food-safety advice about it is advisory, not a domain block.
        lot.Transition(LotTransitionKind.Freeze, null, null, Later);

        Assert.Equal(LotLifecycleState.Frozen, lot.Lifecycle.State);
        Assert.Equal(StorageLocation.Freezer, lot.Storage.Location);
        Assert.Equal(Later, lot.Lifecycle.FrozenAt);
        Assert.Null(lot.Lifecycle.ThawStartedAt);
        Assert.Null(lot.Lifecycle.ThawedAt);
        Assert.Throws<InvalidOperationException>(() => lot.Transition(LotTransitionKind.Freeze, null, null, Later));
    }

    [Fact]
    public void StartThawRequiresTheFreezerAndDefaultsToTheRefrigerator()
    {
        Assert.Throws<InvalidOperationException>(() => Lot(StorageLocation.Pantry).Transition(LotTransitionKind.StartThaw, null, null, Later));
        Assert.Throws<InvalidOperationException>(() => Lot(StorageLocation.Freezer).Transition(LotTransitionKind.StartThaw, Storage(StorageLocation.Freezer), null, Later));

        // A product bought frozen is AsAcquired in the freezer and can thaw directly.
        var lot = Lot(StorageLocation.Freezer);
        lot.Transition(LotTransitionKind.StartThaw, null, null, Later);
        Assert.Equal(LotLifecycleState.Thawing, lot.Lifecycle.State);
        Assert.Equal(StorageLocation.Refrigerator, lot.Storage.Location);
        Assert.Equal(Later, lot.Lifecycle.ThawStartedAt);

        var counter = Lot(StorageLocation.Freezer);
        counter.Transition(LotTransitionKind.StartThaw, Storage(StorageLocation.Pantry), null, Later);
        Assert.Equal(StorageLocation.Pantry, counter.Storage.Location);
    }

    [Fact]
    public void CompleteThawRequiresAThawingLot()
    {
        Assert.Throws<InvalidOperationException>(() => Lot(StorageLocation.Refrigerator).Transition(LotTransitionKind.CompleteThaw, null, null, Later));

        var lot = Lot(StorageLocation.Freezer);
        lot.Transition(LotTransitionKind.StartThaw, null, null, Created.AddHours(1));
        lot.Transition(LotTransitionKind.CompleteThaw, null, null, Later);
        Assert.Equal(LotLifecycleState.Thawed, lot.Lifecycle.State);
        Assert.Equal(Later, lot.Lifecycle.ThawedAt);
    }

    [Fact]
    public void OnlyFreezeCanTargetTheFreezer()
    {
        var lot = Lot(StorageLocation.Pantry, PackageState.Sealed);
        Assert.Throws<InvalidOperationException>(() => lot.Transition(LotTransitionKind.Open, Storage(StorageLocation.Freezer), null, Later));
    }

    [Fact]
    public void PartialThawSplitsTheLotAndConservesQuantity()
    {
        // The accepted example: 2,000 g frozen chicken, thaw 500 g.
        var lot = Lot(StorageLocation.Refrigerator, PackageState.Sealed, 2000m);
        lot.Transition(LotTransitionKind.Freeze, null, null, Created);
        var key = Guid.NewGuid();

        var (derived, sourceTransaction, derivedTransaction) = lot.SplitTransition(LotTransitionKind.StartThaw, 500m, null, key, Later);

        Assert.Equal(new LotQuantity.Measured(1500m, CanonicalUnit.Gram), lot.Quantity);
        Assert.Equal(LotLifecycleState.Frozen, lot.Lifecycle.State);
        Assert.Equal(StorageLocation.Freezer, lot.Storage.Location);
        Assert.Equal(new LotQuantity.Measured(500m, CanonicalUnit.Gram), derived.Quantity);
        Assert.Equal(LotLifecycleState.Thawing, derived.Lifecycle.State);
        Assert.Equal(StorageLocation.Refrigerator, derived.Storage.Location);
        Assert.Equal(lot.Id, derived.Lifecycle.ParentLotId);
        Assert.Equal(lot.ProductId, derived.ProductId);
        Assert.Equal(lot.OwnerUserId, derived.OwnerUserId);
        Assert.Equal(lot.PackageState, derived.PackageState);
        Assert.Equal(Created, derived.Lifecycle.FrozenAt);

        Assert.Equal(InventoryTransactionType.TransitionSplit, sourceTransaction.Type);
        Assert.Equal(new LotQuantity.Measured(2000m, CanonicalUnit.Gram), sourceTransaction.PreviousQuantity);
        Assert.Equal(lot.Quantity, sourceTransaction.ResultingQuantity);
        Assert.Equal(derived.Id, sourceTransaction.Transition!.RelatedLotId);
        Assert.Equal(InventoryTransactionType.TransitionDerived, derivedTransaction.Type);
        Assert.Equal(derived.Id, derivedTransaction.LotId);
        Assert.Null(derivedTransaction.PreviousQuantity);
        Assert.Equal(lot.Id, derivedTransaction.Transition!.RelatedLotId);
        Assert.Equal(LotLifecycleState.Frozen, derivedTransaction.Transition.PreviousState);
        Assert.Equal(LotLifecycleState.Thawing, derivedTransaction.Transition.ResultingState);
        Assert.Equal(key, sourceTransaction.IdempotencyKey);
        Assert.Equal(key, derivedTransaction.IdempotencyKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    [InlineData(1500)]
    [InlineData(0.0001)]
    public void PartialQuantityMustBePositiveBelowTheCurrentQuantityAndCanonical(decimal quantity)
    {
        var lot = Lot(StorageLocation.Pantry, null, 1000m);
        Assert.Throws<InvalidOperationException>(() => lot.SplitTransition(LotTransitionKind.Freeze, quantity, null, null, Later));
        Assert.Equal(new LotQuantity.Measured(1000m, CanonicalUnit.Gram), lot.Quantity);
    }

    [Fact]
    public void PartialTransitionsRequireAMeasuredLotAndASplittableKind()
    {
        var qualitative = InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), LotQuantity.FromAvailability(AvailabilityState.Available), Storage(StorageLocation.Pantry), null, null, null, Created);
        Assert.Throws<InvalidOperationException>(() => qualitative.SplitTransition(LotTransitionKind.Freeze, 1m, null, null, Later));

        var lot = Lot(StorageLocation.Pantry, PackageState.Sealed, 1000m);
        Assert.Throws<InvalidOperationException>(() => lot.SplitTransition(LotTransitionKind.Open, 100m, null, null, Later));
        Assert.Throws<InvalidOperationException>(() => lot.SplitTransition(LotTransitionKind.CompleteThaw, 100m, null, null, Later));
    }

    [Fact]
    public void ARejectedPartialTransitionLeavesTheSourceUnchanged()
    {
        var lot = Lot(StorageLocation.Pantry, null, 1000m);
        var version = lot.Version;
        Assert.Throws<InvalidOperationException>(() => lot.SplitTransition(LotTransitionKind.StartThaw, 100m, null, null, Later));
        Assert.Equal(version, lot.Version);
        Assert.Equal(new LotQuantity.Measured(1000m, CanonicalUnit.Gram), lot.Quantity);
    }

    [Fact]
    public void DeletedLotsCannotTransition()
    {
        var lot = Lot(StorageLocation.Pantry, null, 1000m);
        lot.Delete(null, null, Later);
        Assert.Throws<InvalidOperationException>(() => lot.Transition(LotTransitionKind.Freeze, null, null, Later));
        Assert.Throws<InvalidOperationException>(() => lot.SplitTransition(LotTransitionKind.Freeze, 1m, null, null, Later));
    }

    private static InventoryLot Lot(StorageLocation location, PackageState? packageState = null, decimal grams = 1000m) =>
        InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), new LotQuantity.Measured(grams, CanonicalUnit.Gram), Storage(location), packageState, null, null, Created);

    private static LotStorage Storage(StorageLocation location)
    {
        Assert.True(LotStorage.TryCreate(location, location == StorageLocation.Other ? "Shelf" : null, out var storage));
        return storage!;
    }
}
