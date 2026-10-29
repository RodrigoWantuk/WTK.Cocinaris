using System.Text.RegularExpressions;

namespace Cocinaris.Modules.Recipes.Ai;

/// <summary>A soft quality issue of a schema-valid answer: a flag for the ledger and a repair instruction for the model.</summary>
/// <param name="Flag"><c>language</c> or <c>detail</c>.</param>
/// <param name="Instruction">Repair instruction sent back to the model.</param>
public sealed record RecipeQualityIssue(string Flag, string Instruction);

/// <summary>
/// Deterministic soft checks of a schema-valid answer against the requested output language and
/// instruction detail (PLAN-0040, owner feedback of 2026-10-29). A soft issue asks for the one
/// repair the operation allows, but never discards a valid answer: if the repair is no better, the
/// first valid answer is kept and the issue is recorded as a quality flag.
/// </summary>
/// <remarks>
/// Language is estimated from function words that are distinctive in Portuguese, Spanish, and
/// English. Recipe names and ingredient names are excluded because they often come from the
/// owner's pantry in any language. Short texts (fewer than 25 words) are not judged.
/// </remarks>
public static partial class RecipeOutputQuality
{
    private const int MinimumWords = 25;
    private const int DetailedMinimumStageCharacters = 120;
    private const int DetailedMinimumAverageCharacters = 200;
    private const int StandardMinimumStageCharacters = 50;

    private static readonly Dictionary<string, HashSet<string>> FunctionWords = new(StringComparer.Ordinal)
    {
        ["pt-BR"] = ["e", "com", "até", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas", "os", "ou", "ao", "à", "adicione", "mexa", "cozinhe", "aqueça", "depois", "enquanto", "quando", "não", "é", "são", "fogo", "panela", "minutos"],
        ["es"] = ["y", "con", "hasta", "del", "el", "en", "los", "las", "al", "añada", "agregue", "remueva", "cocine", "caliente", "luego", "mientras", "cuando", "no", "es", "son", "fuego", "olla", "sartén", "minutos"],
        ["en"] = ["the", "and", "with", "until", "of", "to", "in", "on", "into", "over", "add", "stir", "cook", "heat", "then", "while", "when", "is", "are", "it", "your", "pan", "pot", "minutes"]
    };

    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.Ordinal)
    {
        ["pt-BR"] = "Brazilian Portuguese",
        ["es"] = "Spanish",
        ["en"] = "English"
    };

    /// <summary>Checks candidate summaries against the requested language.</summary>
    public static IReadOnlyList<RecipeQualityIssue> CheckSuggest(SuggestCandidatesResponse response, string? outputLanguage) =>
        LanguageIssue(response.Candidates.Select(candidate => candidate.Summary), outputLanguage) is { } issue ? [issue] : [];

    /// <summary>Checks an expanded recipe's stages against the requested language and detail level.</summary>
    /// <param name="response">Schema-valid expansion.</param>
    /// <param name="outputLanguage">Requested language tag (<c>pt-BR</c>, <c>en</c>, <c>es</c>).</param>
    /// <param name="detailLevel">Requested detail: <c>Minimal</c>, <c>Standard</c>, or <c>Detailed</c>.</param>
    public static IReadOnlyList<RecipeQualityIssue> CheckExpand(ExpandSelectedResponse response, string? outputLanguage, string detailLevel)
    {
        var issues = new List<RecipeQualityIssue>();
        var stages = response.Recipe.Stages;
        if (LanguageIssue(stages.Select(stage => stage.Instructions).Concat(stages.SelectMany(stage => stage.SensoryCues)), outputLanguage) is { } language)
        {
            issues.Add(language);
        }

        var lengths = stages.Select(stage => stage.Instructions.Trim().Length).ToList();
        if (lengths.Count > 0)
        {
            var tooShort = detailLevel switch
            {
                "Detailed" => lengths.Min() < DetailedMinimumStageCharacters || lengths.Average() < DetailedMinimumAverageCharacters,
                "Minimal" => false,
                _ => lengths.Min() < StandardMinimumStageCharacters
            };
            if (tooShort)
            {
                issues.Add(new RecipeQualityIssue("detail", detailLevel == "Detailed"
                    ? $"instructionStyle is detailed, but some stages are too short. Rewrite every stage's instructions as a full walkthrough of at least {DetailedMinimumStageCharacters} characters: each action in order with quantities, utensil, heat, time, and how to tell it is done."
                    : $"Some stages are too short. Every stage's instructions must say what to do with which ingredients and quantities, the heat, and the time, in at least {StandardMinimumStageCharacters} characters."));
            }
        }

        return issues;
    }

    /// <summary>The detail level the request asked for, from the same rule as the request envelope.</summary>
    public static string DetailLevel(RecipeUserPresets presets) =>
        presets.InstructionDetail ?? (presets.CookingSkill is "Beginner" or "Developing" ? "Detailed" : "Standard");

    /// <summary>Estimates the language of the texts; null when it matches, is too short to judge, or no tag was requested.</summary>
    public static RecipeQualityIssue? LanguageIssue(IEnumerable<string> texts, string? outputLanguage)
    {
        var expected = outputLanguage ?? "pt-BR";
        if (!FunctionWords.ContainsKey(expected))
        {
            return null;
        }

        var words = texts.SelectMany(text => Word().Matches(text.ToLowerInvariant()).Select(match => match.Value)).ToList();
        if (words.Count < MinimumWords)
        {
            return null;
        }

        var scores = FunctionWords.ToDictionary(pair => pair.Key, pair => words.Count(pair.Value.Contains));
        var detected = scores.MaxBy(pair => pair.Value);
        // Only a clear majority of another language's function words counts; mixed text passes.
        if (detected.Key == expected || detected.Value < 5 || detected.Value < scores[expected] * 2)
        {
            return null;
        }

        return new RecipeQualityIssue("language", $"The human-readable text is in {LanguageNames[detected.Key]}, but outputLanguage is {expected}. Rewrite every human-readable string in {LanguageNames[expected]}, keeping all identifiers and structure unchanged.");
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
