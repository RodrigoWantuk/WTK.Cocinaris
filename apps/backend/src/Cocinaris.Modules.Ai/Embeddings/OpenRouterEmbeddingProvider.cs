using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cocinaris.Modules.Ai.Images;

namespace Cocinaris.Modules.Ai.Embeddings;

/// <summary>
/// OpenRouter Embeddings API adapter (<c>POST /embeddings</c>). Sends one batch with the provider
/// order pinned, fallbacks off, and data collection denied, then truncates and renormalizes each
/// vector. It never retries; the resolution job owns retries.
/// </summary>
public sealed class OpenRouterEmbeddingProvider(HttpClient httpClient, OpenRouterOptions openRouter, EmbeddingOptions options) : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Largest response body read: 64 inputs × 4096 floats as JSON text stays well below this.</summary>
    private const int MaxResponseBytes = 8 * 1024 * 1024;

    /// <inheritdoc />
    public string Name => "openrouter";

    /// <inheritdoc />
    public string Space => $"{options.Model}|{string.Join(',', options.ProviderOrder)}|{options.Dimensions}";

    /// <inheritdoc />
    public int Dimensions => options.Dimensions;

    /// <inheritdoc />
    public async Task<EmbeddingResult> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        int Elapsed() => (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var body = new OpenRouterEmbeddingRequest(options.Model, request.Inputs, new OpenRouterProviderPreferences(options.ProviderOrder, false, "deny"));
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(openRouter.BaseUrl), "embeddings"))
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SerializerOptions), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openRouter.ApiKey);
        message.Headers.TryAddWithoutValidation("X-Title", "Cocinaris");

        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var kind = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || status >= 500 ? EmbeddingFailureKind.Transient : EmbeddingFailureKind.Invalid;
                return new EmbeddingResult(null, null, null, options.Model, Elapsed(), kind, $"http_{status}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var bounded = new MemoryStream();
            var buffer = new byte[81_920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (bounded.Length + read > MaxResponseBytes)
                {
                    return new EmbeddingResult(null, null, null, options.Model, Elapsed(), EmbeddingFailureKind.Invalid, "response_too_large");
                }

                bounded.Write(buffer, 0, read);
            }

            var parsed = JsonSerializer.Deserialize<OpenRouterEmbeddingResponse>(bounded.ToArray(), SerializerOptions);
            var tokens = parsed?.Usage?.PromptTokens;
            var cost = parsed?.Usage?.Cost;
            // Every input must come back exactly once; a partial answer is unusable because the
            // vectors could not be attributed to their texts with certainty.
            if (parsed?.Data is not { } items || items.Count != request.Inputs.Count || items.Select(item => item.Index).Distinct().Count() != items.Count
                || items.Any(item => item.Index < 0 || item.Index >= request.Inputs.Count))
            {
                return new EmbeddingResult(null, tokens, cost, options.Model, Elapsed(), EmbeddingFailureKind.Invalid, "wrong_count");
            }

            var vectors = new float[request.Inputs.Count][];
            foreach (var item in items)
            {
                if (item.Embedding is null || EmbeddingVectors.TruncateAndNormalize(item.Embedding, options.Dimensions) is not { } vector)
                {
                    return new EmbeddingResult(null, tokens, cost, options.Model, Elapsed(), EmbeddingFailureKind.Invalid, "bad_vector");
                }

                vectors[item.Index] = vector;
            }

            return new EmbeddingResult(vectors, tokens, cost, parsed.Model ?? options.Model, Elapsed(), EmbeddingFailureKind.None);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EmbeddingResult(null, null, null, options.Model, Elapsed(), EmbeddingFailureKind.Transient, "timeout");
        }
        catch (HttpRequestException)
        {
            return new EmbeddingResult(null, null, null, options.Model, Elapsed(), EmbeddingFailureKind.Transient, "network");
        }
        catch (JsonException)
        {
            return new EmbeddingResult(null, null, null, options.Model, Elapsed(), EmbeddingFailureKind.Invalid, "bad_json");
        }
    }

    private sealed record OpenRouterEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("provider")] OpenRouterProviderPreferences Provider);

    private sealed record OpenRouterProviderPreferences(
        [property: JsonPropertyName("order")] string[] Order,
        [property: JsonPropertyName("allow_fallbacks")] bool AllowFallbacks,
        [property: JsonPropertyName("data_collection")] string DataCollection);
}

/// <summary>
/// Test and development provider: a deterministic bag of character trigrams hashed into the
/// vector, so texts that share spelling are close and unrelated texts are not. It costs nothing
/// and is never used in a deployment with a real key.
/// </summary>
public sealed class FakeEmbeddingProvider(int dimensions = 1024) : IEmbeddingProvider
{
    /// <inheritdoc />
    public string Name => "fake";

    /// <inheritdoc />
    public string Space => $"fake-trigram|{dimensions}";

    /// <inheritdoc />
    public int Dimensions => dimensions;

    /// <summary>Number of calls made, for tests.</summary>
    public int Calls { get; private set; }

    /// <summary>Total texts embedded, for tests.</summary>
    public int Texts { get; private set; }

    /// <inheritdoc />
    public Task<EmbeddingResult> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        Texts += request.Inputs.Count;
        var vectors = request.Inputs.Select(Vector).ToList();
        return Task.FromResult(new EmbeddingResult(vectors, request.Inputs.Sum(text => text.Length / 4 + 1), 0m, "fake-embedding", 1, EmbeddingFailureKind.None));
    }

    private float[] Vector(string text)
    {
        // Instruction prefixes are stripped so a query and a catalog document share one space.
        var body = text.Contains("\nQuery: ", StringComparison.Ordinal) ? text[(text.LastIndexOf("\nQuery: ", StringComparison.Ordinal) + 8)..] : text;
        var padded = $"  {body.ToLowerInvariant()}  ";
        var values = new float[dimensions];
        for (var index = 0; index + 3 <= padded.Length; index++)
        {
            // string.GetHashCode is randomized per process; FNV-1a keeps vectors stable across runs.
            values[Fnv(padded.AsSpan(index, 3)) % dimensions] += 1;
        }

        return EmbeddingVectors.TruncateAndNormalize(values, dimensions) ?? values;
    }

    private static uint Fnv(ReadOnlySpan<char> text)
    {
        var hash = 2166136261u;
        foreach (var character in text)
        {
            hash = (hash ^ character) * 16777619u;
        }

        return hash;
    }
}
