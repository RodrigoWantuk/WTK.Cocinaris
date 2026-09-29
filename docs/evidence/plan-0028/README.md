# PLAN-0028 recipe gateway live smoke

- **Plan:** PLAN-0028
- **Status:** Passed (synthetic suggest + expand)
- **Provider/model:** deepseek / `deepseek-chat`
- **Provider calls:** 3 (suggest attempt + suggest repair + expand)
- **Bounded estimated cost:** ~US$ 0.00243 (ceiling US$ 0.05)
- **Evidence:** [`live-smoke-summary.json`](live-smoke-summary.json)

## Command

```bash
export DEEPSEEK_API_KEY='...'   # never commit
export PLAN0028_LIVE_SMOKE=1
export PLAN0028_COST_CEILING_USD=0.05
export DEEPSEEK_MODEL=deepseek-chat
dotnet run --project scripts/ai/RecipeGatewayLiveSmoke/RecipeGatewayLiveSmoke.csproj -c Release
```

Outside CI only. Uses production envelope builders (full protocol `0.3` response schemas), `DeepSeekAiProvider`, and `RecipeProtocolValidator`. Maximum one repair per operation.
