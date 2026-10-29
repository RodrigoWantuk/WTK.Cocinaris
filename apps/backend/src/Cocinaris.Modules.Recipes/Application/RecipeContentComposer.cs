using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>One ingredient row the owner wrote or edited.</summary>
/// <param name="IngredientId">Identifier of the ingredient in the base revision; <see langword="null"/> for a new row.</param>
/// <param name="DisplayName">Ingredient name as the owner wants it shown.</param>
/// <param name="Quantity">Quantity for the recipe's servings, in <paramref name="Unit"/>.</param>
/// <param name="Unit">Canonical recipe unit: <c>g</c>, <c>ml</c>, or <c>unit</c>.</param>
/// <param name="Optional">Whether the dish works without it.</param>
public sealed record RecipeDraftIngredient(string? IngredientId, string DisplayName, decimal Quantity, string Unit, bool Optional);

/// <summary>One stage the owner wrote or edited.</summary>
/// <param name="StageId">Identifier of the stage in the base revision; <see langword="null"/> for a new stage.</param>
/// <param name="Name">Short stage title.</param>
/// <param name="Instructions">Stage instructions; line breaks separate steps.</param>
/// <param name="ActiveMinutes">Hands-on minutes.</param>
/// <param name="PassiveMinutes">Waiting minutes.</param>
/// <param name="SensoryCues">Signs that the stage is done.</param>
public sealed record RecipeDraftStage(string? StageId, string Name, string Instructions, int ActiveMinutes, int PassiveMinutes, IReadOnlyList<string>? SensoryCues);

/// <summary>Editable recipe content: everything the editor shows, in display order.</summary>
public sealed record RecipeContentDraft(
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    string? Yield,
    IReadOnlyList<RecipeDraftIngredient> Ingredients,
    IReadOnlyList<string>? Equipment,
    IReadOnlyList<RecipeDraftStage> Stages);

/// <summary>Result of composing a draft: normalized JSON on success, field errors otherwise.</summary>
public sealed record RecipeComposition(string? NormalizedRecipeJson, IReadOnlyDictionary<string, string[]> Errors)
{
    /// <summary>Gets whether the draft was valid.</summary>
    public bool IsValid => NormalizedRecipeJson is not null;
}

/// <summary>
/// Turns owner-written recipe content into revision JSON in the protocol 0.3 shape that the
/// recipe page and the cooking proposal already read. Deterministic code owns this; no model is
/// involved.
/// </summary>
/// <remarks>
/// When a base revision exists, fields the editor does not show are kept (preparations, storage,
/// assumptions, the thumbnail descriptor). Ingredients matched by <c>ingredientId</c> keep their
/// pantry link (<c>sourceType</c> and <c>inventoryItemId</c>), so a revised recipe still proposes
/// the same lot when cooked. New ingredients are <c>additional</c>. References to removed stages
/// or ingredients are dropped from <c>dependsOn</c>, <c>usesIngredientIds</c>, and dependencies.
/// </remarks>
public static class RecipeContentComposer
{
    /// <summary>Maximum ingredients per recipe.</summary>
    public const int MaxIngredients = 40;
    /// <summary>Maximum stages per recipe.</summary>
    public const int MaxStages = 30;
    /// <summary>Maximum instruction characters per stage. Generated stages stay within 700; owners may write more.</summary>
    public const int MaxInstructionsLength = 2000;

    private const int MaxNameLength = 80;
    private const int MaxYieldLength = 80;
    private const int MaxEquipment = 20;
    private const int MaxEquipmentNameLength = 60;
    private const int MaxCues = 6;
    private const int MaxCueLength = 120;
    private const int MaxMinutes = 1440;
    private const decimal MaxQuantity = 100_000m;
    private static readonly HashSet<string> MealTypes = new(StringComparer.Ordinal) { "breakfast", "lunch", "dinner", "snack" };
    private static readonly HashSet<string> Units = new(StringComparer.Ordinal) { "g", "ml", "unit" };
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>Validates the draft and composes the revision JSON.</summary>
    /// <param name="baseJson">Current revision content when editing or deriving; <see langword="null"/> for a new manual recipe.</param>
    /// <param name="draft">Owner-written content.</param>
    /// <param name="recipeReference">Value written to the content's <c>recipeId</c> for a new manual recipe.</param>
    /// <param name="revisionNumber">Revision number the content belongs to.</param>
    public static RecipeComposition Compose(string? baseJson, RecipeContentDraft draft, string recipeReference, int revisionNumber)
    {
        var errors = Validate(draft);
        if (errors.Count > 0)
        {
            return new RecipeComposition(null, errors.ToDictionary(item => item.Key, item => item.Value.ToArray()));
        }

        var root = ParseBase(baseJson) ?? NewSkeleton(recipeReference);
        var baseIngredients = Index(root["ingredients"], "ingredientId");
        var baseStages = Index(root["stages"], "stageId");
        var baseEquipment = (root["equipment"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];

        var ingredientIds = new List<string>();
        var ingredients = new JsonArray();
        foreach (var item in draft.Ingredients)
        {
            // A repeated identifier in the draft is a copied row: it becomes a new ingredient.
            var matched = item.IngredientId is not null && !ingredientIds.Contains(item.IngredientId) && baseIngredients.TryGetValue(item.IngredientId, out var found) ? found : null;
            var id = matched is not null ? item.IngredientId! : NextId("ing", ingredientIds, baseIngredients.Keys);
            ingredientIds.Add(id);
            var row = new JsonObject
            {
                ["ingredientId"] = id,
                ["sourceType"] = matched?["sourceType"]?.GetValue<string>() ?? "additional",
                ["displayName"] = item.DisplayName.Trim()
            };
            if (matched?["inventoryItemId"] is JsonValue lot)
            {
                row["inventoryItemId"] = lot.GetValue<string>();
            }

            row["requiredQuantity"] = item.Quantity;
            row["unit"] = item.Unit;
            row["optional"] = item.Optional;
            ingredients.Add(row);
        }

        var stageIds = new List<string>();
        var stages = new JsonArray();
        foreach (var item in draft.Stages)
        {
            var matched = item.StageId is not null && !stageIds.Contains(item.StageId) && baseStages.TryGetValue(item.StageId, out var found) ? found : null;
            var id = matched is not null ? item.StageId! : NextId("stage", stageIds, baseStages.Keys);
            // A new stage follows the previous one; a kept stage keeps the dependencies that still exist.
            var dependsOn = matched is not null
                ? Strings(matched["dependsOn"]).Where(stageIds.Contains).ToList()
                : stageIds.Count > 0 ? [stageIds[^1]] : [];
            var uses = matched is not null ? Strings(matched["usesIngredientIds"]).Where(ingredientIds.Contains).ToList() : [];
            stageIds.Add(id);
            stages.Add(new JsonObject
            {
                ["stageId"] = id,
                ["name"] = item.Name.Trim(),
                ["instructions"] = item.Instructions.Trim(),
                ["activeMinutes"] = item.ActiveMinutes,
                ["passiveMinutes"] = item.PassiveMinutes,
                ["dependsOn"] = ToArray(dependsOn),
                ["sensoryCues"] = ToArray((item.SensoryCues ?? []).Select(cue => cue.Trim()).Where(cue => cue.Length > 0)),
                ["usesIngredientIds"] = ToArray(uses)
            });
        }

        var equipment = new JsonArray();
        var equipmentIds = new List<string>();
        foreach (var name in (draft.Equipment ?? []).Select(item => item.Trim()).Where(item => item.Length > 0))
        {
            var matched = baseEquipment.FirstOrDefault(item => string.Equals(item["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));
            var id = matched?["equipmentId"]?.GetValue<string>() is { } existing && !equipmentIds.Contains(existing)
                ? existing
                : NextId("eq", equipmentIds, baseEquipment.Select(item => item["equipmentId"]?.GetValue<string>() ?? string.Empty));
            equipmentIds.Add(id);
            equipment.Add(new JsonObject { ["equipmentId"] = id, ["name"] = name, ["capabilities"] = matched?["capabilities"]?.DeepClone() ?? new JsonArray() });
        }

        if (root["dependencies"] is JsonArray dependencies)
        {
            var removedStages = baseStages.Keys.Where(id => !stageIds.Contains(id)).ToHashSet(StringComparer.Ordinal);
            root["dependencies"] = ToArray(dependencies.OfType<JsonObject>()
                .Where(item => !removedStages.Contains(item["fromId"]?.GetValue<string>() ?? string.Empty)
                    && !removedStages.Contains(item["toId"]?.GetValue<string>() ?? string.Empty))
                .Select(item => item.DeepClone()));
        }

        root["revision"] = revisionNumber;
        root["name"] = draft.Name.Trim();
        root["mealTypes"] = ToArray(draft.MealTypes.Distinct(StringComparer.Ordinal));
        root["servings"] = draft.Servings;
        root["yield"] = string.IsNullOrWhiteSpace(draft.Yield) ? string.Empty : draft.Yield.Trim();
        root["ingredients"] = ingredients;
        root["equipment"] = equipment;
        root["stages"] = stages;
        return new RecipeComposition(root.ToJsonString(SerializerOptions), new Dictionary<string, string[]>());
    }

    private static Dictionary<string, List<string>> Validate(RecipeContentDraft draft)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > MaxNameLength)
        {
            Add("name", "The name is required and must be at most 80 characters.");
        }

        if (draft.MealTypes is null || draft.MealTypes.Count is < 1 or > 4 || draft.MealTypes.Any(item => !MealTypes.Contains(item)))
        {
            Add("mealTypes", "Choose between one and four of breakfast, lunch, dinner, and snack.");
        }

        if (draft.Servings is < 1 or > 24)
        {
            Add("servings", "Servings must be between 1 and 24.");
        }

        if (draft.Yield is { } yieldText && yieldText.Trim().Length > MaxYieldLength)
        {
            Add("yield", "The yield must be at most 80 characters.");
        }

        if (draft.Ingredients is null || draft.Ingredients.Count is < 1 or > MaxIngredients)
        {
            Add("ingredients", "A recipe needs between 1 and 40 ingredients.");
        }
        else
        {
            for (var index = 0; index < draft.Ingredients.Count; index++)
            {
                var item = draft.Ingredients[index];
                if (string.IsNullOrWhiteSpace(item.DisplayName) || item.DisplayName.Trim().Length > MaxNameLength)
                {
                    Add($"ingredients[{index}].displayName", "The ingredient name is required and must be at most 80 characters.");
                }

                if (item.Quantity <= 0 || item.Quantity > MaxQuantity)
                {
                    Add($"ingredients[{index}].quantity", "The quantity must be greater than zero and at most 100000.");
                }

                if (!Units.Contains(item.Unit ?? string.Empty))
                {
                    Add($"ingredients[{index}].unit", "The unit must be g, ml, or unit.");
                }
            }
        }

        if (draft.Stages is null || draft.Stages.Count is < 1 or > MaxStages)
        {
            Add("stages", "A recipe needs between 1 and 30 stages.");
        }
        else
        {
            for (var index = 0; index < draft.Stages.Count; index++)
            {
                var item = draft.Stages[index];
                if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length > MaxNameLength)
                {
                    Add($"stages[{index}].name", "The stage title is required and must be at most 80 characters.");
                }

                if (string.IsNullOrWhiteSpace(item.Instructions) || item.Instructions.Trim().Length > MaxInstructionsLength)
                {
                    Add($"stages[{index}].instructions", "The instructions are required and must be at most 2000 characters.");
                }

                if (item.ActiveMinutes is < 0 or > MaxMinutes || item.PassiveMinutes is < 0 or > MaxMinutes)
                {
                    Add($"stages[{index}].minutes", "Minutes must be between 0 and 1440.");
                }

                var cues = item.SensoryCues ?? [];
                if (cues.Count > MaxCues || cues.Any(cue => cue is null || cue.Trim().Length > MaxCueLength))
                {
                    Add($"stages[{index}].sensoryCues", "Up to 6 signs of doneness, each at most 120 characters.");
                }
            }
        }

        var equipment = draft.Equipment ?? [];
        if (equipment.Count > MaxEquipment || equipment.Any(item => item is null || item.Trim().Length > MaxEquipmentNameLength))
        {
            Add("equipment", "Up to 20 utensils, each at most 60 characters.");
        }

        return errors;
    }

    private static JsonObject? ParseBase(string? baseJson)
    {
        if (string.IsNullOrWhiteSpace(baseJson) || JsonNode.Parse(baseJson) is not JsonObject root)
        {
            return null;
        }

        // Older rows wrap the content in a `recipe` object; revisions are always written flat.
        return root["recipe"] is JsonObject wrapped ? (JsonObject)wrapped.DeepClone() : root;
    }

    private static JsonObject NewSkeleton(string recipeReference) => new()
    {
        ["recipeId"] = recipeReference,
        ["revision"] = 1,
        ["name"] = string.Empty,
        ["mealTypes"] = new JsonArray(),
        ["servings"] = 1,
        ["yield"] = string.Empty,
        ["ingredients"] = new JsonArray(),
        ["equipment"] = new JsonArray(),
        ["preparations"] = new JsonArray(),
        ["stages"] = new JsonArray(),
        ["dependencies"] = new JsonArray(),
        ["producedComponents"] = new JsonArray(),
        ["reconciliationHints"] = new JsonArray(),
        ["assumptions"] = new JsonArray()
    };

    private static Dictionary<string, JsonObject> Index(JsonNode? array, string key)
    {
        var index = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in (array as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            if (item[key] is JsonValue value && value.TryGetValue<string>(out var id) && !string.IsNullOrEmpty(id))
            {
                index.TryAdd(id, item);
            }
        }

        return index;
    }

    private static IEnumerable<string> Strings(JsonNode? node) =>
        (node as JsonArray)?.OfType<JsonValue>().Select(item => item.TryGetValue<string>(out var text) ? text : null).OfType<string>() ?? [];

    private static JsonArray ToArray(IEnumerable<string> values) => new(values.Select(item => (JsonNode)JsonValue.Create(item)!).ToArray());

    private static JsonArray ToArray(IEnumerable<JsonNode> values) => new(values.ToArray());

    private static string NextId(string prefix, IReadOnlyCollection<string> used, IEnumerable<string> reserved)
    {
        var taken = used.Concat(reserved).ToHashSet(StringComparer.Ordinal);
        for (var number = 1; ; number++)
        {
            var id = prefix + "-" + number.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(id))
            {
                return id;
            }
        }
    }
}
