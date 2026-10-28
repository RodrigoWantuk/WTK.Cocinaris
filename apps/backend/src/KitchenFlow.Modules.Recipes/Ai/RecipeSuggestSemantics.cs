using System.Text.Json;
using System.Text.RegularExpressions;

namespace KitchenFlow.Modules.Recipes.Ai;

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

            var summaryWords = ProtocolCheck.WordCount(candidate.Summary);
            if (summaryWords > 18)
            {
                errors.Add($"{prefix}.summary has {summaryWords} words (max 18)");
            }

            var fingerprint = string.Join('|', new[] { candidate.DishFormat, candidate.PrimaryTechnique }.Concat(candidate.PrimaryIngredientRefs));
            if (!fingerprints.Add(fingerprint))
            {
                errors.Add($"{prefix} is not materially distinct from another candidate");
            }

            foreach (var claim in UnsupportedClaims)
            {
                if (claim.IsMatch(candidate.Name) || claim.IsMatch(candidate.Summary))
                {
                    errors.Add($"{prefix} contains unsupported quality claim");
                    break;
                }
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

        return errors;
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
