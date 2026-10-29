using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Planning.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed menu plan store (PLAN-0029). Every query is owner-scoped. Entries are saved
/// one at a time, last write wins: a plan is advisory and edited by its single owner, and no
/// inventory depends on it.
/// </summary>
public sealed class PostgreSqlMenuPlanStore(ApplicationDbContext database) : IMenuPlanStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuPlan>> ListAsync(Guid ownerUserId, bool archived, CancellationToken cancellationToken)
    {
        var records = await database.MenuPlans.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && (archived ? item.Status == nameof(MenuPlanStatus.Archived) : item.Status != nameof(MenuPlanStatus.Archived)))
            .OrderByDescending(item => item.StartDate).ThenByDescending(item => item.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuPlan>> ListAcceptedOnAsync(Guid ownerUserId, DateOnly date, CancellationToken cancellationToken)
    {
        var records = await database.MenuPlans.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.Status == nameof(MenuPlanStatus.Accepted) && item.StartDate <= date && item.EndDate >= date)
            .OrderBy(item => item.StartDate)
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<MenuPlan?> FindAsync(Guid ownerUserId, Guid planId, CancellationToken cancellationToken)
    {
        var record = await database.MenuPlans.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.Id == planId, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<MenuPlan?> FindByCreationKeyAsync(Guid ownerUserId, Guid creationKey, CancellationToken cancellationToken)
    {
        var record = await database.MenuPlans.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.CreationKey == creationKey, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuEntry>> ListEntriesAsync(Guid ownerUserId, IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken)
    {
        if (planIds.Count == 0)
        {
            return [];
        }

        var records = await database.MenuEntries.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && planIds.Contains(item.MenuPlanId))
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<MenuEntry?> FindEntryByCreationKeyAsync(Guid ownerUserId, Guid creationKey, CancellationToken cancellationToken)
    {
        var record = await database.MenuEntries.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.CreationKey == creationKey, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(MenuPlan plan, CancellationToken cancellationToken)
    {
        database.MenuPlans.Add(ToRecord(plan));
        return await TrySaveAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryAddEntryAsync(MenuPlan plan, MenuEntry entry, CancellationToken cancellationToken)
    {
        database.MenuEntries.Add(ToRecord(entry));
        database.MenuPlans.Attach(ToRecord(plan)).Property(item => item.UpdatedAt).IsModified = true;
        return await TrySaveAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(MenuPlan plan, CancellationToken cancellationToken)
    {
        database.MenuPlans.Update(ToRecord(plan));
        await SaveAndClearAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveEntryAsync(MenuPlan plan, MenuEntry entry, CancellationToken cancellationToken)
    {
        database.MenuEntries.Update(ToRecord(entry));
        database.MenuPlans.Attach(ToRecord(plan)).Property(item => item.UpdatedAt).IsModified = true;
        await SaveAndClearAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveEntryAsync(MenuPlan plan, MenuEntry entry, CancellationToken cancellationToken)
    {
        database.MenuEntries.Remove(ToRecord(entry));
        database.MenuPlans.Attach(ToRecord(plan)).Property(item => item.UpdatedAt).IsModified = true;
        await SaveAndClearAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> ListCheckedAsync(Guid ownerUserId, Guid planId, CancellationToken cancellationToken)
    {
        var keys = await database.ShoppingChecks.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.MenuPlanId == planId)
            .Select(item => item.ItemKey)
            .ToListAsync(cancellationToken);
        return keys.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task SetCheckedAsync(Guid ownerUserId, Guid planId, string itemKey, bool isChecked, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!isChecked)
        {
            await database.ShoppingChecks.Where(item => item.OwnerUserId == ownerUserId && item.MenuPlanId == planId && item.ItemKey == itemKey).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        // Insert-if-absent keeps a double tap from failing on the primary key.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO planning.shopping_checks ("MenuPlanId", "ItemKey", "OwnerUserId", "CheckedAt") VALUES ({planId}, {itemKey}, {ownerUserId}, {now}) ON CONFLICT DO NOTHING""",
            cancellationToken);
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private async Task SaveAndClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private static MenuPlan ToDomain(MenuPlanRecord record) => MenuPlan.Restore(
        record.Id, record.OwnerUserId, record.CreationKey, record.Name, record.StartDate, record.EndDate, Enum.Parse<MenuPlanStatus>(record.Status), record.CreatedAt, record.UpdatedAt, record.AcceptedAt);

    private static MenuEntry ToDomain(MenuEntryRecord record) => MenuEntry.Restore(
        record.Id, record.MenuPlanId, record.OwnerUserId, record.CreationKey, record.RecipeId, record.PinnedRevisionNumber, record.PlannedDate, record.MealType, record.Servings, Enum.Parse<MenuEntryStatus>(record.Status), record.CreatedAt, record.UpdatedAt);

    private static MenuPlanRecord ToRecord(MenuPlan plan) => new()
    {
        Id = plan.Id,
        OwnerUserId = plan.OwnerUserId,
        CreationKey = plan.CreationKey,
        Name = plan.Name,
        StartDate = plan.StartDate,
        EndDate = plan.EndDate,
        Status = plan.Status.ToString(),
        CreatedAt = plan.CreatedAt,
        UpdatedAt = plan.UpdatedAt,
        AcceptedAt = plan.AcceptedAt
    };

    private static MenuEntryRecord ToRecord(MenuEntry entry) => new()
    {
        Id = entry.Id,
        MenuPlanId = entry.MenuPlanId,
        OwnerUserId = entry.OwnerUserId,
        CreationKey = entry.CreationKey,
        RecipeId = entry.RecipeId,
        PinnedRevisionNumber = entry.PinnedRevisionNumber,
        PlannedDate = entry.PlannedDate,
        MealType = entry.MealType,
        Servings = entry.Servings,
        Status = entry.Status.ToString(),
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt
    };
}
