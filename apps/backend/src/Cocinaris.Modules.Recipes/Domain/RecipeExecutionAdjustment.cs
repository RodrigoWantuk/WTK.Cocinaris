namespace Cocinaris.Modules.Recipes.Domain;

/// <summary>
/// One change the cook made to an ingredient during this cooking session (PLAN-0042 Phase 2):
/// they used something else, used a different amount, or left it out. Adjustments belong to the
/// session; they change the completion proposal and can be carried into a new revision or a
/// variation, but they never change the recipe by themselves.
/// </summary>
/// <param name="IngredientId">Recipe ingredient the change applies to.</param>
/// <param name="Kind"><c>substitute</c>, <c>quantity</c>, or <c>skip</c>.</param>
/// <param name="SubstituteName">For <c>substitute</c>: what was used instead (1–120 characters).</param>
/// <param name="Quantity">Amount used this time, in the ingredient's unit (for <c>quantity</c>, and optionally for <c>substitute</c>).</param>
public sealed record RecipeExecutionAdjustment(string IngredientId, string Kind, string? SubstituteName, decimal? Quantity)
{
    /// <summary>Most adjustments one session holds.</summary>
    public const int MaxCount = 40;

    /// <summary>Longest substitute name.</summary>
    public const int MaxNameLength = 120;

    /// <summary>The allowed kinds.</summary>
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "substitute", "quantity", "skip" };

    /// <summary>Whether this adjustment is well formed; <paramref name="error"/> says why not.</summary>
    public bool IsValid(out string? error)
    {
        error = string.IsNullOrWhiteSpace(IngredientId) || IngredientId.Length > 64 ? "Each adjustment needs an ingredient."
            : !Kinds.Contains(Kind) ? "Unknown adjustment kind."
            : Kind == "substitute" && (string.IsNullOrWhiteSpace(SubstituteName) || SubstituteName.Trim().Length > MaxNameLength) ? "A substitution needs what was used instead (at most 120 characters)."
            : Kind == "quantity" && Quantity is null ? "A quantity change needs the amount used."
            : Quantity is { } value && (value <= 0 || value > 1_000_000 || decimal.Round(value, 3) != value) ? "The amount must be positive, with at most three decimals."
            : Kind == "skip" && (SubstituteName is not null || Quantity is not null) ? "Leaving an ingredient out takes no name or amount."
            : null;
        return error is null;
    }
}
