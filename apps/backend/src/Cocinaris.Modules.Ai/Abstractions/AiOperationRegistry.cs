namespace Cocinaris.Modules.Ai.Abstractions;

/// <summary>
/// Registered configuration for one AI Gateway operation: schema versions, model policy, timeout,
/// repair allowance, and the estimated usage-unit cost reserved before invocation. ADR-0005 requires
/// every AI capability to have a versioned, explicit registration before it may be invoked.
/// </summary>
/// <param name="Operation">Stable operation name, for example <c>recipe.suggest_candidates.v1</c>.</param>
/// <param name="WorkflowVersion">Versioned prompt/workflow identifier used for observability and evaluation.</param>
/// <param name="ProtocolVersion">Request/response contract protocol version, for example <c>0.3</c>.</param>
/// <param name="PreferNonThinking">Whether this operation prefers a faster non-thinking model variant.</param>
/// <param name="TimeoutSeconds">Maximum time allowed for one provider call.</param>
/// <param name="MaxRepairAttempts">Maximum number of repair re-invocations after an invalid provider output.</param>
/// <param name="EstimatedUsageUnits">Usage units reserved from the ledger before invocation.</param>
/// <param name="ContextBudgetCharacters">Maximum serialized character length of the assembled context payload.</param>
/// <param name="MaxOutputTokens">Completion token limit sent to the provider for one call.</param>
/// <param name="EstimatedCostMicroUsd">
/// Provider cost held against the monthly ceiling while the operation runs (PLAN-0040): two
/// attempts at the primary model's price with a full-length answer, rounded up.
/// </param>
/// <param name="EstimatedTokens">
/// Provider tokens held against the rolling 24-hour token windows while the operation runs: two
/// attempts, each with a full prompt and a full-length answer.
/// </param>
public sealed record AiOperationDefinition(
    string Operation,
    string WorkflowVersion,
    string ProtocolVersion,
    bool PreferNonThinking,
    int TimeoutSeconds,
    int MaxRepairAttempts,
    int EstimatedUsageUnits,
    int ContextBudgetCharacters,
    int MaxOutputTokens = 4096,
    long EstimatedCostMicroUsd = 5_000,
    int EstimatedTokens = 20_000);

/// <summary>
/// Immutable in-process registry of AI Gateway operations. Registration happens once at startup;
/// unregistered operation names fail fast with <c>ai_capability_unavailable</c> at the call boundary.
/// </summary>
public sealed class AiOperationRegistry
{
    /// <summary>Stable registered name for cook-now candidate suggestion.</summary>
    public const string SuggestCandidates = "recipe.suggest_candidates.v1";

    /// <summary>Stable registered name for selected-candidate expansion.</summary>
    public const string ExpandSelected = "recipe.expand_selected.v1";

    /// <summary>Stable registered name for recipe thumbnail generation (PLAN-0030).</summary>
    public const string ThumbnailGenerate = "recipe.thumbnail.generate.v1";

    private readonly Dictionary<string, AiOperationDefinition> _operations;

    /// <summary>Creates the registry from an explicit set of operation definitions.</summary>
    public AiOperationRegistry(IEnumerable<AiOperationDefinition> operations)
    {
        _operations = operations.ToDictionary(item => item.Operation, StringComparer.Ordinal);
    }

    /// <summary>Creates the default registry containing the recipe generation operations used by PLAN-0028.</summary>
    public static AiOperationRegistry CreateDefault() => new(
    [
        new AiOperationDefinition(SuggestCandidates, "recipe-suggest-candidates.v1", "0.3", PreferNonThinking: true, TimeoutSeconds: 30, MaxRepairAttempts: 1, EstimatedUsageUnits: 3, ContextBudgetCharacters: 12_000, MaxOutputTokens: 4096, EstimatedCostMicroUsd: 3_000, EstimatedTokens: 14_000),
        new AiOperationDefinition(ExpandSelected, "recipe-expand-selected.v1", "0.3", PreferNonThinking: true, TimeoutSeconds: 45, MaxRepairAttempts: 1, EstimatedUsageUnits: 5, ContextBudgetCharacters: 12_000, MaxOutputTokens: 6144, EstimatedCostMicroUsd: 5_000, EstimatedTokens: 18_000),
        // One image per call, priced per image; the job reserves the configured image price instead
        // of this estimate. Images use no text tokens, so they never count against the token windows.
        new AiOperationDefinition(ThumbnailGenerate, "recipe-thumbnail-style-v2", "1", PreferNonThinking: true, TimeoutSeconds: 90, MaxRepairAttempts: 0, EstimatedUsageUnits: 1, ContextBudgetCharacters: 2_000, MaxOutputTokens: 0, EstimatedCostMicroUsd: 8_000, EstimatedTokens: 0)
    ]);

    /// <summary>Attempts to resolve a registered operation definition.</summary>
    public bool TryGet(string operation, out AiOperationDefinition definition) => _operations.TryGetValue(operation, out definition!);
}
