using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>Result of <see cref="RecipeAiInvocation.InvokeWithRepairAsync"/>: a validated response, or a failure code and detail.</summary>
/// <typeparam name="T">The closed response type.</typeparam>
/// <param name="Response">The accepted response, or null on failure.</param>
/// <param name="Usage">Usage summed across every provider call, failed ones included.</param>
/// <param name="FailureCode">Stable application error code on failure.</param>
/// <param name="FailureDetail">Problem detail on failure.</param>
internal sealed record AiInvocationOutcome<T>(T? Response, AiCallUsage Usage, string? FailureCode, string? FailureDetail)
    where T : class;

/// <summary>
/// The provider call, repair loop, and error mapping shared by the recipe AI operations
/// (suggest, expand, and revise). AI output is never returned until it passes the given validator.
/// </summary>
internal static class RecipeAiInvocation
{
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("Cocinaris.Recipes.Generation");
    private static readonly System.Diagnostics.Metrics.Counter<long> Generations = Meter.CreateCounter<long>("cocinaris.ai.generation", "{generation}", "Recipe AI operations by outcome: first_call (accepted on the first call), recovered (accepted after a repair or the fallback round), or failed.");

    /// <summary>
    /// Calls the provider and allows <see cref="AiOperationDefinition.MaxRepairAttempts"/> repairs; if that
    /// round ends without an answer because the answer was invalid or the provider was down, a second
    /// round runs on the fallback model (PLAN-0039 R2). A timeout gets no second round here, because
    /// the person is waiting; a job retries it. A schema-invalid answer is repaired with its validation
    /// errors. A schema-valid answer with soft quality issues (language, detail) is also sent for
    /// repair, but it is kept: if the repair is not clean, the first valid answer is returned with its
    /// quality flags (PLAN-0040). Usage (tokens, latency, attempts) is summed across every call, failed
    /// ones included, and every call is recorded without content when the provider is an
    /// <see cref="IAiAttemptSink"/> (PLAN-0039 R5).
    /// </summary>
    public static async Task<AiInvocationOutcome<T>> InvokeWithRepairAsync<T>(
        IAiProvider aiProvider,
        AiOperationDefinition definition,
        string correlationId,
        string initialPayload,
        Func<(IReadOnlyList<string> Errors, string Output), string> buildRepair,
        Func<string, RecipeProtocolValidationResult<T>> validate,
        Func<T, IReadOnlyList<RecipeQualityIssue>> checkQuality,
        CancellationToken cancellationToken)
        where T : class
    {
        string? modelUsed = null;
        int? promptTokens = null;
        int? completionTokens = null;
        var attempts = 0;
        var calls = 0;
        int? latencyMs = null;
        AiProviderFailureKind? lastTransportFailure = null;
        IReadOnlyList<string>? lastErrors = null;
        string? lastOutput = null;
        (T Response, IReadOnlyList<RecipeQualityIssue> Issues, IReadOnlyList<string> Normalized)? kept = null;
        var sink = aiProvider as IAiAttemptSink;
        AiCallUsage Usage(string? flags = null) => new(aiProvider.Name, modelUsed, promptTokens, completionTokens, attempts, latencyMs, flags);
        string? Flags(IReadOnlyList<RecipeQualityIssue> issues, IReadOnlyList<string> normalized)
        {
            var parts = issues.Select(issue => issue.Flag).Distinct().ToList();
            if (normalized.Count > 0)
            {
                parts.Add("normalized:" + string.Join(";", normalized));
            }

            var joined = string.Join(",", parts);
            return joined.Length == 0 ? null : joined.Length > 100 ? joined[..100] : joined;
        }

        async Task RecordAsync(int round, int call, string outcome, string? rules, AiProviderInvocationResult result)
        {
            if (sink is null)
            {
                return;
            }

            await sink.RecordAttemptAsync(new AiAttemptRecord(DateTimeOffset.UtcNow, definition.Operation, result.ModelUsed, round, call, outcome, rules, result.LatencyMs, result.PromptTokens, result.CompletionTokens, correlationId), cancellationToken);
        }

        AiInvocationOutcome<T> Accept(T response, IReadOnlyList<RecipeQualityIssue> issues, IReadOnlyList<string> normalized)
        {
            Generations.Add(1, new KeyValuePair<string, object?>("operation", definition.Operation), new KeyValuePair<string, object?>("outcome", calls == 1 ? "first_call" : "recovered"));
            return new AiInvocationOutcome<T>(response, Usage(Flags(issues, normalized)), null, null);
        }

        for (var round = 1; round <= 2; round++)
        {
            if (round == 2 && (kept is not null || (lastErrors is null && lastTransportFailure != AiProviderFailureKind.Unavailable)))
            {
                break;
            }

            // The second round starts again from the original request: the other model gets a clean start.
            lastErrors = round == 2 ? null : lastErrors;
            for (var attempt = 0; attempt <= definition.MaxRepairAttempts; attempt++)
            {
                var payload = attempt == 0 ? initialPayload : buildRepair((lastErrors ?? ["validation_failed"], lastOutput ?? string.Empty));
                var invocation = await aiProvider.InvokeAsync(new AiProviderInvocationRequest(definition.Operation, payload, definition.PreferNonThinking, definition.TimeoutSeconds, correlationId, definition.MaxOutputTokens, UseFallbackModel: round == 2), cancellationToken);
                attempts += invocation.ProviderCalls;
                calls++;
                latencyMs = SumNullable(latencyMs, invocation.LatencyMs);
                if (!invocation.IsSuccess)
                {
                    lastTransportFailure = invocation.FailureKind;
                    await RecordAsync(round, attempt + 1, invocation.FailureKind == AiProviderFailureKind.Timeout ? "timeout" : "unavailable", null, invocation);
                    continue;
                }

                modelUsed = invocation.ModelUsed;
                promptTokens = SumNullable(promptTokens, invocation.PromptTokens);
                completionTokens = SumNullable(completionTokens, invocation.CompletionTokens);
                var validation = validate(invocation.RawContent!);
                var normalized = validation.Normalized ?? [];
                if (!validation.IsValid || validation.Response is null)
                {
                    lastErrors = validation.Errors;
                    lastOutput = invocation.RawContent;
                    await RecordAsync(round, attempt + 1, "invalid_output", RuleLabels(validation.Errors, 300), invocation);
                    continue;
                }

                var issues = checkQuality(validation.Response);
                if (issues.Count == 0)
                {
                    await RecordAsync(round, attempt + 1, "accepted", normalized.Count == 0 ? null : "normalized:" + string.Join(";", normalized), invocation);
                    return Accept(validation.Response, issues, normalized);
                }

                await RecordAsync(round, attempt + 1, "quality_issues", string.Join(",", issues.Select(issue => issue.Flag).Distinct()), invocation);
                // Keep the first valid answer; a later one replaces it only if it has fewer issues.
                if (kept is null || issues.Count < kept.Value.Issues.Count)
                {
                    kept = (validation.Response, issues, normalized);
                }

                lastErrors = issues.Select(issue => issue.Instruction).ToList();
                lastOutput = invocation.RawContent;
            }
        }

        if (kept is { } accepted)
        {
            return Accept(accepted.Response, accepted.Issues, accepted.Normalized);
        }

        Generations.Add(1, new KeyValuePair<string, object?>("operation", definition.Operation), new KeyValuePair<string, object?>("outcome", "failed"));
        // The ledger keeps which rules the last answer broke, so repeated failures can be traced
        // to a rule (owner report 2026-10-30: the first request sometimes failed, the second worked).
        return new AiInvocationOutcome<T>(null, Usage(lastErrors is null ? null : RecipeCookNowApplicationService.InvalidFlags(lastErrors)), MapProviderFailure(lastTransportFailure, lastErrors is not null), DescribeProviderFailure(lastTransportFailure, lastErrors is not null));
    }

    /// <summary>
    /// Content-free labels of rule violations: quoted values, identifiers (ids, GUIDs), and indices are
    /// removed, and the rule's own numbers, such as its limits, are kept.
    /// </summary>
    /// <param name="errors">Validation errors.</param>
    /// <param name="maxLength">Longest result.</param>
    public static string RuleLabels(IReadOnlyList<string> errors, int maxLength)
    {
        var labels = errors
            .Select(error => System.Text.RegularExpressions.Regex.Replace(error, "\\[\\d+\\]|^\\$\\.", string.Empty))
            .Select(error => System.Text.RegularExpressions.Regex.Replace(error, "\"[^\"]*\"|\\b[0-9a-fA-F]{8}-[0-9a-fA-F-]{27,}\\b|\\b(?=[\\w-]*[A-Za-z])(?=[\\w-]*[0-9])[\\w-]+\\b|[(),<>=:]", " "))
            .Select(error => string.Join(' ', error.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(6);
        var joined = string.Join(";", labels);
        return joined.Length > maxLength ? joined[..maxLength] : joined;
    }

    /// <summary>Maps a refused usage reservation to its stable error code.</summary>
    public static string MapReservationFailure(AiUsageReservationOutcome outcome) => outcome switch
    {
        AiUsageReservationOutcome.Disabled => "ai_capability_unavailable",
        AiUsageReservationOutcome.GlobalBudgetExhausted or AiUsageReservationOutcome.UserBudgetExhausted or AiUsageReservationOutcome.MonthlyCostExhausted
            or AiUsageReservationOutcome.GlobalTokenWindowExhausted or AiUsageReservationOutcome.UserTokenWindowExhausted => "ai_budget_exhausted",
        AiUsageReservationOutcome.ConcurrencyExhausted => "ai_operation_conflict",
        _ => "ai_budget_unavailable"
    };

    /// <summary>Describes a refused usage reservation.</summary>
    public static string ReservationDetail(AiUsageReservationOutcome outcome) => outcome switch
    {
        AiUsageReservationOutcome.Disabled => "The AI Gateway is currently disabled.",
        AiUsageReservationOutcome.GlobalBudgetExhausted => "The global AI usage budget for today is exhausted.",
        AiUsageReservationOutcome.UserBudgetExhausted => "Your daily AI usage budget is exhausted.",
        AiUsageReservationOutcome.ConcurrencyExhausted => "You already have the maximum number of AI requests in progress.",
        AiUsageReservationOutcome.MonthlyCostExhausted => "The monthly AI spending limit for this deployment is reached.",
        AiUsageReservationOutcome.GlobalTokenWindowExhausted => "The AI usage limit for the last 24 hours is reached; it frees up as earlier calls leave the window.",
        AiUsageReservationOutcome.UserTokenWindowExhausted => "Your AI usage limit for the last 24 hours is reached; it frees up as your earlier calls leave the window.",
        _ => "The AI Gateway is currently unavailable."
    };

    private static int? SumNullable(int? left, int? right) =>
        left is null && right is null ? null : (left ?? 0) + (right ?? 0);

    private static string MapProviderFailure(AiProviderFailureKind? failureKind, bool sawInvalidOutput)
    {
        if (sawInvalidOutput)
        {
            return "ai_output_invalid";
        }

        return failureKind switch
        {
            AiProviderFailureKind.Timeout => "ai_provider_timeout",
            AiProviderFailureKind.Unavailable => "ai_provider_unavailable",
            _ => "ai_provider_unavailable"
        };
    }

    private static string DescribeProviderFailure(AiProviderFailureKind? failureKind, bool sawInvalidOutput)
    {
        if (sawInvalidOutput)
        {
            return "The AI provider returned output that failed protocol validation after the allowed repair attempt.";
        }

        return failureKind switch
        {
            AiProviderFailureKind.Timeout => "The AI provider timed out before returning a usable response.",
            AiProviderFailureKind.Unavailable => "The AI provider is currently unavailable.",
            _ => "The AI provider did not return a usable response."
        };
    }
}
