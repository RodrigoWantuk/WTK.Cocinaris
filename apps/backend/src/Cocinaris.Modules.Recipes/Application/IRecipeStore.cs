using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.Modules.Recipes.Application;

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
/// <see cref="RecipeRevision"/> content used by cook-now and cooking. Revisions are never updated or
/// deleted once saved. Library reads and edits go through <see cref="IRecipeLibraryStore"/>.
/// </summary>
public interface IRecipeStore
{
    /// <summary>Atomically persists a newly created recipe and its first immutable revision.</summary>
    Task SaveNewAsync(Recipe recipe, RecipeRevision revision, CancellationToken cancellationToken);

    /// <summary>Finds one owned recipe's current-revision detail without cross-user disclosure.</summary>
    Task<RecipeDetail?> FindDetailAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the owner's recipes that are not archived, with their current-revision content, most
    /// recently updated first, at most <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<RecipeDetail>> ListActiveAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken);
}
