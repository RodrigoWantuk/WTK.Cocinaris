using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Images;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Ingredients;

namespace Cocinaris.Modules.Recipes.Thumbnails;

/// <summary>Lifecycle of one generated image, keyed by render key.</summary>
public enum RecipeThumbnailState
{
    /// <summary>Waiting for the job; also used while deferred by a quota until <c>NextAttemptAt</c>.</summary>
    Pending,

    /// <summary>Claimed by the job; a lease past its expiry returns it to pending.</summary>
    Generating,

    /// <summary>Stored and servable.</summary>
    Ready,

    /// <summary>Gave up (refused, invalid image, or repeated transient failure); the placeholder stays.</summary>
    Failed
}

/// <summary>One cached image or pending job.</summary>
/// <param name="RenderKey">Cache key (see <see cref="RecipeThumbnailDescriptor"/>).</param>
/// <param name="VisualIdentityKey">Appearance key, for analysis of reuse.</param>
/// <param name="DescriptorJson">Canonical descriptor the prompt is built from.</param>
/// <param name="RequestedByUserId">Owner whose recipe first needed the image; quotas and the ledger use it.</param>
/// <param name="State">Lifecycle state.</param>
/// <param name="Attempts">Provider calls made.</param>
/// <param name="NextAttemptAt">Earliest next try while pending.</param>
/// <param name="FileName">Stored file name when ready.</param>
/// <param name="MediaType">Media type read from the image's own header.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="FailureCode">Bounded failure category.</param>
/// <param name="SourceRenderKey">When the image is reused from a similar dish (PLAN-0055 Phase 3), the render key whose file it shows; null for a generated image.</param>
/// <param name="ReuseScore">Similarity score of that reuse, from 0 to 1.</param>
public sealed record RecipeThumbnailAsset(
    string RenderKey,
    string VisualIdentityKey,
    string DescriptorJson,
    Guid RequestedByUserId,
    RecipeThumbnailState State,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    string? FileName,
    string? MediaType,
    int? Width,
    int? Height,
    string? FailureCode,
    string? SourceRenderKey = null,
    double? ReuseScore = null);

/// <summary>What a recipe view shows: <c>ready</c> (with a URL), <c>pending</c>, or <c>none</c> (placeholder).</summary>
/// <param name="State"><c>ready</c>, <c>pending</c>, or <c>none</c>.</param>
/// <param name="Version">Short render-key prefix for cache busting when ready.</param>
public sealed record RecipeThumbnailView(string State, string? Version)
{
    /// <summary>No image: written by hand, invalid descriptor, disabled, or failed.</summary>
    public static readonly RecipeThumbnailView None = new("none", null);
}

/// <summary>Persistence port for thumbnail assets (schema <c>recipes</c>).</summary>
public interface IRecipeThumbnailStore
{
    /// <summary>Finds assets by render key.</summary>
    Task<IReadOnlyDictionary<string, RecipeThumbnailAsset>> FindAsync(IReadOnlyCollection<string> renderKeys, CancellationToken cancellationToken);

    /// <summary>Inserts pending assets; existing keys are left untouched, so concurrent misses collapse into one job.</summary>
    Task EnqueueAsync(IReadOnlyCollection<RecipeThumbnailAsset> assets, CancellationToken cancellationToken);

    /// <summary>
    /// Claims the oldest due pending asset, or one whose generation lease expired, setting it to
    /// <see cref="RecipeThumbnailState.Generating"/> until <paramref name="leaseUntil"/>. Concurrent
    /// workers never claim the same asset.
    /// </summary>
    Task<RecipeThumbnailAsset?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    /// <summary>Saves the asset's new state after a job step.</summary>
    Task SaveAsync(RecipeThumbnailAsset asset, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Ready, generated (not reused) images of one dish format, newest first, as candidates for
    /// reuse by a similar dish (PLAN-0055 Phase 3).
    /// </summary>
    Task<IReadOnlyList<RecipeThumbnailAsset>> FindReusableAsync(string dishFormat, int limit, CancellationToken cancellationToken);

    /// <summary>Counts images generated (ledger reservations of the thumbnail operation) since <paramref name="since"/>, for one owner or, with null, everyone.</summary>
    Task<int> CountGeneratedSinceAsync(Guid? ownerUserId, DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>Size of a served recipe image.</summary>
public enum RecipeThumbnailSize
{
    /// <summary>The full 1024 × 576 image, for the recipe page.</summary>
    Full,

    /// <summary>A 480 × 270 image, for recipe cards in lists.</summary>
    Small
}

/// <summary>
/// Re-encodes a validated provider image into the served WebP files (PLAN-0030 follow-up). The
/// provider's original is kept, so a later encoder change re-encodes without paying for a new image.
/// </summary>
public interface IThumbnailImageEncoder
{
    /// <summary>
    /// Encodes <paramref name="original"/> as WebP at <paramref name="size"/>; null when the bytes
    /// cannot be decoded, in which case the caller serves the original.
    /// </summary>
    byte[]? EncodeWebP(byte[] original, RecipeThumbnailSize size);
}

/// <summary>Stores generated image files outside the database.</summary>
public interface IRecipeThumbnailFileStore
{
    /// <summary>Writes a file atomically (temporary name, then rename).</summary>
    Task WriteAsync(string fileName, byte[] bytes, CancellationToken cancellationToken);

    /// <summary>Opens a stored file for reading, or null when it is missing.</summary>
    Stream? OpenRead(string fileName);

    /// <summary>Whether a stored file exists.</summary>
    bool Exists(string fileName);
}

/// <summary>
/// Recipe thumbnails (PLAN-0030). Reads resolve each recipe's image and queue a missing one. A
/// background job generates queued images one at a time, cache-first and within budget.
/// </summary>
/// <remarks>
/// A thumbnail never blocks or changes a recipe: every failure keeps the placeholder. The job
/// checks the image quotas, reserves the image price in the AI usage ledger (so images count
/// toward the monthly AI ceiling), calls the provider once, and checks the bytes. Only then does
/// it store the file and settle at the reported cost. Quotas defer a job rather than fail it.
/// </remarks>
public sealed class RecipeThumbnailService(
    IRecipeThumbnailStore store,
    IRecipeThumbnailFileStore files,
    IThumbnailImageEncoder encoder,
    IImageGenerationProvider? provider,
    AiUsageGovernor governor,
    RecipeThumbnailOptions options,
    TimeProvider timeProvider)
{
    private const int MaxAttempts = 2;

    /// <summary>Whether generation can run: enabled with a provider configured.</summary>
    public bool Enabled => options.Enabled && provider is not null;

    private string RenderPolicy => $"{options.Model}\n{options.Resolution}\n{options.AspectRatio}\n{options.ExpectedWidth}x{options.ExpectedHeight}";

    /// <summary>The descriptor of a stored revision, or null when it cannot have an image.</summary>
    public RecipeThumbnailDescriptor? Describe(string? thumbnailVisualJson) => RecipeThumbnailDescriptor.TryCreate(thumbnailVisualJson, RenderPolicy);

    /// <summary>
    /// Resolves the thumbnail of each revision descriptor and, when generation is enabled, queues
    /// the missing ones for <paramref name="ownerUserId"/>.
    /// </summary>
    /// <param name="ownerUserId">The reading owner, charged for images their recipes queue.</param>
    /// <param name="descriptors">Stored descriptor JSON keyed by any caller identifier (for example the recipe id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyDictionary<TKey, RecipeThumbnailView>> ResolveAsync<TKey>(Guid ownerUserId, IReadOnlyDictionary<TKey, string> descriptors, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var described = descriptors
            .Select(entry => (entry.Key, Descriptor: Describe(entry.Value)))
            .Where(entry => entry.Descriptor is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Descriptor!);
        var result = descriptors.Keys.ToDictionary(key => key, _ => RecipeThumbnailView.None);
        if (described.Count == 0)
        {
            return result;
        }

        var assets = await store.FindAsync(described.Values.Select(item => item.RenderKey).Distinct().ToList(), cancellationToken);
        var now = timeProvider.GetUtcNow();
        var missing = new Dictionary<string, RecipeThumbnailAsset>(StringComparer.Ordinal);
        foreach (var (key, descriptor) in described)
        {
            if (assets.TryGetValue(descriptor.RenderKey, out var asset))
            {
                result[key] = asset.State switch
                {
                    RecipeThumbnailState.Ready => new RecipeThumbnailView("ready", descriptor.RenderKey[..16]),
                    RecipeThumbnailState.Failed => RecipeThumbnailView.None,
                    _ => Enabled ? new RecipeThumbnailView("pending", null) : RecipeThumbnailView.None
                };
            }
            else if (Enabled)
            {
                missing.TryAdd(descriptor.RenderKey, new RecipeThumbnailAsset(descriptor.RenderKey, descriptor.VisualIdentityKey, descriptor.CanonicalJson, ownerUserId, RecipeThumbnailState.Pending, 0, now, null, null, null, null, null));
                result[key] = new RecipeThumbnailView("pending", null);
            }
        }

        if (missing.Count > 0)
        {
            await store.EnqueueAsync(missing.Values.ToList(), cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Opens the stored image of a descriptor at <paramref name="size"/>, or null when none is
    /// ready. Served images are WebP. An image stored before WebP encoding existed is encoded on
    /// its first read; if encoding fails, the provider's original is served instead.
    /// </summary>
    public async Task<(Stream Content, string MediaType)?> OpenAsync(string? thumbnailVisualJson, RecipeThumbnailSize size, CancellationToken cancellationToken)
    {
        if (Describe(thumbnailVisualJson) is not { } descriptor)
        {
            return null;
        }

        var assets = await store.FindAsync([descriptor.RenderKey], cancellationToken);
        if (!assets.TryGetValue(descriptor.RenderKey, out var asset) || asset.State != RecipeThumbnailState.Ready || asset.FileName is null || asset.MediaType is null)
        {
            return null;
        }

        // A reused image serves the WebP files of the image it reuses.
        var fileKey = asset.SourceRenderKey ?? asset.RenderKey;
        var variant = VariantName(fileKey, size);
        if (!files.Exists(variant))
        {
            await WriteVariantsAsync(fileKey, asset.FileName, null, cancellationToken);
        }

        if (files.OpenRead(variant) is { } encoded)
        {
            return (encoded, "image/webp");
        }

        var original = files.OpenRead(asset.FileName);
        return original is null ? null : (original, asset.MediaType);
    }

    /// <summary>File name of a served WebP size; never collides with the provider original (<c>key.png</c>, <c>key.jpg</c>, <c>key.webp</c>).</summary>
    public static string VariantName(string renderKey, RecipeThumbnailSize size) =>
        $"{renderKey}.{(size == RecipeThumbnailSize.Small ? "small" : "full")}.webp";

    /// <summary>Encodes and stores both WebP sizes from the original; a failed encoding leaves the original as the served file.</summary>
    private async Task WriteVariantsAsync(string renderKey, string originalFileName, byte[]? originalBytes, CancellationToken cancellationToken)
    {
        if (originalBytes is null)
        {
            await using var stream = files.OpenRead(originalFileName);
            if (stream is null)
            {
                return;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            originalBytes = buffer.ToArray();
        }

        foreach (var size in new[] { RecipeThumbnailSize.Full, RecipeThumbnailSize.Small })
        {
            if (encoder.EncodeWebP(originalBytes, size) is { Length: > 0 } webp)
            {
                await files.WriteAsync(VariantName(renderKey, size), webp, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Processes at most one queued image. Returns true when one was claimed, so the job can keep
    /// draining without waiting for the next poll.
    /// </summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var asset = await store.ClaimNextAsync(now, now.AddSeconds(options.TimeoutSeconds * 2 + 30), cancellationToken);
        if (asset is null)
        {
            return false;
        }

        // A similar dish's image, scored deterministically, costs nothing and needs no quota.
        if (await TryReuseAsync(asset, now, cancellationToken))
        {
            return true;
        }

        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        if (await store.CountGeneratedSinceAsync(null, dayStart, cancellationToken) >= options.GlobalDailyImages
            || await store.CountGeneratedSinceAsync(asset.RequestedByUserId, dayStart, cancellationToken) >= options.UserDailyImages)
        {
            await Defer(asset, dayStart.AddDays(1), "daily_quota", cancellationToken);
            return true;
        }

        var reservation = await governor.ReserveAsync(asset.RequestedByUserId, AiOperationRegistry.ThumbnailGenerate, 1, options.PriceMicroUsd, 0, $"thumbnail:{asset.RenderKey[..16]}", cancellationToken);
        if (reservation.Outcome != AiUsageReservationOutcome.Reserved || reservation.ReservationId is not { } reservationId)
        {
            await Defer(asset, now.AddHours(1), "budget", cancellationToken);
            return true;
        }

        var descriptor = Describe(asset.DescriptorJson);
        if (descriptor is null)
        {
            await governor.ReleaseAtCostAsync(reservationId, null, 0, cancellationToken);
            await Fail(asset, 0, "descriptor", cancellationToken);
            return true;
        }

        var generated = await provider!.GenerateAsync(new ImageGenerationRequest(descriptor.Prompt, options.Resolution, options.AspectRatio), cancellationToken);
        var attempts = asset.Attempts + 1;
        var cost = generated.CostUsd is { } reported ? (long)Math.Ceiling(reported * 1_000_000m) : generated.IsSuccess ? options.PriceMicroUsd : 0;
        var usage = new AiCallUsage(provider.Name, generated.Model, null, null, 1, generated.LatencyMs, generated.FailureCode);
        if (!generated.IsSuccess)
        {
            await governor.ReleaseAtCostAsync(reservationId, usage, cost, cancellationToken);
            if (generated.Failure == ImageGenerationFailureKind.Transient && attempts < MaxAttempts)
            {
                await store.SaveAsync(asset with { State = RecipeThumbnailState.Pending, Attempts = attempts, NextAttemptAt = now.AddMinutes(10), FailureCode = generated.FailureCode }, now, cancellationToken);
            }
            else
            {
                await Fail(asset, attempts, generated.FailureCode ?? generated.Failure.ToString().ToLowerInvariant(), cancellationToken);
            }

            return true;
        }

        var inspected = generated.Bytes!.Length <= options.MaxBytes ? ImageInspector.Inspect(generated.Bytes) : null;
        if (inspected is null || !IsLandscape16By9(inspected))
        {
            // A billed image that fails validation is still paid for, so the cost is recorded.
            await governor.ReleaseAtCostAsync(reservationId, usage with { QualityFlags = "invalid_image" }, cost, cancellationToken);
            await Fail(asset, attempts, inspected is null ? "invalid_image" : "wrong_size", cancellationToken);
            return true;
        }

        var fileName = $"{asset.RenderKey}.{inspected.Extension}";
        await files.WriteAsync(fileName, generated.Bytes, cancellationToken);
        // The served WebP sizes are written before the asset is Ready, so its first read is already small.
        await WriteVariantsAsync(asset.RenderKey, fileName, generated.Bytes, cancellationToken);
        var sizeFlag = inspected.Width == options.ExpectedWidth && inspected.Height == options.ExpectedHeight ? null : $"size_{inspected.Width}x{inspected.Height}";
        await store.SaveAsync(asset with { State = RecipeThumbnailState.Ready, Attempts = attempts, FileName = fileName, MediaType = inspected.MediaType, Width = inspected.Width, Height = inspected.Height, FailureCode = sizeFlag }, now, cancellationToken);
        await governor.SettleAtCostAsync(reservationId, 1, usage with { QualityFlags = sizeFlag }, cost, cancellationToken);
        return true;
    }

    /// <summary>
    /// Reuses the most similar ready image of the same dish format when its score reaches
    /// <see cref="RecipeThumbnailOptions.ReuseThreshold"/> (PLAN-0055 Phase 3). Only images rendered
    /// under the current policy (same style, model, and size) qualify, and only generated ones, so
    /// a reuse never chains to another reuse.
    /// </summary>
    private async Task<bool> TryReuseAsync(RecipeThumbnailAsset asset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (options.ReuseThreshold > 1 || ThumbnailSimilarity.DishFormat(asset.DescriptorJson) is null)
        {
            return false;
        }

        using var document = System.Text.Json.JsonDocument.Parse(asset.DescriptorJson);
        var format = document.RootElement.GetProperty("dishFormat").GetString()!;
        var best = (await store.FindReusableAsync(format, 200, cancellationToken))
            .Where(candidate => candidate.RenderKey != asset.RenderKey && candidate.FileName is not null && Describe(candidate.DescriptorJson)?.RenderKey == candidate.RenderKey)
            .Select(candidate => (Candidate: candidate, Score: ThumbnailSimilarity.Score(asset.DescriptorJson, candidate.DescriptorJson, IngredientCatalog.Default)))
            .Where(entry => entry.Score >= options.ReuseThreshold)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Candidate.RenderKey, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best.Candidate is not { } source)
        {
            return false;
        }

        await store.SaveAsync(asset with
        {
            State = RecipeThumbnailState.Ready,
            FileName = source.FileName,
            MediaType = source.MediaType,
            Width = source.Width,
            Height = source.Height,
            FailureCode = null,
            SourceRenderKey = source.RenderKey,
            ReuseScore = Math.Round(best.Score, 3)
        }, now, cancellationToken);
        return true;
    }

    // The policy asks for 16:9; anything else (a square fallback, a portrait) would crop badly on cards.
    private static bool IsLandscape16By9(InspectedImage image) =>
        image.Width is >= 256 and <= 4096 && Math.Abs(image.Width * 9.0 / (image.Height * 16.0) - 1) <= 0.02;

    private Task Defer(RecipeThumbnailAsset asset, DateTimeOffset until, string reason, CancellationToken cancellationToken) =>
        store.SaveAsync(asset with { State = RecipeThumbnailState.Pending, NextAttemptAt = until, FailureCode = reason }, timeProvider.GetUtcNow(), cancellationToken);

    private Task Fail(RecipeThumbnailAsset asset, int attempts, string code, CancellationToken cancellationToken) =>
        store.SaveAsync(asset with { State = RecipeThumbnailState.Failed, Attempts = attempts, FailureCode = code.Length > 40 ? code[..40] : code }, timeProvider.GetUtcNow(), cancellationToken);
}
