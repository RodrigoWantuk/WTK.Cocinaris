using System.Text.Json;
using System.Text.RegularExpressions;

namespace KitchenFlow.Modules.Recipes.Ai;

/// <summary>
/// Semantic validation for <c>recipe.expand_selected.v1</c> responses. Mirrors
/// <c>packages/contracts/ai/recipe/lib/validate-core.mjs#validateExpandSemantics</c>.
/// </summary>
public static partial class RecipeExpandSemantics
{
    private static readonly Regex[] PrivateThumbnailPatterns =
    [
        InventoryReferencePattern(), UserWordPattern(), AllergyPattern(), PantryPattern(), RestrictionPattern(), HouseholdPattern(), OwnerNamePattern(), UrlPattern()
    ];

    private static readonly Regex[] UnsupportedThumbnailClaims =
    [
        SafeClaim(), HealthyThumbnailClaim(), FreshThumbnailClaim(), AuthenticThumbnailClaim(), GuaranteedClaim(), NutritionClaim()
    ];

    private static readonly Regex LuxuryComponentPattern = LuxuryComponent();

    /// <summary>Validates thumbnail privacy/claim bounds and request-fidelity rules.</summary>
    public static List<string> Validate(ExpandSelectedResponse response, JsonElement? request)
    {
        var errors = new List<string>();
        var recipe = response.Recipe;
        var visual = recipe.ThumbnailVisual;

        if (ProtocolCheck.WordCount(visual.AppearanceDescription) > 36)
        {
            errors.Add("thumbnailVisual.appearanceDescription exceeds 36 words");
        }

        var visualBlob = string.Join(' ', new[] { visual.AppearanceDescription }
            .Concat(visual.VisibleComponents)
            .Append(visual.Plating)
            .Append(visual.SauceAppearance)
            .Concat(visual.TextureAndDoneness)
            .Concat(visual.Garnish));

        if (PrivateThumbnailPatterns.Any(pattern => pattern.IsMatch(visualBlob)))
        {
            errors.Add("thumbnailVisual contains privacy-sensitive or identity context");
        }

        if (UnsupportedThumbnailClaims.Any(pattern => pattern.IsMatch(visualBlob)))
        {
            errors.Add("thumbnailVisual contains safety/nutrition/authenticity claim");
        }

        if (request?.Prop("selectedCandidate") is { } selectedCandidate)
        {
            var snapshotIds = new HashSet<string>(
                request.Prop("confirmedInventorySnapshot").Prop("items").AsArray().Select(item => item.Prop("itemId").AsString()).OfType<string>(),
                StringComparer.Ordinal);
            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.SourceType == "inventory" && ingredient.InventoryItemId is not null && snapshotIds.Count > 0 && !snapshotIds.Contains(ingredient.InventoryItemId))
                {
                    errors.Add($"ingredient {ingredient.IngredientId} invents inventoryItemId {ingredient.InventoryItemId}");
                }
            }

            _ = selectedCandidate; // requiredEquipmentIds cross-check is intentionally advisory-only, matching the reference validator.
        }

        var ingredientNames = new HashSet<string>(recipe.Ingredients.Select(item => item.DisplayName.ToLowerInvariant()), StringComparer.Ordinal);
        foreach (var component in visual.VisibleComponents)
        {
            var lower = component.ToLowerInvariant();
            if (LuxuryComponentPattern.IsMatch(lower) && !ingredientNames.Any(name => lower.Contains(name.Split(' ')[0], StringComparison.Ordinal)))
            {
                errors.Add($"thumbnailVisual invents visible component \"{component}\"");
            }
        }

        return errors;
    }

    [GeneratedRegex(@"\binv[-_]?\d+", RegexOptions.IgnoreCase)]
    private static partial Regex InventoryReferencePattern();
    [GeneratedRegex(@"\buser\b", RegexOptions.IgnoreCase)]
    private static partial Regex UserWordPattern();
    [GeneratedRegex(@"\ballerg", RegexOptions.IgnoreCase)]
    private static partial Regex AllergyPattern();
    [GeneratedRegex(@"\bpantry\b", RegexOptions.IgnoreCase)]
    private static partial Regex PantryPattern();
    [GeneratedRegex(@"\brestriction", RegexOptions.IgnoreCase)]
    private static partial Regex RestrictionPattern();
    [GeneratedRegex(@"\bhousehold\b", RegexOptions.IgnoreCase)]
    private static partial Regex HouseholdPattern();
    [GeneratedRegex(@"rodrigowantuk", RegexOptions.IgnoreCase)]
    private static partial Regex OwnerNamePattern();
    [GeneratedRegex(@"https?://", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
    [GeneratedRegex(@"\bsafe\b", RegexOptions.IgnoreCase)]
    private static partial Regex SafeClaim();
    [GeneratedRegex(@"\bhealthy\b", RegexOptions.IgnoreCase)]
    private static partial Regex HealthyThumbnailClaim();
    [GeneratedRegex(@"\bfresh\b", RegexOptions.IgnoreCase)]
    private static partial Regex FreshThumbnailClaim();
    [GeneratedRegex(@"\bauthentic\b", RegexOptions.IgnoreCase)]
    private static partial Regex AuthenticThumbnailClaim();
    [GeneratedRegex(@"\bguaranteed\b", RegexOptions.IgnoreCase)]
    private static partial Regex GuaranteedClaim();
    [GeneratedRegex(@"\bnutrit", RegexOptions.IgnoreCase)]
    private static partial Regex NutritionClaim();
    [GeneratedRegex(@"\b(truffle|caviar|lobster|gold leaf)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LuxuryComponent();
}
