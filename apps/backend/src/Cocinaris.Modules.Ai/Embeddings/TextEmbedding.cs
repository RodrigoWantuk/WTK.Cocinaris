using System.Text.Json.Serialization;

namespace Cocinaris.Modules.Ai.Embeddings;

/// <summary>
/// One batch of texts to embed. The texts are short ingredient names or catalog descriptions
/// assembled by backend code; callers bound the batch size and each text's length.
/// </summary>
/// <param name="Inputs">Texts in the order the vectors are returned.</param>
public sealed record EmbeddingRequest(IReadOnlyList<string> Inputs);

/// <summary>Why an embedding call failed.</summary>
public enum EmbeddingFailureKind
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>Timeout, network error, rate limit, or provider 5xx; worth a later retry.</summary>
    Transient,

    /// <summary>The provider rejected the request or answered with an unusable body.</summary>
    Invalid
}

/// <summary>Outcome of one provider embedding call.</summary>
/// <param name="Vectors">One unit-length vector per input, in input order, when the call succeeded.</param>
/// <param name="PromptTokens">Input tokens the provider reported, when it did.</param>
/// <param name="CostUsd">Cost the provider reported, when it did.</param>
/// <param name="Model">Model identifier that produced the vectors.</param>
/// <param name="LatencyMs">Provider latency.</param>
/// <param name="Failure">Failure category; <see cref="EmbeddingFailureKind.None"/> on success.</param>
/// <param name="FailureCode">Bounded, non-sensitive failure detail for logs.</param>
public sealed record EmbeddingResult(
    IReadOnlyList<float[]>? Vectors,
    int? PromptTokens,
    decimal? CostUsd,
    string Model,
    int LatencyMs,
    EmbeddingFailureKind Failure,
    string? FailureCode = null)
{
    /// <summary>Whether the call produced vectors.</summary>
    public bool IsSuccess => Failure == EmbeddingFailureKind.None && Vectors is { Count: > 0 };
}

/// <summary>
/// Replaceable text-embedding provider behind the AI Gateway (PLAN-0055). Implementations make
/// exactly one provider request per call, never retry, and return vectors already truncated to
/// <see cref="Dimensions"/> and scaled to unit length, so cosine similarity is a dot product.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Provider name recorded in the usage ledger.</summary>
    string Name { get; }

    /// <summary>
    /// Identifies the vector space: model, provider routing, and dimensions. Vectors from different
    /// spaces must never be compared, so this string is part of every cache key.
    /// </summary>
    string Space { get; }

    /// <summary>Length of every returned vector.</summary>
    int Dimensions { get; }

    /// <summary>Embeds one batch.</summary>
    Task<EmbeddingResult> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Ingredient-identity embedding policy under <c>Ai:Embeddings</c> (PLAN-0055, owner decision
/// 2026-10-30: Qwen3 Embedding 8B through OpenRouter).
/// </summary>
/// <remarks>
/// The model returns 4096 dimensions and OpenRouter lists no <c>dimensions</c> parameter for it.
/// Qwen3 Embedding is trained for Matryoshka truncation, so the adapter keeps the first
/// <see cref="Dimensions"/> values and renormalizes. 1024 stays inside pgvector's 2000-dimension
/// index limit. The provider order is pinned because the fp8 deployment of the same model returns
/// slightly different vectors (and cost four times as much on 2026-10-30).
/// </remarks>
public sealed class EmbeddingOptions
{
    /// <summary>Gets or sets whether embeddings are requested. Requires an OpenRouter key.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the OpenRouter model identifier.</summary>
    public string Model { get; set; } = "qwen/qwen3-embedding-8b";

    /// <summary>Gets or sets the OpenRouter providers allowed, in order; no fallback outside this list.</summary>
    public string[] ProviderOrder { get; set; } = ["nebius", "deepinfra"];

    /// <summary>Gets or sets the stored vector length.</summary>
    public int Dimensions { get; set; } = 1024;

    /// <summary>Gets or sets the price per million input tokens in US dollars, used when the provider reports no cost.</summary>
    public decimal PriceUsdPerMillionTokens { get; set; } = 0.01m;

    /// <summary>Gets or sets the provider call timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Validates that the policy is coherent.</summary>
    public bool IsValid() => !string.IsNullOrWhiteSpace(Model) && ProviderOrder.Length > 0 && Dimensions is >= 32 and <= 2000
        && PriceUsdPerMillionTokens >= 0 && TimeoutSeconds > 0;
}

/// <summary>Vector helpers shared by the providers.</summary>
public static class EmbeddingVectors
{
    /// <summary>
    /// Keeps the first <paramref name="dimensions"/> values and scales the result to unit length.
    /// Returns null for a vector that is shorter than requested or has no length.
    /// </summary>
    public static float[]? TruncateAndNormalize(IReadOnlyList<float> values, int dimensions)
    {
        if (values.Count < dimensions)
        {
            return null;
        }

        var result = new float[dimensions];
        double sum = 0;
        for (var index = 0; index < dimensions; index++)
        {
            var value = values[index];
            if (!float.IsFinite(value))
            {
                return null;
            }

            result[index] = value;
            sum += (double)value * value;
        }

        if (sum <= 0)
        {
            return null;
        }

        var scale = (float)(1 / Math.Sqrt(sum));
        for (var index = 0; index < dimensions; index++)
        {
            result[index] *= scale;
        }

        return result;
    }

    /// <summary>Cosine similarity of two unit vectors of equal length.</summary>
    public static double Cosine(float[] left, float[] right)
    {
        double dot = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += (double)left[index] * right[index];
        }

        return dot;
    }
}

/// <summary>OpenAI-compatible embeddings response, as OpenRouter returns it.</summary>
internal sealed record OpenRouterEmbeddingResponse(
    [property: JsonPropertyName("data")] List<OpenRouterEmbeddingItem>? Data,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("usage")] OpenRouterEmbeddingUsage? Usage);

/// <summary>One returned vector.</summary>
internal sealed record OpenRouterEmbeddingItem(
    [property: JsonPropertyName("embedding")] float[]? Embedding,
    [property: JsonPropertyName("index")] int Index);

/// <summary>Reported usage.</summary>
internal sealed record OpenRouterEmbeddingUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("cost")] decimal? Cost);
