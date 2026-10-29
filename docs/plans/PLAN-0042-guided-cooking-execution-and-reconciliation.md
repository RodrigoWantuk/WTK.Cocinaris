# PLAN-0042: Guided Cooking, Execution, and Atomic Inventory Reconciliation

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-29)
- **Created:** 2026-10-29
- **Last updated:** 2026-10-29
- **Branch:** `agent/plan-0042-cook-and-reconcile`
- **Pull request:** Not opened
- **Roadmap entry:** [`PLAN-0031-amendment-2026-10-28-coverage-review.md`](PLAN-0031-amendment-2026-10-28-coverage-review.md) (PLAN-0042, G-14)
- **Related documents:** `docs/domain/recipes-and-cooking.md` (execution start, finalization), `docs/domain/inventory-lifecycle.md` (recipe completion reconciliation), `docs/product/user-journeys.md`

## Owner direction (2026-10-29)

"Vamos tentar fazer entregas grandes, quero uma versão usável de ponta a ponta logo." The roadmap places PLAN-0042 after PLAN-0029 (planning) and PLAN-0038 (recipe library). Cooking a cook-now recipe needs neither a menu nor a library, and it closes the loop that makes the product usable end to end: pantry, then recipe suggestion, then cooking, then the pantry updated. Phase 1 therefore runs now. Menu-pinned executions, reservations, and revision/derivation at finalization wait for PLAN-0029 and PLAN-0038.

## Phases

| Phase | Outcome | Depends on |
| --- | --- | --- |
| 1 | Start a recipe execution explicitly; cooking view with stage checklist; deterministic, editable completion proposal from the pinned revision and the pantry; completion that consumes the confirmed lots and stores leftovers in one database transaction; abandonment; resume from the home | PLAN-0028, PLAN-0023 preparation contract, PLAN-0037 |
| 2 | Execution-local substitutions and notes; save changes as a new revision or a derived recipe at finalization | PLAN-0038 |
| 3 | Menu-entry executions, reservation release, preparation-route due windows | PLAN-0029 |
| 4 | Stage detail levels by profile, sensory references, timers | Phase 1 |

Text troubleshooting is PLAN-0045.

## Phase 1 scope

### Included

1. **Execution aggregate** (`recipes.recipe_executions`): owner, recipe, pinned revision, servings (1–24), status `InProgress | Completed | Abandoned`, start/finish instants, reconciling inventory batch, completion idempotency key, optional rating (1–5) and private notes (≤ 1000). At most one in-progress execution per recipe (filtered unique index); starting again returns it.
2. **Proposal**, deterministic, recomputed on every read while in progress:
   - each ingredient's quantity scales by servings cooked / revision servings;
   - whole units round up, and grams and milliliters round to whole numbers;
   - lines taken from the pantry are `ready` or `insufficient` (the proposal is capped at what the lot holds);
   - a lot that is used up, deleted, or measured in another unit is `lot_unavailable`;
   - ingredients the recipe did not take from the pantry are `not_tracked`;
   - the response lists every active measured lot, so the user can choose a lot for any line.
3. **Completion**, atomic:
   - the user confirms or edits every line, and the optional leftover (portions, storage, use-by date);
   - the inventory preparation workflow runs with source `RecipeExecution` and the execution as its source reference;
   - it consumes the lots (each protected by its version) and creates one prepared leftover lot named after the recipe;
   - the execution state change commits in the same PostgreSQL transaction;
   - a stale lot version fails with 412, and nothing is consumed;
   - a retry with the same `Idempotency-Key` replays, and another key is refused (422);
   - the database allows one reconciliation per execution (unique index).
4. **Inventory changes:**
   - `PreparationSourceType.RecipeExecution`, plus `SourceReferenceId` on preparation batches with a check constraint;
   - a recipe execution may consume nothing (leftovers only) or store nothing (everything eaten), and leftovers must not exceed the portions cooked;
   - manual preparations keep their rules;
   - transaction reason `recipe_execution`.
5. **API:**
   - `POST /api/v1/recipes/{recipeId}/executions`;
   - `GET /api/v1/recipes/executions/in-progress`;
   - `GET /api/v1/recipes/executions/{executionId}`;
   - `POST …/{executionId}/completion` (requires `Idempotency-Key`);
   - `POST …/{executionId}/abandonment`.

   All are owner-scoped, CSRF-protected and rate limited like the other recipe mutations.
6. **Frontend:**
   - a "Cozinhar" action on the recipe detail page;
   - a cooking page with ingredient and stage checklists (kept in the browser only, per execution);
   - a completion review with editable quantities, a lot picker, leftovers, rating and notes;
   - a summary after confirmation;
   - in-progress executions on the home page (with PLAN-0021).
7. Documentation, OpenAPI snapshot, regenerated client, targeted tests.

### Excluded

- Revisions and derived recipes from execution changes (Phase 2).
- Menu reservations (Phase 3).
- Consumed-portion accounting beyond leftovers: portions eaten are servings cooked minus leftovers, and they are not stored as a separate record.
- Execution history pages. The data is stored, and a history view comes with PLAN-0038.

## Acceptance criteria (Phase 1)

- [x] Viewing a recipe never starts an execution; starting is an explicit POST, and a second start returns the in-progress execution.
- [x] The proposal is deterministic, scales with servings, never exceeds a lot, and marks unavailable and untracked ingredients.
- [x] Completion consumes the confirmed lots and stores the leftovers atomically with the execution state change; a stale lot rolls everything back.
- [x] Completion is idempotent per key; a second completion is refused; another user gets 404.
- [ ] The cooking page and the completion review work end to end in the browser, and the pantry reflects the result.
- [ ] Deployed to the owner host.

## Execution state

- **Current run delivery target:** Phase 1 complete, together with the PLAN-0021 home tiers that make the loop visible from "Hoje".
- **Current checkpoint:** Backend complete: domain, persistence, migration `20261029032441_AddRecipeExecutions`, API, OpenAPI snapshot, regenerated client.
- **Validation:**
  - unit tests: `RecipeExecutionTests` (3) and the prepared-component tests (6), all passing;
  - integration tests: `RecipeExecutionApiTests` (2: full loop with leftovers, replay, refusal, isolation; stale version rolls back), `Plan0027*`, `InventoryLifecycleTransitionApiTests` (shared test host extracted to `ApiTestHost.cs`) and `PostgreSqlMigrationTests`: 43 passing;
  - OpenAPI lint valid (one pre-existing warning).
- **Exact next action:** Frontend cooking page and completion review, then the home tiers, documentation, deploy, draft PR.
- **Blockers:** None.

## Progress log

### 2026-10-29T03:40:00Z — Claude Code

- Claimed the plan for Phase 1 per the owner's end-to-end direction. Reused the PLAN-0023 preparation transaction for reconciliation instead of a second consumption path. It already validates owner-scoped lots, per-lot versions, idempotency, history and provenance, and stores leftovers as prepared lots, so the PLAN-0037 attention rules apply to them.
- Backend delivered and validated as recorded above.
