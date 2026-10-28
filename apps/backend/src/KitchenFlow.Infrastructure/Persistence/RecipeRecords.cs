namespace KitchenFlow.Infrastructure.Persistence;

/// <summary>Persistence representation of one user-owned recipe identity.</summary>
public sealed class RecipeRecord
{
    /// <summary>Gets or sets the recipe identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the revision number of the current authoritative content.</summary>
    public int CurrentRevisionNumber { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last new revision.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Immutable, append-only persistence representation of one recipe revision. Rows are never
/// updated or deleted after insertion; an append-only database trigger enforces this invariant.
/// </summary>
public sealed class RecipeRevisionRecord
{
    /// <summary>Gets or sets the revision identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the owning recipe identifier.</summary>
    public Guid RecipeId { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the strictly sequential revision number.</summary>
    public int RevisionNumber { get; set; }
    /// <summary>Gets or sets the recipe display name at this revision.</summary>
    public required string Name { get; set; }
    /// <summary>Gets or sets the applicable meal types at this revision, serialized as a JSON string array.</summary>
    public required string MealTypesJson { get; set; }
    /// <summary>Gets or sets the servings count at this revision.</summary>
    public int Servings { get; set; }
    /// <summary>Gets or sets the deterministically normalized full recipe content as validated protocol 0.3 JSON.</summary>
    public required string NormalizedRecipeJson { get; set; }
    /// <summary>Gets or sets the deterministically normalized indexable thumbnail visual descriptor JSON.</summary>
    public required string ThumbnailVisualJson { get; set; }
    /// <summary>Gets or sets the stable candidate identifier this revision was expanded from.</summary>
    public required string SourceCandidateId { get; set; }
    /// <summary>Gets or sets the generation session that produced this revision.</summary>
    public Guid SourceGenerationSessionId { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Persistence representation of one owner-scoped cook-now recipe generation session.</summary>
public sealed class RecipeGenerationSessionRecord
{
    /// <summary>Gets or sets the session identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the client-supplied idempotency key that created this session.</summary>
    public Guid IdempotencyKey { get; set; }
    /// <summary>Gets or sets the execution mode. PLAN-0028 supports only <c>cook_now</c>.</summary>
    public required string ExecutionMode { get; set; }
    /// <summary>Gets or sets the controlled lifecycle status.</summary>
    public required string Status { get; set; }
    /// <summary>Gets or sets the validated candidate snapshot JSON, present once candidates are ready.</summary>
    public string? CandidatesSnapshotJson { get; set; }
    /// <summary>Gets or sets the stable identifier of the selected candidate, present once selected.</summary>
    public string? SelectedCandidateId { get; set; }
    /// <summary>Gets or sets the identifier of the recipe produced from the selected candidate.</summary>
    public Guid? SelectedRecipeId { get; set; }
    /// <summary>Gets or sets the client-supplied select-and-expand idempotency key, when selection completed.</summary>
    public Guid? SelectIdempotencyKey { get; set; }
    /// <summary>Gets or sets a non-sensitive failure classification.</summary>
    public string? FailureReason { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last state transition.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant after which the session may no longer be acted upon.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
