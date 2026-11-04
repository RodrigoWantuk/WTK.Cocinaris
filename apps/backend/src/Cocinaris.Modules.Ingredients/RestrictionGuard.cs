using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocinaris.Modules.Ingredients;

/// <summary>One restriction the person confirmed, as the profile stores it.</summary>
/// <param name="Code">Stable code, curated or custom.</param>
/// <param name="Category">Profile category name, for example <c>Allergy</c> or <c>Dislike</c>.</param>
public sealed record RestrictionCode(string Code, string Category);

/// <summary>Outcome of checking ingredients against the person's restrictions.</summary>
public enum RestrictionGuardStatus
{
    /// <summary>No rule objects to any ingredient.</summary>
    Allowed,
    /// <summary>An ingredient could not be verified; it may be shown with a note.</summary>
    Unverified,
    /// <summary>An ingredient breaks a rule; the dish must not be offered.</summary>
    Blocked
}

/// <summary>A guard result, with the rule and ingredient that decided it.</summary>
/// <param name="Status">Strongest outcome found.</param>
/// <param name="Code">Restriction code behind a non-allowed outcome.</param>
/// <param name="Ingredient">Ingredient name behind a non-allowed outcome; null when the code itself has no rule.</param>
public sealed record RestrictionGuardResult(RestrictionGuardStatus Status, string? Code = null, string? Ingredient = null);

/// <summary>
/// Deterministic check of a dish's ingredients against the person's restrictions (PLAN-0062). Model
/// output is untrusted, so telling the model about restrictions is not enough: this runs on every AI
/// or library card before it is shown, and again on the expanded recipe. It helps but does not replace
/// reading labels, so the interface must say so.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A catalog name is checked by category and slug. Every name is also checked by keyword,
/// because a name such as "molho de queijo" may not resolve to the catalog.</item>
/// <item>Matching is on whole words, ignoring case and accents, and accepts a plural ending.
/// Exception phrases ("leite de coco") are removed from the name first.</item>
/// <item>A rule marked unverifiable, or a custom code with no rule, cannot be checked, so every dish
/// is <see cref="RestrictionGuardStatus.Unverified"/>: it errs toward telling the person to check.</item>
/// <item>Under an allergy, intolerance, or medical code, a name outside the catalog with no keyword
/// match is unverified, because no rule can vouch for it.</item>
/// <item>Dislikes never block. They are reported by <see cref="DislikeMatches"/> for ranking.</item>
/// </list>
/// </remarks>
public sealed class RestrictionGuard
{
    private static readonly Lazy<RestrictionGuard> Embedded = new(LoadEmbedded);
    private static readonly HashSet<string> StrictCategories = new(StringComparer.Ordinal) { "Allergy", "Intolerance", "MedicalRestriction" };

    private readonly Dictionary<string, Rule> _rules;
    private readonly IngredientCatalog _catalog;

    private RestrictionGuard(Dictionary<string, Rule> rules, IngredientCatalog catalog)
    {
        _rules = rules;
        _catalog = catalog;
    }

    /// <summary>Gets the guard built from the rules shipped with this build.</summary>
    public static RestrictionGuard Default => Embedded.Value;

    /// <summary>Restriction codes that can never be checked: no rule, or a rule marked unverifiable. Dislikes are excluded because they never block.</summary>
    /// <param name="restrictions">The person's confirmed restrictions.</param>
    public IReadOnlyList<string> UnverifiableCodes(IEnumerable<RestrictionCode> restrictions) => restrictions
        .Where(item => item.Category != "Dislike" && (!_rules.TryGetValue(item.Code, out var rule) || rule.Unverifiable))
        .Select(item => item.Code)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>Checks ingredient names; a block wins over an unverified result.</summary>
    /// <param name="ingredientNames">Every ingredient of the dish, optional ones included.</param>
    /// <param name="keys">Matching keys that resolve names to the catalog.</param>
    /// <param name="restrictions">The person's confirmed restrictions.</param>
    public RestrictionGuardResult Check(IEnumerable<string> ingredientNames, IngredientMatchKeys keys, IEnumerable<RestrictionCode> restrictions)
    {
        var names = ingredientNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToList();
        RestrictionGuardResult? unverified = null;
        foreach (var restriction in restrictions.Where(item => item.Category != "Dislike"))
        {
            if (!_rules.TryGetValue(restriction.Code, out var rule) || rule.Unverifiable)
            {
                unverified ??= new RestrictionGuardResult(RestrictionGuardStatus.Unverified, restriction.Code);
                continue;
            }

            foreach (var name in names)
            {
                var item = IngredientPerishability.ItemOfKey(keys.Key(name), _catalog) ?? (_catalog.Aliases.TryGetValue(IngredientText.Normalize(name), out var alias) ? alias : null);
                if (Forbids(rule, item, name))
                {
                    return new RestrictionGuardResult(RestrictionGuardStatus.Blocked, restriction.Code, name);
                }

                if (item is not null && rule.UnverifiedSlugs.Contains(item.Slug))
                {
                    unverified ??= new RestrictionGuardResult(RestrictionGuardStatus.Unverified, restriction.Code, name);
                }
                else if (item is null && StrictCategories.Contains(restriction.Category))
                {
                    unverified ??= new RestrictionGuardResult(RestrictionGuardStatus.Unverified, restriction.Code, name);
                }
            }
        }

        return unverified ?? new RestrictionGuardResult(RestrictionGuardStatus.Allowed);
    }

    /// <summary>Number of the person's dislikes that match these ingredients. Dislikes affect only the ranking.</summary>
    /// <param name="ingredientNames">Ingredients of the dish.</param>
    /// <param name="keys">Matching keys.</param>
    /// <param name="restrictions">The person's confirmed preferences and restrictions.</param>
    public int DislikeMatches(IEnumerable<string> ingredientNames, IngredientMatchKeys keys, IEnumerable<RestrictionCode> restrictions)
    {
        var names = ingredientNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToList();
        return restrictions.Count(restriction =>
            restriction.Category == "Dislike"
            && _rules.TryGetValue(restriction.Code, out var rule)
            && names.Any(name => Forbids(rule, IngredientPerishability.ItemOfKey(keys.Key(name), _catalog), name)));
    }

    /// <summary>Parses rules from JSON; fails fast on a slug or category that does not exist, so a typo cannot silently weaken a rule.</summary>
    /// <param name="json">Rule document.</param>
    /// <param name="catalog">Catalog the rules refer to.</param>
    /// <exception cref="InvalidOperationException">The document is invalid or names an unknown slug or category.</exception>
    public static RestrictionGuard Parse(string json, IngredientCatalog catalog)
    {
        var document = JsonSerializer.Deserialize<RuleDocument>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Restriction rules are empty.");
        var categories = catalog.Items.Select(item => item.Category).ToHashSet(StringComparer.Ordinal);
        var groups = document.Groups ?? [];
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        foreach (var entry in document.Rules ?? [])
        {
            var groupNames = entry.Groups ?? [];
            var parts = groupNames.Select(name => groups.TryGetValue(name, out var group) ? group : throw new InvalidOperationException($"Restriction rule '{entry.Code}' names unknown group '{name}'.")).Append(entry).ToList();
            var rule = new Rule(
                entry.Unverifiable,
                parts.SelectMany(part => part.Categories ?? []).ToHashSet(StringComparer.Ordinal),
                parts.SelectMany(part => part.Slugs ?? []).ToHashSet(StringComparer.Ordinal),
                parts.SelectMany(part => part.UnverifiedSlugs ?? []).ToHashSet(StringComparer.Ordinal),
                parts.SelectMany(part => part.Keywords ?? []).Select(IngredientText.Normalize).Where(word => word.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
                parts.SelectMany(part => part.Exceptions ?? []).Select(IngredientText.Normalize).Where(word => word.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(word => word.Length).ToList());
            foreach (var category in rule.Categories.Where(category => !categories.Contains(category)))
            {
                throw new InvalidOperationException($"Restriction rule '{entry.Code}' names unknown category '{category}'.");
            }

            foreach (var slug in rule.Slugs.Concat(rule.UnverifiedSlugs).Where(slug => !catalog.BySlug.ContainsKey(slug)))
            {
                throw new InvalidOperationException($"Restriction rule '{entry.Code}' names unknown slug '{slug}'.");
            }

            if (!rules.TryAdd(entry.Code, rule))
            {
                throw new InvalidOperationException($"Restriction rule '{entry.Code}' appears twice.");
            }
        }

        return new RestrictionGuard(rules, catalog);
    }

    private static bool Forbids(Rule rule, IngredientCatalogItem? item, string name)
    {
        if (item is not null && (rule.Categories.Contains(item.Category) || rule.Slugs.Contains(item.Slug)) && !IsException(rule, name))
        {
            return true;
        }

        var text = Words(name);
        foreach (var exception in rule.Exceptions)
        {
            text = text.Replace($" {Words(exception).Trim()} ", " ", StringComparison.Ordinal);
        }

        return rule.Keywords.Any(keyword =>
        {
            var word = Words(keyword).Trim();
            return text.Contains($" {word} ", StringComparison.Ordinal)
                || text.Contains($" {word}s ", StringComparison.Ordinal)
                || text.Contains($" {word}es ", StringComparison.Ordinal);
        });
    }

    private static bool IsException(Rule rule, string name)
    {
        var text = Words(name);
        return rule.Exceptions.Any(exception => text.Contains($" {Words(exception).Trim()} ", StringComparison.Ordinal));
    }

    /// <summary>Normalized text with punctuation as spaces and a space on each side, so whole words can be searched.</summary>
    private static string Words(string name)
    {
        var builder = new StringBuilder(" ");
        foreach (var character in IngredientText.Normalize(name))
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return builder.Append(' ').ToString();
    }

    private static RestrictionGuard LoadEmbedded()
    {
        const string resource = "Cocinaris.Modules.Ingredients.restriction-rules.json";
        using var stream = typeof(RestrictionGuard).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' was not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), IngredientCatalog.Default);
    }

    private sealed record Rule(bool Unverifiable, HashSet<string> Categories, HashSet<string> Slugs, HashSet<string> UnverifiedSlugs, List<string> Keywords, List<string> Exceptions);

    private sealed record RuleDocument(
        [property: JsonPropertyName("groups")] Dictionary<string, RulePart>? Groups,
        [property: JsonPropertyName("rules")] List<RulePart>? Rules);

    private sealed record RulePart(
        [property: JsonPropertyName("code")] string Code = "",
        [property: JsonPropertyName("unverifiable")] bool Unverifiable = false,
        [property: JsonPropertyName("groups")] List<string>? Groups = null,
        [property: JsonPropertyName("categories")] List<string>? Categories = null,
        [property: JsonPropertyName("slugs")] List<string>? Slugs = null,
        [property: JsonPropertyName("unverifiedSlugs")] List<string>? UnverifiedSlugs = null,
        [property: JsonPropertyName("keywords")] List<string>? Keywords = null,
        [property: JsonPropertyName("exceptions")] List<string>? Exceptions = null);
}
