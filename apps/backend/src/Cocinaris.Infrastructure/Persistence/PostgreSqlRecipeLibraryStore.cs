using System.Text.Json;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed recipe library store. Every query is owner-scoped. Revisions are only ever
/// inserted (an append-only trigger rejects updates); the recipe row carries the current revision
/// number, which is advanced with a compare-and-set so two concurrent edits cannot both win.
/// </summary>
public sealed class PostgreSqlRecipeLibraryStore(ApplicationDbContext database) : IRecipeLibraryStore
{
    private const string CreationKeyIndex = "IX_recipes_OwnerUserId_CreationKey";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecipeLibraryItem>> ListAsync(Guid ownerUserId, RecipeLibraryQuery query, CancellationToken cancellationToken)
    {
        var recipes = database.Recipes.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId);
        recipes = query.Archived ? recipes.Where(item => item.ArchivedAt != null) : recipes.Where(item => item.ArchivedAt == null);
        if (query.FavoritesOnly)
        {
            recipes = recipes.Where(item => item.IsFavorite);
        }

        var rows = recipes.Join(
            database.RecipeRevisions.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId),
            recipe => new { RecipeId = recipe.Id, Number = recipe.CurrentRevisionNumber },
            revision => new { revision.RecipeId, Number = revision.RevisionNumber },
            (recipe, revision) => new { recipe, revision.Name, revision.MealTypesJson, revision.Servings });
        if (query.Search is { } search)
        {
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            rows = rows.Where(item => EF.Functions.ILike(item.Name, pattern, "\\"));
        }

        var items = await rows.OrderByDescending(item => item.recipe.UpdatedAt).ThenByDescending(item => item.recipe.Id).ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return [];
        }

        var ids = items.Select(item => item.recipe.Id).ToList();
        var completed = await database.RecipeExecutions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && ids.Contains(item.RecipeId) && item.Status == nameof(RecipeExecutionStatus.Completed))
            .Select(item => new { item.RecipeId, item.FinishedAt, item.Rating })
            .ToListAsync(cancellationToken);
        var stats = completed.GroupBy(item => item.RecipeId).ToDictionary(
            group => group.Key,
            group =>
            {
                var latest = group.OrderByDescending(item => item.FinishedAt).First();
                return (Count: group.Count(), latest.FinishedAt, latest.Rating);
            });

        return items.Select(item =>
        {
            var found = stats.TryGetValue(item.recipe.Id, out var stat);
            return new RecipeLibraryItem(
                item.recipe.Id,
                item.Name,
                MealTypes(item.MealTypesJson),
                item.Servings,
                item.recipe.CurrentRevisionNumber,
                Enum.Parse<RecipeOrigin>(item.recipe.Origin),
                item.recipe.IsFavorite,
                item.recipe.ArchivedAt,
                item.recipe.CreatedAt,
                item.recipe.UpdatedAt,
                found ? stat.Count : 0,
                found ? stat.FinishedAt : null,
                found ? stat.Rating : null);
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<Recipe?> FindAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken)
    {
        var record = await database.Recipes.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.Id == recipeId, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<Recipe?> FindByCreationKeyAsync(Guid ownerUserId, Guid creationKey, CancellationToken cancellationToken)
    {
        var record = await database.Recipes.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.CreationKey == creationKey, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<RecipeRevisionContent?> FindRevisionAsync(Guid ownerUserId, Guid recipeId, int revisionNumber, CancellationToken cancellationToken)
    {
        var record = await database.RecipeRevisions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.RecipeId == recipeId && item.RevisionNumber == revisionNumber, cancellationToken);
        return record is null ? null : ToContent(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecipeRevisionContent>> ListRevisionsAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken)
    {
        var records = await database.RecipeRevisions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.RecipeId == recipeId)
            .OrderByDescending(item => item.RevisionNumber)
            .ToListAsync(cancellationToken);
        return records.Select(ToContent).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecipeCookingEntry>> ListCookingAsync(Guid ownerUserId, Guid recipeId, int limit, CancellationToken cancellationToken)
    {
        var records = await database.RecipeExecutions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.RecipeId == recipeId && item.Status != nameof(RecipeExecutionStatus.InProgress))
            .OrderByDescending(item => item.FinishedAt).ThenByDescending(item => item.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return records.Select(item => new RecipeCookingEntry(item.Id, item.RevisionNumber, item.Servings, item.Status, item.StartedAt, item.FinishedAt, item.Rating, item.Notes)).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(Recipe recipe, RecipeRevision revision, CancellationToken cancellationToken)
    {
        database.Recipes.Add(ToRecord(recipe));
        database.RecipeRevisions.Add(ToRecord(revision));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CreationKeyIndex })
        {
            database.ChangeTracker.Clear();
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryAppendRevisionAsync(Recipe recipe, RecipeRevision revision, int expectedRevisionNumber, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var advanced = await database.Recipes
            .Where(item => item.OwnerUserId == recipe.OwnerUserId && item.Id == recipe.Id && item.CurrentRevisionNumber == expectedRevisionNumber)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CurrentRevisionNumber, recipe.CurrentRevisionNumber)
                .SetProperty(item => item.UpdatedAt, recipe.UpdatedAt), cancellationToken);
        if (advanced != 1)
        {
            return false;
        }

        database.RecipeRevisions.Add(ToRecord(revision));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task SaveOrganizationAsync(Recipe recipe, CancellationToken cancellationToken) =>
        await database.Recipes
            .Where(item => item.OwnerUserId == recipe.OwnerUserId && item.Id == recipe.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsFavorite, recipe.IsFavorite)
                .SetProperty(item => item.ArchivedAt, recipe.ArchivedAt), cancellationToken);

    private static Recipe ToDomain(RecipeRecord record) => Recipe.Restore(
        record.Id,
        record.OwnerUserId,
        record.CurrentRevisionNumber,
        record.CreatedAt,
        record.UpdatedAt,
        Enum.Parse<RecipeOrigin>(record.Origin),
        record.IsFavorite,
        record.ArchivedAt,
        record.ParentRecipeId,
        record.ParentRevisionNumber,
        record.DerivationReason,
        record.CreationKey);

    private static RecipeRevisionContent ToContent(RecipeRevisionRecord record) => new(
        record.RevisionNumber,
        record.Name,
        MealTypes(record.MealTypesJson),
        record.Servings,
        record.NormalizedRecipeJson,
        record.ThumbnailVisualJson,
        Enum.Parse<RecipeRevisionOrigin>(record.Origin),
        record.ChangeReason,
        record.SourceExecutionId,
        record.CreatedAt);

    private static RecipeRecord ToRecord(Recipe recipe) => new()
    {
        Id = recipe.Id,
        OwnerUserId = recipe.OwnerUserId,
        CurrentRevisionNumber = recipe.CurrentRevisionNumber,
        CreatedAt = recipe.CreatedAt,
        UpdatedAt = recipe.UpdatedAt,
        Origin = recipe.Origin.ToString(),
        IsFavorite = recipe.IsFavorite,
        ArchivedAt = recipe.ArchivedAt,
        ParentRecipeId = recipe.ParentRecipeId,
        ParentRevisionNumber = recipe.ParentRevisionNumber,
        DerivationReason = recipe.DerivationReason,
        CreationKey = recipe.CreationKey
    };

    private static RecipeRevisionRecord ToRecord(RecipeRevision revision) => new()
    {
        Id = revision.Id,
        RecipeId = revision.RecipeId,
        OwnerUserId = revision.OwnerUserId,
        RevisionNumber = revision.RevisionNumber,
        Name = revision.Name,
        MealTypesJson = JsonSerializer.Serialize(revision.MealTypes, SerializerOptions),
        Servings = revision.Servings,
        NormalizedRecipeJson = revision.NormalizedRecipeJson,
        ThumbnailVisualJson = revision.ThumbnailVisualJson,
        SourceCandidateId = revision.SourceCandidateId,
        SourceGenerationSessionId = revision.SourceGenerationSessionId,
        Origin = revision.Origin.ToString(),
        ChangeReason = revision.ChangeReason,
        SourceExecutionId = revision.SourceExecutionId,
        CreatedAt = revision.CreatedAt
    };

    private static string[] MealTypes(string json) => JsonSerializer.Deserialize<string[]>(json, SerializerOptions) ?? [];
}
