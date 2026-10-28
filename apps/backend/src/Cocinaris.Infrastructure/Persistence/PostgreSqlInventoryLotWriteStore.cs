using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>PostgreSQL implementation of the atomic inventory mutation persistence boundary.</summary>
/// <remarks>
/// PostgreSQL unique-index insertion waits for a competing uncommitted row and then either wins or
/// raises the named unique violation after the winner commits. Consequently, the losing request can
/// clear its failed EF unit of work and immediately read the completed authoritative replay record.
/// The <c>idempotency_in_progress</c> application outcome remains a defensive response for manually
/// reserved or otherwise incomplete records and is not expected during this single-transaction path.
/// </remarks>
public sealed class PostgreSqlInventoryLotWriteStore(ApplicationDbContext database) : IInventoryLotWriteStore
{
    /// <inheritdoc />
    public async Task<InventoryIdempotencyRead?> FindIdempotencyAsync(Guid ownerUserId, string scope, Guid key, CancellationToken cancellationToken)
    {
        var record = await database.IdempotencyRecords.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.Scope == scope && item.Key == key)
            .SingleOrDefaultAsync(cancellationToken);
        return record is null
            ? null
            : new InventoryIdempotencyRead(record.RequestHash, ToSuccess(record.StatusCode), DeserializeResponse(record.ResponseBody), record.CompletedAt);
    }

    /// <inheritdoc />
    public async Task<InventoryWriteOutcome> SaveCreatedAsync(InventoryLotCreationWrite write, CancellationToken cancellationToken)
    {
        var idempotency = write.Idempotency;
        database.AddRange(
            ToRecord(write.Product),
            ToRecord(write.Lot),
            ToRecord(write.InitialTransaction),
            new AuditEventRecord { Id = Guid.NewGuid(), ActorUserId = write.OwnerUserId, EventName = "inventory.lot.created", TargetType = "inventory_lot", TargetId = write.Lot.Id, CorrelationId = write.CorrelationId, MetadataJson = "{}", OccurredAt = write.Lot.CreatedAt },
            ToRecord(write.OwnerUserId, idempotency));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return InventoryWriteOutcome.Saved;
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyConflict(exception))
        {
            // PostgreSQL has resolved the unique-key race. The failed tracked graph must not be
            // reused by the application's replay read, otherwise EF may retry stale inserts.
            database.ChangeTracker.Clear();
            return InventoryWriteOutcome.IdempotencyConflict;
        }
        catch
        {
            database.ChangeTracker.Clear();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<InventoryLotMutationState?> LoadActiveAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken)
    {
        var lot = await database.Lots.AsNoTracking().SingleOrDefaultAsync(item => item.Id == lotId && item.OwnerUserId == ownerUserId && item.DeletedAt == null, cancellationToken);
        if (lot is null)
        {
            return null;
        }

        var product = await database.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == lot.ProductId && item.OwnerUserId == ownerUserId, cancellationToken);
        return product is null ? null : new InventoryLotMutationState(ToDomain(lot), ToDomain(product));
    }

    /// <inheritdoc />
    public async Task<InventoryWriteOutcome> SaveMutationAsync(InventoryLotMutationWrite write, CancellationToken cancellationToken)
    {
        var lot = ToRecord(write.Lot);
        database.Lots.Attach(lot);
        database.Entry(lot).State = EntityState.Modified;
        database.Entry(lot).Property(item => item.Version).OriginalValue = write.ExpectedVersion;
        var product = ToRecord(write.Product);
        database.Products.Attach(product);
        database.Entry(product).State = EntityState.Modified;
        if (write.Transaction is not null)
        {
            database.Transactions.Add(ToRecord(write.Transaction));
        }

        database.AuditEvents.Add(new AuditEventRecord { Id = Guid.NewGuid(), ActorUserId = write.OwnerUserId, EventName = write.AuditEventName, TargetType = "inventory_lot", TargetId = write.Lot.Id, CorrelationId = write.CorrelationId, MetadataJson = write.AuditMetadataJson, OccurredAt = write.Lot.UpdatedAt });
        if (write.Idempotency is not null)
        {
            var item = write.Idempotency;
            database.IdempotencyRecords.Add(ToRecord(write.OwnerUserId, item));
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return InventoryWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            return InventoryWriteOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException exception) when (write.Idempotency is not null && IsIdempotencyKeyConflict(exception))
        {
            // The only classified persistence race is this exact PostgreSQL unique constraint.
            // Clear tracked failed inserts before the caller loads the authoritative replay.
            database.ChangeTracker.Clear();
            return InventoryWriteOutcome.IdempotencyConflict;
        }
        catch
        {
            database.ChangeTracker.Clear();
            throw;
        }
    }

    private static Product ToDomain(ProductRecord item)
    {
        ProductName.TryCreate(item.DisplayName, out var name);
        return Product.Restore(item.Id, item.OwnerUserId, name!, item.CreatedAt, item.UpdatedAt, item.IsDeleted);
    }

    private static InventoryLot ToDomain(LotRecord item)
    {
        var quantity = item.MeasuredValue is { } value ? new LotQuantity.Measured(value, Enum.Parse<CanonicalUnit>(item.MeasuredUnit!)) : LotQuantity.FromAvailability(Enum.Parse<AvailabilityState>(item.AvailabilityState!));
        LotStorage.TryCreate(Enum.Parse<StorageLocation>(item.StorageLocation), item.CustomLocation, out var storage);
        PrivateNotes.TryCreate(item.Notes, out var notes);
        return InventoryLot.Restore(item.Id, item.OwnerUserId, item.ProductId, quantity, storage!, item.PackageState is null ? null : Enum.Parse<PackageState>(item.PackageState), item.PrintedExpirationDate is null ? null : new PrintedExpiration(item.PrintedExpirationDate.Value, ExpirationProvenance.UserEntered), notes, item.Version, item.ConcurrencyToken, item.CreatedAt, item.UpdatedAt, item.DeletedAt);
    }

    private static ProductRecord ToRecord(Product item) => new() { Id = item.Id, OwnerUserId = item.OwnerUserId, DisplayName = item.DisplayName, NormalizedSearchName = item.NormalizedSearchName, CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt, IsDeleted = item.IsDeleted };
    private static LotRecord ToRecord(InventoryLot item) => new() { Id = item.Id, OwnerUserId = item.OwnerUserId, ProductId = item.ProductId, MeasuredValue = item.Quantity is LotQuantity.Measured measured ? measured.Value : null, MeasuredUnit = item.Quantity is LotQuantity.Measured unit ? unit.Unit.ToString() : null, AvailabilityState = item.Quantity is LotQuantity.Availability availability ? availability.State.ToString() : null, StorageLocation = item.Storage.Location.ToString(), CustomLocation = item.Storage.CustomLocation, PackageState = item.PackageState?.ToString(), PrintedExpirationDate = item.PrintedExpiration?.Date, ExpirationProvenance = item.PrintedExpiration?.Provenance.ToString(), Notes = item.Notes?.Value, Version = item.Version, ConcurrencyToken = item.ConcurrencyToken, CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt, DeletedAt = item.DeletedAt };
    private static TransactionRecord ToRecord(InventoryTransaction item) => new() { Id = item.Id, OwnerUserId = item.OwnerUserId, LotId = item.LotId, Type = item.Type.ToString(), PreviousMeasuredValue = item.PreviousQuantity is LotQuantity.Measured previous ? previous.Value : null, PreviousMeasuredUnit = item.PreviousQuantity is LotQuantity.Measured previousUnit ? previousUnit.Unit.ToString() : null, PreviousAvailabilityState = item.PreviousQuantity is LotQuantity.Availability previousAvailability ? previousAvailability.State.ToString() : null, ResultingMeasuredValue = item.ResultingQuantity is LotQuantity.Measured resulting ? resulting.Value : null, ResultingMeasuredUnit = item.ResultingQuantity is LotQuantity.Measured resultingUnit ? resultingUnit.Unit.ToString() : null, ResultingAvailabilityState = item.ResultingQuantity is LotQuantity.Availability resultingAvailability ? resultingAvailability.State.ToString() : null, ReasonCode = item.ReasonCode, Note = item.Note, IdempotencyKey = item.IdempotencyKey, OccurredAt = item.OccurredAt };
    private static IdempotencyRecord ToRecord(Guid ownerUserId, InventoryIdempotencyWrite item) => new() { Id = Guid.NewGuid(), OwnerUserId = ownerUserId, Scope = item.Scope, Key = item.Key, RequestHash = item.RequestHash, StatusCode = ToStatusCode(item.Success), ResponseBody = JsonSerializer.Serialize(item.Response), ETag = item.Response.ConcurrencyToken.ToString("N"), CreatedAt = item.CreatedAt, CompletedAt = item.CreatedAt };
    private static InventoryLotView? DeserializeResponse(string? value) => string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<InventoryLotView>(value);
    private static int ToStatusCode(InventoryApplicationSuccess success) => success == InventoryApplicationSuccess.Created ? 201 : success == InventoryApplicationSuccess.Deleted ? 204 : 200;
    private static InventoryApplicationSuccess? ToSuccess(int statusCode) => statusCode switch { 201 => InventoryApplicationSuccess.Created, 204 => InventoryApplicationSuccess.Deleted, 200 => InventoryApplicationSuccess.Succeeded, _ => null };

    private static bool IsIdempotencyKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_idempotency_records_OwnerUserId_Scope_Key"
        };
}
