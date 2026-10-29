namespace Cocinaris.Modules.Inventory.Domain;

/// <summary>Identifies the authorized source that created a preparation batch.</summary>
public enum PreparationSourceType
{
    /// <summary>A user explicitly recorded a preparation outside recipe-execution reconciliation.</summary>
    ManualPreparation,
    /// <summary>
    /// Completion of a recipe execution (PLAN-0042): the inputs are the pantry lots the user
    /// confirmed as used, and the outputs are the leftover portions stored, which may be none.
    /// </summary>
    RecipeExecution
}

/// <summary>Identifies the lifecycle state attached to an output of the manual preparation contract.</summary>
public enum PreparedComponentLifecycleState
{
    /// <summary>The lot is a prepared reusable component; its storage location may still be refrigerated or frozen.</summary>
    Prepared
}

/// <summary>Identifies the evidence source for a prepared component's shelf-life information.</summary>
public enum PreparedShelfLifeEvidenceSource
{
    /// <summary>No authoritative date is known.</summary>
    Unknown,
    /// <summary>The user explicitly supplied the evidence.</summary>
    UserEntered,
    /// <summary>A curated Cocinaris rule supplied the evidence.</summary>
    Curated,
    /// <summary>A curated regional reference supplied the evidence.</summary>
    Regional
}

/// <summary>Communicates confidence without presenting shelf-life information as a safety guarantee.</summary>
public enum ShelfLifeEvidenceConfidence
{
    /// <summary>No confidence can be stated because evidence is unknown.</summary>
    Unknown,
    /// <summary>Low confidence evidence.</summary>
    Low,
    /// <summary>Moderate confidence evidence.</summary>
    Medium,
    /// <summary>High confidence evidence.</summary>
    High
}

/// <summary>Immutable shelf-life evidence retained for one prepared output lot.</summary>
public sealed record PreparedShelfLifeEvidence
{
    /// <summary>Creates validated prepared-component shelf-life evidence.</summary>
    /// <exception cref="ArgumentException">Thrown when source, confidence, date, or conditions are inconsistent.</exception>
    public PreparedShelfLifeEvidence(DateOnly? date, PreparedShelfLifeEvidenceSource source, ShelfLifeEvidenceConfidence confidence, string? conditions)
    {
        var normalizedConditions = string.IsNullOrWhiteSpace(conditions) ? null : conditions.Trim();
        if (!Enum.IsDefined(source) || !Enum.IsDefined(confidence) || normalizedConditions?.EnumerateRunes().Count() > 500)
        {
            throw new ArgumentException("Prepared shelf-life evidence is invalid.");
        }

        if (source == PreparedShelfLifeEvidenceSource.Unknown && (date is not null || confidence != ShelfLifeEvidenceConfidence.Unknown))
        {
            throw new ArgumentException("Unknown shelf-life evidence cannot include a date or confidence.");
        }

        if (source != PreparedShelfLifeEvidenceSource.Unknown && (date is null || confidence == ShelfLifeEvidenceConfidence.Unknown))
        {
            throw new ArgumentException("Known shelf-life evidence requires a date and confidence.");
        }

        Date = date;
        Source = source;
        Confidence = confidence;
        Conditions = normalizedConditions;
    }

    /// <summary>Gets the optional advisory calendar date.</summary>
    public DateOnly? Date { get; }

    /// <summary>Gets the source of the evidence.</summary>
    public PreparedShelfLifeEvidenceSource Source { get; }

    /// <summary>Gets the confidence associated with the evidence.</summary>
    public ShelfLifeEvidenceConfidence Confidence { get; }

    /// <summary>Gets privacy-sensitive handling conditions when explicitly recorded.</summary>
    public string? Conditions { get; }
}

/// <summary>Represents the immutable identity and common provenance of one preparation transaction.</summary>
public sealed class PreparationBatch
{
    private PreparationBatch(Guid id, Guid ownerUserId, Guid outputProductId, PreparationSourceType sourceType, Guid? sourceReferenceId, DateTimeOffset preparedAt, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        OutputProductId = outputProductId;
        SourceType = sourceType;
        SourceReferenceId = sourceReferenceId;
        PreparedAt = preparedAt;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the immutable preparation transaction identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the owner that is permitted to inspect this batch and its provenance.</summary>
    public Guid OwnerUserId { get; }

    /// <summary>Gets the single product represented by all v1 output portions.</summary>
    public Guid OutputProductId { get; }

    /// <summary>Gets the explicit creation source.</summary>
    public PreparationSourceType SourceType { get; }

    /// <summary>Gets the recipe execution that produced the batch; present only for <see cref="PreparationSourceType.RecipeExecution"/>.</summary>
    public Guid? SourceReferenceId { get; }

    /// <summary>Gets when the food was prepared, in UTC.</summary>
    public DateTimeOffset PreparedAt { get; }

    /// <summary>Gets when the authoritative transaction was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Creates a preparation batch with valid ownership, source, and timestamps.</summary>
    /// <param name="ownerUserId">Owner of the batch.</param>
    /// <param name="outputProductId">Product represented by the outputs.</param>
    /// <param name="preparedAt">When the food was prepared, in UTC; not in the future.</param>
    /// <param name="now">Creation instant.</param>
    /// <param name="sourceType">Creation source.</param>
    /// <param name="sourceReferenceId">Recipe execution identifier; required for, and only for, <see cref="PreparationSourceType.RecipeExecution"/>.</param>
    /// <exception cref="ArgumentException">Thrown when identity, timestamp, or source reference is invalid.</exception>
    public static PreparationBatch Create(Guid ownerUserId, Guid outputProductId, DateTimeOffset preparedAt, DateTimeOffset now, PreparationSourceType sourceType = PreparationSourceType.ManualPreparation, Guid? sourceReferenceId = null)
    {
        if (ownerUserId == Guid.Empty || outputProductId == Guid.Empty || preparedAt > now || !Enum.IsDefined(sourceType))
        {
            throw new ArgumentException("Preparation batch identity or timestamp is invalid.");
        }

        if ((sourceType == PreparationSourceType.RecipeExecution) != (sourceReferenceId is { } reference && reference != Guid.Empty))
        {
            throw new ArgumentException("Only a recipe-execution batch carries a source reference, and it must carry one.");
        }

        return new PreparationBatch(Guid.NewGuid(), ownerUserId, outputProductId, sourceType, sourceReferenceId, preparedAt, now);
    }
}
