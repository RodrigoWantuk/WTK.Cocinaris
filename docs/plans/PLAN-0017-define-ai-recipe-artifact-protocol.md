# PLAN-0017: Define AI Recipe Artifact Protocol and Model Evaluation Pack

- **Status:** Completed (documentation delivered via PR #31; residual research transferred to PLAN-0022 — see [completion handoff](PLAN-0017-completion-and-research-handoff.md))
- **Type:** Documentation
- **Priority:** High
- **Owner:** Stakeholder + AI architecture collaboration
- **Created:** 2026-08-01
- **Last updated:** 2026-08-02
- **Branch:** `docs/plan-0017-ai-recipe-artifacts`
- **Delivery:** Replacement draft PR for PR #23
- **Related ADRs:** ADR-0005
- **Related plans:** PLAN-0008 thumbnail economics/policy; PLAN-0013 kitchen orchestration

## Objective

Define the smallest useful, economical and reliable AI surface for recipe discovery, immediate cooking and multi-day menu planning.

AI generates versioned JSON artifacts reused by selection, planning, shopping projection, preparation schedules, reminders, guided cooking, reconciliation and thumbnail lookup. Deterministic code remains authoritative wherever rules or calculations suffice.

## Numbering decision

This work was originally opened as PLAN-0016 in draft PR #23. PLAN-0016 was subsequently assigned to the production session/inventory frontend implementation. This documentation plan is therefore renumbered to **PLAN-0017**.

The original PR and branch remain historical references. The merge candidate is the clean PLAN-0017 branch rebuilt from current `main`.

## Product decisions

1. Generate exactly three compact candidates in one call.
2. Expand only the selected candidate.
3. Support explicit `cook_now` and `menu_planning` modes.
4. Include local datetime, timezone, stable meal type, target time and lead window.
5. Provide deliberate inventory-first, flexible/planned-purchase-reuse and exploratory strategies.
6. Keep physical stock, planned purchases and prepared components as separate availability sources.
7. Use backend-computed `availableForPlanning` as authoritative.
8. Reserve accepted demand without prematurely changing physical stock.
9. Aggregate shopping demand globally by canonical ingredient identity.
10. Resolve packages deterministically; only reliable sources create reusable projected surplus.
11. Keep all balances, sufficiency, reservations, packages, percentages and shopping quantities outside the model.
12. Make ingredient state and advance-preparation requirements explicit.
13. Return relative preparation dependencies; backend calculates reminders.
14. Reject `cook_now` candidates whose blocking preparation exceeds the available window.
15. Require recognizable dishes/conventional techniques and material diversity.
16. Use application-owned generation sessions and bounded semantic rejection memory.
17. Treat all supplied text as untrusted data.
18. Disable ordinary recipe-generation web search.
19. Evaluate DeepSeek V4 Flash thinking-high as a provisional quality baseline with a strict latency gate.
20. Add a versioned, indexable `thumbnailVisual` descriptor to every persisted expanded recipe.
21. Generate the visual descriptor only during selected-recipe expansion, not compact candidate generation.
22. Let backend validation/canonicalization own the visual identity used by thumbnail caching.
23. Do not use recipe title or free-form summary as the durable image cache key.
24. Keep provider, prompt, style, image model, rendering, storage, quota, moderation and fallback policy under PLAN-0008 and `recipe.thumbnail.generate.v1`.
25. Never block saving, planning or cooking because an image is unavailable.

## Included

- `recipe.suggest_candidates.v1` and `recipe.expand_selected.v1` contracts.
- `cook_now` and `menu_planning` semantics.
- Three-candidate strategy and diversity rules.
- Generation-session and rejection-memory boundaries.
- Physical/planned/prepared availability semantics.
- Reservation, shopping aggregation and package-projection boundaries.
- Ingredient state, advance preparation and reminder ownership.
- Hardened prompt and synthetic evaluation fixtures.
- Text-model latency/token/cost evaluation.
- Expanded-recipe `thumbnailVisual` contract.
- Contract-level integration with PLAN-0008 visual identity/cache policy.

## Excluded

- Production provider integration.
- Inventory, menu or shopping implementation.
- Final JSON Schema/strict-tool implementation.
- Canonical ingredient identity implementation.
- Store/catalog integrations.
- Nutrition guarantees or authoritative food-safety decisions.
- Image-provider integration, object storage, persistent image jobs, quotas, moderation, image validation or frontend image states.
- Automatic execution or merging.

## Required artifacts

- `docs/ai/recipe-artifact-protocol.md`
- `docs/ai/prompts/recipe-suggest-candidates.system.txt`
- `docs/ai/examples/README.md`
- fixtures `01`–`10`, including `thumbnailVisual` expansion contracts;
- `docs/ai/provider-evaluations/deepseek-v4-flash-2026-08-01.md`.

## Thumbnail visual contract

Conceptual expanded-recipe field:

```json
{
  "thumbnailVisual": {
    "schemaVersion": "1",
    "appearanceDescription": "Cooked chicken breast in tomato sauce, served with white rice and sautéed carrot slices on one plate.",
    "visibleComponents": [
      "cooked chicken breast",
      "tomato sauce",
      "white rice",
      "sautéed carrot slices"
    ],
    "dishFormat": "plated_main_with_sides",
    "plating": "single modern dinner plate",
    "sauceAppearance": "smooth red tomato sauce coating the chicken",
    "textureAndDoneness": [
      "fully cooked chicken",
      "loose white rice",
      "lightly browned carrots"
    ],
    "garnish": [],
    "dominantColors": ["red", "white", "orange"],
    "excludedElements": ["cutlery", "napkin", "text", "logo", "people"]
  }
}
```

Normative rules:

- describe only visible properties of the finished dish;
- include only components present in the normalized recipe and expected to be visible;
- preserve material state such as cooked, roasted, sautéed, blended, sliced, whole or melted;
- do not invent garnish, sides, brands, packaging, tableware or restaurant styling;
- exclude identity, pantry quantities, restrictions, private notes, source text, URLs and household history;
- do not make safety, nutrition, freshness, authenticity or guaranteed-result claims;
- keep fields bounded and canonicalizable;
- backend validates consistency, normalizes the descriptor and computes semantic visual identity;
- prompt/model/style/render policy remains outside the recipe artifact;
- image failure never invalidates the recipe.

## Token and experience requirements

Candidate discovery must:

- return three options in one request;
- omit detailed steps;
- cap summaries at 18 words;
- replay no prior reasoning or full response history;
- send at most nine prior semantic summaries;
- reuse stable cache-friendly prefixes;
- persist and reuse expansions.

`thumbnailVisual` is produced only during expansion and must remain concise.

Synchronous `cook_now` target:

```text
p50 <= 15 seconds
p95 <= 25 seconds
empty/truncated JSON = 0
maximum automatic repair attempts = 1
```

If thinking-high cannot meet the gate after compaction, use a validated non-thinking fallback for synchronous `cook_now`. Menu planning may retain higher reasoning behind progressive UI when measured quality justifies it.

## Validation requirements

Before completion:

1. Parse every fixture as JSON.
2. Run at least three repetitions of principal scenarios.
3. Capture model/backend fingerprint, prompt version, thinking, tokens, cache usage, TTFT, total latency, raw output, schema/semantic validity, diversity, plausibility, repair count and cost.
4. Validate exact IDs, names, units, states and sources.
5. Validate hard constraints, equipment/capabilities and lead-time feasibility.
6. Validate assumptions and diversity fingerprints.
7. Validate projected-purchase reuse without model-authored arithmetic.
8. Validate injection resistance.
9. Validate `thumbnailVisual` limits, visible-component fidelity, material state and absence of private context.
10. Validate deterministic visual canonicalization and cache identity separation.
11. Validate visually equivalent recipes may share identity while materially different recipes do not.
12. Validate recipe expansion succeeds independently from thumbnail generation.
13. Obtain stakeholder approval before merge.

## Execution state

- **Current checkpoint:** Protocol `0.3-draft`, ten fixtures, hardened candidate prompt, provider evaluation and the expanded-recipe visual descriptor are consolidated under PLAN-0017.
- **Last completed step:** Resolved the PLAN-0016 collision, rebuilt the branch from current `main`, migrated expansion fixtures to `thumbnailVisual`, and connected the recipe contract to PLAN-0008 thumbnail caching.
- **Exact next action:** Run repeated DeepSeek V4 Flash benchmarks for fixtures 08–10 and expansion fixtures 06–07, recording latency/token/semantic and visual-descriptor results.
- **Blockers:** Repeated benchmark evidence, final strict-output choice, canonical ingredient identity, package-confidence thresholds and final PLAN-0008 thumbnail-contract acceptance.
- **Validation performed:** Documentation and JSON fixture consistency review only; no production provider or image integration.
- **Working tree state:** Replacement draft delivery for PR #23; no automatic merge.

## Progress log

### 2026-10-28T15:00:00Z — Claude Code (PLAN-0032 drift reconciliation)

- Header status reconciled with the registry and the completion handoff. Historical entries below are unchanged.

### 2026-08-02 — Renumbering and thumbnail integration

- Renumbered the recipe protocol from PLAN-0016 to PLAN-0017.
- Preserved the pre-rebase state in `archive/pr23-plan0017-pre-rebase`.
- Rebuilt `docs/plan-0017-ai-recipe-artifacts` directly from current `main`.
- Added `thumbnailVisual` to the protocol and expansion fixtures.
- Assigned visual validation/canonicalization to backend code.
- Kept paid image generation, provider configuration, storage and lifecycle under PLAN-0008.

### 2026-08-01 — Protocol 0.3 consolidation

- Defined immediate cooking and sequential menu planning.
- Fixed candidate count at three.
- Added local meal context, projected-purchase reuse, reservations and reminders.
- Added bounded generation-session memory and a hardened prompt.
- Selected DeepSeek V4 Flash thinking-high as a provisional quality baseline subject to latency gates.
