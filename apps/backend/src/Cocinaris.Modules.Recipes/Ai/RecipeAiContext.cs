namespace Cocinaris.Modules.Recipes.Ai;

/// <summary>One bounded on-hand inventory item exposed to the AI Gateway as untrusted structured context.</summary>
public sealed record RecipeInventoryContextItem(string ItemId, string UserName, decimal Quantity, string Unit, string AvailabilitySource, string? IngredientRef);

/// <summary>One bounded declared equipment item exposed to the AI Gateway.</summary>
public sealed record RecipeEquipmentContextItem(string EquipmentId, string Name, IReadOnlyList<string> Capabilities);

/// <summary>Bounded owner presets relevant to recipe generation.</summary>
/// <param name="Servings">Default servings.</param>
/// <param name="Preferences">Confirmed preference codes.</param>
/// <param name="Restrictions">Confirmed restriction codes.</param>
/// <param name="AllowedAssumptions">Pantry assumptions the user allows.</param>
/// <param name="OutputLanguage">Language for every human-readable string the model writes: <c>pt-BR</c>, <c>en</c>, or <c>es</c>; null until resolved from the request.</param>
/// <param name="InstructionDetail">Profile instruction detail (<c>Minimal</c>, <c>Standard</c>, <c>Detailed</c>), or null when not declared.</param>
/// <param name="CookingSkill">Profile skill band (<c>Beginner</c> to <c>Advanced</c>), or null when not declared.</param>
public sealed record RecipeUserPresets(int Servings, IReadOnlyList<string> Preferences, IReadOnlyList<string> Restrictions, IReadOnlyList<string> AllowedAssumptions, string? OutputLanguage = null, string? InstructionDetail = null, string? CookingSkill = null);

/// <summary>Execution-mode context; PLAN-0028 supports only <c>cook_now</c>.</summary>
/// <remarks>
/// For <c>menu_planning</c> (PLAN-0029 Phase 2), <paramref name="PlannedDate"/> and
/// <paramref name="TargetMealType"/> describe the menu slot, and <paramref name="AlreadyPlannedRecipeNames"/>
/// lists meals already on the menu so the candidates differ from them.
/// </remarks>
public sealed record RecipeExecutionContext(string ExecutionMode, int? AvailableLeadMinutes, DateOnly? PlannedDate = null, string? TargetMealType = null, IReadOnlyList<string>? AlreadyPlannedRecipeNames = null);

/// <summary>Complete bounded context assembled for one <c>recipe.suggest_candidates.v1</c> request.</summary>
public sealed record RecipeSuggestRequestContext(
    string RequestId,
    IReadOnlyList<RecipeInventoryContextItem> InventoryItems,
    IReadOnlyList<RecipeEquipmentContextItem> Equipment,
    RecipeUserPresets Presets,
    RecipeExecutionContext ExecutionContext);

/// <summary>Minimal selected-candidate projection carried into an expansion request.</summary>
public sealed record RecipeSelectedCandidateContext(
    string CandidateId,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    IReadOnlyList<string> RequiredEquipmentIds,
    IReadOnlyList<RecipeInventoryContextItem> InventoryUses,
    IReadOnlyList<(string Name, decimal Quantity, string Unit, bool Optional)> AdditionalIngredients);

/// <summary>Complete bounded context assembled for one <c>recipe.expand_selected.v1</c> request.</summary>
public sealed record RecipeExpandRequestContext(
    string RequestId,
    IReadOnlyList<RecipeInventoryContextItem> InventoryItems,
    IReadOnlyList<RecipeEquipmentContextItem> Equipment,
    RecipeUserPresets Presets,
    RecipeSelectedCandidateContext SelectedCandidate);
