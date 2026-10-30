# PLAN-0055: Ingredient Identity — Curated Catalog, Exact-Text Embedding Cache, and Cross-Language Matching

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High. It is the foundation for reusing recipe ideas and dish images instead of calling AI again (owner, 2026-10-30).
- **Owner:** Claude Code (claimed 2026-10-30)
- **Created:** 2026-10-30
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0055-ingredient-identity`, stacked on `agent/plan-0054-phases-2-4-and-plan-0030` (PR #58), because it uses the OpenRouter configuration that PR delivers
- **Pull request:** Draft, opened after the first commit; merge after PR #58
- **Decision:** [ADR-0010](../architecture/decisions/0010-ingredient-identity-catalog-embeddings-pgvector.md)
- **Design:** [`docs/ai/ingredient-identity.md`](../ai/ingredient-identity.md)

## Objective

Stop unnecessary AI calls for recipes and images by first giving ingredients a shared identity. Recipe-idea reuse and image reuse both need to index by main ingredients. Today ingredients are free text in several languages, so neither is possible.

## Owner decisions (2026-10-30)

- Standardize each ingredient name by what it really is, using a very cheap external model: OpenRouter `qwen/qwen3-embedding-8b`. A tokenizer was discussed and set aside, because tokens do not carry meaning (ADR-0010, option A).
- Before every embedding call, reuse the stored embedding of the **exact** same text, and only of the exact text.
- Choose recipes and images with our own algorithm: score candidates, return the best one above a threshold, and generate only below it. This applies to the phases after this one.
- Proceed with pgvector on the host. Use Docker Compose where it makes the environment reproducible.
- Image prompt asks for a natural, photorealistic photograph (delivered under PLAN-0030 in PR #58).

## Phases

| Phase | Outcome | Status |
| --- | --- | --- |
| 1 | Curated catalog, alias table, non-blocking resolution job (catalog → trigram → embeddings with exact-text cache), cross-language matching in the cooking proposal, shopping list, and menu reservations; pgvector in every environment | Delivered in this run |
| 2 | Shared recipe-idea library: generic ideas indexed by canonical main ingredients, deterministic retrieval → hard filters → scoring, AI only for the gap, plus a novelty share | Next |
| 3 | Dish-image reuse by score: a visual signature from canonical visible components and dish format; reuse the best image at or above a threshold, generate below it | After Phase 2 |
| 4 | Live-model evaluation and threshold tuning with real names (needs the OpenRouter key) | When the key is active |

## Phase 1 scope delivered

- **Module.** New module `Cocinaris.Modules.Ingredients`:
  - `IngredientText` (normalization shared with Recipes and Planning);
  - `IngredientCatalog` (embedded, validated as unambiguous);
  - `IngredientMatchKeys`;
  - `IIngredientMatching` / `IngredientIdentityService`;
  - `IngredientResolutionService`;
  - `IIngredientStore`.
- **Catalog.** 146 canonical ingredients with 950 normalized names in English, Portuguese, and Spanish. Ambiguous words are left out (for example "pimenta" alone, "beans", Spanish "batata").
- **AI Gateway.**
  - `IEmbeddingProvider`, with `OpenRouterEmbeddingProvider` (provider order pinned, `allow_fallbacks: false`, `data_collection: "deny"`, truncation to 1024 dimensions and renormalization, whole-batch validation) and `FakeEmbeddingProvider`.
  - Operation `ingredient.embed.v1` registered; every call goes through the usage ledger.
- **System principal.** `SystemPrincipals.Maintenance` (Identity module) owns provider calls no person triggered.
- **Persistence.**
  - Schema `ingredients`: `catalog_items`, `aliases` (GIN trigram index), `resolution_queue`, `catalog_state`, `text_embeddings`, and `catalog_vectors` (pgvector).
  - Migration `AddIngredientIdentity`, which creates the extensions and the system principal.
- **Matching.** The cooking proposal, shopping list, and reservations compare catalog ingredient keys, falling back to normalized names. Units are still compared separately.
- **Background job.** `IngredientResolutionWorker` (not started in Testing; removed in integration hosts).
- **Environment.**
  - Development compose and Testcontainers use `pgvector/pgvector:0.8.7-pg18-trixie`.
  - The host has `postgresql-17-pgvector`.
  - The deploy script creates the extensions as superuser and refuses to deploy without pgvector.
- **Operations.** `scripts/ops/ingredient-identity-report.sh`; runbook section "pgvector"; `api.env.example` key `Ai__Embeddings__Enabled`.
- **Documentation.** ADR-0010, `docs/ai/ingredient-identity.md`, privacy record (ingredient identity tables), domain docs (matching), AI overview, architecture overview, environment guide.
- **Evaluation fixture.** `apps/backend/tests/fixtures/ingredient-identity-eval.json` (43 names) for the live-model check in Phase 4.

## Acceptance criteria (Phase 1)

- [x] A recipe ingredient in one language proposes a pantry lot named in another for curated names, with no provider call.
- [x] Shopping lines and reservations merge stock across languages for the same ingredient and unit.
- [x] Without any alias, matching behaves exactly as before (existing projector and execution tests unchanged and passing).
- [x] Requests never wait for a provider; unknown names are queued with their exact text, at most once.
- [x] The job decides by curated name, then trigram (curated names only), then embeddings with threshold and margin. It never rewrites a decided alias.
- [x] The embedding of a text is reused only for the identical text in the identical space, role, and instruction version.
- [x] Every provider call is reserved and settled in the usage ledger, charged to the owner or to the maintenance principal.
- [x] Changing the catalog, space, or thresholds re-checks unresolved names, using cached vectors.
- [x] Without a key the product works; curated and trigram matches still apply.
- [x] Development, tests, and the host have pgvector; deploy refuses to run without it.
- [ ] Live-model evaluation with the real provider (Phase 4): the test exists; its run with the key is pending (owner command in `docs/ai/ingredient-identity.md#evaluation`).

## Execution state

- **Current checkpoint:** Phase 1 implemented, tested, and documented on `agent/plan-0055-ingredient-identity`.
- **Validation (2026-10-30, host `rwantuk`):**
  - Backend build: clean (warnings as errors).
  - Unit tests: 155 passed, including 18 new `IngredientIdentityTests`.
  - Architecture tests: 14 passed.
  - Integration tests (Testcontainers, `pgvector/pgvector:0.8.7-pg18-trixie`): 10 passed in `RecipeExecutionApiTests`, including the 2 new ingredient identity tests (catalog matching through HTTP; the job end to end with trigram, embedding, ledger, and exact-text cache reuse).
  - Migration tests: 21 passed.
  - `dotnet format --verify-no-changes`: clean (EF had written UTF-8 BOMs on the new migration files; removed per `.editorconfig`).
- **Not verified:** the live OpenRouter embeddings call (no key yet), and threshold quality on real names.
- **Deployment:** the branch has not been deployed in this commit; the deploy outcome is recorded in the next progress entry or in the PR.
- **Exact next action:**
  1. The owner merges PR #58, then this PR.
  2. Deploy `main`. The deploy creates the extensions and runs the migration.
  3. Set `Ai__Embeddings__Enabled=true` once `Ai__Providers__OpenRouter__ApiKey` is in `/etc/wtk-cocinaris/api.env`.
  4. Start Phase 2 (shared recipe-idea library) with its own design section in `docs/ai/`.
- **Blockers:** none for Phase 2. Phase 4 needs the OpenRouter key (owner).

## Progress log

### 2026-10-30 — Claude Code

- Found that PR #57 had been squash-merged at its Phase 2 head, so PLAN-0054 Phases 2b–4, PLAN-0030, and the natural-photo prompt never reached `main`. Rebased them unchanged (identical tree) onto `main` as `agent/plan-0054-phases-2-4-and-plan-0030` and opened draft PR #58.
- Installed `postgresql-17-pgvector` 0.8.0 on the host (owner authorization 2026-10-30).
- Implemented Phase 1 as listed above. Measured `pg_trgm` scores on real names to set the trigram threshold: 0.77 for "azeite extra virgem", 0.5 for "alho poro" against "alho".
- An integration run showed the resolution worker starting in the `Development`-environment test hosts and querying before migration. The shared test host now removes it, as the thumbnail test already did for its own worker.

### 2026-10-30 — Claude Code (delivery and evaluation)

- PR #59 merged into the branch of #58 (its stacked base) after #58 had already been squash-merged, so Phase 1 never reached `main`. The same commit is cherry-picked onto `main` in PR #60. Its tree is identical to the deployed release `f770b05`.
- On the host: the owner deployed the branch and enabled `Ai__Embeddings__Enabled` with the OpenRouter key. The catalog synced with the Qwen space in its evaluation. No names were queued yet, because no unknown names had been read.
- Queuing the fixture names directly in the deployment database was refused by the session's permission policy. Added `IngredientLiveEvaluationTests` instead: an opt-in test that runs the real model against a throwaway database when `COCINARIS_EVAL_OPENROUTER_API_KEY` is set, and fails only on wrong links. Without the key it returns at once (verified).
- Next: the owner runs the command in `docs/ai/ingredient-identity.md#evaluation`. Thresholds are tuned only from its output. Then start Phase 2.

