using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cocinaris.Modules.Ai.Abstractions;

namespace Cocinaris.Modules.Ai.Providers;

/// <summary>
/// Replaceable DeepSeek chat-completions adapter. Sends the already-bounded, already-untrusted-marked
/// request payload as a user message next to a fixed system prompt and returns raw provider text for
/// downstream schema and semantic validation. This adapter never repairs; the AI Gateway operation
/// layer owns the single-repair policy. Its only retry is one fallback-model call after a provider
/// error (PLAN-0040).
/// </summary>
public sealed class DeepSeekAiProvider(HttpClient httpClient, DeepSeekOptions options) : IAiProvider
{
    /// <inheritdoc />
    public string Name => "deepseek";

    /// <inheritdoc />
    /// <remarks>
    /// Calls the configured model; if that call fails with a provider error (not a timeout), calls
    /// the fallback model once (PLAN-0040). Latency covers both calls. A timeout is not retried here,
    /// because the operation layer already owns the single repair and the user is waiting.
    /// </remarks>
    public async Task<AiProviderInvocationResult> InvokeAsync(AiProviderInvocationRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await CallAsync(options.Model, request, cancellationToken);
        var calls = 1;
        if (!result.IsSuccess && result.FailureKind == AiProviderFailureKind.Unavailable
            && !string.IsNullOrWhiteSpace(options.FallbackModel) && !string.Equals(options.FallbackModel, options.Model, StringComparison.OrdinalIgnoreCase))
        {
            result = await CallAsync(options.FallbackModel, request, cancellationToken);
            calls = 2;
        }

        return result.WithTiming((int)Stopwatch.GetElapsedTime(started).TotalMilliseconds, calls);
    }

    private async Task<AiProviderInvocationResult> CallAsync(string model, AiProviderInvocationRequest request, CancellationToken cancellationToken)
    {
        var thinking = !request.PreferNonThinking;
        var supportsThinking = DeepSeekOptions.SupportsThinkingParameter(model);
        var body = new DeepSeekChatRequest(
            model,
            [
                new DeepSeekChatMessage("system", options.SystemPrompt),
                new DeepSeekChatMessage("user", request.Payload)
            ],
            new DeepSeekResponseFormat("json_object"),
            0.2,
            request.MaxOutputTokens,
            supportsThinking ? new DeepSeekThinking(thinking ? "enabled" : "disabled") : null,
            supportsThinking && thinking ? options.ReasoningEffort : null);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl), "/chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(body, SerializerOptions), Encoding.UTF8, "application/json");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        try
        {
            using var response = await httpClient.SendAsync(httpRequest, timeoutSource.Token);
            if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
            {
                return AiProviderInvocationResult.Failure(AiProviderFailureKind.Timeout);
            }

            if (!response.IsSuccessStatusCode)
            {
                return AiProviderInvocationResult.Failure(AiProviderFailureKind.Unavailable);
            }

            var payload = await response.Content.ReadAsStringAsync(timeoutSource.Token);
            var parsed = JsonSerializer.Deserialize<DeepSeekChatResponse>(payload, SerializerOptions);
            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            // The provider reports the model that answered; record it so cost uses the right price.
            return string.IsNullOrWhiteSpace(content)
                ? AiProviderInvocationResult.Failure(AiProviderFailureKind.Unavailable)
                : AiProviderInvocationResult.Success(content, parsed?.Model ?? model, parsed?.Usage?.PromptTokens, parsed?.Usage?.CompletionTokens);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiProviderInvocationResult.Failure(AiProviderFailureKind.Timeout);
        }
        catch (HttpRequestException)
        {
            return AiProviderInvocationResult.Failure(AiProviderFailureKind.Unavailable);
        }
        catch (JsonException)
        {
            return AiProviderInvocationResult.Failure(AiProviderFailureKind.Unavailable);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private sealed record DeepSeekChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<DeepSeekChatMessage> Messages,
        [property: JsonPropertyName("response_format")] DeepSeekResponseFormat ResponseFormat,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("thinking")] DeepSeekThinking? Thinking,
        [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort);

    private sealed record DeepSeekThinking([property: JsonPropertyName("type")] string Type);

    private sealed record DeepSeekChatMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] string Content);

    private sealed record DeepSeekResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record DeepSeekChatResponse([property: JsonPropertyName("model")] string? Model, [property: JsonPropertyName("choices")] IReadOnlyList<DeepSeekChoice>? Choices, [property: JsonPropertyName("usage")] DeepSeekUsage? Usage);

    private sealed record DeepSeekChoice([property: JsonPropertyName("message")] DeepSeekChatMessage? Message);

    private sealed record DeepSeekUsage([property: JsonPropertyName("prompt_tokens")] int? PromptTokens, [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
