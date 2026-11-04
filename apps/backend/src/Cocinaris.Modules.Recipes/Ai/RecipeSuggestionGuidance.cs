namespace Cocinaris.Modules.Recipes.Ai;

/// <summary>
/// What the person asks of a suggestion request before the three candidates are generated
/// (PLAN-0056 Phase 2, owner reports 3 and 6; PLAN-0060). Every value is a closed code or a lot id,
/// except <paramref name="Hint"/>, the one free text: it is untrusted, cleaned by
/// <see cref="Cocinaris.Modules.Ai.Safety.UntrustedText"/>, sent only inside a named data field, and only a soft wish.
/// </summary>
/// <param name="MealKind"><c>full_meal</c>, <c>light_meal</c>, or <c>snack</c>, or null for any.</param>
/// <param name="DishFamilies">Up to three of <see cref="DishFamilyCodes"/>; empty for any.</param>
/// <param name="Cuisine">One of <see cref="CuisineCodes"/>, or null for any.</param>
/// <param name="Time"><c>quick</c> (20 minutes or less), <c>medium</c> (45 or less), or null for any.</param>
/// <param name="RequiredLotIds">Pantry lots every candidate must use (at most 5).</param>
/// <param name="ExcludedLotIds">Pantry lots never sent to the AI and never used (at most 50).</param>
/// <param name="Hint">A short wish in the person's words (at most 300 characters); inspiration for the model, never a rule.</param>
/// <param name="Adaptation"><c>adapt_to_pantry</c> (make it work with what is at home), <c>stay_faithful</c> (closer to the original, a longer shopping list is fine), or null for the default balance.</param>
public sealed record RecipeSuggestionGuidance(
    string? MealKind,
    IReadOnlyList<string> DishFamilies,
    string? Cuisine,
    string? Time,
    IReadOnlyList<Guid> RequiredLotIds,
    IReadOnlyList<Guid> ExcludedLotIds,
    string? Hint = null,
    string? Adaptation = null)
{
    /// <summary>Accepted meal kinds.</summary>
    public static readonly IReadOnlySet<string> MealKindCodes = new HashSet<string>(StringComparer.Ordinal) { "full_meal", "light_meal", "snack" };

    /// <summary>Accepted dish families.</summary>
    public static readonly IReadOnlySet<string> DishFamilyCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "pasta", "rice", "meat", "poultry", "fish_seafood", "eggs", "vegetarian", "soup_broth", "salad", "sandwich_wrap", "baked", "dessert"
    };

    /// <summary>Accepted cuisines.</summary>
    public static readonly IReadOnlySet<string> CuisineCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "brazilian", "italian", "mexican", "japanese", "chinese", "indian", "middle_eastern", "mediterranean", "french", "spanish", "american", "portuguese"
    };

    /// <summary>Accepted time codes and their maximum total minutes.</summary>
    public static readonly IReadOnlyDictionary<string, int> TimeLimits = new Dictionary<string, int>(StringComparer.Ordinal) { ["quick"] = 20, ["medium"] = 45 };

    /// <summary>Accepted ingredient approaches.</summary>
    public static readonly IReadOnlySet<string> AdaptationCodes = new HashSet<string>(StringComparer.Ordinal) { "adapt_to_pantry", "stay_faithful" };

    /// <summary>Longest hint, in characters.</summary>
    public const int MaxHintLength = 300;

    /// <summary>Most required ingredients to buy a candidate may list under <c>adapt_to_pantry</c> before it is sent back once.</summary>
    public const int MaxPurchasesWhenAdapting = 2;

    /// <summary>Most required lots.</summary>
    public const int MaxRequired = 5;

    /// <summary>Most excluded lots.</summary>
    public const int MaxExcluded = 50;

    /// <summary>Most dish families.</summary>
    public const int MaxDishFamilies = 3;

    /// <summary>Most total minutes a candidate may take, or null when time is free.</summary>
    public int? MaxTotalMinutes => Time is not null && TimeLimits.TryGetValue(Time, out var minutes) ? minutes : null;

    /// <summary>Whether the guidance asks for a kind, family, cuisine, hint, or ingredient approach (soft preferences only the model can follow).</summary>
    public bool HasStylePreferences => MealKind is not null || DishFamilies.Count > 0 || Cuisine is not null || !string.IsNullOrWhiteSpace(Hint) || Adaptation is not null;

    /// <summary>Validation errors by field; empty when the guidance is acceptable.</summary>
    public IReadOnlyDictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (MealKind is not null && !MealKindCodes.Contains(MealKind))
        {
            errors["guidance.mealKind"] = ["Unknown meal kind."];
        }

        if (DishFamilies.Count > MaxDishFamilies || DishFamilies.Any(code => !DishFamilyCodes.Contains(code)) || DishFamilies.Distinct(StringComparer.Ordinal).Count() != DishFamilies.Count)
        {
            errors["guidance.dishFamilies"] = [$"At most {MaxDishFamilies} distinct known dish families."];
        }

        if (Cuisine is not null && !CuisineCodes.Contains(Cuisine))
        {
            errors["guidance.cuisine"] = ["Unknown cuisine."];
        }

        if (Time is not null && !TimeLimits.ContainsKey(Time))
        {
            errors["guidance.time"] = ["Unknown time."];
        }

        if (Hint is { Length: > MaxHintLength })
        {
            errors["guidance.hint"] = [$"At most {MaxHintLength} characters."];
        }

        if (Adaptation is not null && !AdaptationCodes.Contains(Adaptation))
        {
            errors["guidance.adaptation"] = ["Unknown ingredient approach."];
        }

        if (RequiredLotIds.Count > MaxRequired || RequiredLotIds.Distinct().Count() != RequiredLotIds.Count)
        {
            errors["guidance.requiredLotIds"] = [$"At most {MaxRequired} distinct required items."];
        }

        if (ExcludedLotIds.Count > MaxExcluded || ExcludedLotIds.Distinct().Count() != ExcludedLotIds.Count)
        {
            errors["guidance.excludedLotIds"] = [$"At most {MaxExcluded} distinct excluded items."];
        }

        if (RequiredLotIds.Intersect(ExcludedLotIds).Any())
        {
            errors["guidance.excludedLotIds"] = ["An item cannot be both required and excluded."];
        }

        return errors;
    }
}
