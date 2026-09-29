using System.Text.Json;
using KitchenFlow.Modules.Ai.Abstractions;
using KitchenFlow.Modules.Ai.Providers;
using KitchenFlow.Modules.Recipes.Ai;

/// <summary>
/// Bounded PLAN-0028 live DeepSeek smoke outside CI. Exercises production envelope builders,
/// DeepSeekAiProvider, and RecipeProtocolValidator for one suggest and one expand (max one repair each).
/// </summary>
/// <remarks>
/// Requires PLAN0028_LIVE_SMOKE=1 and DEEPSEEK_API_KEY. Never prints the API key.
/// Optional: PLAN0028_COST_CEILING_USD (default 0.05), DEEPSEEK_BASE_URL, DEEPSEEK_MODEL.
/// </remarks>
if (Environment.GetEnvironmentVariable("PLAN0028_LIVE_SMOKE") != "1")
{
    Console.Error.WriteLine("Refusing live provider. Set PLAN0028_LIVE_SMOKE=1 and DEEPSEEK_API_KEY.");
    return 2;
}

var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("DEEPSEEK_API_KEY is not set.");
    return 2;
}

var costCeiling = decimal.Parse(Environment.GetEnvironmentVariable("PLAN0028_COST_CEILING_USD") ?? "0.05");
var baseUrl = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL") ?? "https://api.deepseek.com";
var model = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? "deepseek-v4-flash";
// DeepSeek V4 Flash public prices used for bounded estimate (USD / 1M tokens).
const decimal InputPricePerMillion = 0.14m;
const decimal OutputPricePerMillion = 0.28m;

var options = new DeepSeekOptions
{
    Enabled = true,
    ApiKey = apiKey,
    BaseUrl = baseUrl.TrimEnd('/'),
    NonThinkingModel = model,
    ThinkingModel = model
};

using var httpClient = new HttpClient();
var provider = new DeepSeekAiProvider(httpClient, options);

var inventory = new[]
{
    new RecipeInventoryContextItem("inv-001", "Chicken breast", 500m, "g", "on_hand", "ingredient-chicken-breast"),
    new RecipeInventoryContextItem("inv-002", "White rice", 400m, "g", "on_hand", "ingredient-white-rice"),
    new RecipeInventoryContextItem("inv-003", "Carrot", 3m, "unit", "on_hand", "ingredient-carrot"),
    new RecipeInventoryContextItem("inv-004", "Eggs", 6m, "unit", "on_hand", "ingredient-eggs")
};
var equipment = new[]
{
    new RecipeEquipmentContextItem("eq-stove", "Stove", ["burner", "simmer"]),
    new RecipeEquipmentContextItem("eq-skillet", "Skillet", ["sear", "saute"]),
    new RecipeEquipmentContextItem("eq-pan", "Saucepan", ["boil", "simmer"])
};
var presets = new RecipeUserPresets(2, ["quick"], ["no shellfish"], ["oil", "salt", "water"]);
var suggestContext = new RecipeSuggestRequestContext(
    "plan-0028-smoke-suggest-001",
    inventory,
    equipment,
    presets,
    new RecipeExecutionContext("cook_now", 45));

var totalCalls = 0;
var totalPrompt = 0;
var totalCompletion = 0;

var suggestPayload = RecipeAiRequestEnvelopes.BuildSuggestRequest(suggestContext);
AssertEnvelopeHasFullSchema(suggestPayload, AiOperationRegistry.SuggestCandidates);
using var suggestRequestDoc = JsonDocument.Parse(suggestPayload);
var (suggestOk, suggestRaw, suggestCalls, suggestPrompt, suggestCompletion, suggestRepair) =
    await RunWithOptionalRepairAsync(
        provider,
        AiOperationRegistry.SuggestCandidates,
        suggestPayload,
        raw =>
        {
            var result = RecipeProtocolValidator.ValidateSuggest(raw, suggestRequestDoc.RootElement);
            return (result.IsValid, result.Errors);
        },
        (errors, previous) => RecipeAiRequestEnvelopes.BuildSuggestRepairRequest(suggestContext, errors, previous),
        "suggest");
totalCalls += suggestCalls;
totalPrompt += suggestPrompt;
totalCompletion += suggestCompletion;

if (!suggestOk || suggestRaw is null)
{
    WriteSummary(model, totalCalls, suggestRepair, expandRepair: false, suggestOk: false, expandOk: false, totalPrompt, totalCompletion, costCeiling, "suggest_failed");
    return 1;
}

using var suggestDoc = JsonDocument.Parse(suggestRaw);
var firstCandidate = suggestDoc.RootElement.GetProperty("candidates")[0];
var selected = new RecipeSelectedCandidateContext(
    firstCandidate.GetProperty("candidateId").GetString()!,
    firstCandidate.GetProperty("name").GetString()!,
    [firstCandidate.GetProperty("targetMealType").GetString()!],
    firstCandidate.GetProperty("servings").GetInt32(),
    firstCandidate.GetProperty("requiredEquipmentIds").EnumerateArray().Select(e => e.GetString()!).ToList(),
    MapInventoryUses(firstCandidate),
    MapAdditional(firstCandidate));

var expandContext = new RecipeExpandRequestContext(
    "plan-0028-smoke-expand-001",
    inventory,
    equipment,
    presets,
    selected);
var expandPayload = RecipeAiRequestEnvelopes.BuildExpandRequest(expandContext);
AssertEnvelopeHasFullSchema(expandPayload, AiOperationRegistry.ExpandSelected);
using var expandRequestDoc = JsonDocument.Parse(expandPayload);
var (expandOk, _, expandCalls, expandPrompt, expandCompletion, expandRepair) =
    await RunWithOptionalRepairAsync(
        provider,
        AiOperationRegistry.ExpandSelected,
        expandPayload,
        raw =>
        {
            var result = RecipeProtocolValidator.ValidateExpand(raw, expandRequestDoc.RootElement);
            return (result.IsValid, result.Errors);
        },
        (errors, previous) => RecipeAiRequestEnvelopes.BuildExpandRepairRequest(expandContext, errors, previous),
        "expand");
totalCalls += expandCalls;
totalPrompt += expandPrompt;
totalCompletion += expandCompletion;

var estimatedCost = EstimateUsd(totalPrompt, totalCompletion);
WriteSummary(
    model,
    totalCalls,
    suggestRepair,
    expandRepair,
    suggestOk,
    expandOk,
    totalPrompt,
    totalCompletion,
    costCeiling,
    expandOk ? "passed" : "expand_failed");

if (estimatedCost > costCeiling)
{
    Console.Error.WriteLine($"Estimated cost {estimatedCost:F6} exceeded ceiling {costCeiling:F6}.");
    return 1;
}

return suggestOk && expandOk ? 0 : 1;

static void AssertEnvelopeHasFullSchema(string payload, string operation)
{
    using var doc = JsonDocument.Parse(payload);
    var root = doc.RootElement;
    if (root.GetProperty("operation").GetString() != operation)
    {
        throw new InvalidOperationException($"Envelope operation mismatch for {operation}.");
    }

    if (!root.TryGetProperty("responseSchema", out var schema) || schema.ValueKind != JsonValueKind.Object)
    {
        throw new InvalidOperationException($"Envelope for {operation} is missing full responseSchema.");
    }

    if (!schema.TryGetProperty("type", out _) && !schema.TryGetProperty("properties", out _) && !schema.TryGetProperty("$defs", out _) && !schema.TryGetProperty("$ref", out _))
    {
        throw new InvalidOperationException($"Envelope for {operation} responseSchema is not a complete JSON Schema object.");
    }

    Console.WriteLine($"envelope {operation}: full responseSchema present ({schema.GetRawText().Length} chars)");
}

static async Task<(bool Ok, string? Raw, int Calls, int Prompt, int Completion, bool UsedRepair)> RunWithOptionalRepairAsync(
    IAiProvider provider,
    string operation,
    string initialPayload,
    Func<string, (bool Ok, IReadOnlyList<string> Errors)> validate,
    Func<IReadOnlyList<string>, string, string> buildRepair,
    string label)
{
    var calls = 0;
    var prompt = 0;
    var completion = 0;
    var usedRepair = false;
    var payload = initialPayload;

    for (var attempt = 0; attempt < 2; attempt++)
    {
        if (attempt == 1)
        {
            usedRepair = true;
        }

        calls++;
        var invocation = await provider.InvokeAsync(
            new AiProviderInvocationRequest(operation, payload, PreferNonThinking: true, TimeoutSeconds: 120, CorrelationId: $"plan-0028-{label}-{attempt}"),
            CancellationToken.None);
        if (!invocation.IsSuccess || string.IsNullOrWhiteSpace(invocation.RawContent))
        {
            Console.Error.WriteLine($"{label} attempt {attempt}: provider failure {invocation.FailureKind}");
            return (false, null, calls, prompt, completion, usedRepair);
        }

        prompt += invocation.PromptTokens ?? 0;
        completion += invocation.CompletionTokens ?? 0;
        var (ok, errors) = validate(invocation.RawContent!);
        if (ok)
        {
            Console.WriteLine($"{label}: Passed (attempt={attempt}, repair={usedRepair}, promptTokens={invocation.PromptTokens}, completionTokens={invocation.CompletionTokens})");
            return (true, invocation.RawContent, calls, prompt, completion, usedRepair);
        }

        Console.WriteLine($"{label}: invalid on attempt {attempt} ({errors.Count} errors); {(attempt == 0 ? "repairing" : "giving up")}");
        foreach (var error in errors.Take(5))
        {
            Console.WriteLine($"  - {error}");
        }

        if (attempt == 0)
        {
            var repairPayload = buildRepair(errors.Take(12).ToList(), invocation.RawContent!);
            if (!repairPayload.Contains("\"repair\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Repair payload missing repair object.");
            }

            payload = repairPayload;
            continue;
        }

        return (false, null, calls, prompt, completion, usedRepair);
    }

    return (false, null, calls, prompt, completion, usedRepair);
}

static IReadOnlyList<RecipeInventoryContextItem> MapInventoryUses(JsonElement candidate)
{
    if (!candidate.TryGetProperty("inventoryUses", out var uses))
    {
        return [];
    }

    return uses.EnumerateArray().Select(item => new RecipeInventoryContextItem(
        item.GetProperty("inventoryItemId").GetString()!,
        item.GetProperty("userName").GetString()!,
        item.GetProperty("requiredQuantity").GetDecimal(),
        item.GetProperty("unit").GetString()!,
        item.GetProperty("availabilitySource").GetString()!,
        item.TryGetProperty("ingredientRef", out var ingredientRef) ? ingredientRef.GetString() : null)).ToList();
}

static IReadOnlyList<(string Name, decimal Quantity, string Unit, bool Optional)> MapAdditional(JsonElement candidate)
{
    if (!candidate.TryGetProperty("additionalIngredients", out var additional))
    {
        return [];
    }

    return additional.EnumerateArray().Select(item => (
        item.GetProperty("name").GetString()!,
        item.GetProperty("quantity").GetDecimal(),
        item.GetProperty("unit").GetString()!,
        item.TryGetProperty("optional", out var optional) && optional.GetBoolean())).ToList();
}

static decimal EstimateUsd(int promptTokens, int completionTokens) =>
    (promptTokens / 1_000_000m) * InputPricePerMillion + (completionTokens / 1_000_000m) * OutputPricePerMillion;

static void WriteSummary(
    string model,
    int totalCalls,
    bool suggestRepair,
    bool expandRepair,
    bool suggestOk,
    bool expandOk,
    int promptTokens,
    int completionTokens,
    decimal costCeiling,
    string status)
{
    var estimated = EstimateUsd(promptTokens, completionTokens);
    var summary = new
    {
        plan = "PLAN-0028",
        smoke = "recipe-gateway-live-smoke",
        provider = "deepseek",
        model,
        providerCalls = totalCalls,
        suggest = new { result = suggestOk ? "Passed" : "Failed", repairRequired = suggestRepair },
        expand = new { result = expandOk ? "Passed" : "Failed", repairRequired = expandRepair },
        promptTokens,
        completionTokens,
        estimatedCostUsd = estimated,
        costCeilingUsd = costCeiling,
        status
    };
    var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    var evidenceDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "docs", "evidence", "plan-0028"));
    Directory.CreateDirectory(evidenceDir);
    File.WriteAllText(Path.Combine(evidenceDir, "live-smoke-summary.json"), json);
}
