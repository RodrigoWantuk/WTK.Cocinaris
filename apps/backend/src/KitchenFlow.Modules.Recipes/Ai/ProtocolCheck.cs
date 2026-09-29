using System.Text.Json;
using System.Text.RegularExpressions;

namespace KitchenFlow.Modules.Recipes.Ai;

/// <summary>
/// Structural bound-checking helpers shared by the protocol 0.3 response validators. Every check
/// mirrors one constraint from <c>packages/contracts/ai/recipe/schemas</c> so the .NET validators
/// stay in lock-step with the closed JSON Schema 2020-12 contracts that remain the source of truth.
/// </summary>
public static partial class ProtocolCheck
{
    /// <summary>Pattern for the shared <c>stableId</c> schema definition.</summary>
    public static readonly Regex StableId = StableIdPattern();

    /// <summary>Allowed canonical units.</summary>
    public static readonly HashSet<string> CanonicalUnits = new(StringComparer.Ordinal) { "g", "ml", "unit" };

    /// <summary>Reports an <c>additionalProperties: false</c> violation when extension data was captured.</summary>
    public static void RejectAdditionalProperties(List<string> errors, string path, Dictionary<string, JsonElement>? extra)
    {
        if (extra is { Count: > 0 })
        {
            errors.Add($"{path} has unexpected properties: {string.Join(", ", extra.Keys)}");
        }
    }

    /// <summary>Validates a required non-empty string against inclusive length bounds.</summary>
    public static void StringLength(List<string> errors, string path, string? value, int minLength, int maxLength)
    {
        if (value is null || value.Length < minLength || value.Length > maxLength)
        {
            errors.Add($"{path} must be a string with length between {minLength} and {maxLength}");
        }
    }

    /// <summary>Validates a required stable identifier string.</summary>
    public static void StableIdValue(List<string> errors, string path, string? value)
    {
        if (value is null || value.Length is < 1 or > 64 || !StableId.IsMatch(value))
        {
            errors.Add($"{path} must be a valid stableId");
        }
    }

    /// <summary>Validates a value is a member of an allowed enum set.</summary>
    public static void EnumValue(List<string> errors, string path, string? value, IReadOnlyCollection<string> allowed)
    {
        if (value is null || !allowed.Contains(value))
        {
            errors.Add($"{path} must be one of: {string.Join(", ", allowed)}");
        }
    }

    /// <summary>Validates a required inclusive integer range.</summary>
    public static void IntegerRange(List<string> errors, string path, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add($"{path} must be between {min} and {max}");
        }
    }

    /// <summary>Validates the shared bounded positive <c>quantity</c> schema definition.</summary>
    public static void QuantityValue(List<string> errors, string path, decimal value)
    {
        if (value <= 0 || value > 100_000)
        {
            errors.Add($"{path} must be greater than 0 and at most 100000");
        }
    }

    /// <summary>Validates a required array item-count range.</summary>
    public static void ArrayLength<T>(List<string> errors, string path, IReadOnlyList<T>? value, int minItems, int maxItems)
    {
        if (value is null || value.Count < minItems || value.Count > maxItems)
        {
            errors.Add($"{path} must contain between {minItems} and {maxItems} items");
        }
    }

    /// <summary>Validates a required constant string value.</summary>
    public static void Const(List<string> errors, string path, string? value, string expected)
    {
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            errors.Add($"{path} must equal '{expected}'");
        }
    }

    /// <summary>Validates every item of a required stableId array.</summary>
    public static void ArrayOfStableIds(List<string> errors, string path, IReadOnlyList<string> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            StableIdValue(errors, $"{path}[{index}]", values[index]);
        }
    }

    /// <summary>Validates every item of a required bounded-length string array.</summary>
    public static void ArrayOfStrings(List<string> errors, string path, IReadOnlyList<string> values, int maxLength)
    {
        for (var index = 0; index < values.Count; index++)
        {
            StringLength(errors, $"{path}[{index}]", values[index], 1, maxLength);
        }
    }

    /// <summary>Validates every item of a required enum-constrained string array.</summary>
    public static void ArrayOfEnums(List<string> errors, string path, IReadOnlyList<string> values, IReadOnlyCollection<string> allowed)
    {
        for (var index = 0; index < values.Count; index++)
        {
            EnumValue(errors, $"{path}[{index}]", values[index], allowed);
        }
    }

    /// <summary>Counts whitespace-delimited words, mirroring the shared TypeScript/JavaScript word-count rule.</summary>
    public static int WordCount(string? value) => string.IsNullOrWhiteSpace(value) ? 0 : value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$")]
    private static partial Regex StableIdPattern();
}
