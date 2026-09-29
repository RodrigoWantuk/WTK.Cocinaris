using System.Text.Json;
using System.Text.Json.Serialization;

namespace KitchenFlow.Modules.Recipes.Ai;

/// <summary>
/// Closed structural representation of one <c>recipe.expand_selected.v1</c> protocol 0.3 response.
/// This C# shape must stay in lock-step with
/// <c>packages/contracts/ai/recipe/schemas/recipe-expand-selected.response.v0.3.json</c>.
/// </summary>
public sealed class ExpandSelectedResponse
{
    /// <summary>Gets the operation discriminator, which must equal <c>recipe.expand_selected.v1</c>.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets the protocol schema version, which must equal <c>0.3</c>.</summary>
    public required string SchemaVersion { get; init; }
    /// <summary>Gets the expanded recipe artifact.</summary>
    public required ExpandRecipe Recipe { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Performs full structural validation mirroring the closed JSON Schema 2020-12 contract.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        ProtocolCheck.RejectAdditionalProperties(errors, "$", Extra);
        ProtocolCheck.Const(errors, "$.operation", Operation, "recipe.expand_selected.v1");
        ProtocolCheck.Const(errors, "$.schemaVersion", SchemaVersion, "0.3");
        Recipe.Validate(errors, "$.recipe");
        return errors;
    }
}

/// <summary>One immutable normalized recipe artifact.</summary>
public sealed class ExpandRecipe
{
    private static readonly HashSet<string> AllowedMealTypes = new(StringComparer.Ordinal) { "breakfast", "lunch", "dinner", "snack" };

    /// <summary>Gets the stable recipe identifier.</summary>
    public required string RecipeId { get; init; }
    /// <summary>Gets the revision number.</summary>
    public required int Revision { get; init; }
    /// <summary>Gets the recipe display name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the applicable meal types.</summary>
    public required List<string> MealTypes { get; init; }
    /// <summary>Gets the servings count.</summary>
    public required int Servings { get; init; }
    /// <summary>Gets the human-readable yield description.</summary>
    public required string Yield { get; init; }
    /// <summary>Gets the bounded ingredient list.</summary>
    public required List<ExpandIngredient> Ingredients { get; init; }
    /// <summary>Gets the bounded required-equipment list.</summary>
    public required List<ExpandEquipment> Equipment { get; init; }
    /// <summary>Gets the bounded advance-preparation task list.</summary>
    public required List<ExpandPreparation> Preparations { get; init; }
    /// <summary>Gets the bounded execution stage list.</summary>
    public required List<ExpandStage> Stages { get; init; }
    /// <summary>Gets the bounded stage dependency list.</summary>
    public required List<ExpandDependency> Dependencies { get; init; }
    /// <summary>Gets storage and reheating guidance.</summary>
    public required ExpandStorage Storage { get; init; }
    /// <summary>Gets bounded produced reusable component descriptions.</summary>
    public required List<string> ProducedComponents { get; init; }
    /// <summary>Gets bounded inventory reconciliation hints.</summary>
    public required List<string> ReconciliationHints { get; init; }
    /// <summary>Gets bounded pantry-staple assumptions used.</summary>
    public required List<string> Assumptions { get; init; }
    /// <summary>Gets the indexable thumbnail visual descriptor.</summary>
    public required ExpandThumbnailVisual ThumbnailVisual { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this recipe's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StableIdValue(errors, $"{path}.recipeId", RecipeId);
        ProtocolCheck.IntegerRange(errors, $"{path}.revision", Revision, 1, 100_000);
        ProtocolCheck.StringLength(errors, $"{path}.name", Name, 1, 80);
        ProtocolCheck.ArrayLength(errors, $"{path}.mealTypes", MealTypes, 1, 4);
        ProtocolCheck.ArrayOfEnums(errors, $"{path}.mealTypes", MealTypes, AllowedMealTypes);
        ProtocolCheck.IntegerRange(errors, $"{path}.servings", Servings, 1, 24);
        ProtocolCheck.StringLength(errors, $"{path}.yield", Yield, 1, 80);
        ProtocolCheck.ArrayLength(errors, $"{path}.ingredients", Ingredients, 1, 40);
        for (var index = 0; index < Ingredients.Count; index++) { Ingredients[index].Validate(errors, $"{path}.ingredients[{index}]"); }
        ProtocolCheck.ArrayLength(errors, $"{path}.equipment", Equipment, 0, 12);
        for (var index = 0; index < Equipment.Count; index++) { Equipment[index].Validate(errors, $"{path}.equipment[{index}]"); }
        ProtocolCheck.ArrayLength(errors, $"{path}.preparations", Preparations, 0, 8);
        for (var index = 0; index < Preparations.Count; index++) { Preparations[index].Validate(errors, $"{path}.preparations[{index}]"); }
        ProtocolCheck.ArrayLength(errors, $"{path}.stages", Stages, 1, 14);
        for (var index = 0; index < Stages.Count; index++) { Stages[index].Validate(errors, $"{path}.stages[{index}]"); }
        ProtocolCheck.ArrayLength(errors, $"{path}.dependencies", Dependencies, 0, 40);
        for (var index = 0; index < Dependencies.Count; index++) { Dependencies[index].Validate(errors, $"{path}.dependencies[{index}]"); }
        Storage.Validate(errors, $"{path}.storage");
        ProtocolCheck.ArrayLength(errors, $"{path}.producedComponents", ProducedComponents, 0, 8);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.producedComponents", ProducedComponents, 80);
        ProtocolCheck.ArrayLength(errors, $"{path}.reconciliationHints", ReconciliationHints, 0, 12);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.reconciliationHints", ReconciliationHints, 160);
        ProtocolCheck.ArrayLength(errors, $"{path}.assumptions", Assumptions, 0, 8);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.assumptions", Assumptions, 40);
        ThumbnailVisual.Validate(errors, $"{path}.thumbnailVisual");
    }
}

/// <summary>One ingredient line, sourced from inventory, an additional purchase, or a pantry assumption.</summary>
public sealed class ExpandIngredient
{
    private static readonly HashSet<string> SourceTypes = new(StringComparer.Ordinal) { "inventory", "additional", "assumption" };

    /// <summary>Gets the stable ingredient identifier.</summary>
    public required string IngredientId { get; init; }
    /// <summary>Gets the ingredient source classification.</summary>
    public required string SourceType { get; init; }
    /// <summary>Gets the display name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets the source inventory item identifier, present only when <see cref="SourceType"/> is <c>inventory</c>.</summary>
    public string? InventoryItemId { get; init; }
    /// <summary>Gets the required quantity.</summary>
    public required decimal RequiredQuantity { get; init; }
    /// <summary>Gets the canonical unit.</summary>
    public required string Unit { get; init; }
    /// <summary>Gets whether the ingredient is optional.</summary>
    public required bool Optional { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this ingredient's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StableIdValue(errors, $"{path}.ingredientId", IngredientId);
        ProtocolCheck.EnumValue(errors, $"{path}.sourceType", SourceType, SourceTypes);
        ProtocolCheck.StringLength(errors, $"{path}.displayName", DisplayName, 1, 80);
        if (InventoryItemId is not null) { ProtocolCheck.StableIdValue(errors, $"{path}.inventoryItemId", InventoryItemId); }
        ProtocolCheck.QuantityValue(errors, $"{path}.requiredQuantity", RequiredQuantity);
        ProtocolCheck.EnumValue(errors, $"{path}.unit", Unit, ProtocolCheck.CanonicalUnits);
    }
}

/// <summary>One piece of required equipment.</summary>
public sealed class ExpandEquipment
{
    /// <summary>Gets the stable equipment identifier.</summary>
    public required string EquipmentId { get; init; }
    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the bounded capability list.</summary>
    public required List<string> Capabilities { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this equipment reference's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StableIdValue(errors, $"{path}.equipmentId", EquipmentId);
        ProtocolCheck.StringLength(errors, $"{path}.name", Name, 1, 80);
        ProtocolCheck.ArrayLength(errors, $"{path}.capabilities", Capabilities, 0, 12);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.capabilities", Capabilities, 48);
    }
}

/// <summary>One advance-preparation task that may run before cooking starts.</summary>
public sealed class ExpandPreparation
{
    /// <summary>Gets the stable task identifier.</summary>
    public required string TaskId { get; init; }
    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the bounded instruction text.</summary>
    public required string Instructions { get; init; }
    /// <summary>Gets active minutes.</summary>
    public required int ActiveMinutes { get; init; }
    /// <summary>Gets passive minutes.</summary>
    public required int PassiveMinutes { get; init; }
    /// <summary>Gets the minimum lead minutes before cooking may start.</summary>
    public required int MinimumLeadMinutes { get; init; }
    /// <summary>Gets whether this task can run on a prior day.</summary>
    public required bool CanRunPreviousDay { get; init; }
    /// <summary>Gets the bounded list of components this task produces.</summary>
    public required List<string> Produces { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this preparation task's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StableIdValue(errors, $"{path}.taskId", TaskId);
        ProtocolCheck.StringLength(errors, $"{path}.name", Name, 1, 80);
        ProtocolCheck.StringLength(errors, $"{path}.instructions", Instructions, 1, 700);
        ProtocolCheck.IntegerRange(errors, $"{path}.activeMinutes", ActiveMinutes, 0, 480);
        ProtocolCheck.IntegerRange(errors, $"{path}.passiveMinutes", PassiveMinutes, 0, 2880);
        ProtocolCheck.IntegerRange(errors, $"{path}.minimumLeadMinutes", MinimumLeadMinutes, 0, 10_080);
        ProtocolCheck.ArrayLength(errors, $"{path}.produces", Produces, 0, 6);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.produces", Produces, 80);
    }
}

/// <summary>One execution stage of the cooking process.</summary>
public sealed class ExpandStage
{
    /// <summary>Gets the stable stage identifier.</summary>
    public required string StageId { get; init; }
    /// <summary>Gets the display name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the bounded instruction text.</summary>
    public required string Instructions { get; init; }
    /// <summary>Gets active minutes.</summary>
    public required int ActiveMinutes { get; init; }
    /// <summary>Gets passive minutes.</summary>
    public required int PassiveMinutes { get; init; }
    /// <summary>Gets the stable identifiers of stages that must precede this stage.</summary>
    public required List<string> DependsOn { get; init; }
    /// <summary>Gets bounded sensory doneness cues.</summary>
    public required List<string> SensoryCues { get; init; }
    /// <summary>Gets the stable ingredient identifiers used in this stage.</summary>
    public required List<string> UsesIngredientIds { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this stage's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StableIdValue(errors, $"{path}.stageId", StageId);
        ProtocolCheck.StringLength(errors, $"{path}.name", Name, 1, 80);
        ProtocolCheck.StringLength(errors, $"{path}.instructions", Instructions, 1, 700);
        ProtocolCheck.IntegerRange(errors, $"{path}.activeMinutes", ActiveMinutes, 0, 480);
        ProtocolCheck.IntegerRange(errors, $"{path}.passiveMinutes", PassiveMinutes, 0, 2880);
        ProtocolCheck.ArrayLength(errors, $"{path}.dependsOn", DependsOn, 0, 8);
        ProtocolCheck.ArrayOfStableIds(errors, $"{path}.dependsOn", DependsOn);
        ProtocolCheck.ArrayLength(errors, $"{path}.sensoryCues", SensoryCues, 0, 6);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.sensoryCues", SensoryCues, 120);
        ProtocolCheck.ArrayLength(errors, $"{path}.usesIngredientIds", UsesIngredientIds, 0, 20);
        ProtocolCheck.ArrayOfStableIds(errors, $"{path}.usesIngredientIds", UsesIngredientIds);
    }
}

/// <summary>One directional dependency between two stages.</summary>
public sealed class ExpandDependency
{
    private static readonly HashSet<string> Relationships = new(StringComparer.Ordinal) { "before", "after", "requires" };

    /// <summary>Gets the source stage identifier.</summary>
    public required string FromId { get; init; }
    /// <summary>Gets the target stage identifier.</summary>
    public required string ToId { get; init; }
    /// <summary>Gets the relationship classification.</summary>
    public required string Relationship { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this dependency's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.StableIdValue(errors, $"{path}.fromId", FromId);
        ProtocolCheck.StableIdValue(errors, $"{path}.toId", ToId);
        ProtocolCheck.EnumValue(errors, $"{path}.relationship", Relationship, Relationships);
    }
}

/// <summary>Storage and reheating guidance for the completed dish.</summary>
public sealed class ExpandStorage
{
    /// <summary>Gets the refrigeration window in hours.</summary>
    public required int RefrigerateHours { get; init; }
    /// <summary>Gets whether the dish freezes well.</summary>
    public required bool FreezeCompatible { get; init; }
    /// <summary>Gets bounded reheating guidance.</summary>
    public required string ReheatNotes { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this storage guidance's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.IntegerRange(errors, $"{path}.refrigerateHours", RefrigerateHours, 0, 336);
        ProtocolCheck.StringLength(errors, $"{path}.reheatNotes", ReheatNotes, 1, 240);
    }
}

/// <summary>
/// Indexable visual descriptor of the completed dish used for later thumbnail generation
/// (PLAN-0030). It never carries private context, cache hashes, or provider/model policy.
/// </summary>
public sealed class ExpandThumbnailVisual
{
    /// <summary>Gets the thumbnail descriptor schema version, which must equal <c>1</c>.</summary>
    public required string SchemaVersion { get; init; }
    /// <summary>Gets the bounded overall appearance description.</summary>
    public required string AppearanceDescription { get; init; }
    /// <summary>Gets the bounded list of visible plated components.</summary>
    public required List<string> VisibleComponents { get; init; }
    /// <summary>Gets the dish-format classification.</summary>
    public required string DishFormat { get; init; }
    /// <summary>Gets the bounded plating description.</summary>
    public required string Plating { get; init; }
    /// <summary>Gets the bounded sauce appearance description.</summary>
    public required string SauceAppearance { get; init; }
    /// <summary>Gets bounded texture and doneness cues.</summary>
    public required List<string> TextureAndDoneness { get; init; }
    /// <summary>Gets bounded garnish descriptions.</summary>
    public required List<string> Garnish { get; init; }
    /// <summary>Gets bounded dominant color descriptions.</summary>
    public required List<string> DominantColors { get; init; }
    /// <summary>Gets bounded elements that must never appear in a rendered thumbnail.</summary>
    public required List<string> ExcludedElements { get; init; }
    /// <summary>Gets unexpected properties captured to enforce the closed schema.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Validates this thumbnail visual descriptor's structural bounds.</summary>
    public void Validate(List<string> errors, string path)
    {
        ProtocolCheck.RejectAdditionalProperties(errors, path, Extra);
        ProtocolCheck.Const(errors, $"{path}.schemaVersion", SchemaVersion, "1");
        ProtocolCheck.StringLength(errors, $"{path}.appearanceDescription", AppearanceDescription, 1, 280);
        ProtocolCheck.ArrayLength(errors, $"{path}.visibleComponents", VisibleComponents, 1, 12);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.visibleComponents", VisibleComponents, 64);
        ProtocolCheck.StringLength(errors, $"{path}.dishFormat", DishFormat, 1, 64);
        ProtocolCheck.StringLength(errors, $"{path}.plating", Plating, 1, 80);
        ProtocolCheck.StringLength(errors, $"{path}.sauceAppearance", SauceAppearance, 1, 120);
        ProtocolCheck.ArrayLength(errors, $"{path}.textureAndDoneness", TextureAndDoneness, 0, 8);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.textureAndDoneness", TextureAndDoneness, 80);
        ProtocolCheck.ArrayLength(errors, $"{path}.garnish", Garnish, 0, 6);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.garnish", Garnish, 64);
        ProtocolCheck.ArrayLength(errors, $"{path}.dominantColors", DominantColors, 1, 8);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.dominantColors", DominantColors, 32);
        ProtocolCheck.ArrayLength(errors, $"{path}.excludedElements", ExcludedElements, 0, 12);
        ProtocolCheck.ArrayOfStrings(errors, $"{path}.excludedElements", ExcludedElements, 48);
    }
}
