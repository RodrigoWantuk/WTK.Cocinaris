using System.Text.Json;
using System.Text.Json.Serialization;

namespace KitchenFlow.Modules.Recipes.Ai;

/// <summary>
/// Closed structural representation of one <c>recipe.suggest_candidates.v1</c> protocol 0.3 response.
/// Required members throw <see cref="JsonException"/> when missing during deserialization; captured
/// <c>JsonExtensionData</c> on every object level enforces the schema's <c>additionalProperties: false</c>.
/// This C# shape must stay in lock-step with <c>packages/contracts/ai/recipe/schemas/recipe-suggest-candidates.response.v0.3.json</c>.
/// </summary>
public sealed class SuggestCandidatesResponse
{
    /// <summary>Gets the operation discriminator, which must equal <c>recipe.suggest_candidates.v1</c>.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets the protocol schema version, which must equal <c>0.3</c>.</summary>
    public required string SchemaVersion { get; init; }
    /// <summary>Gets the exactly-three candidate list.</summary>
    public required List<SuggestCandidate> Candidates { get; init; }
    /// <summary>Gets the bounded clarification list.</summary>
    public required List<SuggestClarification> Clarifications { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Performs full structural validation mirroring the closed JSON Schema 2020-12 contract.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        ProtocolCheck.RejectAdditionalProperties(errors, "$", Extra);
        ProtocolCheck.Const(errors, "$.operation", Operation, "recipe.suggest_candidates.v1");
        ProtocolCheck.Const(errors, "$.schemaVersion", SchemaVersion, "0.3");
        ProtocolCheck.ArrayLength(errors, "$.candidates", Candidates, 3, 3);
        ProtocolCheck.ArrayLength(errors, "$.clarifications", Clarifications, 0, 6);
        for (var index = 0; index < Candidates.Count; index++) { Candidates[index].Validate(errors, $"$.candidates[{index}]"); }
        for (var index = 0; index < Clarifications.Count; index++) { Clarifications[index].Validate(errors, $"$.clarifications[{index}]"); }
        return errors;
    }
}

/// <summary>One protocol 0.3 compact recipe candidate.</summary>
public sealed class SuggestCandidate
{
    private static readonly HashSet<string> Strategies = new(StringComparer.Ordinal) { "on_hand_first", "on_hand_flexible", "planned_purchase_reuse", "exploratory" };
    private static readonly HashSet<string> MealTypes = new(StringComparer.Ordinal) { "breakfast", "lunch", "dinner", "snack" };
    private static readonly HashSet<string> Difficulties = new(StringComparer.Ordinal) { "easy", "medium", "hard" };

    /// <summary>Gets the stable candidate identifier.</summary>
    public required string CandidateId { get; init; }
    /// <summary>Gets the candidate generation strategy.</summary>
    public required string CandidateStrategy { get; init; }
    /// <summary>Gets the human-readable candidate name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the target meal type.</summary>
    public required string TargetMealType { get; init; }
    /// <summary>Gets the dish-format classification.</summary>
    public required string DishFormat { get; init; }
    /// <summary>Gets the primary cooking technique.</summary>
    public required string PrimaryTechnique { get; init; }
    /// <summary>Gets the bounded list of primary ingredient references.</summary>
    public required List<string> PrimaryIngredientRefs { get; init; }
    /// <summary>Gets the bounded human-readable summary.</summary>
    public required string Summary { get; init; }
    /// <summary>Gets the servings count.</summary>
    public required int Servings { get; init; }
    /// <summary>Gets the active/passive/total time profile.</summary>
    public required SuggestTimeProfile Time { get; init; }
    /// <summary>Gets the difficulty classification.</summary>
    public required string Difficulty { get; init; }
    /// <summary>Gets the bounded list of required equipment identifiers.</summary>
    public required List<string> RequiredEquipmentIds { get; init; }
    /// <summary>Gets the bounded list of required equipment capabilities.</summary>
    public required List<string> RequiredCapabilities { get; init; }
    /// <summary>Gets the bounded list of on-hand inventory uses.</summary>
    public required List<SuggestInventoryUse> InventoryUses { get; init; }
    /// <summary>Gets the bounded list of additional (non-inventory) ingredients.</summary>
    public required List<SuggestAdditionalIngredient> AdditionalIngredients { get; init; }
    /// <summary>Gets the advance-preparation profile.</summary>
    public required SuggestPreparationProfile PreparationProfile { get; init; }
    /// <summary>Gets the bounded list of pantry-staple assumptions used.</summary>
    public required List<string> AssumptionsUsed { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this candidate's structural bounds against the closed schema.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StableIdValue(errors, $"{path}.candidateId", CandidateId);
        ProtocolCheck.EnumValue(errors, $"{path}.candidateStrategy", CandidateStrategy, Strategies);
        ProtocolCheck.StringLength(errors, $"{path}.name", Name, 1, 80);
        ProtocolCheck.EnumValue(errors, $"{path}.targetMealType", TargetMealType, MealTypes);
        ProtocolCheck.StringLength(errors, $"{path}.dishFormat", DishFormat, 1, 64);
        ProtocolCheck.StringLength(errors, $"{path}.primaryTechnique", PrimaryTechnique, 1, 64);
        ProtocolCheck.ArrayLength(errors, $"{path}.primaryIngredientRefs", PrimaryIngredientRefs, 1, 6);
        ProtocolCheck.ArrayOfStableIds(errors, $"{path}.primaryIngredientRefs", PrimaryIngredientRefs);
        ProtocolCheck.StringLength(errors, $"{path}.summary", Summary, 1, 160);
        ProtocolCheck.IntegerRange(errors, $"{path}.servings", Servings, 1, 24);
        Time.Validate(errors, $"{path}.time");
        ProtocolCheck.EnumValue(errors, $"{path}.difficulty", Difficulty, Difficulties);
        ProtocolCheck.ArrayLength(errors, $"{path}.requiredEquipmentIds", RequiredEquipmentIds, 0, 12);
        ProtocolCheck.ArrayOfStableIds(errors, $"{path}.requiredEquipmentIds", RequiredEquipmentIds);
        ProtocolCheck.ArrayLength(errors, $"{path}.requiredCapabilities", RequiredCapabilities, 0, 16);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.requiredCapabilities", RequiredCapabilities, 48);
        ProtocolCheck.ArrayLength(errors, $"{path}.inventoryUses", InventoryUses, 0, 10);
        for (var index = 0; index < InventoryUses.Count; index++) { InventoryUses[index].Validate(errors, $"{path}.inventoryUses[{index}]"); }
        ProtocolCheck.ArrayLength(errors, $"{path}.additionalIngredients", AdditionalIngredients, 0, 5);
        for (var index = 0; index < AdditionalIngredients.Count; index++) { AdditionalIngredients[index].Validate(errors, $"{path}.additionalIngredients[{index}]"); }
        PreparationProfile.Validate(errors, $"{path}.preparationProfile");
        ProtocolCheck.ArrayLength(errors, $"{path}.assumptionsUsed", AssumptionsUsed, 0, 8);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.assumptionsUsed", AssumptionsUsed, 40);
    }
}

/// <summary>Active/passive/total minute breakdown.</summary>
public sealed class SuggestTimeProfile
{
    /// <summary>Gets active minutes requiring hands-on attention.</summary>
    public required int ActiveMinutes { get; init; }
    /// <summary>Gets passive (unattended) minutes.</summary>
    public required int PassiveMinutes { get; init; }
    /// <summary>Gets the total elapsed minutes.</summary>
    public required int TotalMinutes { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates the time profile bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.IntegerRange(errors, $"{path}.activeMinutes", ActiveMinutes, 0, 480);
        ProtocolCheck.IntegerRange(errors, $"{path}.passiveMinutes", PassiveMinutes, 0, 2880);
        ProtocolCheck.IntegerRange(errors, $"{path}.totalMinutes", TotalMinutes, 0, 2880);
    }
}

/// <summary>One declared use of an on-hand inventory item.</summary>
public sealed class SuggestInventoryUse
{
    private static readonly HashSet<string> AvailabilitySources = new(StringComparer.Ordinal) { "on_hand", "planned_purchase", "prepared_component" };
    private static readonly HashSet<string> IngredientStates = new(StringComparer.Ordinal) { "raw", "cooked", "frozen", "thawed", "soaked", "chopped", "prepared_component", "unknown" };

    /// <summary>Gets the stable ingredient reference used within the candidate.</summary>
    public required string IngredientRef { get; init; }
    /// <summary>Gets the stable inventory item identifier from the request snapshot.</summary>
    public required string InventoryItemId { get; init; }
    /// <summary>Gets the preserved user-facing inventory item name.</summary>
    public required string UserName { get; init; }
    /// <summary>Gets the required quantity.</summary>
    public required decimal RequiredQuantity { get; init; }
    /// <summary>Gets the canonical unit.</summary>
    public required string Unit { get; init; }
    /// <summary>Gets the preserved availability source from the request snapshot.</summary>
    public required string AvailabilitySource { get; init; }
    /// <summary>Gets the required ingredient state.</summary>
    public required string RequiredState { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this inventory use's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StableIdValue(errors, $"{path}.ingredientRef", IngredientRef);
        ProtocolCheck.StableIdValue(errors, $"{path}.inventoryItemId", InventoryItemId);
        ProtocolCheck.StringLength(errors, $"{path}.userName", UserName, 1, 120);
        ProtocolCheck.QuantityValue(errors, $"{path}.requiredQuantity", RequiredQuantity);
        ProtocolCheck.EnumValue(errors, $"{path}.unit", Unit, ProtocolCheck.CanonicalUnits);
        ProtocolCheck.EnumValue(errors, $"{path}.availabilitySource", AvailabilitySource, AvailabilitySources);
        ProtocolCheck.EnumValue(errors, $"{path}.requiredState", RequiredState, IngredientStates);
    }
}

/// <summary>One additional ingredient not sourced from inventory.</summary>
public sealed class SuggestAdditionalIngredient
{
    /// <summary>Gets the ingredient display name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the required quantity.</summary>
    public required decimal RequiredQuantity { get; init; }
    /// <summary>Gets the canonical unit.</summary>
    public required string Unit { get; init; }
    /// <summary>Gets whether the ingredient is optional.</summary>
    public required bool Optional { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this additional ingredient's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StringLength(errors, $"{path}.name", Name, 1, 120);
        ProtocolCheck.QuantityValue(errors, $"{path}.requiredQuantity", RequiredQuantity);
        ProtocolCheck.EnumValue(errors, $"{path}.unit", Unit, ProtocolCheck.CanonicalUnits);
    }
}

/// <summary>Advance-preparation profile for a candidate.</summary>
public sealed class SuggestPreparationProfile
{
    private static readonly HashSet<string> BlockingCodes = new(StringComparer.Ordinal) { "thaw", "soak", "marinate", "ferment", "cool", "proof", "rest", "other" };

    /// <summary>Gets whether the candidate requires advance preparation.</summary>
    public required bool RequiresAdvancePreparation { get; init; }
    /// <summary>Gets the minimum lead minutes before cooking may start.</summary>
    public required int MinimumLeadMinutes { get; init; }
    /// <summary>Gets the bounded blocking preparation codes.</summary>
    public required List<string> BlockingPreparationCodes { get; init; }
    /// <summary>Gets whether the candidate may produce reusable prepared components.</summary>
    public required bool MayProduceReusableComponents { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this preparation profile's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.IntegerRange(errors, $"{path}.minimumLeadMinutes", MinimumLeadMinutes, 0, 10_080);
        ProtocolCheck.ArrayLength(errors, $"{path}.blockingPreparationCodes", BlockingPreparationCodes, 0, 8);
        ProtocolCheck.ArrayOfEnums(errors, $"{path}.blockingPreparationCodes", BlockingPreparationCodes, BlockingCodes);
    }
}

/// <summary>One clarifying question the model chose not to guess an answer for.</summary>
public sealed class SuggestClarification
{
    /// <summary>Gets the stable clarification code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets the human-readable clarifying question.</summary>
    public required string Question { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this clarification's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StringLength(errors, $"{path}.code", Code, 1, 64);
        ProtocolCheck.StringLength(errors, $"{path}.question", Question, 1, 160);
    }
}
