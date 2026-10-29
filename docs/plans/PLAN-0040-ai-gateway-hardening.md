# PLAN-0040: AI Gateway Hardening — Evaluated Model, Monthly Spend Ceiling, Latency, and Output Quality

- **Status:** Completed
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-29)
- **Created:** 2026-10-29
- **Last updated:** 2026-10-29
- **Branch:** `agent/plan-0040-ai-hardening` (merged and deleted); token windows on `agent/plan-0029-ai-menu`
- **Pull request:** Merged via [PR #54](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/54) at `47752fc`; token windows in the PLAN-0029 Phase 2 PR
- **Roadmap entry:** [`PLAN-0031-amendment-2026-10-28-coverage-review.md`](PLAN-0031-amendment-2026-10-28-coverage-review.md) (PLAN-0040, D-3, G-07)
- **Decision:** [ADR-0009](../architecture/decisions/0009-recipe-ai-provider-deepseek-v4-flash.md)
- **Related documents:** `docs/ai/usage-and-cost-governance.md`, `docs/ai/decisions/model-routing-cook-now-menu-planning-2026-08-05.md`, `docs/ai/recipe-artifact-protocol.md`

## Objective

Make recipe AI calls use the model the owner evaluated, bound monthly spend, measure latency and cost per call, and enforce the requested output language and instruction detail as far as deterministic code can. This finishes the gateway work that PLAN-0028 deferred, and it unblocks AI-assisted menu planning (PLAN-0029 Phase 2).

## Findings that started the run

- **Model.** Production used `deepseek-chat`, the PLAN-0028 default in `appsettings.json`, instead of `deepseek-v4-flash`, the model the owner evaluated (D-3). The ledger showed two settled calls, both on `deepseek-chat`.
- **Spend.** No monetary limit existed; only usage units were governed.
- **Failed calls.** A released call recorded no tokens, so a failed call's real cost was invisible.
- **Quality.** The owner's 2026-10-29 feedback (English recipes, terse steps) left language and detail as evaluation criteria for this plan.

## Scope delivered

1. **Provider** (`DeepSeekAiProvider`, `DeepSeekOptions`).
   - **Model:** `Model = deepseek-v4-flash`. `thinking` is set per operation (disabled for synchronous cook-now), `reasoning_effort` is sent only with thinking, and `max_tokens` comes from the operation registration.
   - **Fallback:** one call to `FallbackModel` (default `deepseek-chat`) after a provider error, never after a timeout. The fallback request omits `thinking`, which older aliases do not take.
   - **Recording:** the answering model reported by the provider and the wall-clock latency.
   - **Replaced settings:** `NonThinkingModel` and `ThinkingModel` are replaced by `Model`, `FallbackModel`, and `ReasoningEffort`.
2. **Cost** (`AiPricingOptions`, `Ai:Pricing`).
   - **Prices:** list prices per model (v4-flash US$ 0.14 / 0.28 per million input / output tokens, as recorded by the PLAN-0022 evaluation; `deepseek-chat` 0.28 / 0.42). An unlisted model is charged at the highest listed price.
   - **Unit:** cost is exact in micro-dollars, rounded up.
3. **Monthly ceiling** (`Ai:Usage:MonthlyCostCeilingUsd`, US$ 5 by default).
   - **Where it is checked:** in the same advisory-locked reservation as the existing unit and concurrency ceilings.
   - **What counts:** closed calls at real cost, open ones at the operation estimate (suggest US$ 0.003, expand US$ 0.005).
   - **Failed calls:** released calls keep tokens and cost.
   - **When exceeded:** `MonthlyCostExhausted`, which maps to `ai_budget_exhausted`.
   - **Copy:** the user-facing message now says the AI reached its usage limit and that saved recipes, pantry, and menu still work.
4. **Ledger** (migration `AddAiCostAndLatency`, additive).
   - **New columns:** `EstimatedCostMicroUsd`, `CostMicroUsd`, `LatencyMs`, `Attempts`, `QualityFlags`.
   - **Shared writes:** settlement and release are written by the same static helpers from the ledger store and the recipe unit of work.
   - **Existing rows:** they keep a null cost and are listed by the report.
5. **Soft quality checks** (`RecipeOutputQuality`).
   - **Language:** checked against `outputLanguage`, on candidate summaries and on stage instructions and cues.
   - **Detail:** stage length checked against `instructionStyle` (detailed: each stage ≥ 120 characters and an average ≥ 200; standard: each ≥ 50; minimal: unchecked).
   - **Repair:** a soft issue uses the single repair, but the first valid answer is kept when the repair is not clean, and its flags (`language`, `detail`) go to the ledger. The suggest and expand repair loops were merged into one generic loop that sums tokens, latency, and attempts across every call.
6. **Operations.** `scripts/ops/ai-usage-report.sh` reports:
   - this month's spend against the ceiling;
   - calls by operation, model, and outcome, with attempts and cost;
   - latency p50/p95/max;
   - quality flags.

   This is how G-07 latency characterization accumulates from real use.
7. **Documentation.** ADR-0009, the cost governance document, the routing decision status, the protocol document's quality checks, and runbook configuration.

### Rolling 24-hour token windows (owner request, 2026-10-29)

"Conseguimos colocar janelas de 24 em 24h de uso máximo de tokens ao invés de limitar apenas mensalmente?" The monthly cost ceiling stays. In addition, every reservation checks provider tokens (prompt plus completion) in the rolling 24 hours before it:

- `Ai:Usage:GlobalTokensPer24Hours`: 600 000 tokens for the whole deployment, about the monthly US$ 5 divided by 30 at v4-flash prices;
- `Ai:Usage:UserTokensPer24Hours`: 150 000 tokens per owner, about ten recipes.

Closed calls count at their reported tokens, failed ones included; open reservations count at their operation's `EstimatedTokens` (suggest 14 000, expand 18 000). The check runs under the same advisory lock, so concurrency cannot exceed it. When a window is exceeded, the operation returns `ai_budget_exhausted`, and the problem detail says the limit frees up as earlier calls leave the window. Migration `AddAiTokenWindows` adds `EstimatedTokens` to the ledger, and the usage report shows the deployment's and the busiest owner's tokens in the window. The integration test `TokenWindows_LimitRolling24HoursPerOwnerAndForTheDeployment` covers the per-owner and deployment limits, settlement at real tokens, and the moving window.

### Not delivered

- **Streaming and time to first token.** Latency is total provider time; TTFT needs streaming and remains unmeasured.
- **A live evaluation campaign against the provider.** This run made no provider calls; the API key lives only in the service environment, and agents do not read credentials. The first real recipe after deploy shows the model, cost, and latency in the report.
- **Per-user monetary plans and billing** (PLAN-0050).
- **Thinking-high menu planning** (PLAN-0029 Phase 2).

## Acceptance criteria

- [x] Requests use `deepseek-v4-flash` with an explicit `thinking` mode and per-operation `max_tokens`; a provider error falls back once; a timeout does not.
- [x] Monthly spend cannot exceed the ceiling: closed calls count at real cost (failed ones included) and open ones at estimate, under the existing advisory lock.
- [x] Every call records cost, latency, attempts, and quality flags; the report shows spend, p50/p95, and flags.
- [x] A valid answer in the wrong language or with terse stages triggers one repair; a clean repair replaces it, and otherwise the valid answer is kept with a flag. No valid answer is lost.
- [x] Deployed (`main` at `47752fc`). The live call check waits for the owner's first recipe after deploy; no live call has happened yet (usage report, 2026-10-29).

## Execution state

- **Current run delivery target:** Scope above, in one PR from fresh `main`.
- **Current checkpoint:** Implemented and validated as below. Deploy after the commit, run the usage report on the host, then open the draft PR.
- **Validation (lean, targeted):**
  - **Backend unit tests:** 39 pass across the recipe and AI suites. They include the new `AiGatewayHardeningTests`:
    - the request body and fallback;
    - the thinking parameters and no retry on timeout;
    - pricing;
    - the language and detail heuristics.

    They also include two new cook-now tests: a repair that wins, and a kept answer with a flag.
  - **Backend integration tests:** the recipe AI gateway, Plan0027, and migration tests (38) pass. The new monthly-ceiling test against PostgreSQL also passes (real cost of released and settled calls, estimates of open ones, month boundary, stored fields), and its class (4 tests) passes.
  - **Frontend:** the recipe and i18n tests pass after the copy change.
- **Not run:** a live provider call (no key in the agent environment), the full suites, and the full local gates.
- **Exact next action:** Deploy, run `scripts/ops/ai-usage-report.sh` on the host, and open the draft PR. After the owner generates a recipe, check the model, cost, latency, and flags in the report. Next plan after merge: PLAN-0029 Phase 2 (AI-assisted menu).
- **Blockers:** None.

## Progress log

### 2026-10-29T14:30:00Z — Claude Code

- Claimed after PR #53 was merged and `main` was deployed.
- The ledger showed production on `deepseek-chat`; `appsettings.json` pinned it.
- Delivered the scope above in one run.

### 2026-10-29T15:20:00Z — Claude Code

- The owner merged PR #54 (`47752fc`), and `main` was deployed. Acceptance criteria are met, and the plan is completed.
- The owner asked for rolling 24-hour token windows. They were delivered on the PLAN-0029 Phase 2 branch, as described above, because the AI menu is the heaviest token consumer. No live recipe has been generated since the deploy, so the model check stays with the owner.
