using System.Text.Json;

namespace KitchenFlow.Modules.Recipes.Ai;

/// <summary>
/// Minimal defensive accessors for traversing untrusted, loosely structured request JSON (for example
/// request evaluation fixtures under <c>docs/ai/examples</c>) during semantic cross-checks.
/// </summary>
internal static class JsonElementExtensions
{
    /// <summary>Returns a nested property, or <see langword="null"/> if absent or the parent is not an object.</summary>
    public static JsonElement? Prop(this JsonElement element, string name) => Prop((JsonElement?)element, name);

    /// <summary>Returns a nested property, or <see langword="null"/> if absent or the parent is not an object.</summary>
    public static JsonElement? Prop(this JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty(name, out var result))
        {
            return null;
        }

        return result;
    }

    /// <summary>Returns the string value, or <see langword="null"/> if absent or not a string.</summary>
    public static string? AsString(this JsonElement? element) => element is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>Returns the numeric value, or <see langword="null"/> if absent or not a number.</summary>
    public static double? AsNumber(this JsonElement? element) => element is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

    /// <summary>Returns array items, or an empty sequence if absent or not an array.</summary>
    public static IReadOnlyList<JsonElement> AsArray(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray().ToList() : [];
}
