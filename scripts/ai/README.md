# Recipe AI live evaluation scripts

These scripts call external providers and are **not** part of the default repository test suite.

## PLAN-0022 lean campaign

```bash
export DEEPSEEK_API_KEY='...'          # ignored local secret; never commit
export PLAN0022_LIVE_EVAL=1
export PLAN0022_COST_CEILING_USD=0.05  # optional; default 0.05
node scripts/ai/recipe-live-eval.mjs
```

Rules:

- synthetic fixtures only;
- never print the API key;
- stop when the cost ceiling is reached;
- at most one repair retry for empty/truncated/invalid JSON;
- evidence is written under `docs/evidence/plan-0022/`;
- summaries report `scenariosEvaluated` and `providerCallsAttempted` separately (repairs count as provider calls);
- non-streaming runs set `ttftMs` to null (`unavailable_without_streaming`) and distinguish response-header timing from total body-inclusive latency.

## PLAN-0028 production gateway smoke

```bash
export DEEPSEEK_API_KEY='...'          # ignored local secret; never commit
export PLAN0028_LIVE_SMOKE=1
export PLAN0028_COST_CEILING_USD=0.05  # optional; default 0.05
export DEEPSEEK_MODEL=deepseek-chat    # optional
dotnet run --project scripts/ai/RecipeGatewayLiveSmoke/RecipeGatewayLiveSmoke.csproj -c Release
```

Rules:

- synthetic fixtures only;
- exercises production `RecipeAiRequestEnvelopes`, `DeepSeekAiProvider`, and `RecipeProtocolValidator`;
- full protocol `0.3` response schemas embedded in the provider payload;
- at most one repair per operation with a distinct repair payload;
- evidence under `docs/evidence/plan-0028/` (summary only; no secrets or private data).
