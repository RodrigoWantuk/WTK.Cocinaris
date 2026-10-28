namespace KitchenFlow.Modules.Recipes.Application;

/// <summary>Transport-neutral input for the <c>request candidates</c> cook-now command.</summary>
public sealed record RequestCandidatesCommand(Guid? IdempotencyKey, string CorrelationId);

/// <summary>Transport-neutral input for the <c>select candidate</c> cook-now command.</summary>
public sealed record SelectCandidateCommand(Guid SessionId, string CandidateId, Guid? IdempotencyKey, string CorrelationId);

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

/// <summary>API-facing summary projection of one owned recipe.</summary>
public sealed record RecipeSummaryView(Guid RecipeId, string Name, IReadOnlyList<string> MealTypes, int Servings, DateTimeOffset CreatedAt);

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
