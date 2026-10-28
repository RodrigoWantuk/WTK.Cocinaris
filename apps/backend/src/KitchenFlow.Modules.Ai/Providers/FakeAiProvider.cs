using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using KitchenFlow.Modules.Ai.Abstractions;

namespace KitchenFlow.Modules.Ai.Providers;

/// <summary>
/// Deterministic in-memory <see cref="IAiProvider"/> for automated tests and the Testing
/// environment. Production code must never register this provider when a live DeepSeek key is
/// configured. Each call for a given operation is served from an ordered per-operation script
/// queue when scripts are present; otherwise a protocol 0.3-valid default response is synthesized
/// from the request payload so suggest and expand succeed without a live provider.
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<AiProviderInvocationRequest, AiProviderInvocationResult>>> _scripts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _invocationCounts = new(StringComparer.Ordinal);
    private readonly bool _useProtocolDefaultsWhenUnscripted;

    /// <summary>Creates a fake provider that returns protocol-valid defaults when no script is queued.</summary>
    public FakeAiProvider() : this(useProtocolDefaultsWhenUnscripted: true)
    {
    }

    /// <summary>
    /// Creates a fake provider with explicit default-response behavior.
    /// </summary>
    /// <param name="useProtocolDefaultsWhenUnscripted">
    /// When <see langword="true"/>, unscripted invocations synthesize valid protocol 0.3 JSON from
    /// the request. When <see langword="false"/>, unscripted invocations fail as unavailable so
    /// repair-loop tests can assert failure after scripted invalid attempts.
    /// </param>
    public FakeAiProvider(bool useProtocolDefaultsWhenUnscripted)
    {
        _useProtocolDefaultsWhenUnscripted = useProtocolDefaultsWhenUnscripted;
    }

    /// <inheritdoc />
    public string Name => "fake";

    /// <summary>Enqueues one scripted response for the next matching invocation of <paramref name="operation"/>.</summary>
    public void Enqueue(string operation, AiProviderInvocationResult result) => Enqueue(operation, _ => result);

    /// <summary>Enqueues one scripted response factory for the next matching invocation of <paramref name="operation"/>.</summary>
    public void Enqueue(string operation, Func<AiProviderInvocationRequest, AiProviderInvocationResult> factory) =>
        _scripts.GetOrAdd(operation, _ => new ConcurrentQueue<Func<AiProviderInvocationRequest, AiProviderInvocationResult>>()).Enqueue(factory);

    /// <summary>Gets the number of times <see cref="InvokeAsync"/> was called for one operation.</summary>
    public int InvocationCount(string operation) => _invocationCounts.GetValueOrDefault(operation);

    /// <inheritdoc />
    public Task<AiProviderInvocationResult> InvokeAsync(AiProviderInvocationRequest request, CancellationToken cancellationToken)
    {
        _invocationCounts.AddOrUpdate(request.Operation, 1, static (_, count) => count + 1);
        if (_scripts.TryGetValue(request.Operation, out var queue) && queue.TryDequeue(out var next))
        {
            return Task.FromResult(next(request));
        }

        if (_useProtocolDefaultsWhenUnscripted)
        {
            return Task.FromResult(AiProviderInvocationResult.Success(BuildDefaultContent(request), "fake-model", 10, 20));
        }

        return Task.FromResult(AiProviderInvocationResult.Failure(AiProviderFailureKind.Unavailable));
    }

    /// <summary>Builds a protocol 0.3-valid default response for the registered recipe operations.</summary>
    public static string BuildDefaultContent(AiProviderInvocationRequest request) =>
        request.Operation switch
        {
            AiOperationRegistry.SuggestCandidates => BuildSuggestResponse(request.Payload),
            AiOperationRegistry.ExpandSelected => BuildExpandResponse(request.Payload),
            _ => throw new InvalidOperationException($"FakeAiProvider has no default response for operation '{request.Operation}'.")
        };

    private static string BuildSuggestResponse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var inventory = root.TryGetProperty("availabilitySnapshot", out var availability)
            ? availability.GetProperty("items").EnumerateArray().ToList()
            : [];
        var equipment = root.TryGetProperty("equipmentSnapshot", out var equipmentSnapshot)
            ? equipmentSnapshot.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("equipmentId").GetString()!).Where(id => !string.IsNullOrWhiteSpace(id)).ToList()
            : [];
        var servings = root.TryGetProperty("userPresets", out var presets) && presets.TryGetProperty("servings", out var servingsElement)
            ? servingsElement.GetInt32()
            : 2;

        var inventoryUses = BuildInventoryUses(inventory);
        var requiredEquipment = equipment.Take(3).ToList();
        var candidates = new JsonArray
        {
            BuildCandidate("c1", "on_hand_first", "Pan-seared skillet dinner", "dinner", "plated_main_with_sides", "pan_sear", ["ingredient-primary"], "Quick skillet dinner using declared on-hand items.", servings, requiredEquipment, inventoryUses, "easy"),
            BuildCandidate("c2", "on_hand_flexible", "Flexible skillet scramble", "dinner", "single_skillet", "saute", ["ingredient-secondary"], "Flexible saute using declared inventory when present.", servings, requiredEquipment, inventoryUses, "easy"),
            BuildCandidate("c3", "exploratory", "Simple simmered bowl", "dinner", "bowl", "simmer", ["ingredient-tertiary"], "Simple simmered bowl with pantry staples only.", servings, requiredEquipment, [], "medium")
        };

        var response = new JsonObject
        {
            ["operation"] = AiOperationRegistry.SuggestCandidates,
            ["schemaVersion"] = "0.3",
            ["candidates"] = candidates,
            ["clarifications"] = new JsonArray()
        };
        return response.ToJsonString();
    }

    private static JsonArray BuildInventoryUses(IReadOnlyList<JsonElement> inventory)
    {
        var uses = new JsonArray();
        foreach (var item in inventory.Take(3))
        {
            var id = item.GetProperty("inventoryItemId").GetString();
            var userName = item.GetProperty("userName").GetString();
            var unit = item.GetProperty("unit").GetString();
            var availabilitySource = item.GetProperty("availabilitySource").GetString();
            if (id is null || userName is null || unit is null || availabilitySource is null)
            {
                continue;
            }

            var quantity = item.TryGetProperty("quantity", out var quantityElement) ? Math.Max(1m, quantityElement.GetDecimal() / 2m) : 1m;
            var use = new JsonObject
            {
                ["ingredientRef"] = item.TryGetProperty("ingredientRef", out var ingredientRef) && ingredientRef.ValueKind == JsonValueKind.String && ingredientRef.GetString() is { Length: > 0 } value
                    ? value
                    : $"ingredient-{id[..Math.Min(8, id.Length)]}",
                ["inventoryItemId"] = id,
                ["userName"] = userName,
                ["requiredQuantity"] = quantity,
                ["unit"] = unit,
                ["availabilitySource"] = availabilitySource,
                ["requiredState"] = "raw"
            };
            uses.Add(use);
        }

        return uses;
    }

    private static JsonObject BuildCandidate(
        string candidateId,
        string strategy,
        string name,
        string mealType,
        string dishFormat,
        string technique,
        string[] primaryIngredientRefs,
        string summary,
        int servings,
        IReadOnlyList<string> requiredEquipment,
        JsonArray inventoryUses,
        string difficulty) => new()
        {
            ["candidateId"] = candidateId,
            ["candidateStrategy"] = strategy,
            ["name"] = name,
            ["targetMealType"] = mealType,
            ["dishFormat"] = dishFormat,
            ["primaryTechnique"] = technique,
            ["primaryIngredientRefs"] = new JsonArray(primaryIngredientRefs.Select(item => (JsonNode)item).ToArray()),
            ["summary"] = summary,
            ["servings"] = servings,
            ["time"] = new JsonObject { ["activeMinutes"] = 20, ["passiveMinutes"] = 10, ["totalMinutes"] = 30 },
            ["difficulty"] = difficulty,
            ["requiredEquipmentIds"] = new JsonArray(requiredEquipment.Select(item => (JsonNode)item).ToArray()),
            ["requiredCapabilities"] = new JsonArray("burner", "saute"),
            ["inventoryUses"] = inventoryUses.DeepClone(),
            ["additionalIngredients"] = new JsonArray(),
            ["preparationProfile"] = new JsonObject
            {
                ["requiresAdvancePreparation"] = false,
                ["minimumLeadMinutes"] = 0,
                ["blockingPreparationCodes"] = new JsonArray(),
                ["mayProduceReusableComponents"] = false
            },
            ["assumptionsUsed"] = new JsonArray("salt", "cooking oil")
        };

    private static string BuildExpandResponse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var selected = root.GetProperty("selectedCandidate");
        var candidateId = selected.GetProperty("candidateId").GetString() ?? "c1";
        var name = selected.GetProperty("name").GetString() ?? "Saved cook-now recipe";
        var servings = selected.TryGetProperty("servings", out var servingsElement) ? servingsElement.GetInt32() : 2;
        var mealTypes = selected.TryGetProperty("mealTypes", out var mealTypesElement)
            ? mealTypesElement.EnumerateArray().Select(item => item.GetString()!).Where(item => !string.IsNullOrWhiteSpace(item)).ToList()
            : ["dinner"];
        var inventoryUses = selected.TryGetProperty("inventoryUses", out var usesElement)
            ? usesElement.EnumerateArray().ToList()
            : [];
        var additional = selected.TryGetProperty("additionalIngredients", out var additionalElement)
            ? additionalElement.EnumerateArray().ToList()
            : [];
        var equipmentIds = selected.TryGetProperty("requiredEquipmentIds", out var equipmentElement)
            ? equipmentElement.EnumerateArray().Select(item => item.GetString()!).Where(item => !string.IsNullOrWhiteSpace(item)).ToList()
            : [];

        var ingredients = new JsonArray();
        var ingredientIds = new List<string>();
        var index = 0;
        foreach (var use in inventoryUses)
        {
            index++;
            var ingredientId = $"ing-{index}";
            ingredientIds.Add(ingredientId);
            var inventoryItemId = use.TryGetProperty("itemId", out var itemId) ? itemId.GetString() : use.GetProperty("inventoryItemId").GetString();
            ingredients.Add(new JsonObject
            {
                ["ingredientId"] = ingredientId,
                ["sourceType"] = "inventory",
                ["displayName"] = use.GetProperty("userName").GetString(),
                ["inventoryItemId"] = inventoryItemId,
                ["requiredQuantity"] = use.GetProperty("requiredQuantity").GetDecimal(),
                ["unit"] = use.GetProperty("unit").GetString(),
                ["optional"] = false
            });
        }

        foreach (var item in additional)
        {
            index++;
            var ingredientId = $"ing-{index}";
            ingredientIds.Add(ingredientId);
            ingredients.Add(new JsonObject
            {
                ["ingredientId"] = ingredientId,
                ["sourceType"] = "additional",
                ["displayName"] = item.GetProperty("name").GetString(),
                ["requiredQuantity"] = item.GetProperty("requiredQuantity").GetDecimal(),
                ["unit"] = item.GetProperty("unit").GetString(),
                ["optional"] = item.TryGetProperty("optional", out var optional) && optional.GetBoolean()
            });
        }

        if (ingredients.Count == 0)
        {
            ingredients.Add(new JsonObject
            {
                ["ingredientId"] = "ing-oil",
                ["sourceType"] = "assumption",
                ["displayName"] = "Cooking oil",
                ["requiredQuantity"] = 15,
                ["unit"] = "ml",
                ["optional"] = false
            });
            ingredientIds.Add("ing-oil");
        }

        var equipment = new JsonArray(equipmentIds.Select(id => (JsonNode)new JsonObject
        {
            ["equipmentId"] = id,
            ["name"] = id,
            ["capabilities"] = new JsonArray("burner")
        }).ToArray());
        if (equipment.Count == 0)
        {
            equipment.Add(new JsonObject
            {
                ["equipmentId"] = "eq-stove",
                ["name"] = "Stove",
                ["capabilities"] = new JsonArray("burner")
            });
        }

        var response = new JsonObject
        {
            ["operation"] = AiOperationRegistry.ExpandSelected,
            ["schemaVersion"] = "0.3",
            ["recipe"] = new JsonObject
            {
                ["recipeId"] = $"recipe-{candidateId}",
                ["revision"] = 1,
                ["name"] = name.Length > 80 ? name[..80] : name,
                ["mealTypes"] = new JsonArray(mealTypes.Select(item => (JsonNode)item).ToArray()),
                ["servings"] = servings,
                ["yield"] = $"{servings} portions",
                ["ingredients"] = ingredients,
                ["equipment"] = equipment,
                ["preparations"] = new JsonArray(),
                ["stages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["stageId"] = "stage-cook",
                        ["name"] = "Cook",
                        ["instructions"] = "Prepare and cook the selected dish using the declared ingredients.",
                        ["activeMinutes"] = 20,
                        ["passiveMinutes"] = 10,
                        ["dependsOn"] = new JsonArray(),
                        ["sensoryCues"] = new JsonArray("cooked through"),
                        ["usesIngredientIds"] = new JsonArray(ingredientIds.Select(item => (JsonNode)item).ToArray())
                    }
                },
                ["dependencies"] = new JsonArray(),
                ["storage"] = new JsonObject
                {
                    ["refrigerateHours"] = 48,
                    ["freezeCompatible"] = true,
                    ["reheatNotes"] = "Reheat gently until warmed through."
                },
                ["producedComponents"] = new JsonArray(),
                ["reconciliationHints"] = new JsonArray("Confirm remaining inventory after plating."),
                ["assumptions"] = new JsonArray("salt", "cooking oil"),
                ["thumbnailVisual"] = new JsonObject
                {
                    ["schemaVersion"] = "1",
                    ["appearanceDescription"] = "Simple plated dinner with cooked ingredients and light sauce.",
                    ["visibleComponents"] = new JsonArray("main protein", "side vegetable"),
                    ["dishFormat"] = "plated_main_with_sides",
                    ["plating"] = "centered plate with side portions",
                    ["sauceAppearance"] = "light glaze",
                    ["textureAndDoneness"] = new JsonArray("cooked through", "tender"),
                    ["garnish"] = new JsonArray(),
                    ["dominantColors"] = new JsonArray("brown", "green"),
                    ["excludedElements"] = new JsonArray("text", "hands", "packaging")
                }
            }
        };
        return response.ToJsonString();
    }
}
