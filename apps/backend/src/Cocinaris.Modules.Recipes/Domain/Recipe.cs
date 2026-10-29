namespace Cocinaris.Modules.Recipes.Domain;

/// <summary>How a recipe identity came to exist.</summary>
public enum RecipeOrigin
{
    /// <summary>Created from an AI cook-now selection.</summary>
    Generated,
    /// <summary>Written by the owner from scratch.</summary>
    Manual,
    /// <summary>Derived from another of the owner's recipes; it records the parent and the reason.</summary>
    Derived
}

/// <summary>How one revision's content was produced.</summary>
public enum RecipeRevisionOrigin
{
    /// <summary>Expanded by the AI Gateway from a selected candidate.</summary>
    Generated,
    /// <summary>First revision of a recipe the owner wrote.</summary>
    Manual,
    /// <summary>A later revision the owner edited (correction or refinement).</summary>
    Edited,
    /// <summary>First revision of a derived recipe.</summary>
    Derived
}

/// <summary>
/// User-owned recipe identity. A recipe is privately owned by exactly one user. Content lives in
/// immutable <see cref="RecipeRevision"/> rows; editing appends a revision, and a material change
/// becomes a derived recipe with its own identity that records its parent (PLAN-0038).
/// Favorite and archive flags are library organization and never touch revision content.
/// </summary>
public sealed class Recipe
{
    /// <summary>Maximum length of a derivation reason.</summary>
    public const int MaxReasonLength = 200;

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

    /// <summary>Gets how the recipe identity was created.</summary>
    public RecipeOrigin Origin { get; private init; }

    /// <summary>Gets whether the owner marked the recipe as a favorite. A favorite follows the current revision.</summary>
    public bool IsFavorite { get; private set; }

    /// <summary>Gets the UTC instant the owner archived the recipe, or <see langword="null"/> while it is in the library.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Gets the parent recipe of a derived recipe.</summary>
    public Guid? ParentRecipeId { get; private init; }

    /// <summary>Gets the parent revision a derived recipe started from.</summary>
    public int? ParentRevisionNumber { get; private init; }

    /// <summary>Gets the owner's reason for deriving the recipe.</summary>
    public string? DerivationReason { get; private init; }

    /// <summary>Gets the idempotency key of the request that created a manual or derived recipe.</summary>
    public Guid? CreationKey { get; private init; }

    /// <summary>Creates a new recipe whose first, AI-generated revision is about to be attached.</summary>
    public static Recipe Create(Guid ownerUserId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = ownerUserId,
        CurrentRevisionNumber = 1,
        CreatedAt = now,
        UpdatedAt = now,
        Origin = RecipeOrigin.Generated
    };

    /// <summary>Creates a recipe the owner writes from scratch.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="creationKey">Request idempotency key; a retry with the same key returns this recipe.</param>
    /// <param name="now">Creation instant.</param>
    public static Recipe CreateManual(Guid ownerUserId, Guid creationKey, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = ownerUserId,
        CurrentRevisionNumber = 1,
        CreatedAt = now,
        UpdatedAt = now,
        Origin = RecipeOrigin.Manual,
        CreationKey = creationKey
    };

    /// <summary>Creates a derived recipe that records its parent recipe, parent revision, and reason.</summary>
    /// <exception cref="ArgumentException">The reason is blank or longer than <see cref="MaxReasonLength"/>, or the parent is invalid.</exception>
    public static Recipe CreateDerived(Guid ownerUserId, Guid creationKey, Guid parentRecipeId, int parentRevisionNumber, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            throw new ArgumentException("A derivation reason must be nonblank and at most 200 characters.", nameof(reason));
        }

        if (parentRecipeId == Guid.Empty || parentRevisionNumber < 1)
        {
            throw new ArgumentException("A derived recipe needs a parent recipe and revision.", nameof(parentRecipeId));
        }

        return new Recipe
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            CurrentRevisionNumber = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Origin = RecipeOrigin.Derived,
            ParentRecipeId = parentRecipeId,
            ParentRevisionNumber = parentRevisionNumber,
            DerivationReason = reason.Trim(),
            CreationKey = creationKey
        };
    }

    /// <summary>Restores a recipe loaded by an infrastructure adapter without exposing persistence types to the domain.</summary>
    public static Recipe Restore(
        Guid id,
        Guid ownerUserId,
        int currentRevisionNumber,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        RecipeOrigin origin = RecipeOrigin.Generated,
        bool isFavorite = false,
        DateTimeOffset? archivedAt = null,
        Guid? parentRecipeId = null,
        int? parentRevisionNumber = null,
        string? derivationReason = null,
        Guid? creationKey = null)
    {
        if (id == Guid.Empty || ownerUserId == Guid.Empty || currentRevisionNumber < 1 || updatedAt < createdAt)
        {
            throw new ArgumentException("Persisted recipe identity, revision number, or timestamps are invalid.");
        }

        return new Recipe
        {
            Id = id,
            OwnerUserId = ownerUserId,
            CurrentRevisionNumber = currentRevisionNumber,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Origin = origin,
            IsFavorite = isFavorite,
            ArchivedAt = archivedAt,
            ParentRecipeId = parentRecipeId,
            ParentRevisionNumber = parentRevisionNumber,
            DerivationReason = derivationReason,
            CreationKey = creationKey
        };
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

    /// <summary>Marks or unmarks the recipe as a favorite.</summary>
    public void SetFavorite(bool isFavorite) => IsFavorite = isFavorite;

    /// <summary>Archives the recipe (hidden from the library, kept with its history) or brings it back.</summary>
    public void SetArchived(bool archived, DateTimeOffset now) => ArchivedAt = archived ? ArchivedAt ?? now : null;
}

/// <summary>
/// Immutable, append-only recipe content snapshot: either produced from one already-validated
/// <c>recipe.expand_selected.v1</c> AI Gateway response, or written by the owner in the protocol 0.3
/// shape so cooking and display read both the same way. Once persisted, a revision's content is
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

    /// <summary>Gets the stable candidate identifier a generated revision was expanded from; absent for authored revisions.</summary>
    public string? SourceCandidateId { get; private init; }

    /// <summary>Gets the generation session that produced a generated revision; absent for authored revisions.</summary>
    public Guid? SourceGenerationSessionId { get; private init; }

    /// <summary>Gets how this revision's content was produced.</summary>
    public RecipeRevisionOrigin Origin { get; private init; }

    /// <summary>Gets the owner's reason for an edited revision.</summary>
    public string? ChangeReason { get; private init; }

    /// <summary>Gets the completed execution whose adjustments this revision saves, when it was written after cooking.</summary>
    public Guid? SourceExecutionId { get; private init; }

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
        Validate(name, mealTypes, servings, normalizedRecipeJson, thumbnailVisualJson);
        if (string.IsNullOrWhiteSpace(sourceCandidateId))
        {
            throw new ArgumentException("A generated recipe revision must record the source candidate identifier.");
        }

        return new RecipeRevision
        {
            Origin = RecipeRevisionOrigin.Generated,
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

    /// <summary>
    /// Creates a revision the owner wrote: the first revision of a manual or derived recipe, or a
    /// later edited revision. The content was composed and validated by <c>RecipeContentComposer</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The origin is <see cref="RecipeRevisionOrigin.Generated"/>, the number does not match the origin, or the content is invalid.</exception>
    public static RecipeRevision CreateAuthored(
        Guid recipeId,
        Guid ownerUserId,
        int revisionNumber,
        RecipeRevisionOrigin origin,
        string name,
        IReadOnlyList<string> mealTypes,
        int servings,
        string normalizedRecipeJson,
        string thumbnailVisualJson,
        string? changeReason,
        Guid? sourceExecutionId,
        DateTimeOffset now)
    {
        Validate(name, mealTypes, servings, normalizedRecipeJson, thumbnailVisualJson);
        var expectsFirst = origin is RecipeRevisionOrigin.Manual or RecipeRevisionOrigin.Derived;
        if (origin == RecipeRevisionOrigin.Generated || revisionNumber < 1 || expectsFirst != (revisionNumber == 1))
        {
            throw new ArgumentException("An authored revision is revision 1 of a manual or derived recipe, or a later edited revision.");
        }

        if (changeReason is { Length: > Recipe.MaxReasonLength })
        {
            throw new ArgumentException("A change reason must be at most 200 characters.");
        }

        return new RecipeRevision
        {
            Id = Guid.NewGuid(),
            RecipeId = recipeId,
            OwnerUserId = ownerUserId,
            RevisionNumber = revisionNumber,
            Origin = origin,
            Name = name,
            MealTypes = mealTypes,
            Servings = servings,
            NormalizedRecipeJson = normalizedRecipeJson,
            ThumbnailVisualJson = thumbnailVisualJson,
            ChangeReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason.Trim(),
            SourceExecutionId = sourceExecutionId,
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
        string? sourceCandidateId,
        Guid? sourceGenerationSessionId,
        DateTimeOffset createdAt,
        RecipeRevisionOrigin origin = RecipeRevisionOrigin.Generated,
        string? changeReason = null,
        Guid? sourceExecutionId = null)
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
            CreatedAt = createdAt,
            Origin = origin,
            ChangeReason = changeReason,
            SourceExecutionId = sourceExecutionId
        };
    }

    private static void Validate(string name, IReadOnlyList<string> mealTypes, int servings, string normalizedRecipeJson, string thumbnailVisualJson)
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
    }
}
