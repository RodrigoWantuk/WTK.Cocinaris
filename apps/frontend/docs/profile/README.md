# Profile frontend (PLAN-0020)

Production profile, household, preferences, and equipment surfaces under `/app/perfil*`.

## Contract rules

- Absent profile: `GET /api/v1/profile` returns `profileExists: false` with progressive defaults — **not** HTTP 404.
- Ordinary edits use **PATCH**; PUT full replace is repository-only and must not be used by normal forms.
- Progressive fields preserve `value`, `presence` (`absent` | `confirmed` | `removed` | wire `default`), `defaultValue`, and `durability`.
- Preferences/equipment share the same profile aggregate concurrency token; after any successful mutation the workspace reloads all four sources and refreshes the session projection.
- Allergy and MedicalRestriction require explicit confirmation; never inferred.
- Custom preference/equipment codes use opaque `custom_<uuid>` forms; user text lives in `note` / `customName` only.
- Adult declaration mutation is gated by an injected `AdultDeclarationPolicy`. Production defaults to policy unavailable until accepted terms/privacy versions are configured.
- Household/cooking closed-set fields (`language`, `region`, `currency`, `measurementSystem`, `planningCadence`, `shoppingCadence`, `overallSkill`, `confidence`, `preferredInstructionDetail`, `effortTolerance`, `cleanupTolerance`, `repeatMealPreference`, `reheatingPreference`, `leftoverPreference`, `freezingPreference`) are exact backend PascalCase wire unions defined in `src/contracts/profile/controlledCodes.ts`; the adapter (`mapProfile.ts`) fails closed (`ProfileApiError` code `malformed`) on any `value`/`defaultValue` outside that union instead of silently rendering or re-submitting it. `displayName` and `timeZone` stay free text; counts and minutes stay numeric.

## Workspace consistency invariant

`ProfileProvider` loads profile, preferences, equipment, and completeness as **one coordinated unit** and never exposes a snapshot where they disagree:

- Absent profile (`profile.profileExists === false`): every version and `ETag` across the three collections must be `null`, and `completeness.profileExists` must also be `false`.
- Existing profile: every version/`ETag` must be non-null and identify the same aggregate version once `ETag`s are normalized (leading weak `W/` indicator and surrounding quotes stripped) — a header that contradicts the body version makes the workspace inconsistent; neither is preferred over the other.
- A race between the four parallel `GET`s is retried once; if still inconsistent, the provider publishes `status: "version_conflict"` with `workspace: null` rather than ever showing a racy snapshot, and mutations stay blocked until an explicit `reload()`.

See `isWorkspaceConsistent` and the `ProfileWorkspaceStatus` doc comment in `src/features/profile/ProfileProvider.tsx` for the full state machine (`idle` → `loading` → `ready` | `version_conflict` | `session` | `error`), the mutation queue/guard, and `saveRefreshFailed` (a mutation the backend accepted whose mandatory post-mutation reload did not land on `ready`).

## Durability split (`durable` vs `temporary`)

Read-side progressive fields report a `durability` of `durable` (persisted) or `temporary` (resolved from request-scoped context, e.g. a locale header, and not yet saved). Write-side field mutations only ever accept the wire literal `"durable"` — there is currently no request-scoped temporary write path — and `mapProfilePatchToRequest` fails closed on anything else reaching that far. Do not conflate a `temporary` _read_ projection with an allowed _write_ durability value; they are different enums with only one literal in common.

## Custom preference / equipment semantics

- A custom preference or equipment entry has **one** free-text input, not a separate label and note: the typed text is both the display label and the stored `note` (`ProfilePreferencesPage`) / `customName` (`ProfileEquipmentPage`). There is no dual label+note UI for `custom_*` codes.
- Catalog (non-custom) entries keep their own optional note, independent of the custom-entry input.
- Adding a custom entry uses a distinct action label from adding a catalog entry so screen reader and sighted users alike do not conflate the two flows.
- A failed or cancelled add (validation error, cancelled sensitive confirmation) preserves the typed input; it is only cleared after a confirmed successful mutation.
- Sensitive categories (`Allergy`, `MedicalRestriction`) route through an accessible Radix `AlertDialog` confirmation (`aria-modal`, focus trap, Escape-to-cancel, focus restored to the trigger) before mutating — never a plain `alert`/`confirm` or a hand-rolled `div[role=alertdialog]`.

## Numeric and field-level validation

- Numeric progressive fields (`defaultAdultCount`, `defaultChildCount`, `defaultServingCount`, `ordinaryPrepMinutes`, `exceptionalPrepMinutes`, equipment `capacity`) never coerce an empty or invalid input to `0`; an empty/invalid confirm produces a local field error and blocks submission instead. Backend-authoritative ranges are enforced client-side before submit (adults 1–20, children 0–20, servings 1–30, ordinary prep 5–600 minutes, exceptional prep 5–1440 minutes; see `NUMERIC_FIELD_LIMITS` in `ProgressiveFieldControl.tsx`).
- Equipment `capacity`/`capacityUnit` are a coherent pair: an empty capacity maps to `null`; NaN/Infinity/negative capacity is rejected locally; a capacity with no unit (or a unit with no capacity) is rejected as ambiguous rather than sent half-filled (see `validateEquipmentDraft` in `ProfileEquipmentPage.tsx`).
- Backend `ProfileApiError.fieldErrors` (a `path → messages[]` map) is mapped onto the matching rendered control (`aria-invalid`, `aria-describedby`, an inline error paragraph) and summarized in a section-scoped, focus-jumping error summary (`FieldErrorSummary` in `src/features/profile/fieldErrors.tsx`). Backend paths this page cannot match to a rendered field are never silently dropped — they are counted and surfaced as "additional problems" in the summary. Equipment maps the backend's `entries[i].*` index paths back to the submitted draft item via `submittedOrderRef`.

## Unsaved changes and precondition-failed handling

- `UnsavedChangesCoordinatorProvider` mounts on the `/app/perfil*` layout (data router via `createBrowserRouter` so `useBlocker` is available). Active editors register `{ isDirty, discard }`; shell brand/Home/Inventory/Profile links, in-profile Links, `navigate()`, and browser Back/Forward are blocked while dirty through one accessible Radix confirmation (`profile-unsaved-*` test ids). Pages do not need a special link component for ordinary same-tab navigation.
- Modified clicks (Ctrl/Cmd/Shift/middle, `target=_blank`) are not intercepted: the new context opens and the original tab keeps its draft until save/cancel/confirm leave.
- `beforeunload` still arms the browser-native prompt for tab close/reload (browsers forbid custom markup there).
- A `412 precondition_failed` on save retains the user's in-progress draft and surfaces a server-value comparison where supported.

## Post-save synchronization

- `saveRefreshFailed`: mutation accepted, mandatory workspace reload did not reach `ready`. Banner (`role="status"`) states the save succeeded and asks the user to Reload before further edits; `canMutate` stays false; warning clears only after a successful reload (not when a reload attempt starts).
- `sessionRefreshWarning`: mutation and workspace reload succeeded, but BFF session refresh failed. Separate banner with Retry; does not misreport the profile save; shell projections (display name, language, timezone, measurement, completeness, adult state) may be temporarily stale.
- Pages disable mutation controls from shared `canMutate` (`ready` + workspace + !isMutating + !saveRefreshFailed). `workspace_not_ready` maps to localized actionable copy, not the generic save error.

## Architecture

- Presentation models: `src/contracts/profile/` (including `controlledCodes.ts`, the closed-set wire unions shared by the adapter and the UI)
- Live adapter: `src/adapters/live/profile/`
- Workspace coordinator: `src/features/profile/ProfileProvider.tsx`
- Choice catalog: `src/features/profile/catalog/` (stable codes + localized labels)
- Controlled/localized field control: `src/features/profile/ControlledFieldControl.tsx`
- Progressive text/numeric field control: `src/features/profile/ProgressiveFieldControl.tsx`
- Field-error summary/focus utilities: `src/features/profile/fieldErrors.tsx`
- Unsaved-changes guard/dialog: `src/features/profile/UnsavedChangesCoordinator.tsx` (+ dialog helper in `useUnsavedChangesGuard.tsx`)
- Post-save banners: `src/features/profile/ProfileWorkspaceStatusBanner.tsx`
- UI strings: `src/app/i18n/profileUiCatalog.ts`

## Provider route scope

`ProfileProvider` is **not** mounted for every authenticated route. It fetches profile, preferences, equipment, and completeness as soon as it mounts, so mounting it broadly would make routes that never read profile data (`/app/hoje`, `/app/despensa`) call the profile endpoints anyway. In `src/app/ProductionApp.tsx`:

- `SessionScopedRoutes` mounts `SessionProvider` + `InventoryProvider` for every authenticated route (`/acesso`, `/app/hoje`, `/app/despensa*`, the `/app/*` catch-all) — no `ProfileProvider`.
- `ProfileScopedRoutes` wraps `SessionScopedRoutes` with `ProfileProvider` and is used **only** for the `/app/perfil*` subtree, mounted once on the parent `/app/perfil` route with `dados`/`preferencias`/`equipamentos` as nested routes rendered through `Outlet` (so the provider mounts once per visit to the section, not once per sub-route).

Regression coverage: `src/app/ProductionHomeRoutes.test.tsx` and `src/app/ProductionInventoryRoutes.test.tsx` assert zero `/api/v1/profile*` calls from `/app/hoje` and `/app/despensa`; `src/app/ProductionProfileRoutes.test.tsx` asserts the profile endpoints are called and all four sub-routes render.

## Routes

| Path                       | Purpose                                                                   |
| -------------------------- | ------------------------------------------------------------------------- |
| `/app/perfil`              | Overview / completeness, adult declaration status, progressive next steps |
| `/app/perfil/dados`        | Household, locale, cooking, ordered code lists                            |
| `/app/perfil/preferencias` | Preferences and restrictions                                              |
| `/app/perfil/equipamentos` | Equipment ordered replace                                                 |

## Browser smoke coverage

`scripts/browser-smoke-matrix.js` (`yarn smoke:browser`) runs two profile-related steps against the static production build, in addition to the Jest component/integration tests:

- `production profile route gate`: navigates to all four `/app/perfil*` paths **unauthenticated** and asserts each redirects to `/acesso` without ever exposing `profile-overview`.
- `production profile intercepted`: uses Playwright `page.route()` to intercept `/api/v1/session` and `/api/v1/profile*` with hand-built fixtures (`installProfileInterception`/`profileInterceptionFixtures` in that script) and exercises the overview, a household save, the unsaved-changes guard, preferences, and equipment pages against those intercepted responses.

**Neither step talks to a real Cocinaris backend or Keycloak** — `PRODUCTION_BASE` only serves the static SPA bundle, and the second step's "authenticated" behavior is entirely driven by mocked fixtures, not a live session. They prove the frontend renders and behaves correctly against known-shape responses and that the unauthenticated route gate holds; they are not evidence of live-backend integration. Full authenticated profile behavior (load/save/conflict/error paths) is exercised by the Jest suites in `src/features/profile/*.test.tsx` and `src/app/ProductionProfileRoutes.test.tsx`.

## Limitations

- Ordered technique/goal/abandonment lists are catalog-only until a label round-trip contract exists.
- Production adult-declaration acceptance is blocked without reviewed policy versions.
- Live contextual-home consumption of profile remains PLAN-0021.
- No browser-smoke step exercises a real backend session for `/app/perfil*`; see "Browser smoke coverage" above.
