namespace Cocinaris.Modules.Profiles.Domain;

/// <summary>Describes how a progressive profile field is currently represented.</summary>
public enum ProfileFieldPresence
{
    /// <summary>The field has not been supplied by the user.</summary>
    Absent,
    /// <summary>The user explicitly confirmed a durable value.</summary>
    Confirmed,
    /// <summary>The user explicitly removed a previously supplied value.</summary>
    Removed
}

/// <summary>Describes whether a persisted profile value is durable or request-scoped temporary context.</summary>
public enum ProfileValueDurability
{
    /// <summary>The value is stored as durable profile state.</summary>
    Durable,
    /// <summary>The value applies only to the current request context and is not persisted.</summary>
    Temporary
}

/// <summary>Supported measurement systems for household and cooking context.</summary>
public enum MeasurementSystem
{
    /// <summary>Metric units such as grams and milliliters.</summary>
    Metric,
    /// <summary>United States customary units.</summary>
    UsCustomary
}

/// <summary>Planning cadence options for household meal planning.</summary>
public enum PlanningCadence
{
    /// <summary>No regular planning cadence selected.</summary>
    None,
    /// <summary>Weekly planning cadence.</summary>
    Weekly,
    /// <summary>Biweekly planning cadence.</summary>
    Biweekly,
    /// <summary>Monthly planning cadence.</summary>
    Monthly
}

/// <summary>Shopping cadence options for household grocery routines.</summary>
public enum ShoppingCadence
{
    /// <summary>No regular shopping cadence selected.</summary>
    None,
    /// <summary>Daily or near-daily shopping.</summary>
    Daily,
    /// <summary>Weekly shopping cadence.</summary>
    Weekly,
    /// <summary>Biweekly shopping cadence.</summary>
    Biweekly,
    /// <summary>Monthly shopping cadence.</summary>
    Monthly
}

/// <summary>Overall cooking skill bands.</summary>
public enum CookingSkillLevel
{
    /// <summary>Beginner skill band.</summary>
    Beginner,
    /// <summary>Developing skill band.</summary>
    Developing,
    /// <summary>Comfortable skill band.</summary>
    Comfortable,
    /// <summary>Advanced skill band.</summary>
    Advanced
}

/// <summary>Cooking confidence bands separate from overall skill.</summary>
public enum CookingConfidenceLevel
{
    /// <summary>Low confidence band.</summary>
    Low,
    /// <summary>Moderate confidence band.</summary>
    Moderate,
    /// <summary>High confidence band.</summary>
    High
}

/// <summary>Preferred recipe instruction detail level.</summary>
public enum InstructionDetailLevel
{
    /// <summary>Minimal instruction detail.</summary>
    Minimal,
    /// <summary>Standard instruction detail.</summary>
    Standard,
    /// <summary>Detailed instruction detail.</summary>
    Detailed
}

/// <summary>Ordered tolerance bands used by cooking-context preferences.</summary>
public enum PreferenceTolerance
{
    /// <summary>Low tolerance.</summary>
    Low,
    /// <summary>Medium tolerance.</summary>
    Medium,
    /// <summary>High tolerance.</summary>
    High
}

/// <summary>Repeat-meal preference bands.</summary>
public enum RepeatMealPreference
{
    /// <summary>Prefer variety over repeats.</summary>
    PreferVariety,
    /// <summary>Neutral repeat preference.</summary>
    Neutral,
    /// <summary>Comfortable repeating meals.</summary>
    ComfortableRepeating
}

/// <summary>Reheating preference bands.</summary>
public enum ReheatingPreference
{
    /// <summary>Prefer not to reheat meals.</summary>
    Avoid,
    /// <summary>Neutral reheating preference.</summary>
    Neutral,
    /// <summary>Comfortable reheating meals.</summary>
    Comfortable
}

/// <summary>Leftover preference bands.</summary>
public enum LeftoverPreference
{
    /// <summary>Prefer to avoid leftovers.</summary>
    Avoid,
    /// <summary>Neutral leftover preference.</summary>
    Neutral,
    /// <summary>Comfortable using leftovers.</summary>
    Comfortable
}

/// <summary>Freezing and preservation preference bands.</summary>
public enum FreezingPreference
{
    /// <summary>Prefer not to freeze meals.</summary>
    Avoid,
    /// <summary>Neutral freezing preference.</summary>
    Neutral,
    /// <summary>Comfortable freezing meals.</summary>
    Comfortable
}

/// <summary>Preference and restriction categories with distinct safety semantics.</summary>
public enum PreferenceCategory
{
    /// <summary>A positive food preference.</summary>
    Preference,
    /// <summary>A food dislike.</summary>
    Dislike,
    /// <summary>A dietary pattern such as vegetarianism.</summary>
    DietaryPattern,
    /// <summary>A food intolerance.</summary>
    Intolerance,
    /// <summary>A food allergy declared explicitly by the user.</summary>
    Allergy,
    /// <summary>A religious restriction declared explicitly by the user.</summary>
    ReligiousRestriction,
    /// <summary>An ethical restriction declared explicitly by the user.</summary>
    EthicalRestriction,
    /// <summary>A medical restriction declared explicitly by the user.</summary>
    MedicalRestriction
}

/// <summary>Technique list membership kind.</summary>
public enum TechniqueListKind
{
    /// <summary>A technique the user already knows.</summary>
    Known,
    /// <summary>A technique the user wants to learn.</summary>
    ToLearn
}

/// <summary>Adult declaration state for session-safe projections.</summary>
public enum AdultDeclarationState
{
    /// <summary>No adult declaration has been recorded.</summary>
    NotDeclared,
    /// <summary>The user declared adult capacity and accepted the required terms.</summary>
    Declared,
    /// <summary>The user explicitly declined or revoked adult capacity.</summary>
    Declined
}

/// <summary>Validated display name for a profile.</summary>
public sealed class DisplayName
{
    private DisplayName(string value) => Value = value;

    /// <summary>Gets the normalized display name.</summary>
    public string Value { get; }

    /// <summary>Attempts to create a bounded display name.</summary>
    public static bool TryCreate(string? raw, out DisplayName? name)
    {
        name = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length is < 1 or > 80)
        {
            return false;
        }

        name = new DisplayName(trimmed);
        return true;
    }
}

/// <summary>Validated IANA timezone identifier.</summary>
public sealed class IanaTimeZoneId
{
    private IanaTimeZoneId(string value) => Value = value;

    /// <summary>Gets the canonical timezone identifier.</summary>
    public string Value { get; }

    /// <summary>Attempts to validate a timezone identifier against known IANA zones.</summary>
    public static bool TryCreate(string? raw, out IanaTimeZoneId? timeZone)
    {
        timeZone = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length is < 1 or > 64)
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(trimmed);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }

        timeZone = new IanaTimeZoneId(trimmed);
        return true;
    }
}

/// <summary>Validated BCP 47 language tag.</summary>
public sealed class LanguageTag
{
    private LanguageTag(string value) => Value = value;

    /// <summary>Gets the normalized language tag.</summary>
    public string Value { get; }

    /// <summary>Attempts to create a supported language tag.</summary>
    public static bool TryCreate(string? raw, out LanguageTag? language)
    {
        language = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed is not ("en" or "pt-BR" or "es"))
        {
            return false;
        }

        language = new LanguageTag(trimmed);
        return true;
    }
}

/// <summary>Validated ISO 4217 currency code.</summary>
public sealed class CurrencyCode
{
    private CurrencyCode(string value) => Value = value;

    /// <summary>Gets the normalized currency code.</summary>
    public string Value { get; }

    /// <summary>Attempts to create a supported currency code.</summary>
    public static bool TryCreate(string? raw, out CurrencyCode? currency)
    {
        currency = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed is not ("USD" or "BRL" or "EUR"))
        {
            return false;
        }

        currency = new CurrencyCode(trimmed);
        return true;
    }
}

/// <summary>Validated region code.</summary>
public sealed class RegionCode
{
    private RegionCode(string value) => Value = value;

    /// <summary>Gets the normalized region code.</summary>
    public string Value { get; }

    /// <summary>Attempts to create a supported region code.</summary>
    public static bool TryCreate(string? raw, out RegionCode? region)
    {
        region = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed is not ("US" or "BR" or "ES"))
        {
            return false;
        }

        region = new RegionCode(trimmed);
        return true;
    }
}

/// <summary>Validated stable controlled code for preferences, equipment, techniques, and goals.</summary>
public sealed class StableCode
{
    private StableCode(string value) => Value = value;

    /// <summary>Gets the stable non-localized code.</summary>
    public string Value { get; }

    /// <summary>Attempts to create a bounded stable code.</summary>
    public static bool TryCreate(string? raw, out StableCode? code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length is < 2 or > 64 || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        code = new StableCode(trimmed);
        return true;
    }
}

/// <summary>Validated optional private note for explicit user commands.</summary>
public sealed class PrivateNote
{
    private PrivateNote(string value) => Value = value;

    /// <summary>Gets the note text.</summary>
    public string Value { get; }

    /// <summary>Attempts to create a bounded private note.</summary>
    public static bool TryCreate(string? raw, out PrivateNote? note)
    {
        note = null;
        if (raw is null)
        {
            return true;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > 500)
        {
            return false;
        }

        note = trimmed.Length == 0 ? null : new PrivateNote(trimmed);
        return true;
    }
}

/// <summary>Helper for parsing controlled enum names from transport input.</summary>
public static class ProfileEnumParser
{
    /// <summary>Attempts to parse a measurement system from transport input.</summary>
    public static bool TryParseMeasurementSystem(string? raw, out MeasurementSystem value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a planning cadence from transport input.</summary>
    public static bool TryParsePlanningCadence(string? raw, out PlanningCadence value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a shopping cadence from transport input.</summary>
    public static bool TryParseShoppingCadence(string? raw, out ShoppingCadence value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a cooking skill level from transport input.</summary>
    public static bool TryParseCookingSkillLevel(string? raw, out CookingSkillLevel value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a cooking confidence level from transport input.</summary>
    public static bool TryParseCookingConfidenceLevel(string? raw, out CookingConfidenceLevel value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse an instruction detail level from transport input.</summary>
    public static bool TryParseInstructionDetailLevel(string? raw, out InstructionDetailLevel value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a preference tolerance from transport input.</summary>
    public static bool TryParsePreferenceTolerance(string? raw, out PreferenceTolerance value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a repeat-meal preference from transport input.</summary>
    public static bool TryParseRepeatMealPreference(string? raw, out RepeatMealPreference value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a reheating preference from transport input.</summary>
    public static bool TryParseReheatingPreference(string? raw, out ReheatingPreference value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a leftover preference from transport input.</summary>
    public static bool TryParseLeftoverPreference(string? raw, out LeftoverPreference value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a freezing preference from transport input.</summary>
    public static bool TryParseFreezingPreference(string? raw, out FreezingPreference value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Attempts to parse a preference category from transport input.</summary>
    public static bool TryParsePreferenceCategory(string? raw, out PreferenceCategory value) =>
        Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);
}
