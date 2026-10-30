namespace Cocinaris.Modules.Recipes.Application;

/// <summary>Transport-neutral input for the <c>request candidates</c> cook-now command.</summary>
/// <param name="IdempotencyKey">Client replay key.</param>
/// <param name="CorrelationId">Request correlation identifier.</param>
/// <param name="RequestLanguage">Interface language of the request (<c>pt-BR</c>, <c>en</c>, <c>es</c>), used when the profile declares none.</param>
/// <param name="MenuTarget">Menu slot to plan (PLAN-0029 Phase 2); null for cook-now.</param>
/// <param name="Guidance">The person's direction for this request (PLAN-0056); null for none.</param>
public sealed record RequestCandidatesCommand(Guid? IdempotencyKey, string CorrelationId, string? RequestLanguage = null, MenuPlanningTarget? MenuTarget = null, Ai.RecipeSuggestionGuidance? Guidance = null);

/// <summary>A menu slot the candidates are for, resolved by the caller from an owned menu.</summary>
/// <param name="PlannedDate">Day of the meal, or null for "some day in the period".</param>
/// <param name="MealType">Meal, if any.</param>
/// <param name="Servings">Portions to plan, 1 to 24.</param>
/// <param name="AlreadyPlannedRecipeNames">Recipes already on the menu, so the candidates differ from them (at most 20).</param>
public sealed record MenuPlanningTarget(DateOnly? PlannedDate, string? MealType, int Servings, IReadOnlyList<string> AlreadyPlannedRecipeNames);

/// <summary>Transport-neutral input for the <c>select candidate</c> cook-now command.</summary>
/// <param name="SessionId">Generation session.</param>
/// <param name="CandidateId">Selected candidate.</param>
/// <param name="IdempotencyKey">Client replay key.</param>
/// <param name="CorrelationId">Request correlation identifier.</param>
/// <param name="RequestLanguage">Interface language of the request, used when the profile declares none.</param>
public sealed record SelectCandidateCommand(Guid SessionId, string CandidateId, Guid? IdempotencyKey, string CorrelationId, string? RequestLanguage = null);

/// <summary>API-facing projection of one time-boxed cook-now candidate.</summary>
public sealed record RecipeCandidateView(
    string CandidateId,
    string CandidateStrategy,
    string Name,
    string TargetMealType,
    string DishFormat,
    string PrimaryTechnique,
    IReadOnlyList<string> PrimaryIngredientRefs,
    string Summary,
    int Servings,
    int ActiveMinutes,
    int PassiveMinutes,
    int TotalMinutes,
    string Difficulty,
    IReadOnlyList<string> RequiredEquipmentIds,
    IReadOnlyList<string> RequiredCapabilities);

/// <summary>API-facing projection of one recipe generation session.</summary>
public sealed record RecipeGenerationSessionView(Guid SessionId, string Status, IReadOnlyList<RecipeCandidateView>? Candidates, string? FailureReason);

/// <summary>API-facing detail projection of one owned recipe's current revision.</summary>
public sealed record RecipeDetailView(
    Guid RecipeId,
    int RevisionNumber,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    string NormalizedRecipeJson,
    string ThumbnailVisualJson,
    DateTimeOffset CreatedAt);

/// <summary>Describes a stable application error that an outer transport maps to Problem Details.</summary>
public sealed record RecipeApplicationProblem(string ErrorCode, string Detail, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Transport-neutral successful state of a recipe application operation.</summary>
public enum RecipeApplicationSuccess
{
    /// <summary>A read or mutation completed with a representation.</summary>
    Succeeded,
    /// <summary>A new recipe was created.</summary>
    Created
}

/// <summary>Typed transport-neutral outcome of a recipe application command or query.</summary>
public sealed record RecipeApplicationResult<T>(RecipeApplicationSuccess Success, T? Value, RecipeApplicationProblem? Problem)
{
    /// <summary>Creates a successful typed application result.</summary>
    public static RecipeApplicationResult<T> Succeeded(T? value, RecipeApplicationSuccess success = RecipeApplicationSuccess.Succeeded) => new(success, value, null);

    /// <summary>Creates an unsuccessful typed application result.</summary>
    public static RecipeApplicationResult<T> Failure(string errorCode, string detail, IReadOnlyDictionary<string, string[]>? errors = null) => new(RecipeApplicationSuccess.Succeeded, default, new RecipeApplicationProblem(errorCode, detail, errors));
}
