using System.Text.Json;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of the preparation transaction boundary. One <see cref="SaveAsync"/>
/// call stages every parent mutation and output insert in the same EF unit of work; PostgreSQL
/// commits all rows together or rejects the complete graph.
/// </summary>
public sealed class PostgreSqlInventoryPreparationStore(ApplicationDbContext database) : IInventoryPreparationStore
{
    private const string IdempotencyScope = "inventory.preparations.create";

    /// <inheritdoc />
    public async Task<PreparationIdempotencyRead?> FindIdempotencyAsync(Guid ownerUserId, Guid key, CancellationToken cancellationToken)
    {
        var record = await database.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.Scope == IdempotencyScope && item.Key == key, cancellationToken);
        return record is null ? null : new PreparationIdempotencyRead(record.RequestHash, Deserialize(record.ResponseBody), record.CompletedAt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreparationInputState>> LoadActiveInputsAsync(Guid ownerUserId, IReadOnlyList<Guid> lotIds, CancellationToken cancellationToken)
    {
        var records = await (from lot in database.Lots.AsNoTracking()
                             join product in database.Products.AsNoTracking() on new { lot.ProductId, lot.OwnerUserId } equals new { ProductId = product.Id, product.OwnerUserId }
                             where lot.OwnerUserId == ownerUserId && lot.DeletedAt == null && lotIds.Contains(lot.Id) && !product.IsDeleted
                             orderby lot.Id
                             select new { Lot = lot, Product = product }).ToListAsync(cancellationToken);
        return records.Select(item => new PreparationInputState(ToDomain(item.Lot), ToDomain(item.Product))).ToList();
    }

    /// <inheritdoc />
    public async Task<Product?> FindActiveProductAsync(Guid ownerUserId, Guid productId, CancellationToken cancellationToken)
    {
        var record = await database.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == productId && item.OwnerUserId == ownerUserId && !item.IsDeleted, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<PreparationWriteOutcome> SaveAsync(PreparationWrite write, CancellationToken cancellationToken)
    {
        if (write.NewOutputProduct is not null)
        {
            database.Products.Add(ToRecord(write.NewOutputProduct));
        }

        foreach (var input in write.Inputs.OrderBy(item => item.Lot.Id))
        {
            var lot = ToRecord(input.Lot);
            database.Lots.Attach(lot);
            database.Entry(lot).State = EntityState.Modified;
            database.Entry(lot).Property(item => item.Version).OriginalValue = input.ExpectedVersion;
            database.Transactions.Add(ToRecord(input.Transaction));
        }

        database.PreparationBatches.Add(new PreparationBatchRecord
        {
            Id = write.Batch.Id,
            OwnerUserId = write.Batch.OwnerUserId,
            OutputProductId = write.Batch.OutputProductId,
            DeclaredYieldMeasuredValue = write.DeclaredYield.MeasuredValue,
            DeclaredYieldMeasuredUnit = write.DeclaredYield.Unit,
            DeclaredYieldAvailabilityState = write.DeclaredYield.AvailabilityState,
            SourceType = write.Batch.SourceType.ToString(),
            PreparedAt = write.Batch.PreparedAt,
            CreatedAt = write.Batch.CreatedAt
        });

        foreach (var input in write.Inputs)
        {
            database.PreparationInputs.Add(new PreparationInputRecord
            {
                BatchId = write.Batch.Id,
                OwnerUserId = write.OwnerUserId,
                InputLotId = input.Lot.Id,
                ConsumedValue = input.ConsumedValue,
                ConsumedUnit = ((LotQuantity.Measured)input.Transaction.PreviousQuantity!).Unit.ToString()
            });
        }

        foreach (var output in write.Outputs)
        {
            database.Lots.Add(ToRecord(output.Lot));
            database.Transactions.Add(ToRecord(output.Transaction));
            database.PreparationOutputs.Add(new PreparationOutputRecord { BatchId = write.Batch.Id, OwnerUserId = write.OwnerUserId, OutputLotId = output.Lot.Id });
            database.PreparedLots.Add(new PreparedLotRecord
            {
                LotId = output.Lot.Id,
                OwnerUserId = write.OwnerUserId,
                BatchId = write.Batch.Id,
                LifecycleState = PreparedComponentLifecycleState.Prepared.ToString(),
                PreparedAt = write.Batch.PreparedAt,
                ShelfLifeDate = output.ShelfLifeEvidence.Date,
                ShelfLifeSource = output.ShelfLifeEvidence.Source.ToString(),
                ShelfLifeConfidence = output.ShelfLifeEvidence.Confidence.ToString(),
                ShelfLifeConditions = output.ShelfLifeEvidence.Conditions
            });
        }

        database.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            ActorUserId = write.OwnerUserId,
            EventName = "inventory.preparation.created",
            TargetType = "inventory_preparation",
            TargetId = write.Batch.Id,
            CorrelationId = write.CorrelationId,
            // Counts and source are operationally useful while avoiding product names, notes, values, or request bodies.
            MetadataJson = JsonSerializer.Serialize(new { source = "ManualPreparation", inputCount = write.Inputs.Count, outputCount = write.Outputs.Count }),
            OccurredAt = write.Batch.CreatedAt
        });
        database.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserId = write.OwnerUserId,
            Scope = IdempotencyScope,
            Key = write.Idempotency.Key,
            RequestHash = write.Idempotency.RequestHash,
            StatusCode = 201,
            ResponseBody = JsonSerializer.Serialize(write.Idempotency.Response),
            ETag = null,
            CreatedAt = write.Idempotency.CreatedAt,
            CompletedAt = write.Idempotency.CreatedAt
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return PreparationWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            return PreparationWriteOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyConflict(exception))
        {
            database.ChangeTracker.Clear();
            return PreparationWriteOutcome.IdempotencyConflict;
        }
        catch
        {
            database.ChangeTracker.Clear();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PreparationBatchView?> GetAsync(Guid ownerUserId, Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await (from item in database.PreparationBatches.AsNoTracking()
                           join product in database.Products.AsNoTracking() on new { item.OutputProductId, item.OwnerUserId } equals new { OutputProductId = product.Id, product.OwnerUserId }
                           where item.Id == batchId && item.OwnerUserId == ownerUserId
                           select new { Batch = item, Product = product }).SingleOrDefaultAsync(cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var inputs = await database.PreparationInputs.AsNoTracking().Where(item => item.BatchId == batchId && item.OwnerUserId == ownerUserId)
            .OrderBy(item => item.InputLotId).Select(item => new PreparationInputView(item.InputLotId, new InventoryQuantity(item.ConsumedValue, item.ConsumedUnit, null))).ToListAsync(cancellationToken);
        var outputRecords = await (from output in database.PreparationOutputs.AsNoTracking()
                                   join lot in database.Lots.AsNoTracking() on new { Id = output.OutputLotId, output.OwnerUserId } equals new { lot.Id, lot.OwnerUserId }
                                   join prepared in database.PreparedLots.AsNoTracking() on new { LotId = lot.Id, lot.OwnerUserId } equals new { prepared.LotId, prepared.OwnerUserId }
                                   where output.BatchId == batchId && output.OwnerUserId == ownerUserId
                                   orderby lot.CreatedAt, lot.Id
                                   select new { Lot = lot, Prepared = prepared }).ToListAsync(cancellationToken);
        var outputs = outputRecords.Select(item => ToOutputView(item.Lot, batch.Product.DisplayName, item.Prepared)).ToList();
        var declaredYield = ToDeclaredYield(batch.Batch);
        return new PreparationBatchView(batch.Batch.Id, batch.Batch.SourceType, batch.Product.Id, batch.Product.DisplayName, declaredYield, batch.Batch.PreparedAt, inputs, outputs, batch.Batch.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<InventoryLotProvenanceView?> GetLotProvenanceAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken)
    {
        var exists = await database.Lots.AsNoTracking().AnyAsync(item => item.Id == lotId && item.OwnerUserId == ownerUserId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        const int maximumRelatedBatches = 50;
        var consumed = await RelatedBatchIdsAsync(database.PreparationInputs.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId && item.InputLotId == lotId).Select(item => item.BatchId), ownerUserId, maximumRelatedBatches, cancellationToken);
        var produced = await RelatedBatchIdsAsync(database.PreparationOutputs.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId && item.OutputLotId == lotId).Select(item => item.BatchId), ownerUserId, maximumRelatedBatches, cancellationToken);
        var views = await GetManyAsync(ownerUserId, consumed.BatchIds.Concat(produced.BatchIds).Distinct().ToList(), cancellationToken);
        var consumedBy = consumed.BatchIds.Select(id => views[id]).ToList();
        var producedBy = produced.BatchIds.Select(id => views[id]).ToList();
        return new InventoryLotProvenanceView(lotId, consumedBy, consumed.IsTruncated, producedBy, produced.IsTruncated);
    }

    private static PreparationOutputView ToOutputView(LotRecord lot, string productName, PreparedLotRecord prepared) =>
        new(new InventoryLotView(lot.Id, lot.ProductId, productName, new InventoryQuantity(lot.MeasuredValue, lot.MeasuredUnit, lot.AvailabilityState), lot.StorageLocation, lot.CustomLocation, lot.PackageState, lot.PrintedExpirationDate, lot.Notes, lot.ConcurrencyToken, lot.CreatedAt, lot.UpdatedAt), new PreparedLotMetadataView(prepared.BatchId, prepared.LifecycleState, prepared.PreparedAt, prepared.ShelfLifeDate, prepared.ShelfLifeSource, prepared.ShelfLifeConfidence, prepared.ShelfLifeConditions));

    private static InventoryQuantity ToDeclaredYield(PreparationBatchRecord batch) => new(batch.DeclaredYieldMeasuredValue, batch.DeclaredYieldMeasuredUnit, batch.DeclaredYieldAvailabilityState);

    private async Task<IReadOnlyDictionary<Guid, PreparationBatchView>> GetManyAsync(Guid ownerUserId, IReadOnlyList<Guid> batchIds, CancellationToken cancellationToken)
    {
        if (batchIds.Count == 0) { return new Dictionary<Guid, PreparationBatchView>(); }

        var batches = await (from item in database.PreparationBatches.AsNoTracking()
                             join product in database.Products.AsNoTracking() on new { item.OutputProductId, item.OwnerUserId } equals new { OutputProductId = product.Id, product.OwnerUserId }
                             where item.OwnerUserId == ownerUserId && batchIds.Contains(item.Id)
                             select new { Batch = item, Product = product }).ToListAsync(cancellationToken);
        var retainedIds = batches.Select(item => item.Batch.Id).ToList();
        var inputs = await database.PreparationInputs.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId && retainedIds.Contains(item.BatchId)).OrderBy(item => item.InputLotId).ToListAsync(cancellationToken);
        var outputRecords = await (from output in database.PreparationOutputs.AsNoTracking()
                                   join lot in database.Lots.AsNoTracking() on new { Id = output.OutputLotId, output.OwnerUserId } equals new { lot.Id, lot.OwnerUserId }
                                   join prepared in database.PreparedLots.AsNoTracking() on new { LotId = lot.Id, lot.OwnerUserId } equals new { prepared.LotId, prepared.OwnerUserId }
                                   where output.OwnerUserId == ownerUserId && retainedIds.Contains(output.BatchId)
                                   orderby lot.CreatedAt, lot.Id
                                   select new { output.BatchId, Lot = lot, Prepared = prepared }).ToListAsync(cancellationToken);
        return batches.ToDictionary(
            item => item.Batch.Id,
            item => new PreparationBatchView(
                item.Batch.Id,
                item.Batch.SourceType,
                item.Product.Id,
                item.Product.DisplayName,
                ToDeclaredYield(item.Batch),
                item.Batch.PreparedAt,
                inputs.Where(input => input.BatchId == item.Batch.Id).Select(input => new PreparationInputView(input.InputLotId, new InventoryQuantity(input.ConsumedValue, input.ConsumedUnit, null))).ToList(),
                outputRecords.Where(output => output.BatchId == item.Batch.Id).Select(output => ToOutputView(output.Lot, item.Product.DisplayName, output.Prepared)).ToList(),
                item.Batch.CreatedAt));
    }

    /// <summary>
    /// Reads one more identifier than the public bound so provenance can distinguish a complete
    /// result from a deliberately truncated projection without issuing one query per batch.
    /// </summary>
    private async Task<RelatedBatchIds> RelatedBatchIdsAsync(IQueryable<Guid> ids, Guid ownerUserId, int maximumRelatedBatches, CancellationToken cancellationToken)
    {
        var batchIds = await (from id in ids.Distinct()
                              join batch in database.PreparationBatches.AsNoTracking() on new { Id = id, OwnerUserId = ownerUserId } equals new { batch.Id, batch.OwnerUserId }
                              orderby batch.PreparedAt descending, batch.Id descending
                              select batch.Id).Take(maximumRelatedBatches + 1).ToListAsync(cancellationToken);
        return new RelatedBatchIds(batchIds.Take(maximumRelatedBatches).ToList(), batchIds.Count > maximumRelatedBatches);
    }

    /// <summary>Contains a direction's stable bounded batch identifiers and completeness signal.</summary>
    private sealed record RelatedBatchIds(IReadOnlyList<Guid> BatchIds, bool IsTruncated);

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
    private static LotRecord ToRecord(InventoryLot item) => new() { Id = item.Id, OwnerUserId = item.OwnerUserId, ProductId = item.ProductId, MeasuredValue = item.Quantity is LotQuantity.Measured measured ? measured.Value : null, MeasuredUnit = item.Quantity is LotQuantity.Measured measuredUnit ? measuredUnit.Unit.ToString() : null, AvailabilityState = item.Quantity is LotQuantity.Availability availability ? availability.State.ToString() : null, StorageLocation = item.Storage.Location.ToString(), CustomLocation = item.Storage.CustomLocation, PackageState = item.PackageState?.ToString(), PrintedExpirationDate = item.PrintedExpiration?.Date, ExpirationProvenance = item.PrintedExpiration?.Provenance.ToString(), Notes = item.Notes?.Value, Version = item.Version, ConcurrencyToken = item.ConcurrencyToken, CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt, DeletedAt = item.DeletedAt };
    private static TransactionRecord ToRecord(InventoryTransaction item) => new() { Id = item.Id, OwnerUserId = item.OwnerUserId, LotId = item.LotId, Type = item.Type.ToString(), PreviousMeasuredValue = item.PreviousQuantity is LotQuantity.Measured previous ? previous.Value : null, PreviousMeasuredUnit = item.PreviousQuantity is LotQuantity.Measured previousUnit ? previousUnit.Unit.ToString() : null, PreviousAvailabilityState = item.PreviousQuantity is LotQuantity.Availability previousAvailability ? previousAvailability.State.ToString() : null, ResultingMeasuredValue = item.ResultingQuantity is LotQuantity.Measured resulting ? resulting.Value : null, ResultingMeasuredUnit = item.ResultingQuantity is LotQuantity.Measured resultingUnit ? resultingUnit.Unit.ToString() : null, ResultingAvailabilityState = item.ResultingQuantity is LotQuantity.Availability resultingAvailability ? resultingAvailability.State.ToString() : null, ReasonCode = item.ReasonCode, Note = item.Note, IdempotencyKey = item.IdempotencyKey, OccurredAt = item.OccurredAt };
    private static PreparationBatchView? Deserialize(string? body) => string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<PreparationBatchView>(body);
    private static bool IsIdempotencyKeyConflict(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_idempotency_records_OwnerUserId_Scope_Key" };
}
