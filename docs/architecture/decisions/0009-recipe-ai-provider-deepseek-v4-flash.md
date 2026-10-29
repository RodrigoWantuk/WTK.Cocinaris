# ADR-0009 — Recipe AI Provider: DeepSeek V4 Flash with a Monthly Spend Ceiling

- **Status:** Accepted
- **Date:** 2026-10-29
- **Decision owner:** Repository owner (decision D-3, recorded in the PLAN-0031 coverage-review amendment)
- **Plan:** [PLAN-0040](../../plans/PLAN-0040-ai-gateway-hardening.md)
- **Builds on:** ADR-0005 (AI Gateway and usage governance)
- **Evidence:** `docs/ai/provider-evaluations/deepseek-v4-flash-2026-08-01.md`, `docs/ai/provider-evaluations/deepseek-v4-flash-plan-0022-lean-2026-08-05.md`, `docs/ai/decisions/model-routing-cook-now-menu-planning-2026-08-05.md`

## Context

- **Evaluated vs deployed model.** The owner evaluated DeepSeek `deepseek-v4-flash` and found it satisfactory for recipe suggestion and expansion (PLAN-0017, PLAN-0022). The gateway built in PLAN-0028, however, defaulted to the `deepseek-chat` alias, and production used it.
- **No monetary limit.** Usage was governed only by normalized usage units, so no limit existed on what the deployment could spend.
- **Failed calls went uncounted.** Failed calls were released without recording what the provider had billed.
- **Owner feedback.** The owner reported recipes in English despite a Portuguese profile, and steps too short for a profile that asks for detailed instructions.

## Decision

1. **Provider and model.** Recipe operations use the DeepSeek chat-completions API with `deepseek-v4-flash` (`Ai:Providers:DeepSeek:Model`).
   - Thinking is set per operation with the request's `thinking` parameter. Synchronous cook-now operations disable it, as the routing decision requires; `reasoning_effort` is sent only when thinking is enabled.
   - `max_tokens` comes from the operation registration: 4096 for suggestion, 6144 for expansion.
2. **One fallback model.** When the primary call fails with a provider error (not a timeout), the adapter calls `Ai:Providers:DeepSeek:FallbackModel` once (default `deepseek-chat`). This keeps recipe generation working if the provider renames or rejects the primary model. The model that answered is recorded and priced.
3. **Monthly spend ceiling.**
   - **Limit:** `Ai:Usage:MonthlyCostCeilingUsd`, US$ 5 by default and changeable by the owner, is enforced in the same advisory-locked reservation as the existing ceilings.
   - **What counts:** closed calls count at real cost, computed from provider-reported tokens and `Ai:Pricing` list prices; open reservations count at their operation's estimate.
   - **Failed calls:** released calls keep their tokens and cost.
   - **Unlisted models:** a model missing from the price table is charged at the highest listed price.
   - **When exceeded:** the operation fails with `ai_budget_exhausted`, and the non-AI product keeps working.
4. **Observability.** Every call records, on the usage ledger:
   - provider latency summed across attempts;
   - attempts (first call, repairs, and fallbacks);
   - cost in micro-dollars;
   - quality flags.

   `scripts/ops/ai-usage-report.sh` reports spend against the ceiling, latency p50/p95 per operation and model, repairs, and flags. This is how the deferred latency characterization (G-07) accumulates from real use.
5. **Deterministic soft quality checks.** These run after schema and semantic validation.
   - **Language:** distinctive function words of Portuguese, Spanish, and English, checked against `outputLanguage`. Texts under 25 words are not judged.
   - **Stage detail:** checked against `instructionStyle`.
     - Detailed: every stage at least 120 characters and an average of at least 200.
     - Standard: every stage at least 50 characters.
     - Minimal: never flagged.
   - **Repair:** a soft issue uses the operation's single repair. It never discards a valid answer: if the repair is not clean, the first valid answer is kept and its flags are recorded.

## Consequences

- Production moves to the model the owner evaluated. The fallback contains the risk of a wrong model identifier at the cost of one extra call in that case.
- Spend has a hard monthly limit that cannot be exceeded by retries, failures, or concurrency. The limit is approximate only by the gap between the list prices in configuration and the provider's invoice; prices must be checked when the provider changes them.
- A language or detail mismatch costs at most one extra call per operation and is visible in the ledger. A valid recipe is never lost because of it.
- Billing and per-user plans remain PLAN-0050. Thinking-high for menu planning remains with PLAN-0029 Phase 2.

## Alternatives considered

- **Keep `deepseek-chat`:** rejected. It is not the evaluated model, and its behavior can change behind the alias.
- **Reject answers that fail soft checks:** rejected. The heuristic can misjudge short or mixed text, and losing a valid recipe is worse than a recorded flag.
- **A daily monetary ceiling:** not chosen. Monthly matches the provider's billing period and the owner's expectation; the daily unit ceilings still bound bursts.
