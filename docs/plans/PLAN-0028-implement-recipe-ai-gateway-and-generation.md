# PLAN-0028: Implement Recipe AI Gateway and Cook-Now Generation Vertical Slice

- **Status:** Completed
- **Type:** Implementation
- **Priority:** Critical
- **Owner:** Cursor backend/frontend vertical-slice agent
- **Created:** 2026-08-05
- **Last updated:** 2026-08-06
- **Branch:** `agent/plan-0028-recipe-ai-gateway`
- **Pull request:** [PR #44](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/44) — ready for owner review (unmerged)
- **Predecessor:** PLAN-0022 (protocol `0.3` Revised, merged via PR #43)
- **Related ADR:** ADR-0005
- **Related contracts:** `packages/contracts/ai/recipe/`

## Objective

Reconcile the remaining initial-release roadmap after PLAN-0022 and deliver the first production cook-now recipe AI vertical slice:

```text
current inventory and profile
→ request cook-now candidates
→ display three validated candidates
→ select one candidate
→ expand into a normalized recipe
→ persist a user-owned recipe revision
→ open the saved recipe in the production frontend
```

## Included scope

- roadmap reconciliation and initial-release delivery map;
- AI Gateway foundation (`KitchenFlow.Modules.Ai`);
- DeepSeek provider adapter (replaceable, server-side secrets only);
- usage reservation and settlement ledger (no billing UI);
- Recipes module with generation sessions, candidate snapshots, and immutable revisions;
- protocol `0.3` schema/semantic validation in .NET with fixture parity;
- authenticated cook-now API with idempotency and owner isolation;
- OpenAPI and generated TypeScript clients;
- production Recipes frontend routes `/app/receitas`, `/app/receitas/gerar`, `/app/receitas/:recipeId`;
- degraded AI-unavailable behavior;
- Draft placeholders for PLAN-0029 and PLAN-0030;
- lean targeted tests and one final CI execution;
- merge-blocker remediations: atomic reservation, session/selection claims, transactional finalization, full-schema DeepSeek envelopes, real repair payloads.

## Excluded scope

- sequential planning, accepted menus, shopping projection (PLAN-0029);
- recipe thumbnail generation (PLAN-0030);
- contextual-home live menu sources (PLAN-0021);
- subscriptions, paid credits, billing UI;
- live DeepSeek calls in CI / rerunning PLAN-0022 campaign;
- independent validation plan or PR;
- guided cooking, import, sharing, notifications.

## Usage units versus provider tokens

- **KitchenFlow usage units** (`ReservedUnits` / `SettledUnits`) are normalized credits enforced by the usage governor (default: suggest = 3, expand = 5). Ceilings and budget exhaustion apply only to these units.
- **Provider tokens** (`PromptTokens` / `CompletionTokens`) are optional observability metadata from the model provider. They are never treated as budget units and do not affect reservation or settlement arithmetic.
- **Future monetary cost** may later derive from provider tokens and published prices. Billing is out of scope.

## Concurrency and atomicity (merge-blocker remediations)

### Atomic usage reservation

`IAiUsageLedgerStore.TryReserveAsync` performs one PostgreSQL transaction with a dedicated `pg_advisory_xact_lock`, evaluates global daily / owner daily / owner concurrency ceilings, and inserts the reservation only when every ceiling permits it. The governor does not perform separate read/check/insert calls.

### Session claim before provider

`POST /recipes/generation-sessions` claims the owner/idempotency pair (`AwaitingCandidates`) before usage reservation or DeepSeek invocation. Concurrent same-key requests reload the existing session without a second reservation or provider call. Unique-constraint conflicts reload rather than surfacing as unhandled database exceptions.

### Selection claim (`Expanding`)

Before expand reservation/provider, the session atomically transitions `CandidatesReady → Expanding` with selected candidate ID, selection idempotency key, and claim timestamp. Same key + same candidate replays; same key + different candidate/session returns `ai_operation_conflict`; concurrent selections yield exactly one winner.

### Transactional recipe finalization

One persistence transaction creates the recipe identity, first immutable revision, marks the session `Selected`, associates candidate/recipe, and settles usage. Provider invocation remains outside the database transaction.

### Unavoidable crash window

Between a successful provider response and the finalization commit, a process crash can leave a settled-or-open reservation without a persisted recipe, or require release on retry. Concurrent duplicates are still prevented by the persisted execution claim (`AwaitingCandidates` / `Expanding`). Operators must not treat that window as absent.

## Acceptance criteria

- [x] Roadmap delivery map and stale plan reconciliation committed.
- [x] AI Gateway registers `recipe.suggest_candidates.v1` and `recipe.expand_selected.v1`.
- [x] DeepSeek adapter is replaceable; secrets absent from repository defaults.
- [x] Usage ledger reserves, settles, and releases; ceilings and disablement enforced.
- [x] Cook-now flow returns exactly three validated candidates or fails safely after one repair.
- [x] Selected expansion persists one owner-isolated immutable recipe revision.
- [x] Frontend production routes support generate → select → open saved recipe with localized unavailable states.
- [x] OpenAPI drift, migration, focused backend/frontend tests, and recipe contract validators pass.
- [x] Atomic usage reservation, session/selection claims, Expanding status, schema-embedded repair, and focused PostgreSQL concurrency tests remediate PR #44 merge blockers.
- [x] Bounded synthetic live DeepSeek smoke Passed (suggest + expand; max one repair each).
- [x] Draft/ready PR open; no approval/auto-merge/merge by the agent.

## Execution state

- **Current checkpoint:** Merge-blocker remediations complete; focused Fake-only concurrency tests Passed; live synthetic smoke Passed; awaiting final CI on pushed head and owner review of PR #44.
- **Run target:** Complete PLAN-0028 remediations on PR #44 without a separate validation plan or stress campaign.
- **Blockers:** None.
- **Exact next action:** Owner reviews and merges PR #44. Agent must not approve, auto-merge, or merge.

## Progress log

### 2026-08-06 — PR #44 merge-blocker remediations completed

- Replaced non-atomic usage count/sum/insert with `IAiUsageLedgerStore.TryReserveAsync` using PostgreSQL transaction + dedicated `pg_advisory_xact_lock`; `AiUsageGovernor.ReserveAsync` only short-circuits Disabled then calls TryReserveAsync.
- `SettleAsync` persists optional `PromptTokens` / `CompletionTokens`; XML/docs distinguish KitchenFlow units from provider tokens. Migration `20260806011628_AddRecipeAiGatewaySlice` revised in place (Expanding status + token columns).
- Added `TryClaimNewSessionAsync` and `TryClaimSelectionAsync` (CandidatesReady → Expanding); `IRecipeCookNowUnitOfWork` finalizes suggest/expand in one EF transaction (documented crash window after provider return).
- Embedded linked protocol `0.3` response schemas into Recipes; repair payloads include schema + bounded validation errors + previous invalid output (not identical retry).
- Added focused PostgreSQL concurrency integration tests (one synchronized concurrent run each) and updated Fake-only unit tests / frontend Expanding|AwaitingCandidates polling.
- Live synthetic smoke via `scripts/ai/RecipeGatewayLiveSmoke`: suggest Passed (1 repair), expand Passed (0 repair), 3 provider calls, ~US$ 0.00243 estimated, ceiling US$ 0.05. Evidence: `docs/evidence/plan-0028/`.

### 2026-08-05 — Claim

- Created branch `agent/plan-0028-recipe-ai-gateway` from main `cfec79546fcd5e476f7198cdc788a6ce251d35c5`.
- Registered PLAN-0028 as In Progress.

### 2026-08-05 — Roadmap reconciliation

- Added `docs/roadmap/initial-release-delivery-map.md`.
- Reconciled PLAN-0022 as merged through PR #43.
- Set PLAN-0021 to Blocked on PLAN-0029 accepted-menu read contract.
- Updated PLAN-0008 thumbnail amendment delivery (docs merged; implementation pending PLAN-0030).
- Created Draft PLAN-0029 and PLAN-0030 placeholders.
- Updated root README production capability list.

### 2026-08-05 — Backend cook-now API slice

- Wired EF persistence for `ai.usage_ledger` and `recipes.*` with owner-scoped FKs, unique idempotency indexes, and append-only revision trigger; migration `20260806011628_AddRecipeAiGatewaySlice`.
- Implemented AI Gateway (DeepSeek/Unavailable/Fake), usage governor, Recipes module, authenticated `/api/v1/recipes` endpoints.
- Focused unit tests passed with Fake only; OpenAPI exported and TypeScript clients regenerated; recipe contract validators passed.

### 2026-08-05 — Production frontend and completion

- Added production Recipes area (`/app/receitas`, `/gerar`, `/:recipeId`), primary nav (`production-nav-receitas`), `createLiveRecipeRepository` over generated `@kitchenflow/api-client` recipe paths (CSRF + Idempotency-Key), prototype unavailable stub (no mock fixtures), `recipesCatalog` en/pt-BR/es, and degraded unavailable/budget/invalid-output/empty-inventory states.
- Focused frontend tests (`RecipesFlow`, `liveRecipeRepository`, `recipesCatalog`, `productionIsolation`) passed.
- Remaining for the owner: review PR #44. Agent must not approve, auto-merge, or merge.

### 2026-08-05 — CI format remediation

- Applied `dotnet format` and Prettier so Backend `build-and-test` and Frontend `quality` gates pass on the final PR head.
