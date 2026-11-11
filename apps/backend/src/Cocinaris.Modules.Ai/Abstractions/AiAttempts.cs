namespace Cocinaris.Modules.Ai.Abstractions;

/// <summary>
/// One provider call and what came of it (PLAN-0039 R5), for finding why generations fail. It holds no
/// owner, no prompt, and no answer: only the operation, the model, codes, rule names with their
/// limits, sizes, and times. The correlation id links it to the usage row of the same request.
/// </summary>
/// <param name="OccurredAt">When the call ended.</param>
/// <param name="Operation">Registered operation name.</param>
/// <param name="Model">Model that answered, or null when none did.</param>
/// <param name="Round">1 for the primary round, 2 for the fallback round.</param>
/// <param name="Call">Call number within the round, 1 for the first, then repairs.</param>
/// <param name="Outcome">Closed code: <c>accepted</c>, <c>accepted_with_issues</c>, <c>quality_issues</c>, <c>invalid_output</c>, <c>timeout</c>, or <c>unavailable</c>.</param>
/// <param name="Rules">Content-free rule labels: violated rules on failure, fitted fields on success; null when none.</param>
/// <param name="LatencyMs">Time the call took.</param>
/// <param name="PromptTokens">Prompt tokens billed.</param>
/// <param name="CompletionTokens">Completion tokens billed.</param>
/// <param name="CorrelationId">The request's correlation id.</param>
public sealed record AiAttemptRecord(DateTimeOffset OccurredAt, string Operation, string? Model, int Round, int Call, string Outcome, string? Rules, int? LatencyMs, int? PromptTokens, int? CompletionTokens, string CorrelationId);

/// <summary>Stores attempt records. Implementations never throw: a lost record must not fail a generation.</summary>
public interface IAiAttemptStore
{
    /// <summary>Stores one attempt record, best effort.</summary>
    Task RecordAsync(AiAttemptRecord record, CancellationToken cancellationToken);
}

/// <summary>A provider that also takes attempt records; operations record through it when it offers this.</summary>
public interface IAiAttemptSink
{
    /// <summary>Stores one attempt record, best effort.</summary>
    Task RecordAttemptAsync(AiAttemptRecord record, CancellationToken cancellationToken);
}

/// <summary>
/// Wraps the configured provider so every operation's calls are recorded (PLAN-0039 R5) without each
/// operation service taking another dependency. Calls pass through unchanged.
/// </summary>
/// <param name="inner">The real provider.</param>
/// <param name="store">Where attempt records go.</param>
public sealed class RecordingAiProvider(IAiProvider inner, IAiAttemptStore store) : IAiProvider, IAiAttemptSink
{
    /// <inheritdoc />
    public string Name => inner.Name;

    /// <inheritdoc />
    public Task<AiProviderInvocationResult> InvokeAsync(AiProviderInvocationRequest request, CancellationToken cancellationToken) =>
        inner.InvokeAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task RecordAttemptAsync(AiAttemptRecord record, CancellationToken cancellationToken) => store.RecordAsync(record, cancellationToken);
}
