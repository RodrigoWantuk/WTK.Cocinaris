namespace Cocinaris.Modules.Ai.Abstractions;

/// <summary>
/// Transport-neutral invocation request sent by the gateway to one replaceable AI provider.
/// The provider never receives authoritative identifiers, credentials, or unrestricted context;
/// <see cref="Payload"/> is the already-bounded structured request envelope assembled by the caller.
/// </summary>
/// <param name="Operation">Registered AI Gateway operation name, for example <c>recipe.suggest_candidates.v1</c>.</param>
/// <param name="Payload">Serialized JSON request envelope sent to the provider as untrusted-safe structured input.</param>
/// <param name="PreferNonThinking">Whether the gateway prefers a faster non-thinking model variant for this call.</param>
/// <param name="TimeoutSeconds">Maximum time the provider call may take before it is treated as a timeout failure.</param>
/// <param name="CorrelationId">Request correlation identifier propagated to logs and telemetry, never to the provider.</param>
/// <param name="MaxOutputTokens">Completion token limit for the call.</param>
public sealed record AiProviderInvocationRequest(string Operation, string Payload, bool PreferNonThinking, int TimeoutSeconds, string CorrelationId, int MaxOutputTokens = 4096);

/// <summary>Describes why a provider invocation did not produce usable output.</summary>
public enum AiProviderFailureKind
{
    /// <summary>The provider did not respond within <see cref="AiProviderInvocationRequest.TimeoutSeconds"/>.</summary>
    Timeout,
    /// <summary>The provider is unreachable, disabled, or returned a transport/authentication failure.</summary>
    Unavailable
}

/// <summary>
/// Transport-neutral provider invocation outcome. Only <see cref="RawContent"/> carries provider output;
/// the gateway treats it as untrusted text that must be parsed and validated before any further use.
/// </summary>
public sealed record AiProviderInvocationResult
{
    private AiProviderInvocationResult(bool isSuccess, string? rawContent, string? modelUsed, int? promptTokens, int? completionTokens, AiProviderFailureKind? failureKind, int? latencyMs = null, int providerCalls = 1)
    {
        LatencyMs = latencyMs;
        ProviderCalls = providerCalls;
        IsSuccess = isSuccess;
        RawContent = rawContent;
        ModelUsed = modelUsed;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        FailureKind = failureKind;
    }

    /// <summary>Gets whether the provider returned raw output for downstream parsing.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the untrusted raw provider output, present only when <see cref="IsSuccess"/> is <see langword="true"/>.</summary>
    public string? RawContent { get; }

    /// <summary>Gets the concrete model identifier that produced the output.</summary>
    public string? ModelUsed { get; }

    /// <summary>Gets the provider-reported prompt token usage, when available.</summary>
    public int? PromptTokens { get; }

    /// <summary>Gets the provider-reported completion token usage, when available.</summary>
    public int? CompletionTokens { get; }

    /// <summary>Gets the failure classification, present only when <see cref="IsSuccess"/> is <see langword="false"/>.</summary>
    public AiProviderFailureKind? FailureKind { get; }

    /// <summary>Gets the wall-clock time spent in the provider, across fallback calls, when measured.</summary>
    public int? LatencyMs { get; }

    /// <summary>Gets the number of provider calls made (more than 1 when a fallback model was tried).</summary>
    public int ProviderCalls { get; }

    /// <summary>Returns the same result with measured latency and call count.</summary>
    public AiProviderInvocationResult WithTiming(int latencyMs, int providerCalls) =>
        new(IsSuccess, RawContent, ModelUsed, PromptTokens, CompletionTokens, FailureKind, latencyMs, providerCalls);

    /// <summary>Creates a successful invocation outcome.</summary>
    public static AiProviderInvocationResult Success(string rawContent, string modelUsed, int? promptTokens = null, int? completionTokens = null) =>
        new(true, rawContent, modelUsed, promptTokens, completionTokens, null);

    /// <summary>Creates a failed invocation outcome.</summary>
    public static AiProviderInvocationResult Failure(AiProviderFailureKind kind) => new(false, null, null, null, null, kind);
}

/// <summary>
/// Replaceable AI model provider boundary. Implementations own transport, authentication, and
/// provider-specific request shaping; they never see authoritative identifiers or mutate state.
/// </summary>
public interface IAiProvider
{
    /// <summary>Gets the stable provider name recorded in usage and observability records.</summary>
    string Name { get; }

    /// <summary>Invokes the provider for one registered operation and returns untrusted raw output.</summary>
    Task<AiProviderInvocationResult> InvokeAsync(AiProviderInvocationRequest request, CancellationToken cancellationToken);
}
