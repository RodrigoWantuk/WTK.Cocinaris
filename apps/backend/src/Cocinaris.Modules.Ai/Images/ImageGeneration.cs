namespace Cocinaris.Modules.Ai.Images;

/// <summary>
/// One image request to a provider. The prompt is assembled by deterministic backend code from a
/// validated, non-private visual descriptor; it never carries user data.
/// </summary>
/// <param name="Prompt">Complete provider prompt.</param>
/// <param name="Resolution">Provider size tier, for example <c>768</c>.</param>
/// <param name="AspectRatio">Normalized aspect ratio, for example <c>16:9</c>.</param>
public sealed record ImageGenerationRequest(string Prompt, string Resolution, string AspectRatio);

/// <summary>Why an image call failed.</summary>
public enum ImageGenerationFailureKind
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>Timeout, network error, rate limit, or provider 5xx; worth one later retry.</summary>
    Transient,

    /// <summary>The provider refused the prompt or filtered the output.</summary>
    Refused,

    /// <summary>The provider answered without a usable image, or rejected the request shape.</summary>
    Invalid
}

/// <summary>Outcome of one provider image call.</summary>
/// <param name="Bytes">Decoded image bytes when the call succeeded.</param>
/// <param name="MediaType">Media type the provider reported, when it did.</param>
/// <param name="CostUsd">Cost the provider reported for this call, when it did.</param>
/// <param name="Model">Model identifier that produced the image.</param>
/// <param name="LatencyMs">Provider latency.</param>
/// <param name="Failure">Failure category; <see cref="ImageGenerationFailureKind.None"/> on success.</param>
/// <param name="FailureCode">Bounded, non-sensitive failure detail for logs (status code or error type).</param>
public sealed record ImageGenerationResult(
    byte[]? Bytes,
    string? MediaType,
    decimal? CostUsd,
    string Model,
    int LatencyMs,
    ImageGenerationFailureKind Failure,
    string? FailureCode = null)
{
    /// <summary>Whether the call produced image bytes.</summary>
    public bool IsSuccess => Failure == ImageGenerationFailureKind.None && Bytes is { Length: > 0 };
}

/// <summary>
/// Replaceable image-generation provider behind the AI Gateway (PLAN-0030). Implementations call
/// exactly one provider request per invocation and never retry; the thumbnail job owns retries.
/// </summary>
public interface IImageGenerationProvider
{
    /// <summary>Provider name recorded in the usage ledger.</summary>
    string Name { get; }

    /// <summary>Generates one image.</summary>
    Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// OpenRouter configuration under <c>Ai:Providers:OpenRouter</c>. The repository defines no key;
/// deployments supply <see cref="ApiKey"/> through environment-backed configuration.
/// </summary>
public sealed class OpenRouterOptions
{
    /// <summary>Gets or sets the API base address.</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1/";

    /// <summary>Gets or sets the bearer API key. Must never have a non-empty repository default.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Recipe thumbnail policy under <c>Ai:Images</c> (PLAN-0030, owner decision 2026-10-30: FLUX 3
/// Image Klein through OpenRouter, 16:9 at 1024 × 576 because of price).
/// </summary>
/// <remarks>
/// OpenRouter takes a size tier and an aspect ratio, not pixels. The <c>768</c> tier is about
/// 768 × 768 pixels of area, and 768² = 1024 × 576, so tier <c>768</c> at <c>16:9</c> is the
/// requested 1024 × 576. It is also the cheapest tier (US$ 0.008 per image on 2026-10-30). The
/// job checks the returned size against <see cref="ExpectedWidth"/> and <see cref="ExpectedHeight"/>.
/// </remarks>
public sealed class RecipeThumbnailOptions
{
    /// <summary>Gets or sets whether thumbnail generation runs. Requires an OpenRouter key.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the OpenRouter model identifier.</summary>
    public string Model { get; set; } = "black-forest-labs/flux-3-image-klein";

    /// <summary>Gets or sets the provider size tier.</summary>
    public string Resolution { get; set; } = "768";

    /// <summary>Gets or sets the aspect ratio.</summary>
    public string AspectRatio { get; set; } = "16:9";

    /// <summary>Gets or sets the expected image width in pixels.</summary>
    public int ExpectedWidth { get; set; } = 1024;

    /// <summary>Gets or sets the expected image height in pixels.</summary>
    public int ExpectedHeight { get; set; } = 576;

    /// <summary>Gets or sets the price of one image in US dollars, held against the monthly AI ceiling until the provider reports the real cost.</summary>
    public decimal PriceUsd { get; set; } = 0.008m;

    /// <summary>Gets or sets the most images one owner's recipes may generate per UTC day.</summary>
    public int UserDailyImages { get; set; } = 10;

    /// <summary>Gets or sets the most images the deployment may generate per UTC day.</summary>
    public int GlobalDailyImages { get; set; } = 100;

    /// <summary>Gets or sets the largest accepted image, in bytes.</summary>
    public int MaxBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Gets or sets the provider call timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Gets or sets the lowest similarity (0–1) at which a similar dish's image is reused instead
    /// of generating one (PLAN-0055 Phase 3). A value above 1 turns reuse off.
    /// </summary>
    public double ReuseThreshold { get; set; } = 0.8;

    /// <summary>Gets or sets how often the job looks for pending thumbnails, in seconds.</summary>
    public int PollSeconds { get; set; } = 3;

    /// <summary>Gets or sets the directory holding generated images; empty uses the default data directory.</summary>
    public string? StoragePath { get; set; }

    /// <summary>Gets the image price in micro-dollars.</summary>
    public long PriceMicroUsd => (long)Math.Ceiling(PriceUsd * 1_000_000m);

    /// <summary>Validates that limits are positive and the size policy is coherent.</summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(Resolution) && !string.IsNullOrWhiteSpace(AspectRatio)
        && ExpectedWidth is > 0 and <= 4096 && ExpectedHeight is > 0 and <= 4096 && PriceUsd >= 0
        && UserDailyImages > 0 && GlobalDailyImages >= UserDailyImages && MaxBytes > 0 && TimeoutSeconds > 0 && PollSeconds > 0 && ReuseThreshold > 0;
}
