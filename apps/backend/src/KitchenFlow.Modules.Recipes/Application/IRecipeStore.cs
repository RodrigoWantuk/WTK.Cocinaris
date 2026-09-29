using KitchenFlow.Modules.Recipes.Domain;

namespace KitchenFlow.Modules.Recipes.Application;

/// <summary>Persistence-independent summary projection of one owned recipe for list views.</summary>
public sealed record RecipeSummary(Guid RecipeId, string Name, IReadOnlyList<string> MealTypes, int Servings, int RevisionNumber, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Persistence-independent detail projection of one owned recipe's current revision.</summary>
public sealed record RecipeDetail(
    Guid RecipeId,
    int RevisionNumber,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    string NormalizedRecipeJson,
    string ThumbnailVisualJson,
    DateTimeOffset CreatedAt);

/// <summary>
/// Owner-scoped persistence port for <see cref="Recipe"/> aggregates and their immutable
/// <see cref="RecipeRevision"/> content. Revisions are never updated or deleted once saved.
/// </summary>
public interface IRecipeStore
{
    /// <summary>Atomically persists a newly created recipe and its first immutable revision.</summary>
    Task SaveNewAsync(Recipe recipe, RecipeRevision revision, CancellationToken cancellationToken);

    /// <summary>Lists all recipes owned by one user, most recently created first.</summary>
    Task<IReadOnlyList<RecipeSummary>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Finds one owned recipe's current-revision detail without cross-user disclosure.</summary>
    Task<RecipeDetail?> FindDetailAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken);
}
