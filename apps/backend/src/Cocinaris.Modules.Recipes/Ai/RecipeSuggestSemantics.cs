using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cocinaris.Modules.Recipes.Ai;

/// <summary>
/// Semantic validation for <c>recipe.suggest_candidates.v1</c> responses. Mirrors
/// <c>packages/contracts/ai/recipe/lib/validate-core.mjs#validateSuggestSemantics</c> so the .NET AI
/// Gateway rejects the exact same untrusted-output classes that the contract test suite rejects.
/// </summary>
public static partial class RecipeSuggestSemantics
{
    private static readonly Regex[] UnsupportedClaims =
    [
        HomemadeClaim(), FreshClaim(), HealthyClaim(), AuthenticClaim(), TraditionalClaim(), HighProteinClaim()
    ];

    /// <summary>Longest stored summary, in characters. The description sells the dish, so it gets two short sentences (PLAN-0060).</summary>
    public const int MaxSummaryCharacters = 320;

    /// <summary>
    /// Fits a summary to <see cref="MaxSummaryCharacters"/>, at the last sentence end when one falls in
    /// the second half, else at the last space with an ellipsis. Length alone never fails a request: a
    /// word cap here was what made suggestions fail after every repair (owner report 2026-11-04).
    /// </summary>
    /// <param name="summary">The model's summary.</param>
    /// <returns>A summary within the limit, unchanged when it already fits.</returns>
    public static string FitSummary(string summary)
    {
        var text = summary.Trim();
        if (text.Length <= MaxSummaryCharacters)
        {
            return text;
        }

        var window = text[..MaxSummaryCharacters];
        var sentenceEnd = window.LastIndexOfAny(['.', '!', '?']);
        if (sentenceEnd >= MaxSummaryCharacters / 2)
        {
            return window[..(sentenceEnd + 1)];
        }

        var lastSpace = window[..(MaxSummaryCharacters - 1)].LastIndexOf(' ');
        return (lastSpace > 0 ? window[..lastSpace] : window[..(MaxSummaryCharacters - 1)]).TrimEnd() + "…";
    }

    /// <summary>Validates cross-candidate distinctness, injected-claim, and request-fidelity rules.</summary>
    public static List<string> Validate(SuggestCandidatesResponse response, JsonElement? request)
    {
        var errors = new List<string>();
        if (response.Candidates.Count != 3)
        {
            errors.Add($"expected exactly 3 candidates, got {response.Candidates.Count}");
        }

        var strategies = new HashSet<string>(StringComparer.Ordinal);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < response.Candidates.Count; index++)
        {
            var candidate = response.Candidates[index];
            var prefix = $"candidates[{index}]";
            if (!strategies.Add(candidate.CandidateStrategy))
            {
                errors.Add($"{prefix} duplicates strategy {candidate.CandidateStrategy}");
            }

            if (!fingerprints.Add(Fingerprint(candidate)))
            {
                errors.Add($"{prefix} is not materially distinct from another candidate");
            }

            ValidateCandidate(errors, prefix, candidate, request);
        }

        return errors;
    }

    /// <summary>
    /// Validates a shopping plan's dish cards (PLAN-0062). Each candidate passes the same rules as a
    /// suggestion; the rules of the whole answer differ: exactly six, strategies may repeat, and every
    /// dish targets a requested meal.
    /// </summary>
    /// <param name="response">Structurally valid answer.</param>
    /// <param name="request">The request envelope, when available.</param>
    public static List<string> ValidatePlanDeck(PlanDeckResponse response, JsonElement? request)
    {
        var errors = new List<string>();
        if (response.Candidates.Count != PlanDeckResponse.CandidateCount)
        {
            errors.Add($"expected exactly {PlanDeckResponse.CandidateCount} candidates, got {response.Candidates.Count}");
        }

        var mealTypes = request.Prop("planDeck").Prop("mealTypes").AsArray().Select(item => ((JsonElement?)item).AsString()).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < response.Candidates.Count; index++)
        {
            var candidate = response.Candidates[index];
            var prefix = $"candidates[{index}]";
            if (!PlanDeckResponse.Strategies.Contains(candidate.CandidateStrategy))
            {
                errors.Add($"{prefix} uses strategy {candidate.CandidateStrategy}, not allowed in a plan deck");
            }

            if (!fingerprints.Add(Fingerprint(candidate)))
            {
                errors.Add($"{prefix} is not materially distinct from another candidate");
            }

            if (mealTypes.Count > 0 && !mealTypes.Contains(candidate.TargetMealType))
            {
                errors.Add($"{prefix} targets meal {candidate.TargetMealType}, which was not requested");
            }

            ValidateCandidate(errors, prefix, candidate, request);
        }

        return errors;
    }

    /// <summary>
    /// The components that satisfy their rules: every dish that uses one exists and lists its output as
    /// an additional ingredient in the same unit, and the output covers what they all need. A bad
    /// component is dropped here, because the six dishes are useful without it.
    /// </summary>
    /// <param name="response">Validated response.</param>
    /// <param name="request">Original request, for the rules every candidate must pass.</param>
    public static List<PlanDeckComponent> ValidComponents(PlanDeckResponse response, JsonElement? request)
    {
        var byId = response.Candidates.GroupBy(item => item.CandidateId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var valid = new List<PlanDeckComponent>();
        foreach (var component in response.Components ?? [])
        {
            var errors = component.Validate();
            ValidateCandidate(errors, "component", component.Candidate, request);
            if (errors.Count > 0 || component.UsedBy.Any(id => !byId.ContainsKey(id)))
            {
                continue;
            }

            var name = Cocinaris.Modules.Ingredients.IngredientText.Normalize(component.Output.Name);
            var uses = component.UsedBy.Distinct(StringComparer.Ordinal)
                .Select(id => byId[id].AdditionalIngredients.Where(item => item.Unit == component.Output.Unit && Cocinaris.Modules.Ingredients.IngredientText.Normalize(item.Name) == name).ToList())
                .ToList();
            if (uses.All(items => items.Count > 0) && component.Output.Quantity >= uses.Sum(items => items.Sum(item => item.RequiredQuantity)))
            {
                valid.Add(component);
            }
        }

        return valid;
    }

    /// <summary>The semantic identity of a dish: format, technique, and main ingredients.</summary>
    /// <param name="candidate">Candidate.</param>
    public static string Fingerprint(SuggestCandidate candidate) =>
        string.Join('|', new[] { candidate.DishFormat, candidate.PrimaryTechnique }.Concat(candidate.PrimaryIngredientRefs));

    /// <summary>The rules every candidate must pass, whichever operation produced it.</summary>
    /// <param name="errors">Receives the errors.</param>
    /// <param name="prefix">Error path of this candidate.</param>
    /// <param name="candidate">Candidate.</param>
    /// <param name="request">The request envelope, when available.</param>
    public static void ValidateCandidate(List<string> errors, string prefix, SuggestCandidate candidate, JsonElement? request)
    {
        foreach (var claim in UnsupportedClaims)
        {
            if (claim.IsMatch(candidate.Name) || claim.IsMatch(candidate.Summary))
            {
                errors.Add($"{prefix} contains unsupported quality claim");
                break;
            }
        }

        if (RecipeExpandSemantics.ContainsLinkOrMarkup(candidate.Name) || RecipeExpandSemantics.ContainsLinkOrMarkup(candidate.Summary))
        {
            errors.Add($"{prefix} contains a link or markup");
        }

        var assumptions = new HashSet<string>(candidate.AssumptionsUsed.Select(value => value.Trim().ToLowerInvariant()), StringComparer.Ordinal);
        for (var additionalIndex = 0; additionalIndex < candidate.AdditionalIngredients.Count; additionalIndex++)
        {
            var name = candidate.AdditionalIngredients[additionalIndex].Name.Trim().ToLowerInvariant();
            if (name.Length > 0 && assumptions.Contains(name))
            {
                errors.Add($"{prefix}.additionalIngredients[{additionalIndex}] repeats assumption \"{name}\"");
            }
        }

        if (request is not null)
        {
            ValidateAgainstRequest(errors, prefix, candidate, request);
        }
    }

    private static void ValidateAgainstRequest(List<string> errors, string prefix, SuggestCandidate candidate, JsonElement? request)
    {
        var allowedEquipment = new HashSet<string>(
            request.Prop("equipmentSnapshot").Prop("items").AsArray().Select(item => item.Prop("equipmentId").AsString()).OfType<string>(),
            StringComparer.Ordinal);
        foreach (var equipmentId in candidate.RequiredEquipmentIds)
        {
            if (allowedEquipment.Count > 0 && !allowedEquipment.Contains(equipmentId))
            {
                errors.Add($"{prefix} references undeclared equipment {equipmentId}");
            }
        }

        var availabilityById = request.Prop("availabilitySnapshot").Prop("items").AsArray()
            .Select(item => (Id: item.Prop("inventoryItemId").AsString(), Item: item))
            .Where(entry => entry.Id is not null)
            .ToDictionary(entry => entry.Id!, entry => entry.Item, StringComparer.Ordinal);
        for (var useIndex = 0; useIndex < candidate.InventoryUses.Count; useIndex++)
        {
            var use = candidate.InventoryUses[useIndex];
            if (!availabilityById.TryGetValue(use.InventoryItemId, out var source))
            {
                errors.Add($"{prefix}.inventoryUses[{useIndex}] invents inventoryItemId {use.InventoryItemId}");
                continue;
            }

            if (!string.Equals(use.UserName, source.Prop("userName").AsString(), StringComparison.Ordinal))
            {
                errors.Add($"{prefix}.inventoryUses[{useIndex}] renamed userName");
            }

            if (!string.Equals(use.Unit, source.Prop("unit").AsString(), StringComparison.Ordinal))
            {
                errors.Add($"{prefix}.inventoryUses[{useIndex}] changed unit");
            }

            if (!string.Equals(use.AvailabilitySource, source.Prop("availabilitySource").AsString(), StringComparison.Ordinal))
            {
                errors.Add($"{prefix}.inventoryUses[{useIndex}] changed availabilitySource");
            }

            var sourceIngredientRef = source.Prop("ingredientRef").AsString();
            if (!string.IsNullOrEmpty(sourceIngredientRef) && !string.Equals(use.IngredientRef, sourceIngredientRef, StringComparison.Ordinal))
            {
                errors.Add($"{prefix}.inventoryUses[{useIndex}] changed ingredientRef");
            }
        }

        // PLAN-0056: the person's required pantry items and time limit are hard rules.
        var guidance = request.Prop("suggestionGuidance");
        var usedIds = candidate.InventoryUses.Select(use => use.InventoryItemId).ToHashSet(StringComparer.Ordinal);
        foreach (var requiredId in guidance.Prop("requiredInventoryItemIds").AsArray().Select(item => ((JsonElement?)item).AsString()).OfType<string>())
        {
            if (!usedIds.Contains(requiredId))
            {
                errors.Add($"{prefix} does not use required inventory item {requiredId}");
            }
        }

        if (guidance.Prop("maxTotalMinutes").AsNumber() is { } maxMinutes && candidate.Time.TotalMinutes > maxMinutes)
        {
            errors.Add($"{prefix} takes {candidate.Time.TotalMinutes} minutes, more than the requested {maxMinutes}");
        }

        var mode = request.Prop("executionContext").Prop("executionMode").AsString();
        var lead = request.Prop("executionContext").Prop("availableLeadMinutes").AsNumber();
        if (mode == "cook_now" && lead is not null && candidate.PreparationProfile.MinimumLeadMinutes > lead)
        {
            errors.Add($"{prefix} lead minutes exceed cook_now window ({candidate.PreparationProfile.MinimumLeadMinutes} > {lead})");
        }

        if (mode == "cook_now" && candidate.CandidateStrategy == "planned_purchase_reuse")
        {
            errors.Add($"{prefix} uses menu-only strategy in cook_now");
        }

        if (mode == "menu_planning" && candidate.CandidateStrategy == "on_hand_flexible")
        {
            errors.Add($"{prefix} uses cook_now-only strategy in menu_planning");
        }
    }

    [GeneratedRegex(@"\bhomemade\b", RegexOptions.IgnoreCase)]
    private static partial Regex HomemadeClaim();
    [GeneratedRegex(@"\bfresh\b", RegexOptions.IgnoreCase)]
    private static partial Regex FreshClaim();
    [GeneratedRegex(@"\bhealthy\b", RegexOptions.IgnoreCase)]
    private static partial Regex HealthyClaim();
    [GeneratedRegex(@"\bauthentic\b", RegexOptions.IgnoreCase)]
    private static partial Regex AuthenticClaim();
    [GeneratedRegex(@"\btraditional\b", RegexOptions.IgnoreCase)]
    private static partial Regex TraditionalClaim();
    [GeneratedRegex(@"\bhigh[- ]protein\b", RegexOptions.IgnoreCase)]
    private static partial Regex HighProteinClaim();
}
