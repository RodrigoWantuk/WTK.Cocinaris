using Cocinaris.Modules.Profiles.Domain;

namespace Cocinaris.Modules.Profiles.Application;

/// <summary>Redacts sensitive preference and restriction codes from privacy-minimizing history.</summary>
public static class ProfileHistoryRedaction
{
    /// <summary>A preference mutation already validated into canonical category and stable code values.</summary>
    /// <param name="Category">Canonical preference category enum.</param>
    /// <param name="StableCode">Canonical stable code.</param>
    /// <param name="Action">Validated preference command action.</param>
    public sealed record ValidatedPreferenceCommand(PreferenceCategory Category, StableCode StableCode, string Action);

    /// <summary>Builds privacy-safe, canonical history field codes from validated preference commands.</summary>
    /// <param name="commands">Validated preference commands.</param>
    /// <returns>Deterministic privacy-safe history field codes.</returns>
    public static IReadOnlyList<string> RedactPreferenceFieldCodes(IReadOnlyList<ValidatedPreferenceCommand> commands) =>
        commands
            .Select(RedactPreferenceCommand)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

    /// <summary>Builds one privacy-safe history field code from a validated preference command.</summary>
    /// <param name="category">Canonical preference category.</param>
    /// <param name="stableCode">Canonical stable code.</param>
    /// <param name="action">Validated preference command action.</param>
    /// <returns>A redacted marker for sensitive categories, otherwise Category:StableCode.</returns>
    public static string RedactPreferenceCommand(PreferenceCategory category, StableCode stableCode, string action)
    {
        if (category is PreferenceCategory.Allergy or PreferenceCategory.MedicalRestriction)
        {
            var isAllergy = category == PreferenceCategory.Allergy;
            return action switch
            {
                "add" => isAllergy ? "allergy_entry_added" : "medical_restriction_added",
                "remove" => isAllergy ? "allergy_entry_removed" : "medical_restriction_removed",
                "update" => isAllergy ? "allergy_entry_changed" : "medical_restriction_changed",
                _ => isAllergy ? "allergy_entry_changed" : "medical_restriction_changed"
            };
        }

        return $"{category}:{stableCode.Value}";
    }

    private static string RedactPreferenceCommand(ValidatedPreferenceCommand command) =>
        RedactPreferenceCommand(command.Category, command.StableCode, command.Action);

    /// <summary>Builds deterministic canonical equipment history codes from validated stable codes.</summary>
    /// <param name="stableCodes">Canonical stable code values.</param>
    /// <returns>Distinct ordered stable codes suitable for change history.</returns>
    public static IReadOnlyList<string> CanonicalEquipmentFieldCodes(IEnumerable<string> stableCodes) =>
        stableCodes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
}
