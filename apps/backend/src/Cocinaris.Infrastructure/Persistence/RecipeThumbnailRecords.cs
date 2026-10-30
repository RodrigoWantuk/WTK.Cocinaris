namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// One generated recipe image or pending generation job (PLAN-0030), keyed by render key. Rows
/// hold no personal data beyond the owner charged for the image. The descriptor is the validated,
/// non-private appearance of a dish.
/// </summary>
public sealed class RecipeThumbnailRecord
{
    /// <summary>Gets or sets the render cache key (SHA-256, lower-case hex).</summary>
    public required string RenderKey { get; set; }
    /// <summary>Gets or sets the appearance key.</summary>
    public required string VisualIdentityKey { get; set; }
    /// <summary>Gets or sets the canonical descriptor JSON the prompt is built from.</summary>
    public required string DescriptorJson { get; set; }
    /// <summary>Gets or sets the owner whose recipe first needed the image.</summary>
    public Guid RequestedByUserId { get; set; }
    /// <summary>Gets or sets <c>Pending</c>, <c>Generating</c>, <c>Ready</c>, or <c>Failed</c>.</summary>
    public required string State { get; set; }
    /// <summary>Gets or sets the number of provider calls made.</summary>
    public int Attempts { get; set; }
    /// <summary>Gets or sets the earliest next try while pending, or the lease expiry while generating.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }
    /// <summary>Gets or sets the stored file name when ready.</summary>
    public string? FileName { get; set; }
    /// <summary>Gets or sets the media type read from the image header.</summary>
    public string? MediaType { get; set; }
    /// <summary>Gets or sets the width in pixels.</summary>
    public int? Width { get; set; }
    /// <summary>Gets or sets the height in pixels.</summary>
    public int? Height { get; set; }
    /// <summary>Gets or sets the bounded failure, deferral, or size note.</summary>
    public string? FailureCode { get; set; }
    /// <summary>Gets or sets the render key of the similar dish whose image this one reuses (PLAN-0055 Phase 3); null when generated.</summary>
    public string? SourceRenderKey { get; set; }
    /// <summary>Gets or sets the similarity score of that reuse.</summary>
    public double? ReuseScore { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
