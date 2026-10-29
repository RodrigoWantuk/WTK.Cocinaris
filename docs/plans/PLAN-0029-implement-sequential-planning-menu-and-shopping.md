# PLAN-0029: Implement Sequential Planning, Menu, and Shopping Projection

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-29)
- **Created:** 2026-08-05
- **Last updated:** 2026-10-29
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

## Execution state

- **Current run delivery target:** Phase 3 complete, in one PR from fresh `main`.
- **Current checkpoint:** Phase 3 implemented and validated as below. Deploy after the commit, then open the draft PR.
- **Validation (lean, targeted):**
  - **Backend unit tests:** `ReservationProjectorTests` (3 new), `ShoppingProjectorTests`, and `RecipeExecutionTests` pass.
  - **Backend integration tests:** the `RecipeExecutionApiTests` partial class, with the new end-to-end `ReservationApiTests`, `PlanningApiTests`, and `PostgreSqlMigrationTests`: 27 pass. A rerun of the three affected tests after the last DTO change also passed.
  - **Backend formatting:** `dotnet format --verify-no-changes` is clean.
  - **OpenAPI:** exported and lint-valid; the 6 `no-ambiguous-paths` warnings are the pre-existing ones. The client was regenerated.
  - **Frontend tests:** all feature, app, and adapter suites pass (45 files, 357 tests). They include new planning tests for readiness, release, and protection, and the cooking warning.
  - **Frontend static checks:** typecheck, ESLint, and Prettier are clean, the guards pass, and the build passes.
  - **Browser journeys through the replica:**
    - `reservations-journey.mjs`: 6/6;
    - `planning-journey.mjs`: 7/7;
    - `cooking-journey.mjs`: 5/5.
- **Not run:** the full suites, the smoke matrix, and the full local gates (owner direction: lean validation).
- **Exact next action:** Deploy, then open the draft PR. After merge, the candidates are:
  - Phase 2 (AI planning), once PLAN-0040 hardening is done;
  - Phase 4 (package sizes and purchase impact);
  - the PLAN-0042 Phase 2 remainder (substitutions recorded during cooking).
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
