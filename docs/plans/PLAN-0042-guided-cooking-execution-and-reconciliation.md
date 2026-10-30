# PLAN-0042: Guided Cooking, Execution, and Atomic Inventory Reconciliation

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-29)
- **Created:** 2026-10-29
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0042-cook-and-reconcile` (Phase 1, merged and deleted); Phase 2 finalization on `agent/plan-0038-recipe-library`; Phase 2 remainder on `agent/plan-0042-cooking-adjustments`
- **Pull request:** Phase 1 merged via [PR #50](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/50) at `595af8e`
- **Roadmap entry:** [`PLAN-0031-amendment-2026-10-28-coverage-review.md`](PLAN-0031-amendment-2026-10-28-coverage-review.md) (PLAN-0042, G-14)
- **Related documents:** `docs/domain/recipes-and-cooking.md` (execution start, finalization), `docs/domain/inventory-lifecycle.md` (recipe completion reconciliation), `docs/product/user-journeys.md`

## Owner direction (2026-10-29)

"Vamos tentar fazer entregas grandes, quero uma versão usável de ponta a ponta logo." The roadmap places PLAN-0042 after PLAN-0029 (planning) and PLAN-0038 (recipe library). Cooking a cook-now recipe needs neither a menu nor a library, and it closes the loop that makes the product usable end to end: pantry, then recipe suggestion, then cooking, then the pantry updated. Phase 1 therefore runs now. Menu-pinned executions, reservations, and revision/derivation at finalization wait for PLAN-0029 and PLAN-0038.

## Owner feedback handled in this run (2026-10-29)

- "As opções de receita estão vindo em inglês mesmo com meu idioma configurado para português... No contrato com a IA precisa ficar claro o idioma esperado de retorno."
- "No meu perfil eu coloquei receita com passo a passo detalhado, o passo a passo veio ridículo." The recipe showed stage titles only, and ingredients had no quantities.

Causes and fixes:

1. **No language in the AI contract.** Both operations now send `outputLanguage` (tag, name, and an explicit rule) and the DeepSeek system prompt names it. The source is the profile language, then the interface locale (the frontend sends it as `Accept-Language`), then `pt-BR`; only `pt-BR`, `en`, and `es` are accepted.
2. **Profile instruction detail not sent.** Both operations now send `instructionStyle`, built from the profile's preferred instruction detail and skill. `detailed` asks for every action with quantities, utensil, heat, time, and doneness signs, within the 700 characters per stage the schema allows.
3. **The recipe page discarded content the model had returned.** The parser expected `steps[]` and `quantity`, but protocol 0.3 sends `instructions` (string) and `requiredQuantity`. It now reads the protocol fields and also shows timings, doneness cues, and equipment. Recipes already saved display their instructions without being regenerated.

Language and detail cannot be checked deterministically. They are recorded as evaluation criteria for PLAN-0040, and existing recipes keep the language they were generated in.

## Phases

| Phase | Outcome | Depends on |
| --- | --- | --- |
| 1 | Start a recipe execution explicitly; cooking view with stage checklist; deterministic, editable completion proposal from the pinned revision and the pantry; completion that consumes the confirmed lots and stores leftovers in one database transaction; abandonment; resume from the home | PLAN-0028, PLAN-0023 preparation contract, PLAN-0037 |
| 2 | Execution-local substitutions and notes; save changes as a new revision or a derived recipe at finalization (finalization delivered with PLAN-0038; changes during cooking delivered 2026-10-30) | PLAN-0038 |
| 3 | Menu-entry executions (delivered with PLAN-0029 Phase 1: `menuEntryId` on start); reservations and protections in the proposal (delivered with PLAN-0029 Phase 3; reservations are derived, so a cooked meal releases them by no longer counting); preparation-route due windows | PLAN-0029 |
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
- [x] The cooking page and the completion review work end to end in the browser, and the pantry reflects the result (`scripts/e2e/cooking-journey.mjs` through the proxy replica: 5/5).
- [x] Deployed to the owner host (`main` at `595af8e`, release `20261029T102710Z-595af8e7331b`).

## Execution state

- **Current run delivery target:** Phase 1 complete, together with the PLAN-0021 home tiers that make the loop visible from "Hoje", and the owner's language and detail feedback.
- **Current checkpoint:** Phase 1 implemented end to end:
  - backend and API;
  - the cooking page and completion review;
  - the recipe page shows full instructions, quantities, cues and equipment;
  - the live "Hoje" page;
  - the AI request carries the output language and the instruction style.
- **Validation (lean, targeted):**
  - backend unit tests: `RecipeExecutionTests`, `RecipeAiLanguageContractTests`, the recipe cook-now and protocol tests (29 passing);
  - backend integration tests: `RecipeExecutionApiTests`, `Plan0027*`, `InventoryLifecycleTransitionApiTests`, `PostgreSqlMigrationTests` (43 passing, run before the language change, which does not touch persistence);
  - `dotnet format` verified;
  - OpenAPI exported (additions only) and lint valid; client regenerated and drift check passed;
  - frontend: `Cooking.test.tsx` (4 new), recipe, i18n, app shell and home suites (22 files, 183 tests) passing; typecheck, ESLint and Prettier clean; guards (ts-only, interactive-nesting, production-isolation, third-party-html) passing; production build passing;
  - browser journey through the proxy replica: 5/5.
- **Not run:** full backend and frontend suites; the browser smoke matrix; the full local gates (owner direction: lean validation).
- **Phase 1:** merged via PR #50 at `595af8e` and deployed from `main`.
- **Phase 2:** the finalization part was delivered with PLAN-0038. After completion, "Salvar ajustes na receita" and "Criar variação" open the recipe editor with the session's notes, and the saved revision or derived recipe records the session (`sourceExecutionId`).
- **Phase 2 remainder (2026-10-30, branch `agent/plan-0042-cooking-adjustments`):** changes during cooking. The session stores a list of per-ingredient adjustments (`substitute` with a name and optional amount, `quantity`, `skip`) in `recipes.recipe_executions."AdjustmentsJson"` (jsonb, default `[]`, migration `AddExecutionAdjustments`). `PUT /api/v1/recipes/executions/{executionId}/adjustments` replaces the list while the session is in progress (unknown ingredient ids, duplicates, more than 40, or bad values are `validation_failed`; a finished session is `domain_rule_violated`). The proposal follows the adjustments (substitute looked up by its own name, quantity used as given, `skipped` lines) and the editor opened from the finished screen applies them to the draft (rename, amounts converted back to the recipe's servings, skipped → optional) and lists them. The inventory OpenAPI transformer no longer adds the lot `If-Match`/`Idempotency-Key` headers to non-inventory paths ending in `/adjustments`.
  - Validation: unit `RecipeExecutionTests` (5 passing, 2 new); integration `CookingAdjustmentApiTests` (new) with `RecipeExecutionApiTests` execution tests, `ProfileContractAndSecurityTests`, and migration tests (48 run, 47 passing; the one failure was `IdeasWrittenForOnePersonAreServedToAnotherWithoutAnyAiCall`, a test-order flake fixed in this run: the three ideas can tie on score, so the test now accepts any of the second person's lots and rejects the first person's; then 5/5 passing in isolation); architecture tests (14 passing); `dotnet format` verified; OpenAPI regenerated (additions only), client regenerated and drift check passed; frontend `src/features/recipes` (5 files, 31 tests, including a new cooking-adjustment test and `applyCookingAdjustments.test.ts`) and `src/app` (61 tests) passing; typecheck, ESLint, and Prettier clean.
  - Not run: full suites; browser journey.
- **Exact next action:** After the owner merges, deploy `main`. Phase 3 remainder (preparation-route due windows) and Phase 4 are open; the owner's next priority is PLAN-0029 Phase 4.
- **Blockers:** None.

## Progress log

### 2026-10-29T03:40:00Z — Claude Code

- Claimed the plan for Phase 1 per the owner's end-to-end direction. Reused the PLAN-0023 preparation transaction for reconciliation instead of a second consumption path. It already validates owner-scoped lots, per-lot versions, idempotency, history and provenance, and stores leftovers as prepared lots, so the PLAN-0037 attention rules apply to them.
- Backend delivered and validated as recorded above.

### 2026-10-29T05:10:00Z — Claude Code

- Frontend delivered: the cooking page (checklists), the completion review (editable lines, lot picker, leftovers, rating, notes, stale-pantry reload), the "Cozinhar agora" action, and the live "Hoje" page.
- Handled the owner's language and step-detail feedback as described above. Also replaced the "cook-now" jargon in the recipe copy with plain language in three locales.
- Added `scripts/e2e/cooking-journey.mjs` and ran it through the proxy replica: 5/5 passing.

### 2026-10-29T12:30:00Z — Claude Code

- The owner merged PR #50 (`595af8e`), and `main` was deployed.
- Phase 2 finalization was delivered on the PLAN-0038 branch: the finished screen saves changes as a revision or a derived recipe that cites the completed session. Substitutions recorded during cooking remain open.

### 2026-10-30T13:00:00Z — Claude Code

- Delivered the Phase 2 remainder: changes made while cooking (substitute, different amount, left out) are stored on the session, drive the completion proposal, and are applied to the editor draft opened after cooking. The saved recipe changes only when the owner saves.
- Fixed the order-dependent assertion in the idea library integration test found while validating.
