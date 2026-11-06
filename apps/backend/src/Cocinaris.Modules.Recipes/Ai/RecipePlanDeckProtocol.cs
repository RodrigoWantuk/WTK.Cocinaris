using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocinaris.Modules.Recipes.Ai;

/// <summary>
/// Closed structural representation of one <c>recipe.suggest_plan_deck.v1</c> response (PLAN-0062):
/// six dish cards for a shopping plan. Candidates have the same shape as
/// <c>recipe.suggest_candidates.v1</c>, so expansion and the idea library work on them unchanged.
/// This shape stays in lock-step with <c>packages/contracts/ai/recipe/schemas/recipe-plan-deck.response.v0.3.json</c>
/// and, for <c>recipe.suggest_plan_deck.v2</c>, with <c>recipe-plan-deck.response.v0.4.json</c>, which adds components.
/// </summary>
public sealed class PlanDeckResponse
{
    /// <summary>Candidates in one answer. Six keeps the wait near 20 s; later batches arrive in the background.</summary>
    public const int CandidateCount = 6;

    /// <summary>Strategies a plan card may use. They may repeat across cards.</summary>
    public static readonly IReadOnlySet<string> Strategies = new HashSet<string>(StringComparer.Ordinal) { "on_hand_first", "planned_purchase_reuse", "exploratory" };

    /// <summary>Most components in one answer.</summary>
    public const int MaxComponents = 2;

    /// <summary>Gets the operation discriminator: <c>recipe.suggest_plan_deck.v1</c> or <c>.v2</c>.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets the protocol schema version: <c>0.3</c> for v1 and <c>0.4</c> for v2.</summary>
    public required string SchemaVersion { get; init; }
    /// <summary>Gets the candidates.</summary>
    public required List<SuggestCandidate> Candidates { get; init; }
    /// <summary>Gets the reusable components, present only in v2. After validation only valid ones remain.</summary>
    public List<PlanDeckComponent>? Components { get; set; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Performs structural validation mirroring the closed JSON Schema.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        ProtocolCheck.RejectAdditionalProperties(errors, "$", Extra);
        var v2 = Operation == "recipe.suggest_plan_deck.v2";
        if (!v2)
        {
            ProtocolCheck.Const(errors, "$.operation", Operation, "recipe.suggest_plan_deck.v1");
        }

        ProtocolCheck.Const(errors, "$.schemaVersion", SchemaVersion, v2 ? "0.4" : "0.3");
        if (v2)
        {
            ProtocolCheck.ArrayLength(errors, "$.components", Components, 0, MaxComponents);
        }
        else if (Components is not null)
        {
            errors.Add("$.components is not allowed in recipe.suggest_plan_deck.v1");
        }
        ProtocolCheck.ArrayLength(errors, "$.candidates", Candidates, CandidateCount, CandidateCount);
        for (var index = 0; index < Candidates.Count; index++) { Candidates[index].Validate(errors, $"$.candidates[{index}]"); }
        return errors;
    }
}

/// <summary>What a component makes: the ingredient the dishes that use it no longer need to buy.</summary>
public sealed class PlanDeckComponentOutput
{
    /// <summary>Gets the name, which must equal an additional ingredient of every dish that uses it.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the quantity made.</summary>
    public required decimal Quantity { get; init; }
    /// <summary>Gets the canonical unit.</summary>
    public required string Unit { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A reusable base to prepare ahead, such as shredded chicken for two dishes.</summary>
public sealed class PlanDeckComponent
{
    /// <summary>Gets the base as a recipe candidate, so expansion works on it unchanged.</summary>
    public required SuggestCandidate Candidate { get; init; }
    /// <summary>Gets what it makes.</summary>
    public required PlanDeckComponentOutput Output { get; init; }
    /// <summary>Gets the candidate identifiers of the dishes that use it.</summary>
    public required List<string> UsedBy { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Checks the structure, which a bad component fails without failing the batch.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        ProtocolCheck.RejectAdditionalProperties(errors, "$", Extra);
        ProtocolCheck.RejectAdditionalProperties(errors, "$.output", Output.Extra);
        ProtocolCheck.StringLength(errors, "$.output.name", Output.Name, 1, 120);
        ProtocolCheck.QuantityValue(errors, "$.output.quantity", Output.Quantity);
        ProtocolCheck.EnumValue(errors, "$.output.unit", Output.Unit, ProtocolCheck.CanonicalUnits);
        ProtocolCheck.ArrayLength(errors, "$.usedBy", UsedBy, 1, PlanDeckResponse.CandidateCount);
        Candidate.Validate(errors, "$.candidate");
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
/// <param name="Pantry"><c>use</c> to stick to what is at home and chosen, <c>free</c> to ignore it, or null for the plan's default.</param>
/// <param name="Similarity"><c>similar</c> or <c>different</c> from <paramref name="ReferenceDish"/>, or null.</param>
/// <param name="ReferenceDish">The dish to compare with; untrusted.</param>
/// <param name="UsePreferences">Whether the profile's tastes are sent. Restrictions and dislikes always are.</param>
/// <param name="RequestedDish">A specific dish the person asked for, already cleaned; untrusted.</param>
/// <param name="NoFixedDays">The dishes will be eaten at some point in the period, on no fixed day.</param>
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
    string? AnchorIngredient,
    string? Pantry = null,
    string? Similarity = null,
    string? ReferenceDish = null,
    bool UsePreferences = true,
    string? RequestedDish = null,
    bool NoFixedDays = false);
