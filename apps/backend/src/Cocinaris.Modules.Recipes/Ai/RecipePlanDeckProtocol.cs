using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocinaris.Modules.Recipes.Ai;

/// <summary>
/// Closed structural representation of one <c>recipe.suggest_plan_deck.v1</c> response (PLAN-0062):
/// six dish cards for a shopping plan. Candidates have the same shape as
/// <c>recipe.suggest_candidates.v1</c>, so expansion and the idea library work on them unchanged.
/// This shape stays in lock-step with <c>packages/contracts/ai/recipe/schemas/recipe-plan-deck.response.v0.3.json</c>.
/// </summary>
public sealed class PlanDeckResponse
{
    /// <summary>Candidates in one answer. Six keeps the wait near 20 s; later batches arrive in the background.</summary>
    public const int CandidateCount = 6;

    /// <summary>Strategies a plan card may use. They may repeat across cards.</summary>
    public static readonly IReadOnlySet<string> Strategies = new HashSet<string>(StringComparer.Ordinal) { "on_hand_first", "planned_purchase_reuse", "exploratory" };

    /// <summary>Gets the operation discriminator, which must equal <c>recipe.suggest_plan_deck.v1</c>.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets the protocol schema version, which must equal <c>0.3</c>.</summary>
    public required string SchemaVersion { get; init; }
    /// <summary>Gets the candidates.</summary>
    public required List<SuggestCandidate> Candidates { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Performs structural validation mirroring the closed JSON Schema.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        ProtocolCheck.RejectAdditionalProperties(errors, "$", Extra);
        ProtocolCheck.Const(errors, "$.operation", Operation, "recipe.suggest_plan_deck.v1");
        ProtocolCheck.Const(errors, "$.schemaVersion", SchemaVersion, "0.3");
        ProtocolCheck.ArrayLength(errors, "$.candidates", Candidates, CandidateCount, CandidateCount);
        for (var index = 0; index < Candidates.Count; index++) { Candidates[index].Validate(errors, $"$.candidates[{index}]"); }
        return errors;
    }
}

/// <summary>What a shopping plan asks of one batch of cards. Every list is bounded when the envelope is built.</summary>
/// <param name="Days">Days the plan covers.</param>
/// <param name="CookingStyle"><c>once</c>, <c>mix</c>, or <c>daily</c>.</param>
/// <param name="Priority"><c>waste</c>, <c>short</c>, <c>save</c>, or <c>time</c>.</param>
/// <param name="MealNeeds">Meals still to fill per group: <c>main</c>, <c>breakfast</c>, <c>snack</c>.</param>
/// <param name="MealTypes">Meal types the cards may target.</param>
/// <param name="MaxActiveMinutes">Preparation time left in the session, when the plan has one.</param>
/// <param name="ChosenDishes">Dishes already accepted.</param>
/// <param name="ShoppingListIngredients">Ingredients already on the list.</param>
/// <param name="RejectedDishes">Dishes the person refused.</param>
/// <param name="AvoidIngredients">Ingredients the person tired of or disliked in this plan.</param>
/// <param name="AnchorIngredient">The person's typed starting ingredient, already cleaned; untrusted.</param>
public sealed record PlanDeckBrief(
    int Days,
    string CookingStyle,
    string Priority,
    IReadOnlyDictionary<string, int> MealNeeds,
    IReadOnlyList<string> MealTypes,
    int? MaxActiveMinutes,
    IReadOnlyList<string> ChosenDishes,
    IReadOnlyList<string> ShoppingListIngredients,
    IReadOnlyList<string> RejectedDishes,
    IReadOnlyList<string> AvoidIngredients,
    string? AnchorIngredient);
