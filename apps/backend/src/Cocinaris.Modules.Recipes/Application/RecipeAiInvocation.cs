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
    /// <summary>
    /// Calls the provider and allows <see cref="AiOperationDefinition.MaxRepairAttempts"/> repairs.
    /// A schema-invalid answer is repaired with its validation errors. A schema-valid answer with
    /// soft quality issues (language, detail) is also sent for repair, but it is kept: if the repair
    /// is not clean, the first valid answer is returned with its quality flags (PLAN-0040). Usage
    /// (tokens, latency, attempts) is summed across every call, failed ones included.
    /// </summary>
    public static async Task<AiInvocationOutcome<T>> InvokeWithRepairAsync<T>(
        IAiProvider aiProvider,
        AiOperationDefinition definition,
        string correlationId,
        string initialPayload,
        Func<(IReadOnlyList<string> Errors, string Output), string> buildRepair,
        Func<string, (T? Response, IReadOnlyList<string> Errors)> validate,
        Func<T, IReadOnlyList<RecipeQualityIssue>> checkQuality,
        CancellationToken cancellationToken)
        where T : class
    {
        string? modelUsed = null;
        int? promptTokens = null;
        int? completionTokens = null;
        var attempts = 0;
        int? latencyMs = null;
        AiProviderFailureKind? lastTransportFailure = null;
        IReadOnlyList<string>? lastErrors = null;
        string? lastOutput = null;
        (T Response, IReadOnlyList<RecipeQualityIssue> Issues)? kept = null;
        AiCallUsage Usage(string? flags = null) => new(aiProvider.Name, modelUsed, promptTokens, completionTokens, attempts, latencyMs, flags);
        string Flags(IReadOnlyList<RecipeQualityIssue> issues) => string.Join(",", issues.Select(issue => issue.Flag).Distinct());

        for (var attempt = 0; attempt <= definition.MaxRepairAttempts; attempt++)
        {
            var payload = attempt == 0 ? initialPayload : buildRepair((lastErrors ?? ["validation_failed"], lastOutput ?? string.Empty));
            var invocation = await aiProvider.InvokeAsync(new AiProviderInvocationRequest(definition.Operation, payload, definition.PreferNonThinking, definition.TimeoutSeconds, correlationId, definition.MaxOutputTokens), cancellationToken);
            attempts += invocation.ProviderCalls;
            latencyMs = SumNullable(latencyMs, invocation.LatencyMs);
            if (!invocation.IsSuccess)
            {
                lastTransportFailure = invocation.FailureKind;
                continue;
            }

            modelUsed = invocation.ModelUsed;
            promptTokens = SumNullable(promptTokens, invocation.PromptTokens);
            completionTokens = SumNullable(completionTokens, invocation.CompletionTokens);
            var (response, errors) = validate(invocation.RawContent!);
            if (response is null)
            {
                lastErrors = errors;
                lastOutput = invocation.RawContent;
                continue;
            }

            var issues = checkQuality(response);
            if (issues.Count == 0)
            {
                return new AiInvocationOutcome<T>(response, Usage(), null, null);
            }

            // Keep the first valid answer; a later one replaces it only if it has fewer issues.
            if (kept is null || issues.Count < kept.Value.Issues.Count)
            {
                kept = (response, issues);
            }

            lastErrors = issues.Select(issue => issue.Instruction).ToList();
            lastOutput = invocation.RawContent;
        }

        if (kept is { } accepted)
        {
            return new AiInvocationOutcome<T>(accepted.Response, Usage(Flags(accepted.Issues)), null, null);
        }

        // The ledger keeps which rules the last answer broke, so repeated failures can be traced
        // to a rule (owner report 2026-10-30: the first request sometimes failed, the second worked).
        return new AiInvocationOutcome<T>(null, Usage(lastErrors is null ? null : RecipeCookNowApplicationService.InvalidFlags(lastErrors)), MapProviderFailure(lastTransportFailure, lastErrors is not null), DescribeProviderFailure(lastTransportFailure, lastErrors is not null));
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
