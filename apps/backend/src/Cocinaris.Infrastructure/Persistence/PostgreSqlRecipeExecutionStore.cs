using System.Text.Json;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>PostgreSQL store for recipe executions. Every query is structurally scoped to one owner.</summary>
public sealed class PostgreSqlRecipeExecutionStore(ApplicationDbContext database) : IRecipeExecutionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<RecipeExecution?> FindAsync(Guid ownerUserId, Guid executionId, CancellationToken cancellationToken)
    {
        var record = await database.RecipeExecutions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == executionId && item.OwnerUserId == ownerUserId, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<RecipeExecution?> FindInProgressAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken)
    {
        var record = await database.RecipeExecutions.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.RecipeId == recipeId && item.Status == nameof(RecipeExecutionStatus.InProgress), cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecipeExecution>> ListInProgressAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken)
    {
        var records = await database.RecipeExecutions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.Status == nameof(RecipeExecutionStatus.InProgress))
            .OrderByDescending(item => item.StartedAt).ThenByDescending(item => item.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecipeExecution>> ListForMenuEntriesAsync(Guid ownerUserId, IReadOnlyCollection<Guid> menuEntryIds, CancellationToken cancellationToken)
    {
        if (menuEntryIds.Count == 0)
        {
            return [];
        }

        var records = await database.RecipeExecutions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.MenuEntryId != null && menuEntryIds.Contains(item.MenuEntryId.Value))
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<RecipeDetail?> FindRevisionAsync(Guid ownerUserId, Guid recipeId, int revisionNumber, CancellationToken cancellationToken)
    {
        var revision = await database.RecipeRevisions.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.RecipeId == recipeId && item.RevisionNumber == revisionNumber, cancellationToken);
        return revision is null
            ? null
            : new RecipeDetail(revision.RecipeId, revision.RevisionNumber, revision.Name, JsonSerializer.Deserialize<string[]>(revision.MealTypesJson, SerializerOptions) ?? [], revision.Servings, revision.NormalizedRecipeJson, revision.ThumbnailVisualJson, revision.CreatedAt);
    }

    /// <inheritdoc />
    public async Task AddAsync(RecipeExecution execution, CancellationToken cancellationToken)
    {
        database.RecipeExecutions.Add(ToRecord(execution));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(RecipeExecution execution, Guid expectedVersion, CancellationToken cancellationToken)
    {
        var record = ToRecord(execution);
        database.RecipeExecutions.Attach(record);
        database.Entry(record).State = EntityState.Modified;
        database.Entry(record).Property(item => item.Version).OriginalValue = expectedVersion;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private static RecipeExecution ToDomain(RecipeExecutionRecord record) => RecipeExecution.Restore(
        record.Id, record.OwnerUserId, record.RecipeId, record.RevisionNumber, record.Servings, Enum.Parse<RecipeExecutionStatus>(record.Status),
        record.StartedAt, record.UpdatedAt, record.FinishedAt, record.InventoryBatchId, record.CompletionKey, record.Rating, record.Notes, record.Version, record.MenuEntryId);

    private static RecipeExecutionRecord ToRecord(RecipeExecution execution) => new()
    {
        Id = execution.Id,
        OwnerUserId = execution.OwnerUserId,
        RecipeId = execution.RecipeId,
        RevisionNumber = execution.RevisionNumber,
        Servings = execution.Servings,
        Status = execution.Status.ToString(),
        StartedAt = execution.StartedAt,
        UpdatedAt = execution.UpdatedAt,
        FinishedAt = execution.FinishedAt,
        InventoryBatchId = execution.InventoryBatchId,
        CompletionKey = execution.CompletionKey,
        Rating = execution.Rating,
        Notes = execution.Notes,
        Version = execution.Version,
        MenuEntryId = execution.MenuEntryId
    };
}

/// <summary>
/// Opens one PostgreSQL transaction on the scoped <see cref="ApplicationDbContext"/>. The recipe
/// and inventory stores of the request share that context, so their <c>SaveChanges</c> calls join
/// the transaction; EF Core sets a savepoint per call, which keeps the transaction usable after a
/// handled concurrency failure.
/// </summary>
public sealed class PostgreSqlRecipeExecutionUnitOfWork(ApplicationDbContext database) : IRecipeExecutionUnitOfWork
{
    /// <inheritdoc />
    public async Task<IRecipeExecutionTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new Transaction(await database.Database.BeginTransactionAsync(cancellationToken));

    private sealed class Transaction(IDbContextTransaction inner) : IRecipeExecutionTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);

        // Disposing an uncommitted EF transaction rolls it back.
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
