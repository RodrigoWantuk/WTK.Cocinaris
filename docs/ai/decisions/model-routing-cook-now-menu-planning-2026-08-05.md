# Decision: Recipe AI Model Routing (cook_now and menu_planning)

- **Plan:** PLAN-0022
- **Date:** 2026-08-05
- **Status:** Accepted provisional policy. Implemented in the gateway by PLAN-0040 / ADR-0009: `deepseek-v4-flash` with `thinking` disabled for cook-now, one fallback model after a provider error. Latency is now recorded per call; statistics accumulate in `scripts/ops/ai-usage-report.sh`.

## Models

| Role | Provider | Model | Mode |
|---|---|---|---|
| Thinking baseline | DeepSeek | `deepseek-v4-flash` | thinking enabled, `reasoning_effort: high` |
| Fallback | DeepSeek | `deepseek-v4-flash` | thinking disabled |

This does **not** select a permanent exclusive provider for the product. It finalizes the contract evaluation baseline and default routing hypothesis for the first AI Gateway implementation.

## Synchronous `cook_now`

1. Prefer fallback (non-thinking) for synchronous user-visible `cook_now` candidate generation after compaction.
2. Allow at most **one** automatic repair when output is empty, truncated, or schema-invalid.
3. Thinking-high may be used for offline pre-generation or explicit progressive UI, not as the default blocking path.
4. Observed single-run latencies from PLAN-0017 and the PLAN-0022 lean campaign are recorded as observations only. **p50/p95 statistical characterization is deferred.**

## `menu_planning`

1. Default to thinking-high behind progressive UI when quality requires multi-day / projected-surplus reasoning.
2. Fall back to non-thinking on timeout, empty/truncated JSON, or schema failure after one repair.
3. Same closed schemas and semantic validators as `cook_now`.

## Repair and fallback

```text
primary call
→ if empty/truncated/schema-invalid: one repair with stricter JSON reminder
→ if still invalid: operation fails safely without mutating authoritative state
→ optional policy: retry once with fallback model for cook_now
```

Reasoning content is never persisted or exposed.
