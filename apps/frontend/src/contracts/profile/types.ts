/**
 * Application-owned profile, household, preferences, and equipment presentation models.
 *
 * These types are isolated from the generated OpenAPI DTO shapes consumed by the wire
 * client (`@cocinaris/api-client`). Nothing in the component tree may import generated
 * `components["schemas"][...]` types directly for profile data; adapters in
 * `src/adapters/live/profile` translate between the two boundaries.
 */

/**
 * How a progressive profile field is currently represented.
 *
 * - `absent`: the user has not supplied a value and no progressive default applies.
 * - `confirmed`: the user explicitly confirmed a durable value.
 * - `removed`: the user explicitly removed a previously supplied value.
 * - `default`: no value has been confirmed, but the backend resolves a progressive
 *   default (see {@link ProgressiveProfileField.defaultValue}). This is a real, reachable
 *   wire value (for example household counts and language before any explicit
 *   confirmation) and must not be treated as malformed input.
 */
export type ProfileFieldPresence =
  | "absent"
  | "confirmed"
  | "removed"
  | "default";

/**
 * Whether a value *projected from the backend* (a field already stored/resolved on
 * the profile) is durable profile state or a request-scoped temporary context
 * override. This is read-only wire information describing what the backend is
 * currently showing for that field; it is not a permission to write `temporary`
 * back on a mutation — see {@link ProfileMutationDurability} for what a mutation may
 * submit.
 */
export type ProfileFieldProjectionDurability = "durable" | "temporary";

/**
 * One progressive profile field: a value that may be absent, defaulted, confirmed, or
 * explicitly removed, alongside the durability of that value as projected from the
 * backend.
 */
export interface ProgressiveProfileField<T> {
  /** Resolved value for the current presence; `null` when absent or removed. */
  value: T | null;
  presence: ProfileFieldPresence;
  /** Progressive default the backend would apply while presence is `default`; `null` when none exists. */
  defaultValue: T | null;
  durability: ProfileFieldProjectionDurability;
}

/**
 * Explicit preference and restriction categories with distinct safety semantics.
 * Values match the backend `PreferenceCategory` enum PascalCase wire representation.
 * Allergy and MedicalRestriction require heightened, explicit user communication;
 * see `docs/product/audience-and-profile.md`.
 */
export type PreferenceCategory =
  | "Preference"
  | "Dislike"
  | "DietaryPattern"
  | "Intolerance"
  | "Allergy"
  | "ReligiousRestriction"
  | "EthicalRestriction"
  | "MedicalRestriction";

/** Household context fields, each independently progressive. */
export interface HouseholdSnapshot {
  defaultAdultCount: ProgressiveProfileField<number>;
  defaultChildCount: ProgressiveProfileField<number>;
  defaultServingCount: ProgressiveProfileField<number>;
  language: ProgressiveProfileField<string>;
  region: ProgressiveProfileField<string>;
  currency: ProgressiveProfileField<string>;
  measurementSystem: ProgressiveProfileField<string>;
  /** IANA timezone identifier; a browser-detected fallback must never be written here silently. */
  timeZone: ProgressiveProfileField<string>;
  planningCadence: ProgressiveProfileField<string>;
  shoppingCadence: ProgressiveProfileField<string>;
}

/** Cooking context, skill, and effort/cleanup/reheating preferences. */
export interface CookingContextSnapshot {
  overallSkill: ProgressiveProfileField<string>;
  confidence: ProgressiveProfileField<string>;
  preferredInstructionDetail: ProgressiveProfileField<string>;
  ordinaryPrepMinutes: ProgressiveProfileField<number>;
  exceptionalPrepMinutes: ProgressiveProfileField<number>;
  effortTolerance: ProgressiveProfileField<string>;
  cleanupTolerance: ProgressiveProfileField<string>;
  repeatMealPreference: ProgressiveProfileField<string>;
  reheatingPreference: ProgressiveProfileField<string>;
  leftoverPreference: ProgressiveProfileField<string>;
  freezingPreference: ProgressiveProfileField<string>;
}

/** Adult declaration and accepted terms/privacy versions for the current owner. */
export interface AdultDeclarationSnapshot {
  adultDeclared: boolean | null;
  termsVersion: string | null;
  privacyVersion: string | null;
  /** ISO 8601 instant; `null` when no declaration has been accepted. */
  acceptedAt: string | null;
  /** Backend `AdultDeclarationState` PascalCase string: `NotDeclared` | `Declared` | `Declined`. */
  state: string;
}

/**
 * Full owner profile projection, including whether a durable profile row exists.
 * `profileExists: false` must be presented as an absent-profile scaffold, never as an
 * error or as an implicitly created empty profile.
 */
export interface ProfileSnapshot {
  ownerUserId: string;
  displayName: ProgressiveProfileField<string>;
  household: HouseholdSnapshot;
  cookingContext: CookingContextSnapshot;
  adultDeclaration: AdultDeclarationSnapshot;
  knownTechniques: string[];
  techniquesToLearn: string[];
  goals: string[];
  abandonmentReasons: string[];
  /** True when a durable profile row is persisted for the authenticated owner. */
  profileExists: boolean;
  /** Opaque profile version; `null` when no profile has been persisted yet. */
  version: string | null;
  /** Raw `ETag` header value (with quotes) suitable for `If-Match`; `null` when absent. */
  etag: string | null;
  createdAt: string | null;
  updatedAt: string | null;
}

/** One preference or restriction entry as returned by the profile backend. Only `confirmed` entries are ever returned by GET. */
export interface PreferenceEntry {
  entryId: string;
  category: PreferenceCategory;
  stableCode: string;
  note: string | null;
  presence: ProfileFieldPresence;
  sortOrder: number;
}

/** Versioned preference/restriction collection; `version`/`etag` are `null` when no profile exists yet. */
export interface PreferenceSnapshot {
  version: string | null;
  etag: string | null;
  entries: PreferenceEntry[];
}

/** One equipment entry as returned by the profile backend. Order is significant (array position is canonical). */
export interface EquipmentEntry {
  entryId: string;
  stableCode: string;
  customName: string | null;
  capacity: number | null;
  capacityUnit: string | null;
  constraintNote: string | null;
  isActive: boolean;
  sortOrder: number;
}

/** Versioned equipment collection; `version`/`etag` are `null` when no profile exists yet. */
export interface EquipmentSnapshot {
  version: string | null;
  etag: string | null;
  entries: EquipmentEntry[];
}

/** Progressive completeness summary; never blocks inventory or home use. */
export interface ProfileCompleteness {
  percentComplete: number;
  completedSections: number;
  totalSections: number;
  /** Per-section confirmed/active counts keyed by backend-owned section name (for example `household`, `cooking`, `preferences`, `equipment`, `adultDeclaration`). */
  sectionCounts: Record<string, number>;
  /** Backend `AdultDeclarationState` PascalCase string. */
  adultDeclarationState: string;
  profileExists: boolean;
}

/**
 * Combines profile, preferences, equipment, and completeness for a single owner into
 * one workspace. `version`/`etag` mirror the profile snapshot's concurrency token; the
 * preferences and equipment collections carry their own `version`/`etag` because their
 * PUT endpoints can be called independently and may briefly diverge from the profile
 * snapshot already held in memory.
 */
export interface ProfileWorkspace {
  profile: ProfileSnapshot;
  preferences: PreferenceSnapshot;
  equipment: EquipmentSnapshot;
  completeness: ProfileCompleteness;
  /** Mirrors `profile.version`. */
  version: string | null;
  /** Mirrors `profile.etag`. */
  etag: string | null;
}

/**
 * Explicit action requested for one progressive field mutation.
 * `absent` is only meaningful for full-replace (PUT) semantics; PATCH treats an omitted
 * field as "leave untouched" rather than as an `absent` action.
 */
export type FieldMutationAction = "confirm" | "remove" | "absent";

/**
 * Durability a mutation may submit for a field. The backend mutation endpoints only
 * ever accept `durable` (see the generated `ProfileFieldDurability` wire schema,
 * which is the single literal `"durable"`); there is currently no request-scoped
 * temporary write path, so this type intentionally excludes
 * {@link ProfileFieldProjectionDurability}'s `"temporary"` member rather than
 * widening to it.
 */
export type ProfileMutationDurability = "durable";

/** One field-level mutation submitted to the profile mutation endpoints. */
export interface ProfileFieldMutation<T> {
  action: FieldMutationAction;
  /** Required when `action` is `confirm`; ignored otherwise. */
  value?: T | null;
  /** Defaults to `durable` server-side when omitted; only `durable` is accepted. */
  durability?: ProfileMutationDurability;
}

/** Adult declaration mutation; acceptance is only recorded through this explicit command. */
export interface AdultDeclarationMutation {
  adultDeclared: boolean;
  termsVersion: string;
  privacyVersion?: string | null;
}

/**
 * Partial profile mutation body for `PATCH /api/v1/profile`.
 * Every field is optional: an omitted field is left untouched by PATCH semantics.
 * Ordered lists (`knownTechniques`, `techniquesToLearn`, `goals`, `abandonmentReasons`)
 * are whole-list replacements when present; omit them to leave the persisted list untouched.
 */
export interface ProfilePatch {
  displayName?: ProfileFieldMutation<string>;
  defaultAdultCount?: ProfileFieldMutation<number>;
  defaultChildCount?: ProfileFieldMutation<number>;
  defaultServingCount?: ProfileFieldMutation<number>;
  language?: ProfileFieldMutation<string>;
  region?: ProfileFieldMutation<string>;
  currency?: ProfileFieldMutation<string>;
  measurementSystem?: ProfileFieldMutation<string>;
  timeZone?: ProfileFieldMutation<string>;
  planningCadence?: ProfileFieldMutation<string>;
  shoppingCadence?: ProfileFieldMutation<string>;
  overallSkill?: ProfileFieldMutation<string>;
  confidence?: ProfileFieldMutation<string>;
  preferredInstructionDetail?: ProfileFieldMutation<string>;
  ordinaryPrepMinutes?: ProfileFieldMutation<number>;
  exceptionalPrepMinutes?: ProfileFieldMutation<number>;
  effortTolerance?: ProfileFieldMutation<string>;
  cleanupTolerance?: ProfileFieldMutation<string>;
  repeatMealPreference?: ProfileFieldMutation<string>;
  reheatingPreference?: ProfileFieldMutation<string>;
  leftoverPreference?: ProfileFieldMutation<string>;
  freezingPreference?: ProfileFieldMutation<string>;
  adultDeclaration?: AdultDeclarationMutation | null;
  knownTechniques?: string[];
  techniquesToLearn?: string[];
  goals?: string[];
  abandonmentReasons?: string[];
}

/** Explicit preference/restriction command submitted to `PUT /api/v1/profile/preferences`. */
export interface PreferenceCommand {
  action: "add" | "remove" | "update";
  category: PreferenceCategory;
  stableCode: string;
  note?: string | null;
}

/** One equipment entry submitted to `PUT /api/v1/profile/equipment`. Array order is canonical. */
export interface EquipmentInput {
  stableCode: string;
  customName?: string | null;
  capacity?: number | null;
  capacityUnit?: string | null;
  constraintNote?: string | null;
}

/** Per-call context required for any profile mutation. */
export interface ProfileMutationContext {
  /** CSRF token issued by `GET /api/v1/session`. */
  csrfToken: string;
  /** Current known version/`ETag`; `null` on first create. Required (as `If-Match`) after first create. */
  etag: string | null;
  signal?: AbortSignal;
}

/** Stable, machine-readable profile error codes surfaced to application code. */
export type ProfileApiErrorCode =
  | "validation_failed"
  | "authentication_required"
  | "forbidden"
  | "conflict"
  | "profile_already_exists"
  | "precondition_failed"
  | "domain_rule_violated"
  | "precondition_required"
  | "unavailable"
  | "cancelled"
  | "malformed"
  | "unexpected"
  /**
   * Client-side only: never returned by the backend. Thrown by `ProfileProvider`
   * when a mutation is attempted while the shared workspace is not `ready` (still
   * loading, in `version_conflict`, or blocked by a failed post-mutation
   * refresh) — see `ProfileProvider.tsx`.
   */
  | "workspace_not_ready";

/**
 * Thrown by profile repositories and mappers for every failure path. Deterministic
 * code, not raw HTTP status, is the contract application code should branch on.
 */
export class ProfileApiError extends Error {
  readonly code: ProfileApiErrorCode;
  readonly status: number;
  readonly fieldErrors?: Record<string, string[]>;
  /** Correlation id from Problem Details, suitable for support without exposing private request data. */
  readonly traceId?: string;
  /** True when the caller may safely retry the same request without side effects (for example `unavailable`). */
  readonly retryable?: boolean;

  constructor(
    code: ProfileApiErrorCode,
    message: string,
    status: number,
    options?: {
      fieldErrors?: Record<string, string[]>;
      traceId?: string;
      retryable?: boolean;
    },
  ) {
    super(message);
    this.name = "ProfileApiError";
    this.code = code;
    this.status = status;
    this.fieldErrors = options?.fieldErrors;
    this.traceId = options?.traceId;
    this.retryable = options?.retryable;
  }
}
