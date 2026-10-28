# PLAN-0022: Evaluate and Finalize Recipe AI Artifact Contracts

- **Status:** Completed
- **Type:** Research
- **Priority:** High
- **Owner:** Cursor agent (PLAN-0022 lean evaluation)
- **Created:** 2026-08-02
- **Completed:** 2026-08-05
- **Last updated:** 2026-08-05
- **Branch:** `agent/plan-0022-recipe-ai-evaluation`
- **Pull request:** Merged [PR #43](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/43) at `cfec79546fcd5e476f7198cdc788a6ce251d35c5`
- **Predecessor:** PLAN-0017 documentation delivery
- **Related operations plan:** PLAN-0008
- **Related ADR:** ADR-0005
- **Lean evaluation amendment:** [`PLAN-0022-amendment-2026-08-05-lean-evaluation.md`](PLAN-0022-amendment-2026-08-05-lean-evaluation.md)
- **Lean validation policy:** [`PLAN-0007-amendment-2026-08-05-lean-validation.md`](PLAN-0007-amendment-2026-08-05-lean-validation.md)

## Objective

Produce decision-ready evidence for the recipe candidate and selected-expansion protocol, finalize strict machine contracts and thresholds, and accept, revise, or reject the draft protocol **without** implementing production provider integration.

Under the lean amendment, the goal is to **unblock recipe AI contract implementation**, not to statistically select a permanent provider.

## Consolidated ownership

This plan is the sole owner of:

- PLAN-0017's unfinished benchmark campaign (reduced by the lean amendment);
- the recipe-specific model-evaluation portion of PLAN-0008 Phase 3;
- the strict JSON Schema/tool-output decision;
- final candidate/expansion field limits and semantic thresholds.

PLAN-0017 is completed as documentation history. PLAN-0008 consumes this plan's evidence for economics and routing; it does not duplicate the campaign.

## Included scope

- deterministic validation of fixtures 01–10;
- strict candidate and expansion JSON Schemas (`0.3`);
- positive/negative contract fixtures and semantic validators;
- bounded live campaign: 1 thinking + 1 fallback for `cook_now`, 1 thinking + 1 fallback for `menu_planning`;
- at most one repair retry for empty/truncated/invalid output;
- injection/no-authority checks;
- ingredient-identity and package-confidence decisions;
- `cook_now` / menu-planning model policy;
- protocol disposition.

## Excluded scope

- production AI Gateway/provider adapter;
- real-user data;
- authoritative inventory/planning/shopping implementation;
- image generation, object storage, or thumbnail jobs;
- browser/provider credentials in source;
- direct frontend provider calls;
- model reasoning retention or publication;
- selection of a permanent provider solely from this benchmark;
- three-repetition statistical campaigns and p95 claims from the lean sample;
- independent validation PR or PLAN-0029 retest.

## Environment and credential rules

- Use synthetic fixtures only.
- Keep API keys in ignored environment/secret storage (`DEEPSEEK_API_KEY`).
- Never commit or print credentials or private reasoning.
- Bound total test cost before the first call (`PLAN0022_COST_CEILING_USD`, default `0.05`).
- Separate live-provider evaluation from default repository tests (`PLAN0022_LIVE_EVAL=1`).
- On `NOTEBOOK-DEB-RODRIGO`, use GitHub App wrappers, an `agent/` branch, a draft PR, and request `RodrigoWantuk` review.

## Acceptance criteria

- [x] All request fixtures parse and have deterministic validators.
- [x] Strict candidate and expansion schemas are versioned.
- [x] Positive and negative contract fixtures cover schema, references, units, limits, privacy, and injection/no-authority cases.
- [x] Bounded live campaign executed (4 scenarios, 8 actual provider calls, ≤1 repair each) with cost ceiling.
- [x] Exact-reference, canonical-unit, equipment, lead-time, and assumption rules are encoded.
- [x] Ingredient-identity and package-confidence decisions are recorded.
- [x] `cook_now` and menu-planning model policies are recorded.
- [x] Statistical p50/p95 characterization explicitly deferred.
- [x] Protocol disposition is explicit (**Revised** → `0.3`).
- [x] No production integration or user data is introduced.
- [x] No independent validation PR / PLAN-0029 created.
- [x] PLAN-0008 references this evidence rather than duplicating it.

## Execution state

- **Current checkpoint:** PLAN-0022 Completed and merged through PR #43 at `cfec79546fcd5e476f7198cdc788a6ce251d35c5`. Protocol `0.3` Revised; lean live campaign evidence retained with corrected accounting.
- **Run target:** Delivered.
- **Blockers:** None.
- **Exact next action:** None. Successor implementation is PLAN-0028 (AI Gateway and cook-now generation).

## Progress log

### 2026-08-05 — Draft PR opened

- Opened Draft [PR #43](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/43) requesting review from `RodrigoWantuk`.
- Live campaign still blocked on `DEEPSEEK_API_KEY` at that checkpoint.

### 2026-08-05 — Lean live campaign and completion

- Loaded `DEEPSEEK_API_KEY` from ignored `.env` (never committed or printed).
- Ran `PLAN0022_LIVE_EVAL=1 PLAN0022_COST_CEILING_USD=0.05 node scripts/ai/recipe-live-eval.mjs`.
- Results: thinking-high truncated (reasoning exhausted completion budget); fallback produced JSON that failed strict schema but passed semantic checks for both `cook_now` and `menu_planning`.
- Evidence under `docs/evidence/plan-0022/`; addendum `docs/ai/provider-evaluations/deepseek-v4-flash-plan-0022-lean-2026-08-05.md`.
- Validation: `scripts/contracts/validate-recipe-ai.sh` remains Passed.
- Disposition unchanged: **Revised** → protocol `0.3`.
- PLAN-0022 marked Completed. No approval, auto-merge, or merge performed.

### 2026-08-05 — Final handoff push

- Committed remaining PLAN-0021 Ready execution-state sync.
- Pushed final PLAN-0022 completion head to Draft PR #43 for owner review and one final CI run.
- No approval, auto-merge, or merge performed.

### 2026-08-05 — Merged

- Merged through PR #43 at `cfec79546fcd5e476f7198cdc788a6ce251d35c5`.
- Successor: PLAN-0028 implements the production AI Gateway and cook-now vertical slice.
