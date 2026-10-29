using System.Text;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.UnitTests;

public sealed class InventoryDomainTests
{
    [Fact]
    public void ProductNameUsesUnicodeScalarBoundariesAndTrimming()
    {
        Assert.True(ProductName.TryCreate($"  {new string('a', 160)}  ", out var maximum));
        Assert.Equal(160, maximum!.Value.Length);
        Assert.False(ProductName.TryCreate(new string('a', 161), out _));
        Assert.True(ProductName.TryCreate(string.Concat(Enumerable.Repeat("🍅", 160)), out _));
        Assert.False(ProductName.TryCreate(string.Concat(Enumerable.Repeat("🍅", 161)), out _));
        Assert.False(ProductName.TryCreate("\u2003\u2002", out _));
    }

    [Fact]
    public void MeasuredQuantityRejectsNonPositiveAndOverPreciseValues()
    {
        Assert.False(LotQuantity.TryCreateMeasured(0m, CanonicalUnit.Gram, out _));
        Assert.False(LotQuantity.TryCreateMeasured(1.0001m, CanonicalUnit.Gram, out _));
        Assert.True(LotQuantity.TryCreateMeasured(1.125m, CanonicalUnit.Gram, out var quantity));
        Assert.IsType<LotQuantity.Measured>(quantity);
    }

    [Fact]
    public void OtherStorageRequiresCustomLocation()
    {
        Assert.False(LotStorage.TryCreate(StorageLocation.Other, null, out _));
        Assert.False(LotStorage.TryCreate(StorageLocation.Pantry, "Shelf", out _));
        Assert.True(LotStorage.TryCreate(StorageLocation.Other, "Top shelf", out var storage));
        Assert.Equal("Top shelf", storage!.CustomLocation);
    }

    [Fact]
    public void CustomLocationAndPrivateNotesEnforceUnicodeBoundaries()
    {
        Assert.True(LotStorage.TryCreate(StorageLocation.Other, $"  {string.Concat(Enumerable.Repeat("🧊", 80))}  ", out var storage));
        Assert.Equal(80, storage!.CustomLocation!.EnumerateRunes().Count());
        Assert.False(LotStorage.TryCreate(StorageLocation.Other, string.Concat(Enumerable.Repeat("🧊", 81)), out _));
        Assert.True(PrivateNotes.TryCreate($"  {string.Concat(Enumerable.Repeat("🍲", 1000))}  ", out var notes));
        Assert.Equal(1000, notes!.Value.EnumerateRunes().Count());
        Assert.False(PrivateNotes.TryCreate(string.Concat(Enumerable.Repeat("🍲", 1001)), out _));
        Assert.True(PrivateNotes.TryCreate("  ", out var blank));
        Assert.Null(blank);
    }

    [Fact]
    public void ExpirationRequiresDefinedUserProvenance()
    {
        var date = new DateOnly(2026, 12, 31);
        var expiration = new PrintedExpiration(date, ExpirationProvenance.UserEntered);

        Assert.Equal(date, expiration.Date);
        Assert.Equal(ExpirationProvenance.UserEntered, expiration.Provenance);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrintedExpiration(date, (ExpirationProvenance)99));
    }

    [Fact]
    public void ConsumePreservesHistoryAndCannotExceedCurrentQuantity()
    {
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.True(LotQuantity.TryCreateMeasured(100m, CanonicalUnit.Gram, out var quantity));
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        var lot = InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), quantity!, storage!, null, null, null, now);

        var transaction = lot.AdjustMeasured(InventoryTransactionType.Consume, 25m, "meal", null, Guid.NewGuid(), now.AddMinutes(1));

        var resulting = Assert.IsType<LotQuantity.Measured>(transaction.ResultingQuantity);
        Assert.Equal(75m, resulting.Value);
        Assert.Equal(2, lot.Version);
        var discardException = Assert.Throws<InvalidOperationException>(() =>
            lot.AdjustMeasured(InventoryTransactionType.Discard, 76m, "waste", null, Guid.NewGuid(), now.AddMinutes(2)));
        Assert.Equal("The adjustment cannot exceed the current measured quantity.", discardException.Message);
        var consumeException = Assert.Throws<InvalidOperationException>(() =>
            lot.AdjustMeasured(InventoryTransactionType.Consume, 76m, "meal", null, Guid.NewGuid(), now.AddMinutes(3)));
        Assert.Equal("The adjustment cannot exceed the current measured quantity.", consumeException.Message);
    }

    [Fact]
    public void ConsumingBeyondAnEstimateEmptiesTheLotAndKeepsTheEstimateMark()
    {
        var now = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        var lot = InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), new LotQuantity.Measured(500m, CanonicalUnit.Gram, isEstimate: true), storage!, null, null, null, now);

        var partial = lot.AdjustMeasured(InventoryTransactionType.Consume, 180m, "meal", null, Guid.NewGuid(), now.AddMinutes(1));
        var beyond = lot.AdjustMeasured(InventoryTransactionType.Consume, 400m, "meal", null, Guid.NewGuid(), now.AddMinutes(2));

        Assert.Equal(new LotQuantity.Measured(320m, CanonicalUnit.Gram, isEstimate: true), partial.ResultingQuantity);
        Assert.Equal(new LotQuantity.Measured(0m, CanonicalUnit.Gram, isEstimate: true), beyond.ResultingQuantity);
    }

    [Fact]
    public void ACorrectionStatesWhetherTheNewValueIsAnEstimate()
    {
        var now = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        var lot = InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), new LotQuantity.Measured(500m, CanonicalUnit.Gram, isEstimate: true), storage!, null, null, null, now);

        lot.AdjustMeasured(InventoryTransactionType.Correct, 432m, "weighed", null, Guid.NewGuid(), now.AddMinutes(1));
        Assert.False(Assert.IsType<LotQuantity.Measured>(lot.Quantity).IsEstimate);

        lot.AdjustMeasured(InventoryTransactionType.Correct, 250m, "looked", null, Guid.NewGuid(), now.AddMinutes(2), correctionIsEstimate: true);
        Assert.True(Assert.IsType<LotQuantity.Measured>(lot.Quantity).IsEstimate);

        var (derived, _, _) = lot.SplitTransition(LotTransitionKind.Freeze, 100m, null, Guid.NewGuid(), now.AddMinutes(3));
        Assert.True(Assert.IsType<LotQuantity.Measured>(derived.Quantity).IsEstimate);
        Assert.True(Assert.IsType<LotQuantity.Measured>(lot.Quantity).IsEstimate);
    }

    [Fact]
    public void CorrectionCanSetMeasuredQuantityToZeroWithoutChangingItsUnit()
    {
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.True(LotQuantity.TryCreateMeasured(100m, CanonicalUnit.Gram, out var quantity));
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        var lot = InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), quantity!, storage!, null, null, null, now);

        var transaction = lot.AdjustMeasured(InventoryTransactionType.Correct, 0m, "counted", null, Guid.NewGuid(), now.AddMinutes(1));

        Assert.Equal(0m, Assert.IsType<LotQuantity.Measured>(transaction.ResultingQuantity).Value);
        Assert.Equal(CanonicalUnit.Gram, Assert.IsType<LotQuantity.Measured>(transaction.ResultingQuantity).Unit);
    }

    [Fact]
    public void AvailabilityLotsCannotReceiveMeasuredAdjustments()
    {
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        var lot = InventoryLot.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LotQuantity.FromAvailability(AvailabilityState.Available),
            storage!,
            null,
            null,
            null,
            now);

        Assert.Throws<InvalidOperationException>(() =>
            lot.AdjustMeasured(InventoryTransactionType.Consume, 1m, "meal", null, Guid.NewGuid(), now));
    }

    [Theory]
    [InlineData(AvailabilityState.Available)]
    [InlineData(AvailabilityState.Low)]
    [InlineData(AvailabilityState.Unavailable)]
    public void AvailabilityTransitionsRecordPreviousResultAndAdvanceVersion(AvailabilityState resultingState)
    {
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.True(LotStorage.TryCreate(StorageLocation.Refrigerator, null, out var storage));
        var lot = InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), LotQuantity.FromAvailability(AvailabilityState.Low), storage!, null, null, null, now);

        var transaction = lot.ChangeAvailability(resultingState, "checked", "  observed  ", Guid.NewGuid(), now.AddMinutes(1));

        Assert.Equal(AvailabilityState.Low, Assert.IsType<LotQuantity.Availability>(transaction.PreviousQuantity).State);
        Assert.Equal(resultingState, Assert.IsType<LotQuantity.Availability>(transaction.ResultingQuantity).State);
        Assert.Equal(resultingState, Assert.IsType<LotQuantity.Availability>(lot.Quantity).State);
        Assert.Equal(2, lot.Version);
        Assert.Equal(now.AddMinutes(1), lot.UpdatedAt);
    }

    [Theory]
    [InlineData(InventoryTransactionType.Consume)]
    [InlineData(InventoryTransactionType.Discard)]
    public void MeasuredDeltaRequiresPositiveBoundedCanonicalPrecision(InventoryTransactionType type)
    {
        var lot = CreateMeasuredLot(10m, out var now);

        Assert.Throws<InvalidOperationException>(() => lot.AdjustMeasured(type, 0m, "test", null, Guid.NewGuid(), now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => lot.AdjustMeasured(type, 10.0001m, "test", null, Guid.NewGuid(), now.AddMinutes(1)));
        var transaction = lot.AdjustMeasured(type, 10m, "test", null, Guid.NewGuid(), now.AddMinutes(1));

        Assert.Equal(0m, Assert.IsType<LotQuantity.Measured>(transaction.ResultingQuantity).Value);
    }

    [Fact]
    public void DeletionIsSingleUseAndBlocksEverySubsequentMutation()
    {
        var lot = CreateMeasuredLot(10m, out var now);
        var deletion = lot.Delete("mistake", null, now.AddMinutes(1));

        Assert.True(lot.IsDeleted);
        Assert.Equal(InventoryTransactionType.Deleted, deletion.Type);
        Assert.Equal(2, lot.Version);
        Assert.Throws<InvalidOperationException>(() => lot.Delete("again", null, now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => lot.AdjustMeasured(InventoryTransactionType.Consume, 1m, "meal", null, Guid.NewGuid(), now.AddMinutes(2)));
        Assert.True(LotStorage.TryCreate(StorageLocation.Freezer, null, out var storage));
        Assert.Throws<InvalidOperationException>(() => lot.UpdateMetadata(storage!, null, null, null, now.AddMinutes(2)));
    }

    [Fact]
    public void ProductRenameAndMetadataCorrectionPreserveIdentityAndAdvanceTimestamps()
    {
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.True(ProductName.TryCreate("Tomato", out var original));
        var product = Product.Create(Guid.NewGuid(), original!, now);
        Assert.True(ProductName.TryCreate("  Heirloom Tomato  ", out var renamed));
        product.Rename(renamed!, now.AddMinutes(1));
        var lot = CreateMeasuredLot(10m, out _);
        var originalConcurrencyToken = lot.ConcurrencyToken;
        Assert.True(LotStorage.TryCreate(StorageLocation.Freezer, null, out var storage));

        lot.UpdateMetadata(storage!, PackageState.Opened, new PrintedExpiration(new DateOnly(2026, 8, 1), ExpirationProvenance.UserEntered), null, now.AddMinutes(2));

        Assert.Equal("Heirloom Tomato", product.DisplayName);
        Assert.Equal("HEIRLOOM TOMATO", product.NormalizedSearchName);
        Assert.Equal(now.AddMinutes(1), product.UpdatedAt);
        Assert.Equal(2, lot.Version);
        Assert.NotEqual(Guid.Empty, lot.ConcurrencyToken);
        Assert.NotEqual(originalConcurrencyToken, lot.ConcurrencyToken);
        Assert.Equal(now.AddMinutes(2), lot.UpdatedAt);
        Assert.Equal(StorageLocation.Freezer, lot.Storage.Location);
    }

    [Fact]
    public void RestorationRejectsInvalidIdentityVersionQuantityAndTimestamps()
    {
        var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.True(ProductName.TryCreate("Tomato", out var name));
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        Assert.Throws<ArgumentException>(() => Product.Restore(Guid.Empty, Guid.NewGuid(), name!, now, now, false));
        Assert.Throws<ArgumentException>(() => Product.Restore(Guid.NewGuid(), Guid.NewGuid(), name!, now, now.AddMinutes(-1), false));
        Assert.Throws<ArgumentException>(() => InventoryLot.Restore(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new LotQuantity.Measured(1m, CanonicalUnit.Gram), storage!, null, null, null, 0, Guid.NewGuid(), now, now, null));
        Assert.Throws<ArgumentException>(() => InventoryLot.Restore(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new LotQuantity.Measured(1m, CanonicalUnit.Gram), storage!, null, null, null, 1, Guid.NewGuid(), now, now, now.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(() => InventoryLot.Restore(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new LotQuantity.Measured(1m, CanonicalUnit.Gram), storage!, null, null, null, 1, Guid.Empty, now, now, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LotQuantity.Measured(-1m, CanonicalUnit.Gram));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LotQuantity.Measured(1.0001m, CanonicalUnit.Gram));
        Assert.Throws<ArgumentOutOfRangeException>(() => LotQuantity.FromAvailability((AvailabilityState)99));
    }

    [Fact]
    public void AdjustmentCommandValidatesReasonNoteModesAndPrecision()
    {
        Assert.False(InventoryAdjustmentCommand.TryCreate("Consume", 1m, null, null, null, out _, out var missingReason));
        Assert.Contains("reasonCode", missingReason.Keys);
        Assert.False(InventoryAdjustmentCommand.TryCreate("Consume", 1.0001m, null, "meal", null, out _, out var precision));
        Assert.Contains("value", precision.Keys);
        Assert.False(InventoryAdjustmentCommand.TryCreate("AvailabilityChanged", 1m, "Low", "checked", null, out _, out var mixed));
        Assert.Contains("value", mixed.Keys);
        Assert.False(InventoryAdjustmentCommand.TryCreate("Correct", 0m, null, "counted", new string('n', 1001), out _, out var note));
        Assert.Contains("note", note.Keys);
        Assert.True(InventoryAdjustmentCommand.TryCreate("Correct", 0m, null, "  counted  ", "  corrected  ", out var valid, out var errors));
        Assert.Empty(errors);
        Assert.Equal("counted", valid!.ReasonCode);
        Assert.Equal("corrected", valid.Note);
    }

    private static InventoryLot CreateMeasuredLot(decimal value, out DateTimeOffset now)
    {
        now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        Assert.True(LotQuantity.TryCreateMeasured(value, CanonicalUnit.Gram, out var quantity));
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        return InventoryLot.Create(Guid.NewGuid(), Guid.NewGuid(), quantity!, storage!, null, null, null, now);
    }
}
