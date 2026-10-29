# PLAN-0038: Recipe Library — Manual Recipes, Revisions, Derived Recipes, Favorites, and History

- **Status:** Completed
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-29)
- **Created:** 2026-10-29
- **Last updated:** 2026-10-29
- **Branch:** `agent/plan-0038-recipe-library` (merged and deleted)
- **Pull request:** Merged via [PR #51](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/51) at `d85d301`
- **Roadmap entry:** [`PLAN-0031-amendment-2026-10-28-coverage-review.md`](PLAN-0031-amendment-2026-10-28-coverage-review.md) (PLAN-0038: what PLAN-0028 did not deliver)
- **Related documents:** `docs/domain/recipes-and-cooking.md` (recipe identity, revisions and derived recipes, favorites, execution-local state, history), [PLAN-0042](PLAN-0042-guided-cooking-execution-and-reconciliation.md) Phase 2

## Owner direction (2026-10-29)

"Vamos tentar fazer entregas grandes, quero uma versão usável de ponta a ponta logo." Once PLAN-0042 Phase 1 was merged (PR #50), a saved recipe could be cooked but never corrected, and the list was a flat log of AI results. This run makes the library usable:

- write a recipe;
- fix a recipe without losing the earlier version;
- turn a recipe into a variation;
- keep favorites;
- hide what is no longer wanted;
- see when the recipe was cooked and how it went.

It also delivers the finalization half of PLAN-0042 Phase 2: after cooking, the adjustments can be saved as a revision or a derived recipe, and the revision records the session it came from.

## Scope delivered

1. **Domain.**
   - **Recipe origins.** `Recipe.Origin` is `Generated | Manual | Derived`. A derived recipe records its parent recipe, the parent revision, and a required reason of at most 200 characters.
   - **Revision origins.** `RecipeRevision.Origin` is `Generated | Manual | Edited | Derived`, with an optional change reason and an optional source execution.
   - **Organization flags.** Favorite and archive flags are library organization only and never touch revision content.
   - **Revisions stay immutable.** They are append-only; the existing database trigger still rejects updates.
2. **Content composition** (`RecipeContentComposer`). Deterministic validation and composition of owner-written content into the protocol 0.3 shape that the recipe page and the cooking proposal already read.
   - **Limits.**
     - name: 80 characters;
     - meal types: 1–4 of `breakfast`, `lunch`, `dinner`, `snack`;
     - servings: 1–24;
     - ingredients: 1–40, with quantity greater than 0 and at most 100000, unit `g`, `ml`, or `unit`;
     - stages: 1–30, each with title, instructions of at most 2000 characters, minutes from 0 to 1440, and at most 6 sensory cues;
     - equipment: at most 20 items.
   - **When editing:**
     - rows that keep their `ingredientId` keep their pantry link (`sourceType` and `inventoryItemId`), so the proposal still offers the same lot when cooked;
     - new rows are `additional`;
     - references to removed stages or ingredients are pruned;
     - fields the editor does not show are kept: preparations, storage, assumptions, and the thumbnail descriptor.
3. **Persistence.** Migration `AddRecipeLibrary` is additive:
   - **New recipe columns:** origin, favorite, archive instant, parent recipe and revision, derivation reason, creation key.
   - **New revision columns:** origin, change reason, source execution.
   - **Relaxed columns:** the candidate and session columns are now nullable for authored revisions.
   - **Check constraints:**
     - origin values;
     - derived if and only if the parent is recorded;
     - generated if and only if the candidate and session are present;
     - edited if and only if the revision number is above 1.
   - **Foreign keys and indexes:**
     - the parent foreign key is restricted to the same owner;
     - the source execution foreign key is restricted;
     - a unique filtered index on `(OwnerUserId, CreationKey)`.
   - **Existing rows:** they satisfy every new constraint through the defaults.
   - **Concurrent edits:** editing advances `CurrentRevisionNumber` with a compare-and-set in the same transaction as the revision insert, so two concurrent edits cannot both win.
4. **API** under `/api/v1/recipes`. All routes are owner-scoped, and mutations are CSRF-protected and rate limited.
   - **Reads:**
     - `GET /?archived=&favorites=&search=`: the library. Archived recipes appear only with `archived=true`; search is case-insensitive with escaped wildcards. Each row has the times cooked, the last cooked instant, and the last rating.
     - `GET /{id}`: the current revision, plus the library state.
     - `GET /{id}/revisions/{n}`: an earlier revision.
     - `GET /{id}/history`: revisions and finished cooking sessions.
   - **Creation and edits:**
     - `POST /`: a manual recipe. `Idempotency-Key` is required; a replay returns 200 with the same recipe.
     - `POST /{id}/revisions`: the next revision. `expectedRevisionNumber` is required, and a stale edit gets 412.
     - `POST /{id}/derivations`: a derived recipe. `Idempotency-Key` is required, and a key reused for another parent gets 422.
   - **Organization:** `POST /{id}/favorite` and `POST /{id}/archive`.
   - **Cited cooking sessions:** `sourceExecutionId` must be a completed execution of the same recipe.
   - **Changed responses:** `RecipeSummaryResponse` and `RecipeDetailResponse` gained fields; no field was removed.
5. **Frontend.**
   - **Library list:**
     - search with a 300 ms debounce;
     - Todas, Favoritas, and Arquivadas filters, served by the server;
     - a favorite star per recipe;
     - times cooked and the last rating;
     - localized meal types;
     - origin badges;
     - "Escrever receita".
   - **Recipe page:**
     - cook, favorite, edit, variation, and archive/restore actions;
     - a link to the parent recipe for variations;
     - an earlier revision opens read-only with a banner;
     - a history section listing versions (origin, reason, "depois de um preparo") and cooking sessions (date, servings, rating, notes).
   - **Editor** (`/app/receitas/nova`, `/:id/editar`, `/:id/variacao`):
     - name, servings, meal types, and yield;
     - ingredient rows with locale decimals, unit, and optional;
     - equipment, one per line;
     - stages that can be reordered, with instructions, minutes, and cues;
     - a change reason, or a required variation reason;
     - client validation with the backend's limits and field names, and server field errors mapped to the same fields;
     - a stale-edit notice that never overwrites the newer revision.
   - **Finished cooking screen:** "Salvar ajustes na receita" and "Criar variação" open the editor with `?execucao=<id>`. The editor shows that session's notes and rating, and the saved revision records the session.
   - **Library error copy:** errors no longer say the recipe AI is unavailable, because library reads and edits never call it.
6. Contracts, documentation, and tests (below).

### Not delivered here

- **Execution-local substitutions during cooking.** PLAN-0042 Phase 2 lists them; the finalization path above covers saving changes after cooking. Recording substitutions as they happen, and pre-filling the editor from them, remains in PLAN-0042 Phase 2.
- **Library organization beyond favorites and archive** (collections, tags). Not requested; the domain documents only favorites.
- **Deletion.** Recipes are archived, not deleted, because executions and derived recipes reference them. Account deletion is unchanged (it does not touch recipes yet; PLAN-0049).
- **Re-generation of a recipe through the AI as a new revision.** Not part of this run.

## Acceptance criteria

- [x] A recipe written by the owner is saved once per key and can be cooked.
- [x] An edit creates the next immutable revision. The earlier revision stays readable, and a stale edit is refused with 412 without writing anything.
- [x] Edited rows keep their pantry link, so cooking the new revision proposes the same lot.
- [x] A derived recipe has its own identity and records its parent, the parent revision, and a reason. The parent is unchanged, and a retry with the same key returns the same recipe.
- [x] Favorites and archive organize the list without changing content. Archived recipes leave the default list and the home page.
- [x] History shows revisions (with "after cooking" when they came from a session) and finished cooking sessions.
- [x] Another user gets 404 for every read and mutation.
- [x] The browser journey passes through the proxy replica (`scripts/e2e/library-journey.mjs`, 10/10).
- [x] Deployed to the owner host (`main` at `d85d301`, release `20261029T112958Z-d85d301ed2f6`).

## Execution state

- **Current run delivery target:** Everything above, in one PR from fresh `main`.
- **Current checkpoint:** Implemented end to end and validated as below. Deploy after the commit, then open the draft PR.
- **Validation (lean, targeted):**
  - **Backend unit tests:** `RecipeLibraryTests` (4 new), `RecipeExecutionTests`, `RecipeCookNow*` (17 passing).
  - **Backend integration tests:** `RecipeExecutionApiTests` with two new library tests (full edit, derive, favorite, archive, search, history, and cross-user flow; manual creation and cooking), `PostgreSqlMigrationTests`, `Plan0027*` (39 passing).
  - **Formatting:** `dotnet format --verify-no-changes` clean.
  - **OpenAPI:**
    - exported, and lint passes;
    - 6 `no-ambiguous-paths` warnings, all from the pattern already on `main` (`/recipes/executions/{executionId}` against `/recipes/{recipeId}/…`). The GUID route constraint keeps runtime routing unambiguous.
  - **Client:** regenerated.
  - **Frontend tests:** `Library.test.tsx` (6 new) plus the recipe, adapter, app, and home suites, all passing.
  - **Frontend static checks:** typecheck, ESLint, and Prettier clean; the ts-only, interactive-nesting, production-isolation, and third-party-html guards pass; the production build passes.
  - **Browser journeys through the replica:**
    - `library-journey.mjs`: 10/10;
    - `cooking-journey.mjs`: 5/5, after making its search name unique per run (earlier runs' lots matched the shared prefix).
- **Not run:** full backend and frontend suites, the browser smoke matrix, and full local gates (owner direction: lean validation).
- **Exact next action:** Deploy, then open the draft PR. After merge, the candidates are:
  - PLAN-0029 (planning, menu, shopping), now unblocked by PLAN-0037 and PLAN-0038;
  - or PLAN-0042 Phase 2 execution-local substitutions.
- **Blockers:** None.

## Progress log

### 2026-10-29T12:30:00Z — Claude Code

- Claimed and delivered the plan in one run, after PR #50 was merged and `main` was deployed.
- Kept `IRecipeStore` for cook-now and cooking. Added `IRecipeLibraryStore` for library reads and edits, so the AI path did not change. Removed the unused cook-now list method and its projection.
- Found during the browser journey that library list errors showed the AI-unavailable message. Added `recipeLibraryErrorKey` for library reads and edits.

### 2026-10-29T12:40:00Z — Claude Code

- The owner merged PR #51 (`d85d301`), and `main` was deployed. Acceptance criteria are met, and the plan is completed. Remaining library ideas (collections, AI re-generation as a revision) need their own plans if wanted.
