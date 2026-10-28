using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KitchenFlow.Modules.Ai.Abstractions;

namespace KitchenFlow.Modules.Ai.Providers;

/// <summary>
/// Replaceable DeepSeek chat-completions adapter. Sends the already-bounded, already-untrusted-marked
/// request payload as a user message next to a fixed system prompt and returns raw provider text for
/// downstream schema and semantic validation. This adapter never retries and never repairs; the
/// AI Gateway operation layer owns the single-repair policy.
/// </summary>
public sealed class DeepSeekAiProvider(HttpClient httpClient, DeepSeekOptions options) : IAiProvider
{
    /// <inheritdoc />
    public string Name => "deepseek";

    /// <inheritdoc />
    public async Task<AiProviderInvocationResult> InvokeAsync(AiProviderInvocationRequest request, CancellationToken cancellationToken)
    {
        var model = request.PreferNonThinking ? options.NonThinkingModel : options.ThinkingModel;
        var body = new DeepSeekChatRequest(
            model,
            [
                new DeepSeekChatMessage("system", options.SystemPrompt),
                new DeepSeekChatMessage("user", request.Payload)
            ],
            new DeepSeekResponseFormat("json_object"),
            0.2);

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

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<DeepSeekChatResponse>(payload, SerializerOptions);
            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(content)
                ? AiProviderInvocationResult.Failure(AiProviderFailureKind.Unavailable)
                : AiProviderInvocationResult.Success(content, model, parsed?.Usage?.PromptTokens, parsed?.Usage?.CompletionTokens);
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

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record DeepSeekChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<DeepSeekChatMessage> Messages,
        [property: JsonPropertyName("response_format")] DeepSeekResponseFormat ResponseFormat,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record DeepSeekChatMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] string Content);

    private sealed record DeepSeekResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record DeepSeekChatResponse([property: JsonPropertyName("choices")] IReadOnlyList<DeepSeekChoice>? Choices, [property: JsonPropertyName("usage")] DeepSeekUsage? Usage);

    private sealed record DeepSeekChoice([property: JsonPropertyName("message")] DeepSeekChatMessage? Message);

    private sealed record DeepSeekUsage([property: JsonPropertyName("prompt_tokens")] int? PromptTokens, [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
