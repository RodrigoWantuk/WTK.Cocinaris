# PLAN-0037: Inventory Lifecycle Transitions, Shelf-Life Evidence, and Attention

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** Phase 1 `agent/plan-0037-inventory-lifecycle` (merged, PR #47); Phase 2 `agent/plan-0037-phase-2-attention` (draft PR #49 against `main`; PR #48 merged at `bc7ec04`)
- **Pull request:** Phase 1 PR #47 (merged); Phase 2 draft PR #49
- **Related documents:** `docs/domain/inventory-lifecycle.md`, `docs/product/initial-release.md` (Inventory), coverage review gaps G-01, G-02, G-03
- **Dependencies:** PLAN-0033 (names), PLAN-0023 (prepared lots)

## Objective

Make inventory lots follow the accepted food lifecycle instead of only quantity changes. The user can open, move, freeze and thaw a lot, whole or in part. Each transition is recorded as immutable history, and a partial transition splits off a derived lot. Shelf-life evidence and advisory attention states build on these lifecycle facts.

## Phases

| Phase | Outcome | State |
|---|---|---|
| 1 | Lifecycle transitions: lifecycle state and timestamps on lots, `Open`, `Move`, `Freeze`, `StartThaw`, `CompleteThaw`, partial transitions creating a derived lot with provenance, immutable history, API, contract, production UI, tests | This run |
| 2 | Shelf-life evidence and attention: evidence priority (printed date, prepared record, curated rule, unknown), deterministic generic rules by storage, package and lifecycle state, attention states with explanations, pantry attention view | Delivered (this run) |
| 3 | Product identity: culinary category and roles on products, used to refine Phase 2 rules and later by planning | In progress: category from the ingredient catalog (PLAN-0055) drives store sections in the shopping list; rule refinement by category remains |

## Phase 1 scope

### Included

- Domain: `LotLifecycleState` (`AsAcquired`, `Prepared`, `Frozen`, `Thawing`, `Thawed`); `OpenedAt`, `FrozenAt`, `ThawStartedAt`, `ThawedAt`, and `ParentLotId` on lots; transition rules below; transitions return immutable transactions carrying previous/resulting lifecycle state and storage.
- Partial transitions for measured lots: the source keeps the remainder (transaction `TransitionSplit`), and the derived lot gets the same product, printed date, notes and package state, its own new state, and `ParentLotId` (transaction `TransitionDerived`). Both commit atomically with audit and idempotency.
- API: `POST /api/v1/inventory/lots/{lotId}/transitions` with `Idempotency-Key` and `If-Match`; body `kind`, optional `quantity` (partial), optional target storage. The lot response gains lifecycle state, timestamps and parent lot. History entries gain transition kind, lifecycle and storage changes, and the related lot.
- Migration: new nullable columns plus `LifecycleState` (default `AsAcquired`; existing preparation outputs become `Prepared`), check constraints, owner-consistent foreign key for `ParentLotId`.
- Frontend: lot detail actions for each allowed transition with an optional partial quantity, lifecycle state and timestamps in the detail, history labels, three locales.
- Tests: domain unit tests for every rule, integration tests for the endpoint (whole, partial, idempotent replay, stale version, cross-user isolation), frontend tests for the actions.

### Transition rules (advisory domain, deterministic arithmetic)

| Kind | Allowed when | Effect | Partial |
|---|---|---|---|
| `Open` | package not `Opened` | package `Opened`, `OpenedAt`; optional storage change | No (opening affects the package) |
| `Move` | target differs from current; target is not `Freezer`; lot not `Frozen` | storage changes | Yes (dividing into another place) |
| `Freeze` | storage is not `Freezer` or lot is not `Frozen` | storage `Freezer`, state `Frozen`, `FrozenAt`; thaw timestamps cleared | Yes |
| `StartThaw` | storage is `Freezer`; target is not `Freezer` (default `Refrigerator`) | state `Thawing`, `ThawStartedAt`, storage target | Yes |
| `CompleteThaw` | state `Thawing` | state `Thawed`, `ThawedAt` | No |

Refreezing a thawed lot is allowed. Food-safety guidance about it belongs to the Phase 2 attention engine, because shelf-life behavior is advisory and does not block the user.

### Excluded

- Reservations and locks (planning, PLAN-0029).
- Leftover, preserved and recipe-consumption transitions (cooking, PLAN-0042).
- AI-assisted shelf-life estimates (PLAN-0040 and later).

## Acceptance criteria (Phase 1)

- [x] Every rule in the table is enforced by domain code and covered by unit tests.
- [x] A partial transition splits the lot atomically; quantities are conserved exactly; the derived lot references its parent; both histories show the split.
- [x] Idempotent replay returns the original result; a stale `If-Match` returns 412; another user's lot returns 404.
- [x] Migration is idempotent and existing preparation outputs become `Prepared`.
- [x] OpenAPI snapshot, generated clients and the frontend mirror are current.
- [x] Lot detail offers only the allowed transitions, supports a partial quantity, and is localized in pt-BR, en and es.
- [ ] Deployed to the owner host after validation (owner standing instruction).

## Execution state

- **Current run delivery target:** Phase 3, first part (store sections).
- **Current checkpoint:** Phases 1 and 2 are merged. Phase 3, first part, is on `agent/plan-0037-store-sections`:
  - a product's culinary category comes from its catalog ingredient (PLAN-0055);
  - `StoreSections` maps it to a store section;
  - shopping lines carry `storeSection`, and the shopping list groups the lines to buy by section in shopping-trip order;
  - purchases from the meat-and-fish and dairy-and-eggs sections suggest the refrigerator.
- **Validation (2026-10-30):**
  - Backend unit tests: 180 passed, including sections for onion, chicken, milk, rice, and an unknown name.
  - Architecture tests: 14 passed.
  - Integration tests: 16 passed in `RecipeExecutionApiTests`.
  - Frontend: 83 planning, i18n, and adapter tests passed, including grouping, order, labels, and the fridge default. Typecheck, lint, and format are clean. The OpenAPI and API client drift check is clean.
- **Exact next action:** Phase 3, second part: refine the attention rules by category with curated shelf-life rules (for example, opened dairy and raw meat in the refrigerator). This needs a curated, sourced rule table, reviewed as a food-safety change.
- **Blockers:** None. GitHub Actions does not start jobs because the owner account is locked for billing (PR #46); validation runs on the owner host until the owner resolves it.
- **Working tree state:** Phase 1 committed on `agent/plan-0037-inventory-lifecycle`.

## Progress log

### 2026-10-28T22:40:00Z — Claude Code

- Plan created from the PLAN-0031 coverage review (G-01, G-02, G-03) and the accepted inventory lifecycle document. Phase 1 is this run's delivery target.

### 2026-10-28T23:00:00Z — Claude Code

- **Checkpoint:** Phase 1 delivered.
  - Domain: `LotLifecycleState`, `LotTransitionKind`, `LotLifecycle` and `LotTransitionDetail`; `InventoryLot.Transition` and `SplitTransition` share one rule method.
  - Application: `ITransitionInventoryLotUseCase` with idempotency (`inventory.lots.transition`), `If-Match`, nondisclosing ownership checks, and a derived lot written in the same transaction as the source.
  - API: `POST /api/v1/inventory/lots/{lotId}/transitions`; `lifecycle` on lot responses, `transition` on history entries; OpenAPI metadata and examples.
  - Persistence: migration `AddInventoryLifecycleTransitions` (new columns, check constraints, same-owner parent foreign key, preparation outputs backfilled to `Prepared`). The lot/transaction record mapping moved into `InventoryRecordMapper`, shared by the lot and preparation stores, so no store can reset lifecycle columns on write.
  - Frontend: `LotTransitionPanel` and `lotTransitions` rule mirror; lifecycle state, instants and source lot on the detail page; transition history labels and related-lot links; pt-BR, en and es texts. The mapper treats a response without `lifecycle` as `AsAcquired`, so this frontend also works with an older API.
  - Docs: implemented transition rules in `docs/domain/inventory-lifecycle.md`; metric description in the backend README.
- **Validation (owner host):**
  - Backend: format; warnings-as-errors build; unit 93 (17 new), architecture 14, integration 182 (4 new API tests, OIDC fixture); idempotent migration script applied twice (11 rows); OpenAPI export and lint.
  - Contracts: API client regenerated; drift check passed.
  - Frontend: lint, typecheck, guards, Vitest 51 files / 420 tests (10 new), production build, build-mode guard.
- **Failures fixed during validation:** the generated migration lost EF's UTF-8 BOM after an edit, which the format gate rejected; restored with `dotnet format`.
- **Not run:** browser smoke (it covers the prototype journeys this phase does not change). GitHub Actions cannot run until the owner clears the billing lock.

### 2026-10-29T02:10:00Z — Claude Code (Phase 2)

- **Checkpoint:** Phase 2 delivered.
  - `InventoryAttentionAdvisor` holds the deterministic rules in the Inventory module; the rules are documented in `docs/domain/inventory-lifecycle.md`.
  - `GET /api/v1/inventory/attention` returns at most 100 results from the 500 most recently updated active lots, most urgent and earliest-dated first.
  - Every lot response carries `attention`, evaluated at response time. `InventoryCalendar` resolves "today" in the profile time zone.
  - Prepared lots use their recorded shelf-life date (read-time join).
  - Frontend: `AttentionBadge`; a "Precisa de atenção" panel above the pantry list (hidden on failure); a detailed explanation and a "guidance only, check the food" note on the lot detail; pt-BR, en and es texts.
  - No migration.
- **Validation (lean, owner instruction 2026-10-29):**
  - unit 106 (13 new advisor tests, including time-zone date boundaries);
  - integration class `InventoryLifecycleTransitionApiTests` 6/6 (new attention endpoint test);
  - OpenAPI export and lint; API client regenerated;
  - frontend tests of the affected areas: 15 files / 129 tests (4 new);
  - typecheck, lint, interactive-nesting guard.
- **Limitations:** a mutation response of a prepared lot omits its prepared shelf-life date until the next read. Rules are generic until Phase 3 adds product categories.
- **Next action:** Phase 3, product culinary category and roles.

### 2026-10-29T02:30:00Z — Claude Code

- PR #48 was squash-merged (`bc7ec04`), which left PR #49 conflicting. Merged `main` into the Phase 2 branch (no force push); the only conflicts were the registry row and the integration test file, where `main` had the same content as the old base. Kept the branch versions. No code changed, no tests rerun.

### 2026-10-30 — Claude Code (Phase 3, first part)

- Product category now comes from ingredient identity (PLAN-0055) instead of a separate field: the curated catalog already holds a category for every ingredient. The first visible use is the shopping list grouped by store section, which the owner had asked for alongside the household shopping list (PLAN-0054 Phase 5).

