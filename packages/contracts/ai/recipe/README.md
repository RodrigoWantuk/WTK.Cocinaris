# Recipe AI Artifact Contracts (protocol 0.3)

Strict machine contracts for `recipe.suggest_candidates.v1`, `recipe.suggest_plan_deck.v1` and `.v2` (PLAN-0062), and `recipe.expand_selected.v1`.

Governing research plan: [`../../../docs/plans/PLAN-0022-evaluate-and-finalize-recipe-ai-contracts.md`](../../../docs/plans/PLAN-0022-evaluate-and-finalize-recipe-ai-contracts.md)  
Lean amendment: [`../../../docs/plans/PLAN-0022-amendment-2026-08-05-lean-evaluation.md`](../../../docs/plans/PLAN-0022-amendment-2026-08-05-lean-evaluation.md)

## Contents

| Path | Purpose |
|---|---|
| `schemas/` | Closed JSON Schema 2020-12 response contracts |
| `fixtures/responses/positive/` | Schema+semantic pass fixtures |
| `fixtures/responses/negative/` | Expected schema or semantic failures |
| `lib/validate-core.mjs` | Deterministic schema and semantic validators |
| `test.mjs` | Focused contract test entrypoint |
| `validate.mjs` | Validate one response file |
| `check-drift.mjs` | Pin schema/fixture presence and positive validation |

Request evaluation fixtures remain under [`../../../docs/ai/examples/`](../../../docs/ai/examples/).

## Commands

```bash
cd packages/contracts/ai/recipe
npm install
npm test
npm run check:drift
```

Live provider evaluation is separate and must not run in default CI:

```bash
# from repository root, with DEEPSEEK_API_KEY set in the environment
node scripts/ai/recipe-live-eval.mjs
```

## Bounds (protocol 0.3)

- exactly three candidates per suggestion response;
- summary at most 320 characters (the backend fits a longer one; length alone never rejects an answer);
- ≤ 10 inventory uses and ≤ 5 additional ingredients per candidate;
- canonical units: `g`, `ml`, `unit`;
- expansion ≤ 14 stages and ≤ 8 preparations;
- `thumbnailVisual.schemaVersion = "1"` with bounded visible components and no private context, cache hashes, or provider policy.
