namespace Cocinaris.Modules.Recipes.Domain;

/// <summary>Lifecycle of one AI revision proposal.</summary>
public enum RecipeRevisionProposalStatus
{
    /// <summary>Claimed; the AI call is running.</summary>
    Generating,
    /// <summary>A validated new version waits for the owner's decision.</summary>
    Ready,
    /// <summary>The owner accepted it, and it became the recipe's next revision.</summary>
    Accepted,
    /// <summary>The owner discarded it.</summary>
    Discarded,
    /// <summary>The AI call or its validation failed; nothing was proposed.</summary>
    Failed
}

/// <summary>
/// A new version of a recipe the AI wrote from the owner's short request (PLAN-0060). It never
/// changes the recipe by itself: only the owner's explicit acceptance appends it as the next
/// immutable revision, and only while the recipe is still at <see cref="BaseRevisionNumber"/>.
/// </summary>
public sealed class RecipeRevisionProposal
{
    /// <summary>Longest request the owner may write, in characters.</summary>
    public const int MaxInstructionLength = 300;

    private RecipeRevisionProposal()
    {
        Instruction = string.Empty;
    }

    /// <summary>Gets the proposal identifier.</summary>
    public Guid Id { get; private init; }
    /// <summary>Gets the owner.</summary>
    public Guid OwnerUserId { get; private init; }
    /// <summary>Gets the recipe being revised.</summary>
    public Guid RecipeId { get; private init; }
    /// <summary>Gets the revision the proposal was written from.</summary>
    public int BaseRevisionNumber { get; private init; }
    /// <summary>Gets the request key; a retry with the same key returns this proposal.</summary>
    public Guid IdempotencyKey { get; private init; }
    /// <summary>Gets the owner's normalized request. It is untrusted text, kept to show back to the owner.</summary>
    public string Instruction { get; private init; }
    /// <summary>Gets the status.</summary>
    public RecipeRevisionProposalStatus Status { get; private set; }
    /// <summary>Gets the proposed name, while a version is proposed or accepted.</summary>
    public string? Name { get; private set; }
    /// <summary>Gets the proposed meal types.</summary>
    public IReadOnlyList<string> MealTypes { get; private set; } = [];
    /// <summary>Gets the proposed servings.</summary>
    public int? Servings { get; private set; }
    /// <summary>Gets the validated proposed content; cleared once accepted or discarded.</summary>
    public string? NormalizedRecipeJson { get; private set; }
    /// <summary>Gets the validated proposed visual descriptor; cleared once accepted or discarded.</summary>
    public string? ThumbnailVisualJson { get; private set; }
    /// <summary>Gets the revision created by accepting.</summary>
    public int? AcceptedRevisionNumber { get; private set; }
    /// <summary>Gets a non-sensitive failure classification.</summary>
    public string? FailureReason { get; private set; }
    /// <summary>Gets the creation instant.</summary>
    public DateTimeOffset CreatedAt { get; private init; }
    /// <summary>Gets the instant of the last transition.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Claims a new proposal in <see cref="RecipeRevisionProposalStatus.Generating"/>.</summary>
    public static RecipeRevisionProposal Create(Guid ownerUserId, Guid recipeId, int baseRevisionNumber, Guid idempotencyKey, string instruction, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = ownerUserId,
        RecipeId = recipeId,
        BaseRevisionNumber = baseRevisionNumber,
        IdempotencyKey = idempotencyKey,
        Instruction = instruction,
        Status = RecipeRevisionProposalStatus.Generating,
        CreatedAt = now,
        UpdatedAt = now
    };

    /// <summary>Restores a proposal loaded by an infrastructure adapter.</summary>
    public static RecipeRevisionProposal Restore(
        Guid id, Guid ownerUserId, Guid recipeId, int baseRevisionNumber, Guid idempotencyKey, string instruction, RecipeRevisionProposalStatus status,
        string? name, IReadOnlyList<string> mealTypes, int? servings, string? normalizedRecipeJson, string? thumbnailVisualJson, int? acceptedRevisionNumber, string? failureReason,
        DateTimeOffset createdAt, DateTimeOffset updatedAt) => new()
        {
            Id = id,
            OwnerUserId = ownerUserId,
            RecipeId = recipeId,
            BaseRevisionNumber = baseRevisionNumber,
            IdempotencyKey = idempotencyKey,
            Instruction = instruction,
            Status = status,
            Name = name,
            MealTypes = mealTypes,
            Servings = servings,
            NormalizedRecipeJson = normalizedRecipeJson,
            ThumbnailVisualJson = thumbnailVisualJson,
            AcceptedRevisionNumber = acceptedRevisionNumber,
            FailureReason = failureReason,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

    /// <summary>Attaches an already validated version.</summary>
    /// <exception cref="InvalidOperationException">The proposal is not generating.</exception>
    public void AttachVersion(string name, IReadOnlyList<string> mealTypes, int servings, string normalizedRecipeJson, string thumbnailVisualJson, DateTimeOffset now)
    {
        EnsureStatus(RecipeRevisionProposalStatus.Generating);
        Name = name;
        MealTypes = mealTypes;
        Servings = servings;
        NormalizedRecipeJson = normalizedRecipeJson;
        ThumbnailVisualJson = thumbnailVisualJson;
        Status = RecipeRevisionProposalStatus.Ready;
        UpdatedAt = now;
    }

    /// <summary>Records a safe failure.</summary>
    /// <exception cref="InvalidOperationException">The proposal is not generating.</exception>
    public void MarkFailed(string reason, DateTimeOffset now)
    {
        EnsureStatus(RecipeRevisionProposalStatus.Generating);
        Status = RecipeRevisionProposalStatus.Failed;
        FailureReason = reason;
        UpdatedAt = now;
    }

    /// <summary>Records acceptance as <paramref name="revisionNumber"/>; the content now lives in that revision.</summary>
    /// <exception cref="InvalidOperationException">No version is waiting for a decision.</exception>
    public void MarkAccepted(int revisionNumber, DateTimeOffset now)
    {
        EnsureStatus(RecipeRevisionProposalStatus.Ready);
        Status = RecipeRevisionProposalStatus.Accepted;
        AcceptedRevisionNumber = revisionNumber;
        NormalizedRecipeJson = null;
        ThumbnailVisualJson = null;
        UpdatedAt = now;
    }

    /// <summary>Discards the proposed version and its content.</summary>
    /// <exception cref="InvalidOperationException">No version is waiting for a decision.</exception>
    public void MarkDiscarded(DateTimeOffset now)
    {
        EnsureStatus(RecipeRevisionProposalStatus.Ready);
        Status = RecipeRevisionProposalStatus.Discarded;
        NormalizedRecipeJson = null;
        ThumbnailVisualJson = null;
        UpdatedAt = now;
    }

    private void EnsureStatus(RecipeRevisionProposalStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"The proposal is {Status}, not {expected}.");
        }
    }
}
