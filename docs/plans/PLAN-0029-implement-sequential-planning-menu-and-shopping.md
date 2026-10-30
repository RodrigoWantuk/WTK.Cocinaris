# PLAN-0029: Implement Sequential Planning, Menu, and Shopping Projection

- **Status:** Completed
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-29)
- **Created:** 2026-08-05
- **Last updated:** 2026-10-30
- **Branch:** Phase 1 `agent/plan-0029-menu-and-shopping` (merged and deleted); Phase 3 `agent/plan-0029-reservations` (merged and deleted)
- **Pull request:** Phase 1 merged via [PR #52](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/52) at `3efb214`; Phase 3 merged via [PR #53](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/53) at `f299090`
- **Dependencies:** PLAN-0028 (recipe revisions), PLAN-0037 Phases 1–2 (pantry lots and attention), PLAN-0038 (recipe library), PLAN-0042 Phase 1 (cooking); all merged
- **Related documents:** `docs/domain/planning-and-shopping.md`, `docs/product/entry-and-contextual-home.md` (suggestion priority), `docs/domain/inventory-lifecycle.md`

## Objective

Turn saved recipes into a menu the owner can use, and derive the shopping list from it, so the loop closes: plan, then buy, then cook, then the pantry is updated. Planning stays optional and advisory, and it never mutates inventory.

## Owner direction (2026-10-29)

"Merge realizado, pode seguir com sua sugestão." PLAN-0029 was the suggested next step after PLAN-0038, and the owner had asked for large, usable end-to-end deliveries. Phase 1 below is the deterministic slice: menus, the day's meals on the home page, cooking from the menu, and the shopping list. AI-generated plans and reservations follow in later phases.

## Phases

| Phase | Outcome | Depends on |
| --- | --- | --- |
| 1 | Draft/accepted/archived menus with dated or flexible meals; accepted-menu read contract for "Hoje"; cooking from a meal links the session and marks the meal cooked; deterministic shopping projection with check marks and purchases recorded into the pantry | — |
| 2 | Sequential AI planning: request candidates for several meals, accept or replace one, regenerate only the remaining ones (planning input of the domain doc) | PLAN-0040 |
| 3 | Flexible reservations for accepted meals, released when cooked or skipped, with conflict handling in the cooking proposal (PLAN-0042 Phase 3) | Phase 1 |
| 4 | Package sizes and expected remainder (with PLAN-0022 package confidence), planned-versus-actual purchase impact, pre-meal readiness checks and reminders | Phase 1, PLAN-0046 |

## Phase 1 scope delivered

1. **Planning module.** A new `Cocinaris.Modules.Planning` project with XML documentation enforced.
   - **Plan:** `MenuPlan` covers 1–31 days and is `Draft | Accepted | Archived`. An archived plan is read-only.
   - **Entry:** `MenuEntry` has a recipe, an optional day inside the period (no day means flexible), an optional meal, servings (1–24), and a status `Planned | Skipped`. An entry follows the recipe's current revision; a pinned revision is supported by the API but not yet offered in the UI.
   - **Changes:** each change touches one entry. Nothing regenerates the plan.
2. **Cooking state.** "Cooking" and "cooked" are not stored on the entry. They are derived from the cooking session started from it: `recipe_executions.MenuEntryId`, a nullable column with an `ON DELETE SET NULL` foreign key, set by `POST /recipes/{id}/executions` with `menuEntryId`.
   - Completion and reconciliation stay exactly as in PLAN-0042, so the atomicity guarantee is unchanged.
   - A session of another recipe never counts.
3. **Shopping projection** (`ShoppingProjector`). Deterministic and read-only.
   - **Meals included:** meals still to cook (not skipped, not cooked), dated from the owner's current day or flexible.
   - **Quantities:** scaled like the cooking proposal, using the new shared `RecipeIngredientReader`.
   - **Matching:** pantry lots are matched by normalized product name (case, accents, and spaces ignored) and unit. Every new lot has its own product, so product IDs cannot be compared across lots. An ingredient taken from a specific lot uses that lot's product name.
   - **Allocation:** meal by meal in plan order.
   - **Categories:** `required`, `assumed` (basics the recipe assumes), `optional`, `runs_out` (covered, but the plan uses all of it), and `available`.
   - **Package sizes:** none are assumed. Each line is the net quantity to buy.
   - **Check marks:** a check mark is only remembered (`planning.shopping_checks`).
   - **Purchases:** recording a purchase creates a pantry lot through the normal inventory command, and the list is recalculated.
4. **Persistence.** Migration `AddMenuPlanning` is additive.
   - **New schema `planning`** with tables `menu_plans`, `menu_entries`, and `shopping_checks`.
   - **Checks:** status and period, the acceptance instant, meal type, servings, and the pinned revision.
   - **Owner-including foreign keys** to the plan and the recipe.
   - **Unique creation keys** for idempotent creation.
   - **The column on executions** described in item 2.
   - **Concurrency:** entries are saved one at a time with last write wins, because a plan is advisory, edited by its single owner, and no inventory depends on it.
5. **API** under `/api/v1/planning`. All routes are owner-scoped; mutations are CSRF-protected (POST, PUT, DELETE) and rate limited.
   - **Menus:**
     - `GET /menus?archived=` lists them.
     - `POST /menus` creates one and requires `Idempotency-Key`.
     - `GET /menus/{id}` returns one, and `PUT /menus/{id}` renames it or changes its period. A dated meal outside the new period gets 422.
   - **Lifecycle:** `POST /menus/{id}/acceptance` and `POST /menus/{id}/archive`.
   - **Meals:**
     - `POST /menus/{id}/entries` adds one and requires `Idempotency-Key`.
     - `PUT /menus/{id}/entries/{entryId}` changes one, and `DELETE` removes it.
   - **Shopping:** `GET /menus/{id}/shopping?from=` and `PUT /menus/{id}/shopping/checks`.
   - **Home contract:** `GET /today?date=` returns the meals of accepted menus for the owner's current day (dated that day, or flexible), excluding skipped and cooked meals. This is the accepted-menu read contract for PLAN-0021.
   - **OpenAPI fix:** the transformer now declares the CSRF header on PUT operations and never duplicates the profile transformer's header.
6. **Frontend.**
   - **Navigation:** a "Cardápio" item.
   - **`/app/cardapio`:** create a menu (default: seven days from today) and list current or archived menus.
   - **`/app/cardapio/:id`:**
     - every day of the period plus "Sem dia definido", with today marked;
     - add a library recipe to a day, with search, meal, and servings;
     - per meal: cook (linked to the meal), skip or undo, change day, meal, or servings, and remove;
     - status labels;
     - accept ("Usar este cardápio") and archive.
   - **`/app/cardapio/:id/compras`:**
     - lines grouped by category, each with what is needed, what the pantry holds, and which meals use it;
     - check marks;
     - "Comprei", which records the quantity and storage into the pantry.
   - **"Hoje":** "No cardápio de hoje" is the first section, as the suggestion priority requires. It cooks a meal linked to its menu entry, and it degrades independently like the other sections.
   - **Dates:** computed from the browser's local day, because the server does not know the owner's time zone.
7. Documentation, contracts, and tests (below).

### Not delivered in Phase 1

- **AI-assisted planning** (Phase 2). Menus are filled from the owner's saved recipes.
- **Reservations** (Phase 3). An accepted menu reserves nothing, and the cooking proposal is unchanged.
- **Package sizes, expected remainder, planned-versus-actual purchase impact, readiness checks, and reminders** (Phase 4).
- **Pinning a revision from the UI.** The API supports it; the UI always follows the current revision.
- **Automatic handling of missed meals.** A past dated meal that was not cooked stays "planned" on the menu and is left out of the shopping list; skipping it is manual.

## Acceptance criteria (Phase 1)

- [x] The accepted-menu read contract exists and the home page consumes it (PLAN-0021 menu tier).
- [x] Draft, accepted, and archived semantics are deterministic and tested: a draft is not on the home page, and an archived menu is read-only.
- [x] A meal's change never alters another meal; a period change cannot strand a dated meal.
- [x] Cooking from a meal links the session; the meal shows as cooking, then cooked, and leaves the home page and the shopping list.
- [x] The shopping projection never mutates inventory; it allocates the pantry in plan order and matches names regardless of case and accents.
- [x] Package surplus is not exposed, because no package source exists yet (the confidence-`high`-only rule is kept by not guessing).
- [x] Idempotent creation of menus and meals; another user gets 404 everywhere.
- [x] The browser journey passes through the proxy replica (`scripts/e2e/planning-journey.mjs`, 7/7).
- [x] Deployed to the owner host (`main` at `3efb214`, release `20261029T121152Z-3efb214135dc`).

## Phase 2 scope delivered (AI suggestions for a menu slot)

1. **Request.** On any day of an editable menu, or on "Sem dia definido", "Sugerir com IA" asks `recipe.suggest_candidates.v1` for three candidates in `executionMode: menu_planning`. The request carries the planned date, the meal type, the servings, and the names of meals already on the menu (skipped ones excluded, at most 20).
   - **Menu resolved server-side.** The API resolves the menu owner-scoped (404 for another owner) and validates the slot: date inside the period, a known meal, servings 1–24, and a menu that is not archived. The names therefore come from the backend.
   - **Strategies.** The envelope's strategy set for `menu_planning` is `on_hand_first`, `planned_purchase_reuse`, and `exploratory`; the existing semantic validator rejects the cook-now-only strategy.
   - **Planning rule.** The request tells the model to differ in dish format, technique, and main ingredient from the meals already planned.
2. **Free stock only.** The context assembler subtracts protections and other meals' reservations (`IPantryCommitmentLookup`) from pantry quantities, and omits lots that are fully committed. A planned meal can never be suggested stock another meal counts on.
3. **Choosing.** Choosing a candidate expands and saves it like any cook-now selection: same idempotency, the same output language and detail checks, and the same token windows and spend ceiling. The menu then adds it to the slot. If the add fails, choosing again replays the same selection and retries the add, so no duplicate recipe is created.
4. **Sessions.** Migration `AllowMenuPlanningSessions` widens the session mode check to `cook_now` and `menu_planning`. The down migration would fail once `menu_planning` sessions exist; the migration is forward-only.
5. **Model policy.** Menu suggestions use thinking disabled, like cook-now. In the PLAN-0022 samples, thinking-high returned truncated JSON, and the panel already shows that suggestions can take up to a minute.

### Not delivered in Phase 2

- Planning several slots in one request ("fill the week"). Slots are planned one by one, which keeps each suggestion aware of the reservations created by the previous choice (sequential planning).
- Regenerating only some of the candidates, or keeping rejection reasons across rounds.
- Planned purchases as `planned_purchase` availability.

## Phase 3 scope delivered (reservations and protections)

Phase 3 ran before Phase 2. Without it, two cooking sessions could count on the same pantry item, which was the weak point left after Phase 1. Phase 2 also depends on PLAN-0040.

1. **Derived reservations** (`ReservationProjector`).
   - Reservations are not stored. They are recomputed from accepted menus, the pantry, and protections on every read, so they can never drift from either. They never change physical quantity.
   - **Which meals reserve:** meals of accepted menus that are still to cook, reserve ingredients, and are dated from today or flexible. They reserve in plan order.
   - **Which lot is used:** the lot the recipe was written for first, then lots with the same normalized name and unit, earliest printed or prepared date first, then oldest.
   - **Today on the server:** it does not know the owner's time zone, so a meal keeps its reservation until its day has ended everywhere (UTC−12).
2. **Protections** (`planning.lot_protections`, planning-owned as `inventory-lifecycle.md` anticipated).
   - The owner can protect a whole lot or keep a minimum quantity.
   - Protected quantity is set aside before reservations, never proposed for cooking, and not available to a menu's shopping list. The owner can still use it explicitly: nothing blocks the choice.
   - The protection is removed together with its lot.
3. **Releasing a meal.** `menu_entries.ReserveIngredients`: a meal can keep its place in the plan without reserving.
4. **Readiness.** Every meal still to cook shows `ready` or `missing`, with what it lacks.
   - On a menu in use, readiness reflects real reservations.
   - On a draft it simulates accepting the menu after the menus already in use.
   - Optional ingredients and basics the recipe assumes are never reported missing.
5. **Cooking proposal** (through `IPantryCommitmentLookup`, a port the Recipes module owns and Planning implements, so Recipes never depends on Planning).
   - Protected quantity is never proposed, and a fully protected lot gets the new status `protected`.
   - Each line and each pantry lot carries the protected quantity and the other menu meals counting on it. A session started from a meal does not count that meal's own reservation.
   - The review warns, naming the meals, when an entered quantity eats into another meal's reservation or the protected part. It advises; it does not block. Completion and reconciliation are unchanged.
6. **Shopping list.** A menu counts only stock that is neither protected nor reserved by other menus in use.
7. **API.**
   - `GET /api/v1/planning/reservations?from=`;
   - `PUT /api/v1/planning/protections/{lotId}` (`whole`, or `minimum` with a quantity) and `DELETE` on the same path;
   - `reserveIngredients` and `readiness` on menu entries;
   - commitments on cooking proposal lines and pantry lots.
8. **Frontend.**
   - **Meals:** readiness on each meal ("Ingredientes da despensa reservados" or "Falta: …"), and a reserve checkbox in the meal editor.
   - **Pantry item page:** a "Reservas e proteção" section that lists the meals counting on the lot and sets the protection.
   - **Cooking review:** notices that name the meals and the protected part.
9. **Migration** `AddReservationsAndProtections` is additive: a column with a default and a new table.

### Not delivered in Phase 3

- Reallocating reservations by hand, or letting a recommendation reorganize them. Order follows the plan.
- Reminders.

## Phase 4 scope delivered (package sizes, purchase impact, readiness reminders)

Owner direction 2026-10-30: "Siga com plano 42 e 29, não temos pressa em compartilhar casa." Delivered on `agent/plan-0029-purchase-and-readiness` from `main`.

1. **Package sizes.** `PackageSize` (domain) holds the size the owner confirmed for a shopping item key, in the key's unit, at most 100,000. Table `planning.package_sizes` (primary key owner and item key, check constraint on the size, owner foreign key), migration `AddPackageSizes` (additive). `PUT /api/v1/planning/package-sizes` with `{ itemKey, size }` sets it, and `size: null` forgets it; a key that does not end in a canonical unit is `validation_failed`. The source is always the owner: PLAN-0046 package confidence from receipts and imports is not used yet, and nothing is assumed.
2. **Shopping line.** `packageSize`, `packageCount` (whole packages covering what is left to buy), and `expectedRemainder` (what they leave after the menu). Each use gains `missingQuantity`, what that meal still lacks after the pantry is allocated in plan order.
3. **Planned versus actual purchase.** After "Comprei" the shopping page says whether the purchase covers the menu, which meals are left short and by how much, or how much is left over (with a freezing hint for perishable sections and "remember this amount as the package size"). The menu and pantry are never changed by the notice.
4. **Readiness reminders.** `PreparationReminders` derives `buy`, `thaw`, and `use_by` reminders from the reservation projection for the dated meals of menus in use. `GET /api/v1/planning/preparation?from=&days=` (1–7 days, default 2). "Prepare-se" on "Hoje" shows today and tomorrow; it is an optional repository method, so a runtime without it shows nothing.
5. **OpenAPI.** New response schemas are named `MealPreparation*` because `PreparationResponse` already names the inventory preparation batch.

### Not delivered in Phase 4

- Package sizes from receipts or imports (PLAN-0046) and package confidence.
- Notifications outside the app, a per-meal reminder policy, and dismissing a reminder.
- Proposals that change the plan (replace only affected recipes, repurpose the extra product) beyond pointing to the menu.

## Execution state

- **Current run delivery target:** Phase 4 in one PR from fresh `main` (owner direction 2026-10-30).
- **Current checkpoint:** Phase 4 implemented and validated as below; the draft PR follows the commit.
- **Validation (lean, targeted):**
  - **Backend unit tests:** the new `PurchaseAndPreparationTests` (package cover and remainder, per-meal shortfall, buy/thaw/use-by reminders) with `ShoppingProjectorTests` and `ReservationProjectorTests`: 13 passing.
  - **Backend integration tests:** the new `PackageSizesShapeTheShoppingListAndRemindersFollowTheMenuInUse` (sizes per owner, validation, a draft reminds of nothing, buy reminders once in use, a thaw reminder after freezing the reserved lot, the 1–7 day bound) with the planning, reservation, menu-suggestion, migration, and contract tests: 35 passing.
  - **Architecture tests:** 14 passing. `dotnet format --verify-no-changes` is clean.
  - **OpenAPI:** exported (additions only); client regenerated and drift check passed.
  - **Frontend:** planning, cooking, and app suites (13 files, 76 tests, including the new package/impact and "Prepare-se" tests) pass; typecheck, ESLint, and Prettier are clean.
- **Not run:** the full suites, the browser journey, and the full local gates (lean validation).
- **Exact next action:** None; the plan is completed. Merged via #71 and deployed at `99481b8` on 2026-10-30. Unscheduled follow-ups are listed under each phase's "Not delivered".
- **Blockers:** None.

## Progress log

### 2026-08-05 — PLAN-0028 reconciliation

- Draft placeholder created.

### 2026-10-29T12:40:00Z — Claude Code

- Claimed after PR #51 was merged and `main` was deployed.
- Delivered Phase 1 in one run.
- Design choices:
  - **Derived cooking state.** Cooking state is derived from executions instead of stored on the entry, so it can never disagree with the atomic completion.
  - **Name matching.** The shopping list matches pantry lots by normalized name, because lots do not share product identities.
  - **Shared quantity reading.** Ingredient reading and scaling moved to `RecipeIngredientReader`, so the cooking proposal and the shopping list cannot drift apart.

### 2026-10-29T13:40:00Z — Claude Code

- The owner merged PR #52 (`3efb214`), and `main` was deployed.
- Delivered Phase 3 (reservations, protections, readiness, cooking and shopping integration) on `agent/plan-0029-reservations`.
- Reservations are derived rather than stored, and protections are planning-owned. The cooking proposal reads both through a Recipes-owned port.
- The reservation journey first showed the cooking warning without meal names when the cook picked a lot by hand. Pantry lots in the proposal now carry the names of the meals counting on them.

### 2026-10-29T14:30:00Z — Claude Code

- The owner merged PR #53 (`f299090`), and `main` was deployed. Phase 2 (AI-assisted menu) waits for PLAN-0040, now in progress.

### 2026-10-29T15:30:00Z — Claude Code (pause)

- Phase 2 started on `agent/plan-0029-ai-menu`. Only its prerequisite was committed: rolling 24-hour token windows (PLAN-0040 follow-up). The run stopped at the agent's usage limit.
- **Phase 2 design (next run):**
  - **Request.** On a menu day, "Sugerir com IA" calls `recipe.suggest_candidates.v1` with `executionMode: menu_planning`. The request carries the planned date, meal type, and servings, plus the names of meals already on the menu for variety. The API resolves the menu so the names come from the backend.
  - **Strategies.** For `menu_planning` the strategy set is `on_hand_first`, `planned_purchase_reuse`, and `exploratory`.
  - **Pantry context.** The assembler subtracts `IPantryCommitmentLookup` commitments from pantry quantities.
  - **Adding the meal.** The selected candidate is expanded as today; the frontend then adds the recipe to the menu slot.
  - **Schema.** Widen `ck_generation_sessions_execution_mode` to include `menu_planning`.
  - **Model policy.** Non-thinking, because the PLAN-0022 thinking-high samples returned truncated JSON.

### 2026-10-29T16:30:00Z — Claude Code

- The owner merged PR #55 (`59cfcb3`), and `main` was deployed.
- Delivered Phase 2 on `agent/plan-0029-phase-2-ai-menu`: slot-by-slot AI suggestions from free stock, with the menu resolved server-side.

### 2026-10-29T17:40:00Z — Claude Code

- The owner merged PR #56 (`3459df4`), and `main` was deployed.
- Phase 4 waits for PLAN-0054, which the owner prioritized after the flow review. That plan absorbs grouping the household shopping list by store section.

### 2026-10-30T15:00:00Z — Claude Code

- Delivered Phase 4 after the owner's "Siga com plano 42 e 29": package sizes the owner confirms, with whole packages and the expected remainder on the shopping line; the planned-versus-actual notice after a purchase; and "Prepare-se" reminders (buy, thaw, use by) on "Hoje".

### 2026-10-30T16:00:00Z — Claude Code

- Merged `main` (PLAN-0042 PR #70) into the Phase 4 branch to resolve the registry conflict. The model snapshot holds both migrations and the regenerated OpenAPI contract equals the merged one. Re-run after the merge: unit 8/8, integration (Phase 4, cooking adjustments, migrations) 22/22, frontend planning/recipes/app 102/102, typecheck and `dotnet format` clean.

### 2026-10-30T18:00:00Z — Claude Code (registry reconciliation, PLAN-0057 Phase 0)

- Phases 1–4 are merged (#52, #53, #56, #71) and deployed at `99481b8`; the plan is completed. Follow-ups listed under each phase's "Not delivered" stay unscheduled; package sizes from receipts belong to PLAN-0046.
