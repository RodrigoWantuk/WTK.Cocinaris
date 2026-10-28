import type { components } from "@cocinaris/api-client";
import {
  ProfileApiError,
  isValidControlledValue,
  type AdultDeclarationSnapshot,
  type ControlledFieldName,
  type CookingContextSnapshot,
  type EquipmentEntry,
  type EquipmentInput,
  type EquipmentSnapshot,
  type HouseholdSnapshot,
  type PreferenceCategory,
  type PreferenceCommand,
  type PreferenceEntry,
  type PreferenceSnapshot,
  type ProfileCompleteness,
  type ProfileFieldMutation,
  type ProfileFieldProjectionDurability,
  type ProfileFieldPresence,
  type ProfilePatch,
  type ProgressiveProfileField,
  type ProfileSnapshot,
} from "@/contracts/profile";

type ProfileResponseDto = components["schemas"]["ProfileResponse"];
type HouseholdDto = components["schemas"]["HouseholdDto"];
type CookingContextDto = components["schemas"]["CookingContextDto"];
type AdultDeclarationDto = components["schemas"]["AdultDeclarationDto"];
type FieldDtoOfString = components["schemas"]["ProfileFieldDtoOfstring"];
type FieldDtoOfInt = components["schemas"]["ProfileFieldDtoOfint"];
type PreferencesCollectionResponseDto =
  components["schemas"]["PreferencesCollectionResponse"];
type PreferenceResponseDto = components["schemas"]["PreferenceResponse"];
type EquipmentCollectionResponseDto =
  components["schemas"]["EquipmentCollectionResponse"];
type EquipmentResponseDto = components["schemas"]["EquipmentResponse"];
type ProfileCompletenessResponseDto =
  components["schemas"]["ProfileCompletenessResponse"];
type ProfileMutationRequestDto =
  components["schemas"]["ProfileMutationRequest"];
type FieldMutationDtoOfString =
  components["schemas"]["FieldMutationDtoOfstring"];
type FieldMutationDtoOfInt = components["schemas"]["FieldMutationDtoOfint"];
type PreferencesRequestDto = components["schemas"]["PreferencesRequest"];
type EquipmentRequestDto = components["schemas"]["EquipmentRequest"];

const KNOWN_PRESENCE = new Set<ProfileFieldPresence>([
  "absent",
  "confirmed",
  "removed",
  "default",
]);

const KNOWN_PROJECTION_DURABILITY = new Set<ProfileFieldProjectionDurability>([
  "durable",
  "temporary",
]);

const KNOWN_CATEGORIES = new Set<PreferenceCategory>([
  "Preference",
  "Dislike",
  "DietaryPattern",
  "Intolerance",
  "Allergy",
  "ReligiousRestriction",
  "EthicalRestriction",
  "MedicalRestriction",
]);

function malformed(detail: string): never {
  throw new ProfileApiError("malformed", detail, 502);
}

/**
 * Normalizes a presence value from the wire. The backend emits lowercase presence
 * strings (`absent`, `confirmed`, `removed`, `default`); this also tolerates
 * unexpected casing defensively but fails closed for anything else.
 */
function normalizePresence(raw: string): ProfileFieldPresence {
  const lowered = raw.toLowerCase() as ProfileFieldPresence;
  if (!KNOWN_PRESENCE.has(lowered)) {
    malformed(`Unknown profile field presence "${raw}".`);
  }
  return lowered;
}

/**
 * Normalizes a projected field's durability value from the wire (a `GET` response,
 * not a mutation request). The backend currently emits the literal string `durable`
 * and reserves `temporary` for values resolved from request-scoped context; both
 * `Durable`/`Temporary` and lowercase casings are tolerated, but any other value
 * fails closed rather than being silently coerced.
 */
function normalizeProjectionDurability(
  raw: string,
): ProfileFieldProjectionDurability {
  const lowered = raw.toLowerCase() as ProfileFieldProjectionDurability;
  if (!KNOWN_PROJECTION_DURABILITY.has(lowered)) {
    malformed(`Unknown profile field durability "${raw}".`);
  }
  return lowered;
}

function normalizeCategory(raw: string): PreferenceCategory {
  if (!KNOWN_CATEGORIES.has(raw as PreferenceCategory)) {
    malformed(`Unknown preference category "${raw}".`);
  }
  return raw as PreferenceCategory;
}

/** OpenAPI int32 wire pattern for integer-or-string numeric fields. */
const INT32_WIRE_PATTERN = /^-?(?:0|[1-9]\d*)$/;
const INT32_MIN = -2147483648;
const INT32_MAX = 2147483647;

/**
 * Parses an optional finite number from the wire. `null`/`undefined` stay `null`.
 * A present empty/whitespace string, NaN, Infinity, or non-numeric value fails closed
 * rather than becoming `0` via `Number("")`.
 */
function mapOptionalFiniteNumber(
  raw: number | string | null | undefined,
  path: string,
): number | null {
  if (raw === null || raw === undefined) {
    return null;
  }
  if (typeof raw === "string") {
    const trimmed = raw.trim();
    if (trimmed === "") {
      malformed(`Malformed optional numeric value at ${path}.`);
    }
    const value = Number(trimmed);
    if (!Number.isFinite(value)) {
      malformed(`Malformed optional numeric value at ${path}.`);
    }
    return value;
  }
  if (typeof raw !== "number" || !Number.isFinite(raw)) {
    malformed(`Malformed optional numeric value at ${path}.`);
  }
  return raw;
}

/**
 * Parses a required finite integer from the wire. Rejects nullish, blank strings,
 * non-integers, non-numeric strings, NaN/Infinity, and values outside optional bounds.
 * Never uses `Number("")` → `0` coercion.
 */
function mapRequiredFiniteInteger(
  raw: number | string | null | undefined,
  path: string,
  bounds?: { min?: number; max?: number },
): number {
  if (raw === null || raw === undefined) {
    malformed(`Missing required integer at ${path}.`);
  }
  let value: number;
  if (typeof raw === "string") {
    const trimmed = raw.trim();
    if (trimmed === "" || !INT32_WIRE_PATTERN.test(trimmed)) {
      malformed(`Malformed required integer at ${path}.`);
    }
    value = Number(trimmed);
  } else if (typeof raw === "number") {
    value = raw;
  } else {
    malformed(`Malformed required integer at ${path}.`);
  }
  if (!Number.isFinite(value) || !Number.isInteger(value)) {
    malformed(`Malformed required integer at ${path}.`);
  }
  if (value < INT32_MIN || value > INT32_MAX) {
    malformed(`Required integer out of int32 range at ${path}.`);
  }
  if (bounds?.min != null && value < bounds.min) {
    malformed(`Required integer below minimum at ${path}.`);
  }
  if (bounds?.max != null && value > bounds.max) {
    malformed(`Required integer above maximum at ${path}.`);
  }
  return value;
}

function mapField<T>(dto: {
  value: T | null;
  presence: string;
  defaultValue: T | null;
  durability: string;
}): ProgressiveProfileField<T> {
  return {
    value: dto.value ?? null,
    presence: normalizePresence(dto.presence),
    defaultValue: dto.defaultValue ?? null,
    durability: normalizeProjectionDurability(dto.durability),
  };
}

function mapIntField(
  dto: FieldDtoOfInt,
  path: string,
): ProgressiveProfileField<number> {
  return {
    value: mapOptionalFiniteNumber(dto.value, `${path}.value`),
    presence: normalizePresence(dto.presence),
    defaultValue: mapOptionalFiniteNumber(
      dto.defaultValue,
      `${path}.defaultValue`,
    ),
    durability: normalizeProjectionDurability(dto.durability),
  };
}

function mapStringField(
  dto: FieldDtoOfString,
): ProgressiveProfileField<string> {
  return mapField(dto);
}

/**
 * Maps a closed-set ("controlled") household/cooking string field — see
 * `@/contracts/profile/controlledCodes` — failing closed on any `value` or
 * `defaultValue` that is not an exact member of that field's backend enum.
 * A backend that starts emitting a new enum member the frontend does not yet
 * know about must surface as `malformed` rather than being silently rendered
 * or, worse, silently re-submitted on the next unrelated save.
 */
function mapControlledStringField(
  dto: FieldDtoOfString,
  field: ControlledFieldName,
): ProgressiveProfileField<string> {
  if (dto.value != null && !isValidControlledValue(field, dto.value)) {
    malformed(`Unknown ${field} value "${dto.value}".`);
  }
  if (
    dto.defaultValue != null &&
    !isValidControlledValue(field, dto.defaultValue)
  ) {
    malformed(`Unknown ${field} default value "${dto.defaultValue}".`);
  }
  return mapField(dto);
}

function mapHousehold(dto: HouseholdDto): HouseholdSnapshot {
  return {
    defaultAdultCount: mapIntField(
      dto.defaultAdultCount,
      "household.defaultAdultCount",
    ),
    defaultChildCount: mapIntField(
      dto.defaultChildCount,
      "household.defaultChildCount",
    ),
    defaultServingCount: mapIntField(
      dto.defaultServingCount,
      "household.defaultServingCount",
    ),
    language: mapControlledStringField(dto.language, "language"),
    region: mapControlledStringField(dto.region, "region"),
    currency: mapControlledStringField(dto.currency, "currency"),
    measurementSystem: mapControlledStringField(
      dto.measurementSystem,
      "measurementSystem",
    ),
    // Open-ended validated string (IanaTimeZoneId), not a closed enum.
    timeZone: mapStringField(dto.timeZone),
    planningCadence: mapControlledStringField(
      dto.planningCadence,
      "planningCadence",
    ),
    shoppingCadence: mapControlledStringField(
      dto.shoppingCadence,
      "shoppingCadence",
    ),
  };
}

function mapCookingContext(dto: CookingContextDto): CookingContextSnapshot {
  return {
    overallSkill: mapControlledStringField(dto.overallSkill, "overallSkill"),
    confidence: mapControlledStringField(dto.confidence, "confidence"),
    preferredInstructionDetail: mapControlledStringField(
      dto.preferredInstructionDetail,
      "preferredInstructionDetail",
    ),
    ordinaryPrepMinutes: mapIntField(
      dto.ordinaryPrepMinutes,
      "cookingContext.ordinaryPrepMinutes",
    ),
    exceptionalPrepMinutes: mapIntField(
      dto.exceptionalPrepMinutes,
      "cookingContext.exceptionalPrepMinutes",
    ),
    effortTolerance: mapControlledStringField(
      dto.effortTolerance,
      "effortTolerance",
    ),
    cleanupTolerance: mapControlledStringField(
      dto.cleanupTolerance,
      "cleanupTolerance",
    ),
    repeatMealPreference: mapControlledStringField(
      dto.repeatMealPreference,
      "repeatMealPreference",
    ),
    reheatingPreference: mapControlledStringField(
      dto.reheatingPreference,
      "reheatingPreference",
    ),
    leftoverPreference: mapControlledStringField(
      dto.leftoverPreference,
      "leftoverPreference",
    ),
    freezingPreference: mapControlledStringField(
      dto.freezingPreference,
      "freezingPreference",
    ),
  };
}

function mapAdultDeclaration(
  dto: AdultDeclarationDto,
): AdultDeclarationSnapshot {
  return {
    adultDeclared: dto.adultDeclared ?? null,
    termsVersion: dto.termsVersion ?? null,
    privacyVersion: dto.privacyVersion ?? null,
    acceptedAt: dto.acceptedAt ?? null,
    state: dto.state,
  };
}

/**
 * Maps a full profile response. Fails closed (throws {@link ProfileApiError} with
 * code `malformed`) on any unrecognized presence or durability value rather than
 * silently guessing a safe default.
 */
export function mapProfileResponse(
  dto: ProfileResponseDto,
  etag: string | null,
): ProfileSnapshot {
  return {
    ownerUserId: dto.ownerUserId,
    displayName: mapStringField(dto.displayName),
    household: mapHousehold(dto.household),
    cookingContext: mapCookingContext(dto.cookingContext),
    adultDeclaration: mapAdultDeclaration(dto.adultDeclaration),
    knownTechniques: [...dto.knownTechniques],
    techniquesToLearn: [...dto.techniquesToLearn],
    goals: [...dto.goals],
    abandonmentReasons: [...dto.abandonmentReasons],
    profileExists: dto.profileExists,
    version: dto.version ?? null,
    // Never invent an ETag from the body version — missing headers stay null so
    // workspace consistency can fail closed on header/body disagreement.
    etag: dto.profileExists ? etag : null,
    createdAt: dto.createdAt ?? null,
    updatedAt: dto.updatedAt ?? null,
  };
}

function mapPreferenceEntry(dto: PreferenceResponseDto): PreferenceEntry {
  return {
    entryId: dto.entryId,
    category: normalizeCategory(dto.category),
    stableCode: dto.stableCode,
    note: dto.note ?? null,
    presence: normalizePresence(dto.presence),
    sortOrder: mapRequiredFiniteInteger(
      dto.sortOrder,
      "preferences.sortOrder",
      {
        min: 0,
      },
    ),
  };
}

/** Maps a versioned preferences collection. `version`/`etag` are `null` when no profile exists yet. */
export function mapPreferencesCollection(
  dto: PreferencesCollectionResponseDto,
  etag: string | null,
): PreferenceSnapshot {
  const version = dto.version ?? null;
  return {
    version,
    etag: version === null ? null : etag,
    entries: dto.entries.map(mapPreferenceEntry),
  };
}

function mapEquipmentEntry(dto: EquipmentResponseDto): EquipmentEntry {
  return {
    entryId: dto.entryId,
    stableCode: dto.stableCode,
    customName: dto.customName ?? null,
    capacity: mapOptionalFiniteNumber(dto.capacity, "equipment.capacity"),
    capacityUnit: dto.capacityUnit ?? null,
    constraintNote: dto.constraintNote ?? null,
    isActive: dto.isActive,
    sortOrder: mapRequiredFiniteInteger(dto.sortOrder, "equipment.sortOrder", {
      min: 0,
    }),
  };
}

/** Maps a versioned equipment collection. `version`/`etag` are `null` when no profile exists yet. */
export function mapEquipmentCollection(
  dto: EquipmentCollectionResponseDto,
  etag: string | null,
): EquipmentSnapshot {
  const version = dto.version ?? null;
  return {
    version,
    etag: version === null ? null : etag,
    entries: dto.entries.map(mapEquipmentEntry),
  };
}

/** Maps a progressive completeness summary. Section counts are backend-owned, opaque keys. */
export function mapCompleteness(
  dto: ProfileCompletenessResponseDto,
): ProfileCompleteness {
  const sectionCounts: Record<string, number> = {};
  for (const [key, value] of Object.entries(dto.sectionCounts)) {
    sectionCounts[key] = mapRequiredFiniteInteger(
      value,
      `completeness.sectionCounts.${key}`,
      { min: 0 },
    );
  }
  return {
    percentComplete: mapRequiredFiniteInteger(
      dto.percentComplete,
      "completeness.percentComplete",
      { min: 0, max: 100 },
    ),
    completedSections: mapRequiredFiniteInteger(
      dto.completedSections,
      "completeness.completedSections",
      { min: 0 },
    ),
    totalSections: mapRequiredFiniteInteger(
      dto.totalSections,
      "completeness.totalSections",
      { min: 0 },
    ),
    sectionCounts,
    adultDeclarationState: dto.adultDeclarationState,
    profileExists: dto.profileExists,
  };
}

/**
 * Validates and narrows a mutation's requested durability to the single wire literal
 * the generated `ProfileFieldDurability` schema accepts (`"durable"`). There is
 * currently no request-scoped temporary write path: an omitted durability maps to
 * `undefined` (the backend defaults to `durable`), and any other value — in
 * particular a `"temporary"` that reached this far from untrusted runtime input
 * rather than being rejected earlier by the contract's own `ProfileMutationDurability`
 * type — fails closed instead of being cast onto the wire.
 */
function toWireMutationDurability(
  durability: ProfileFieldMutation<unknown>["durability"],
): "durable" | undefined {
  if (durability === undefined) {
    return undefined;
  }
  if (durability !== "durable") {
    malformed(
      `Profile field mutations only accept "durable" durability; received "${String(durability)}".`,
    );
  }
  return durability;
}

/** Converts one presentation field mutation to its wire shape. */
function toStringMutationDto(
  mutation: ProfileFieldMutation<string> | undefined,
): FieldMutationDtoOfString | null {
  if (!mutation) {
    return null;
  }
  return {
    action: mutation.action,
    value: mutation.value ?? null,
    durability: toWireMutationDurability(mutation.durability),
  };
}

function toIntMutationDto(
  mutation: ProfileFieldMutation<number> | undefined,
): FieldMutationDtoOfInt | null {
  if (!mutation) {
    return null;
  }
  return {
    action: mutation.action,
    value: mutation.value ?? null,
    durability: toWireMutationDurability(mutation.durability),
  };
}

/**
 * Maps a presentation {@link ProfilePatch} to the wire `ProfileMutationRequest` body
 * shared by both `PATCH` (partial update) and `PUT` (full replace) profile endpoints.
 * An omitted patch field becomes `null` on the wire, which PATCH treats as "leave
 * untouched" and PUT treats as "reset to absent" — the same request shape carries
 * different semantics depending on which endpoint receives it.
 */
export function mapProfilePatchToRequest(
  patch: ProfilePatch,
): ProfileMutationRequestDto {
  return {
    displayName: toStringMutationDto(patch.displayName),
    defaultAdultCount: toIntMutationDto(patch.defaultAdultCount),
    defaultChildCount: toIntMutationDto(patch.defaultChildCount),
    defaultServingCount: toIntMutationDto(patch.defaultServingCount),
    language: toStringMutationDto(patch.language),
    region: toStringMutationDto(patch.region),
    currency: toStringMutationDto(patch.currency),
    measurementSystem: toStringMutationDto(patch.measurementSystem),
    timeZone: toStringMutationDto(patch.timeZone),
    planningCadence: toStringMutationDto(patch.planningCadence),
    shoppingCadence: toStringMutationDto(patch.shoppingCadence),
    overallSkill: toStringMutationDto(patch.overallSkill),
    confidence: toStringMutationDto(patch.confidence),
    preferredInstructionDetail: toStringMutationDto(
      patch.preferredInstructionDetail,
    ),
    ordinaryPrepMinutes: toIntMutationDto(patch.ordinaryPrepMinutes),
    exceptionalPrepMinutes: toIntMutationDto(patch.exceptionalPrepMinutes),
    effortTolerance: toStringMutationDto(patch.effortTolerance),
    cleanupTolerance: toStringMutationDto(patch.cleanupTolerance),
    repeatMealPreference: toStringMutationDto(patch.repeatMealPreference),
    reheatingPreference: toStringMutationDto(patch.reheatingPreference),
    leftoverPreference: toStringMutationDto(patch.leftoverPreference),
    freezingPreference: toStringMutationDto(patch.freezingPreference),
    adultDeclaration: patch.adultDeclaration
      ? {
          adultDeclared: patch.adultDeclaration.adultDeclared,
          termsVersion: patch.adultDeclaration.termsVersion,
          privacyVersion: patch.adultDeclaration.privacyVersion ?? null,
        }
      : null,
    knownTechniques: patch.knownTechniques ?? null,
    techniquesToLearn: patch.techniquesToLearn ?? null,
    goals: patch.goals ?? null,
    abandonmentReasons: patch.abandonmentReasons ?? null,
  };
}

/** Maps explicit preference/restriction commands to the `PreferencesRequest` wire body. */
export function mapPreferenceCommandsToRequest(
  commands: PreferenceCommand[],
): PreferencesRequestDto {
  return {
    entries: commands.map((command) => ({
      action: command.action,
      category: command.category,
      stableCode: command.stableCode,
      note: command.note ?? null,
    })),
  };
}

/** Maps equipment inputs to the `EquipmentRequest` wire body. Array order is canonical. */
export function mapEquipmentInputsToRequest(
  entries: EquipmentInput[],
): EquipmentRequestDto {
  return {
    entries: entries.map((entry) => ({
      stableCode: entry.stableCode,
      customName: entry.customName ?? null,
      capacity: entry.capacity ?? null,
      capacityUnit: entry.capacityUnit ?? null,
      constraintNote: entry.constraintNote ?? null,
    })),
  };
}
