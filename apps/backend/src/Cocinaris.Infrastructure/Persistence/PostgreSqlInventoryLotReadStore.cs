using Cocinaris.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>PostgreSQL implementation of the explicit owner-scoped inventory read boundary.</summary>
public sealed class PostgreSqlInventoryLotReadStore(ApplicationDbContext database) : IInventoryLotReadStore
{
    /// <inheritdoc />
    public async Task<InventoryLotReadModel?> FindActiveAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken)
    {
        return await (from lot in database.Lots.AsNoTracking()
                      join product in database.Products.AsNoTracking() on lot.ProductId equals product.Id
                      join prepared in database.PreparedLots.AsNoTracking() on lot.Id equals prepared.LotId into preparedLots
                      from prepared in preparedLots.DefaultIfEmpty()
                      where lot.Id == lotId && lot.OwnerUserId == ownerUserId && product.OwnerUserId == ownerUserId && lot.DeletedAt == null
                      select ToReadModel(lot, product.DisplayName, prepared == null ? null : prepared.ShelfLifeDate)).SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InventoryLotReadPage> ListAsync(InventoryLotReadQuery query, CancellationToken cancellationToken)
    {
        var lots = database.Lots.AsNoTracking().Where(lot => lot.OwnerUserId == query.OwnerUserId);
        lots = query.Status switch
        {
            "deleted" => lots.Where(lot => lot.DeletedAt != null),
            "depleted" => lots.Where(lot => lot.DeletedAt == null && ((lot.MeasuredValue != null && lot.MeasuredValue == 0m) || lot.AvailabilityState == "Unavailable")),
            _ => lots.Where(lot => lot.DeletedAt == null && ((lot.MeasuredValue != null && lot.MeasuredValue > 0m) || (lot.MeasuredValue == null && lot.AvailabilityState != "Unavailable")))
        };
        if (query.StorageLocation is not null)
        {
            lots = lots.Where(lot => lot.StorageLocation == query.StorageLocation);
        }

        var records = from lot in lots
                      join product in database.Products.AsNoTracking() on lot.ProductId equals product.Id
                      join prepared in database.PreparedLots.AsNoTracking() on lot.Id equals prepared.LotId into preparedLots
                      from prepared in preparedLots.DefaultIfEmpty()
                      where product.OwnerUserId == query.OwnerUserId
                      select new { Lot = lot, ProductName = product.DisplayName, product.NormalizedSearchName, PreparedShelfLifeDate = prepared == null ? null : prepared.ShelfLifeDate };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            records = records.Where(item => item.NormalizedSearchName.Contains(query.Search.Trim().ToUpperInvariant()));
        }

        if (query.Cursor is not null)
        {
            records = records.Where(item => item.Lot.UpdatedAt < query.Cursor.UpdatedAt || (item.Lot.UpdatedAt == query.Cursor.UpdatedAt && item.Lot.Id.CompareTo(query.Cursor.LotId) < 0));
        }

        var page = await records.OrderByDescending(item => item.Lot.UpdatedAt).ThenByDescending(item => item.Lot.Id).Take(query.PageSize + 1).ToListAsync(cancellationToken);
        var items = page.Take(query.PageSize).Select(item => ToReadModel(item.Lot, item.ProductName, item.PreparedShelfLifeDate)).ToList();
        var next = page.Count > query.PageSize ? new InventoryLotReadCursor(items[^1].UpdatedAt, items[^1].LotId) : null;
        return new InventoryLotReadPage(items, next);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryLotReadModel>> ListActiveAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken)
    {
        var records = await (from lot in database.Lots.AsNoTracking()
                             join product in database.Products.AsNoTracking() on lot.ProductId equals product.Id
                             join prepared in database.PreparedLots.AsNoTracking() on lot.Id equals prepared.LotId into preparedLots
                             from prepared in preparedLots.DefaultIfEmpty()
                             where lot.OwnerUserId == ownerUserId && product.OwnerUserId == ownerUserId && lot.DeletedAt == null
                                   && ((lot.MeasuredValue != null && lot.MeasuredValue > 0m) || (lot.MeasuredValue == null && lot.AvailabilityState != "Unavailable"))
                             orderby lot.UpdatedAt descending, lot.Id descending
                             select new { Lot = lot, ProductName = product.DisplayName, PreparedShelfLifeDate = prepared == null ? null : prepared.ShelfLifeDate })
            .Take(limit)
            .ToListAsync(cancellationToken);
        return records.Select(item => ToReadModel(item.Lot, item.ProductName, item.PreparedShelfLifeDate)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryHistoryReadModel>?> GetHistoryAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken)
    {
        var exists = await database.Lots.AsNoTracking().AnyAsync(lot => lot.Id == lotId && lot.OwnerUserId == ownerUserId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var transactions = await database.Transactions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.LotId == lotId)
            .Select(item => new InventoryHistoryReadModel(item.Id, "Transaction", item.Type, item.PreviousMeasuredValue, item.PreviousMeasuredUnit, item.PreviousAvailabilityState, item.ResultingMeasuredValue, item.ResultingMeasuredUnit, item.ResultingAvailabilityState, item.ReasonCode, null, item.OccurredAt, item.TransitionKind == null ? null : new InventoryTransitionView(item.TransitionKind, item.PreviousLifecycleState!, item.ResultingLifecycleState!, item.PreviousStorageLocation!, item.ResultingStorageLocation!, item.RelatedLotId)))
            .ToListAsync(cancellationToken);
        var corrections = await database.AuditEvents.AsNoTracking()
            .Where(item => item.ActorUserId == ownerUserId && item.TargetType == "inventory_lot" && item.TargetId == lotId && item.EventName == "inventory.lot.metadata_corrected")
            .Select(item => new { item.Id, item.MetadataJson, item.OccurredAt })
            .ToListAsync(cancellationToken);
        var projectedCorrections = corrections.Select(item => new InventoryHistoryReadModel(item.Id, "MetadataCorrection", null, null, null, null, null, null, null, null, ReadChangedFields(item.MetadataJson), item.OccurredAt));
        return transactions.Concat(projectedCorrections).OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.EntryId).ToList();
    }

    private static InventoryLotReadModel ToReadModel(LotRecord lot, string productName, DateOnly? preparedShelfLifeDate) => new(lot.Id, lot.ProductId, productName, lot.MeasuredValue, lot.MeasuredUnit, lot.AvailabilityState, lot.StorageLocation, lot.CustomLocation, lot.PackageState, lot.PrintedExpirationDate, lot.Notes, lot.ConcurrencyToken, lot.CreatedAt, lot.UpdatedAt, lot.DeletedAt, new InventoryLifecycleView(lot.LifecycleState, lot.OpenedAt, lot.FrozenAt, lot.ThawStartedAt, lot.ThawedAt, lot.ParentLotId, preparedShelfLifeDate));

    private static IReadOnlyList<string> ReadChangedFields(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty("changedFields", out var fields) && fields.ValueKind == JsonValueKind.Array
                ? fields.EnumerateArray().Where(field => field.ValueKind == JsonValueKind.String).Select(field => field.GetString()).Where(field => !string.IsNullOrWhiteSpace(field)).Cast<string>().Distinct(StringComparer.Ordinal).ToList()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
