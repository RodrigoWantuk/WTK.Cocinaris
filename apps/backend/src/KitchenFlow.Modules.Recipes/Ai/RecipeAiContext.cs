namespace KitchenFlow.Modules.Recipes.Ai;

/// <summary>One bounded on-hand inventory item exposed to the AI Gateway as untrusted structured context.</summary>
public sealed record RecipeInventoryContextItem(string ItemId, string UserName, decimal Quantity, string Unit, string AvailabilitySource, string? IngredientRef);

/// <summary>One bounded declared equipment item exposed to the AI Gateway.</summary>
public sealed record RecipeEquipmentContextItem(string EquipmentId, string Name, IReadOnlyList<string> Capabilities);

/// <summary>Bounded owner presets relevant to recipe generation.</summary>
public sealed record RecipeUserPresets(int Servings, IReadOnlyList<string> Preferences, IReadOnlyList<string> Restrictions, IReadOnlyList<string> AllowedAssumptions);

/// <summary>Execution-mode context; PLAN-0028 supports only <c>cook_now</c>.</summary>
public sealed record RecipeExecutionContext(string ExecutionMode, int? AvailableLeadMinutes);

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
