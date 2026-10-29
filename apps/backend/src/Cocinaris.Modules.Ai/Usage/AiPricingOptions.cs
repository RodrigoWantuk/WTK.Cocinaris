namespace Cocinaris.Modules.Ai.Usage;

/// <summary>Provider price of one model, in US dollars per million tokens.</summary>
public sealed class AiModelPrice
{
    /// <summary>Gets or sets the price of one million prompt (input) tokens.</summary>
    public decimal InputUsdPerMillion { get; set; }

    /// <summary>Gets or sets the price of one million completion (output) tokens.</summary>
    public decimal OutputUsdPerMillion { get; set; }
}

/// <summary>
/// Published provider prices used to account real spend (PLAN-0040). Prices are configuration under
/// <c>Ai:Pricing</c> because providers change them; the defaults are the DeepSeek list prices recorded
/// in <c>docs/ai/provider-evaluations</c> and must be checked against the provider before relying on
/// them for anything beyond a safety cap. An unlisted model is charged at the most expensive listed
/// price, so a misconfiguration can only make the cap stricter.
/// </summary>
public sealed class AiPricingOptions
{
    /// <summary>Gets the per-model prices, keyed by the model identifier the provider reports.</summary>
    public Dictionary<string, AiModelPrice> Models { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deepseek-v4-flash"] = new AiModelPrice { InputUsdPerMillion = 0.14m, OutputUsdPerMillion = 0.28m },
        ["deepseek-chat"] = new AiModelPrice { InputUsdPerMillion = 0.28m, OutputUsdPerMillion = 0.42m }
    };

    /// <summary>Validates that every price is non-negative and at least one model is priced.</summary>
    public bool IsValid() => Models.Count > 0 && Models.Values.All(price => price.InputUsdPerMillion >= 0 && price.OutputUsdPerMillion >= 0);

    /// <summary>
    /// Cost of one call in micro-dollars (US$ 0.000001), rounded up. Tokens times US$ per million
    /// tokens is exactly micro-dollars.
    /// </summary>
    public long CostMicroUsd(string? model, int? promptTokens, int? completionTokens)
    {
        var price = model is not null && Models.TryGetValue(model, out var listed)
            ? listed
            : new AiModelPrice
            {
                InputUsdPerMillion = Models.Values.Max(item => item.InputUsdPerMillion),
                OutputUsdPerMillion = Models.Values.Max(item => item.OutputUsdPerMillion)
            };
        return (long)Math.Ceiling((promptTokens ?? 0) * price.InputUsdPerMillion + (completionTokens ?? 0) * price.OutputUsdPerMillion);
    }
}

/// <summary>
/// What one AI Gateway operation actually used across its attempts (PLAN-0040): the model that
/// answered last, summed tokens, attempts (1 plus repairs and fallbacks), summed provider latency,
/// and quality flags when a valid answer was accepted with soft issues (for example <c>language</c>).
/// Recorded on settlement and also on release, because a failed call is still billed.
/// </summary>
public sealed record AiCallUsage(string Provider, string? Model, int? PromptTokens, int? CompletionTokens, int Attempts, int? LatencyMs, string? QualityFlags = null);
