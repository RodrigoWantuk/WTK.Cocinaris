/**
 * Exact wire-literal unions for closed-set ("controlled") profile fields.
 *
 * Unlike `src/features/profile/catalog` (a frontend-owned, non-exhaustive
 * convenience catalog for free-form stable codes), every value here is a hard
 * backend enum member or a hard-coded closed value object — see
 * `apps/backend/src/Cocinaris.Modules.Profiles/Domain/ProfileValues.cs`. The
 * backend rejects any other value with `validation_failed`, so these unions must
 * be kept in exact sync with that file, casing included:
 *
 * - `language`: `LanguageTag.TryCreate` (`en` | `pt-BR` | `es`).
 * - `region`: `RegionCode.TryCreate` (`US` | `BR` | `ES`).
 * - `currency`: `CurrencyCode.TryCreate` (`USD` | `BRL` | `EUR`).
 * - Everything else: a C# enum parsed case-insensitively via
 *   `ProfileEnumParser`, so the PascalCase member name is the canonical wire
 *   value this frontend always sends.
 *
 * `displayName` and `timeZone` are intentionally absent: both are open-ended
 * validated strings (`DisplayName`, `IanaTimeZoneId`), not closed enumerations,
 * and stay free-text controls.
 *
 * This module is imported both by the live adapter (`src/adapters/live/profile`,
 * to fail closed on an unrecognized wire value) and by the presentation layer
 * (`src/features/profile`, to render a closed `select`/`radio` control instead of
 * free text), so it lives in `src/contracts/profile` rather than either side.
 */

export const LANGUAGE_CODES = ["en", "pt-BR", "es"] as const;
export const REGION_CODES = ["US", "BR", "ES"] as const;
export const CURRENCY_CODES = ["USD", "BRL", "EUR"] as const;
export const MEASUREMENT_SYSTEM_CODES = ["Metric", "UsCustomary"] as const;
export const PLANNING_CADENCE_CODES = [
  "None",
  "Weekly",
  "Biweekly",
  "Monthly",
] as const;
export const SHOPPING_CADENCE_CODES = [
  "None",
  "Daily",
  "Weekly",
  "Biweekly",
  "Monthly",
] as const;
export const OVERALL_SKILL_CODES = [
  "Beginner",
  "Developing",
  "Comfortable",
  "Advanced",
] as const;
export const CONFIDENCE_CODES = ["Low", "Moderate", "High"] as const;
export const INSTRUCTION_DETAIL_CODES = [
  "Minimal",
  "Standard",
  "Detailed",
] as const;
/** Shared by `effortTolerance` and `cleanupTolerance` (`PreferenceTolerance`). */
export const TOLERANCE_CODES = ["Low", "Medium", "High"] as const;
export const REPEAT_MEAL_PREFERENCE_CODES = [
  "PreferVariety",
  "Neutral",
  "ComfortableRepeating",
] as const;
/** Shared shape reused by reheating/leftover/freezing preference (each a distinct backend enum with identical members). */
export const CARE_BAND_CODES = ["Avoid", "Neutral", "Comfortable"] as const;

/**
 * One entry per controlled household/cooking field: its allowed wire values and
 * the i18n option-group key used to resolve localized option labels (several
 * distinct backend enums share the exact same member set and are grouped under
 * one localization group to avoid duplicate translations).
 */
export const CONTROLLED_FIELDS = {
  language: { codes: LANGUAGE_CODES, group: "language" },
  region: { codes: REGION_CODES, group: "region" },
  currency: { codes: CURRENCY_CODES, group: "currency" },
  measurementSystem: {
    codes: MEASUREMENT_SYSTEM_CODES,
    group: "measurementSystem",
  },
  planningCadence: { codes: PLANNING_CADENCE_CODES, group: "planningCadence" },
  shoppingCadence: { codes: SHOPPING_CADENCE_CODES, group: "shoppingCadence" },
  overallSkill: { codes: OVERALL_SKILL_CODES, group: "overallSkill" },
  confidence: { codes: CONFIDENCE_CODES, group: "confidence" },
  preferredInstructionDetail: {
    codes: INSTRUCTION_DETAIL_CODES,
    group: "instructionDetail",
  },
  effortTolerance: { codes: TOLERANCE_CODES, group: "tolerance" },
  cleanupTolerance: { codes: TOLERANCE_CODES, group: "tolerance" },
  repeatMealPreference: {
    codes: REPEAT_MEAL_PREFERENCE_CODES,
    group: "repeatMealPreference",
  },
  reheatingPreference: { codes: CARE_BAND_CODES, group: "careBand" },
  leftoverPreference: { codes: CARE_BAND_CODES, group: "careBand" },
  freezingPreference: { codes: CARE_BAND_CODES, group: "careBand" },
} as const;

export type ControlledFieldName = keyof typeof CONTROLLED_FIELDS;

export const CONTROLLED_FIELD_NAMES = Object.keys(
  CONTROLLED_FIELDS,
) as ControlledFieldName[];

/** True when `name` is one of the closed-set controlled fields above. */
export function isControlledFieldName(
  name: string,
): name is ControlledFieldName {
  return Object.prototype.hasOwnProperty.call(CONTROLLED_FIELDS, name);
}

/** True when `value` is an exact, case-sensitive match for one of `field`'s allowed wire values. */
export function isValidControlledValue(
  field: ControlledFieldName,
  value: string,
): boolean {
  const codes: readonly string[] = CONTROLLED_FIELDS[field].codes;
  return codes.includes(value);
}

/** The i18n option-group key used to resolve `profile.data.option.<group>.<code>` labels for `field`. */
export function controlledFieldGroup(field: ControlledFieldName): string {
  return CONTROLLED_FIELDS[field].group;
}
