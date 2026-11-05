using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cocinaris.Modules.Ingredients;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>
/// Whether a suggested dish suits the meal it claims to be for (PLAN-0065). The model writes its own
/// meal label and put "snack" on spaghetti, so the label alone proves nothing. Pure and deterministic.
/// </summary>
/// <remarks>
/// It checks words in the name and format, and total time. Mass per serving is deliberately not used:
/// recipes count frying oil and water, so in the session this was tuned on, an accepted snack reached
/// 696 g. A dish the person's own saved recipes or typed words asked for is not checked.
/// </remarks>
public sealed partial class MealFit
{
    private static readonly Lazy<MealFit> Embedded = new(Load);
    private readonly Dictionary<string, MealRule> _rules;

    private MealFit(Dictionary<string, MealRule> rules) => _rules = rules;

    /// <summary>Gets the rules shipped with the module.</summary>
    public static MealFit Default => Embedded.Value;

    /// <summary>True when the dish suits the meal; an unknown meal type is not judged.</summary>
    /// <param name="mealType"><c>breakfast</c>, <c>lunch</c>, <c>dinner</c>, or <c>snack</c>.</param>
    /// <param name="name">Dish name.</param>
    /// <param name="dishFormat">The model's free-text format.</param>
    /// <param name="totalMinutes">Total time.</param>
    public bool Fits(string mealType, string name, string? dishFormat, int totalMinutes)
    {
        if (!_rules.TryGetValue(mealType is "lunch" or "dinner" ? "main" : mealType, out var rule))
        {
            return true;
        }

        if (rule.MaxTotalMinutes is { } limit && totalMinutes > limit)
        {
            return false;
        }

        // The two parts are checked apart, so a phrase never matches across them.
        return !rule.Blocked.IsMatch(IngredientText.Normalize(name)) && !rule.Blocked.IsMatch(IngredientText.Normalize(dishFormat ?? string.Empty));
    }

    private static MealFit Load()
    {
        const string resource = "Cocinaris.Modules.Planning.meal-fit-rules.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded resource {resource}.");
        var raw = JsonSerializer.Deserialize<Dictionary<string, RawRule>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Empty embedded resource {resource}.");
        return new MealFit(raw.ToDictionary(item => item.Key, item => new MealRule(item.Value.MaxTotalMinutes, Words(item.Value.BlockedWords))));
    }

    // Whole words only, so "prato" never matches inside another word.
    private static Regex Words(IEnumerable<string> words) =>
        new($"(?<![\\p{{L}}\\p{{N}}])(?:{string.Join('|', words.Select(Regex.Escape))})(?![\\p{{L}}\\p{{N}}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record RawRule(int? MaxTotalMinutes, IReadOnlyList<string> BlockedWords);

    private sealed record MealRule(int? MaxTotalMinutes, Regex Blocked);
}
