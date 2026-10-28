namespace KitchenFlow.Modules.Recipes.Domain;

/// <summary>
/// User-owned recipe identity. A recipe is privately owned by exactly one user; PLAN-0028 creates
/// exactly one immutable <see cref="RecipeRevision"/> (revision 1) per cook-now selection. Later
/// re-expansion or editing workflows append further revisions without rewriting prior ones.
/// </summary>
public sealed class Recipe
{
    private Recipe()
    {
    }

    /// <summary>Gets the recipe identifier.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the owning user identifier. Recipes are never cross-user shared by reference.</summary>
    public Guid OwnerUserId { get; private init; }

    /// <summary>Gets the revision number of the current authoritative content.</summary>
    public int CurrentRevisionNumber { get; private set; }

    /// <summary>Gets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>Gets the UTC instant of the last new revision.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a new recipe whose first revision is about to be attached.</summary>
    public static Recipe Create(Guid ownerUserId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = ownerUserId,
        CurrentRevisionNumber = 1,
        CreatedAt = now,
        UpdatedAt = now
    };

    /// <summary>Restores a recipe loaded by an infrastructure adapter without exposing persistence types to the domain.</summary>
    public static Recipe Restore(Guid id, Guid ownerUserId, int currentRevisionNumber, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty || ownerUserId == Guid.Empty || currentRevisionNumber < 1 || updatedAt < createdAt)
        {
            throw new ArgumentException("Persisted recipe identity, revision number, or timestamps are invalid.");
        }

        return new Recipe { Id = id, OwnerUserId = ownerUserId, CurrentRevisionNumber = currentRevisionNumber, CreatedAt = createdAt, UpdatedAt = updatedAt };
    }

    /// <summary>Advances the recipe to a newly attached, strictly sequential revision.</summary>
    public void AdvanceToRevision(int revisionNumber, DateTimeOffset now)
    {
        if (revisionNumber != CurrentRevisionNumber + 1)
        {
            throw new InvalidOperationException("A recipe revision must be attached in strict, gapless sequence.");
        }

        CurrentRevisionNumber = revisionNumber;
        UpdatedAt = now;
    }
}

/// <summary>
/// Immutable, append-only recipe content snapshot produced from one already-validated
/// <c>recipe.expand_selected.v1</c> AI Gateway response. Once persisted, a revision's content is
/// never rewritten; corrections or re-generations create a new, higher-numbered revision instead.
/// </summary>
public sealed class RecipeRevision
{
    private const int MaxNameLength = 80;
    private const int MinMealTypes = 1;
    private const int MaxMealTypes = 4;
    private const int MinServings = 1;
    private const int MaxServings = 24;

    private RecipeRevision()
    {
        Name = string.Empty;
        MealTypes = [];
        NormalizedRecipeJson = string.Empty;
        ThumbnailVisualJson = string.Empty;
    }

    /// <summary>Gets the revision identifier.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the owning recipe identifier.</summary>
    public Guid RecipeId { get; private init; }

    /// <summary>Gets the owning user identifier, denormalized for owner-scoped queries without a join.</summary>
    public Guid OwnerUserId { get; private init; }

    /// <summary>Gets the strictly sequential revision number, starting at 1.</summary>
    public int RevisionNumber { get; private init; }

    /// <summary>Gets the recipe display name at this revision.</summary>
    public string Name { get; private init; }

    /// <summary>Gets the applicable meal types at this revision.</summary>
    public IReadOnlyList<string> MealTypes { get; private init; }

    /// <summary>Gets the servings count at this revision.</summary>
    public int Servings { get; private init; }

    /// <summary>Gets the deterministically normalized full recipe content as validated protocol 0.3 JSON.</summary>
    public string NormalizedRecipeJson { get; private init; }

    /// <summary>Gets the deterministically normalized indexable thumbnail visual descriptor JSON (PLAN-0030 input).</summary>
    public string ThumbnailVisualJson { get; private init; }

    /// <summary>Gets the stable candidate identifier this revision was expanded from.</summary>
    public string SourceCandidateId { get; private init; } = string.Empty;

    /// <summary>Gets the generation session that produced this revision.</summary>
    public Guid SourceGenerationSessionId { get; private init; }

    /// <summary>Gets the UTC creation instant. Revisions are never updated after creation.</summary>
    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>Creates the first immutable revision (revision 1) for a newly created recipe.</summary>
    public static RecipeRevision CreateFirst(
        Guid recipeId,
        Guid ownerUserId,
        string name,
        IReadOnlyList<string> mealTypes,
        int servings,
        string normalizedRecipeJson,
        string thumbnailVisualJson,
        string sourceCandidateId,
        Guid sourceGenerationSessionId,
        DateTimeOffset now)
    {
        Validate(name, mealTypes, servings, normalizedRecipeJson, thumbnailVisualJson, sourceCandidateId);
        return new RecipeRevision
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            OwnerUserId = ownerUserId,
            RevisionNumber = 1,
            Name = name,
            MealTypes = mealTypes,
            Servings = servings,
            NormalizedRecipeJson = normalizedRecipeJson,
            ThumbnailVisualJson = thumbnailVisualJson,
            SourceCandidateId = sourceCandidateId,
            SourceGenerationSessionId = sourceGenerationSessionId,
            CreatedAt = now
        };
    }

    /// <summary>Restores a revision loaded by an infrastructure adapter without exposing persistence types to the domain.</summary>
    public static RecipeRevision Restore(
        Guid id,
        Guid recipeId,
        Guid ownerUserId,
        int revisionNumber,
        string name,
        IReadOnlyList<string> mealTypes,
        int servings,
        string normalizedRecipeJson,
        string thumbnailVisualJson,
        string sourceCandidateId,
        Guid sourceGenerationSessionId,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || recipeId == Guid.Empty || ownerUserId == Guid.Empty || revisionNumber < 1)
        {
            throw new ArgumentException("Persisted recipe revision identity or revision number is invalid.");
        }

        return new RecipeRevision
        {
            Id = id,
            RecipeId = recipeId,
            OwnerUserId = ownerUserId,
            RevisionNumber = revisionNumber,
            Name = name,
            MealTypes = mealTypes,
            Servings = servings,
            NormalizedRecipeJson = normalizedRecipeJson,
            ThumbnailVisualJson = thumbnailVisualJson,
            SourceCandidateId = sourceCandidateId,
            SourceGenerationSessionId = sourceGenerationSessionId,
            CreatedAt = createdAt
        };
    }

    private static void Validate(string name, IReadOnlyList<string> mealTypes, int servings, string normalizedRecipeJson, string thumbnailVisualJson, string sourceCandidateId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            throw new ArgumentException("Recipe revision name must be nonblank and at most 80 characters.");
        }

        if (mealTypes.Count is < MinMealTypes or > MaxMealTypes)
        {
            throw new ArgumentException("Recipe revision meal types must contain between 1 and 4 entries.");
        }

        if (servings is < MinServings or > MaxServings)
        {
            throw new ArgumentException("Recipe revision servings must be between 1 and 24.");
        }

        if (string.IsNullOrWhiteSpace(normalizedRecipeJson) || string.IsNullOrWhiteSpace(thumbnailVisualJson))
        {
            throw new ArgumentException("Recipe revision content and thumbnail visual JSON must be present.");
        }

        if (string.IsNullOrWhiteSpace(sourceCandidateId))
        {
            throw new ArgumentException("Recipe revision must record the source candidate identifier.");
        }
    }
}
