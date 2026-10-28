namespace KitchenFlow.Modules.Recipes.Domain;

/// <summary>Lifecycle status of one recipe generation session.</summary>
public enum RecipeGenerationSessionStatus
{
    /// <summary>The session was claimed and owns suggest execution until candidates are ready or the session fails.</summary>
    AwaitingCandidates,
    /// <summary>Validated candidates were persisted and are available for selection.</summary>
    CandidatesReady,
    /// <summary>
    /// A selection was claimed and owns expand execution until the recipe is persisted or the session fails.
    /// Concurrent duplicate selections are rejected while this status is held.
    /// </summary>
    Expanding,
    /// <summary>A candidate was selected, expanded, and persisted as a recipe.</summary>
    Selected,
    /// <summary>The session failed safely; no recipe was produced.</summary>
    Failed
}

/// <summary>
/// Owner-scoped, short-lived aggregate coordinating one cook-now recipe generation flow: candidate
/// suggestion, then selection and expansion into a persisted <see cref="Recipe"/>. Only validated,
/// already-checked AI output may ever be attached; this aggregate never stores raw provider output.
/// </summary>
public sealed class RecipeGenerationSession
{
    private RecipeGenerationSession()
    {
    }

    /// <summary>Gets the session identifier.</summary>
    public Guid Id { get; private init; }
    /// <summary>Gets the owning user identifier.</summary>
    public Guid OwnerUserId { get; private init; }
    /// <summary>Gets the client-supplied idempotency key that created this session, used for safe request replay.</summary>
    public Guid IdempotencyKey { get; private init; }
    /// <summary>Gets the execution mode. PLAN-0028 supports only <c>cook_now</c>.</summary>
    public string ExecutionMode { get; private init; } = "cook_now";
    /// <summary>Gets the current lifecycle status.</summary>
    public RecipeGenerationSessionStatus Status { get; private set; }
    /// <summary>Gets the validated candidate snapshot JSON, present once candidates are ready.</summary>
    public string? CandidatesSnapshotJson { get; private set; }
    /// <summary>Gets the stable identifier of the selected candidate, present once selection is claimed or completed.</summary>
    public string? SelectedCandidateId { get; private set; }
    /// <summary>Gets the identifier of the recipe produced from the selected candidate.</summary>
    public Guid? SelectedRecipeId { get; private set; }
    /// <summary>Gets the client-supplied idempotency key used for the select-and-expand command, when selection was claimed or completed.</summary>
    public Guid? SelectIdempotencyKey { get; private set; }
    /// <summary>Gets a non-sensitive failure classification, present only when <see cref="Status"/> is <see cref="RecipeGenerationSessionStatus.Failed"/>.</summary>
    public string? FailureReason { get; private set; }
    /// <summary>Gets the creation instant.</summary>
    public DateTimeOffset CreatedAt { get; private init; }
    /// <summary>Gets the instant of the last state transition.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>Gets the instant after which the session may no longer be acted upon.</summary>
    public DateTimeOffset ExpiresAt { get; private init; }

    /// <summary>Creates a new cook-now generation session for the client-supplied idempotency key.</summary>
    public static RecipeGenerationSession Create(Guid ownerUserId, Guid idempotencyKey, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = ownerUserId,
        IdempotencyKey = idempotencyKey,
        ExecutionMode = "cook_now",
        Status = RecipeGenerationSessionStatus.AwaitingCandidates,
        CreatedAt = now,
        UpdatedAt = now,
        ExpiresAt = now.AddHours(2)
    };

    /// <summary>Rehydrates a session from persistence without re-validating already-persisted invariants.</summary>
    public static RecipeGenerationSession Rehydrate(
        Guid id, Guid ownerUserId, Guid idempotencyKey, string executionMode, RecipeGenerationSessionStatus status,
        string? candidatesSnapshotJson, string? selectedCandidateId, Guid? selectedRecipeId, Guid? selectIdempotencyKey, string? failureReason,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, DateTimeOffset expiresAt) => new()
        {
            Id = id,
            OwnerUserId = ownerUserId,
            IdempotencyKey = idempotencyKey,
            ExecutionMode = executionMode,
            Status = status,
            CandidatesSnapshotJson = candidatesSnapshotJson,
            SelectedCandidateId = selectedCandidateId,
            SelectedRecipeId = selectedRecipeId,
            SelectIdempotencyKey = selectIdempotencyKey,
            FailureReason = failureReason,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            ExpiresAt = expiresAt
        };

    /// <summary>Returns whether the session may still be acted upon.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Attaches a validated candidate snapshot, transitioning the session to <see cref="RecipeGenerationSessionStatus.CandidatesReady"/>.</summary>
    public void AttachValidatedCandidates(string candidatesSnapshotJson, DateTimeOffset now)
    {
        if (Status != RecipeGenerationSessionStatus.AwaitingCandidates)
        {
            throw new InvalidOperationException("Candidates were already produced for this generation session.");
        }

        CandidatesSnapshotJson = candidatesSnapshotJson;
        Status = RecipeGenerationSessionStatus.CandidatesReady;
        UpdatedAt = now;
    }

    /// <summary>
    /// Claims selection execution by transitioning <see cref="RecipeGenerationSessionStatus.CandidatesReady"/> to
    /// <see cref="RecipeGenerationSessionStatus.Expanding"/> with the selected candidate and select idempotency key.
    /// </summary>
    public void BeginSelection(string candidateId, Guid selectIdempotencyKey, DateTimeOffset now)
    {
        if (Status != RecipeGenerationSessionStatus.CandidatesReady)
        {
            throw new InvalidOperationException("Selection can only be claimed when validated candidates are ready.");
        }

        SelectedCandidateId = candidateId;
        SelectIdempotencyKey = selectIdempotencyKey;
        Status = RecipeGenerationSessionStatus.Expanding;
        UpdatedAt = now;
    }

    /// <summary>Records a safe failure; no recipe is attached. Allowed from any non-selected status, including Expanding.</summary>
    public void MarkFailed(string reason, DateTimeOffset now)
    {
        if (Status is RecipeGenerationSessionStatus.Selected)
        {
            throw new InvalidOperationException("A completed generation session cannot be marked failed.");
        }

        Status = RecipeGenerationSessionStatus.Failed;
        FailureReason = reason;
        UpdatedAt = now;
    }

    /// <summary>
    /// Completes a claimed expansion by recording the produced recipe and transitioning
    /// <see cref="RecipeGenerationSessionStatus.Expanding"/> to <see cref="RecipeGenerationSessionStatus.Selected"/>.
    /// </summary>
    public void CompleteSelection(Guid recipeId, DateTimeOffset now)
    {
        if (Status != RecipeGenerationSessionStatus.Expanding)
        {
            throw new InvalidOperationException("A recipe can only be attached after selection was claimed for expansion.");
        }

        SelectedRecipeId = recipeId;
        Status = RecipeGenerationSessionStatus.Selected;
        UpdatedAt = now;
    }
}
