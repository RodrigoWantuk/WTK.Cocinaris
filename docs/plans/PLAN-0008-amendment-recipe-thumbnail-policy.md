# PLAN-0008 Amendment: Governed Recipe Thumbnail Generation

- **Status:** Ready
- **Type:** Operations / AI governance amendment
- **Priority:** High
- **Owner:** Unassigned launch, growth, and AI-economics agent
- **Parent plan:** [`PLAN-0008`](PLAN-0008-define-lean-launch-ai-economics.md)
- **Created:** 2026-08-02
- **Last updated:** 2026-08-02
- **Branch:** `agent/plan-0008-recipe-thumbnail-policy`
- **Pull request:** PR #29
- **Related plan:** [`PLAN-0017`](PLAN-0017-define-ai-recipe-artifact-protocol.md), merged through PR #31
- **Related policy:** [`docs/ai/recipe-thumbnail-generation.md`](../ai/recipe-thumbnail-generation.md)
- **Related ADR:** ADR-0005

## Objective

Extend PLAN-0008 with a complete, operation-specific policy for optional AI-generated recipe thumbnails while preserving the original launch, acquisition, text-model economics, and monetization plan.

The amendment must make thumbnail generation:

- cache-first;
- asynchronous;
- optional to every recipe workflow;
- independently budgeted;
- provider-replaceable;
- privacy-minimized;
- testable through deterministic contracts and an image evaluation set;
- compatible with the `thumbnailVisual` contract already defined by PLAN-0017.

## Reconciliation decision

PR #29 originally attempted to rewrite large portions of PLAN-0008 and referred to the visual descriptor as future contract work. That approach is superseded by this focused amendment.

The corrected delivery:

1. keeps the merged PLAN-0008 definition intact;
2. records thumbnail-specific decisions in this amendment;
3. places the durable operation policy in `docs/ai/recipe-thumbnail-generation.md`;
4. consumes the `thumbnailVisual` contract already merged through PLAN-0017 / PR #31;
5. updates only the central registry state needed to continue PLAN-0008 execution;
6. preserves the pre-reconciliation branch state at `archive/pr29-pre-plan0017-reconcile`.

## Decisions

1. Register specialized operation `recipe.thumbnail.generate.v1` behind the backend AI Gateway.
2. Use PLAN-0017 `thumbnailVisual` as the only recipe-domain input to visual identity and prompt construction.
3. Keep recipe title and free-form summary out of the durable cache identity.
4. Validate and canonicalize `thumbnailVisual` deterministically before any provider use.
5. Compute separate visual identity and render-policy hashes.
6. Check the render cache before budget reservation.
7. Charge no image-generation credit on a cache hit.
8. Collapse concurrent identical cache misses into one persistent idempotent job.
9. Generate automatically only for selected, expanded, saved, imported, or otherwise durable recipes.
10. Allow transient candidates to consume cache hits, but not to trigger paid generation by default.
11. Keep generation asynchronous and fail-soft.
12. Store accepted images in permanent S3-compatible object storage, separate from temporary import media.
13. Preserve immutable provenance for generated assets.
14. Use independent image-operation quotas, budgets, retries, and kill switches.
15. Never make image availability part of recipe validity.
16. Never communicate allergy safety, doneness safety, nutrition, medical suitability, freshness, shelf life, authenticity, or guaranteed outcome through the image.
17. Never include private pantry, profile, restriction, note, source-page, identity, or household context in the provider prompt.
18. Require explicit evaluation thresholds before production enablement.

## Initial provider hypothesis

The initial candidate is Nano Banana 2 Lite:

```text
gemini-3.1-flash-lite-image
```

Initial evaluation configuration:

| Parameter | Value |
|---|---|
| Output | One image |
| Aspect ratio | `16:9` |
| Size | `1K` |
| Temperature | `1.0` |
| Top P | `0.95` |
| Maximum output tokens | `1000` where supported |
| Thinking | `minimal` |
| Retry | At most one eligible automatic retry |
| Execution | Persistent asynchronous job |

The exact SDK mapping, price, availability, privacy terms, and provider behavior must be refreshed before implementation. The model is an evaluated candidate rather than a permanent architecture dependency.

## Scope

### Included

- operation taxonomy and ownership;
- integration with PLAN-0017 `thumbnailVisual`;
- provider/model hypothesis;
- prompt/style profile;
- visual identity and render cache keys;
- cache reuse and invalidation;
- asynchronous generation eligibility;
- persistent jobs and idempotency requirements;
- image validation and moderation requirements;
- storage, provenance, retention, cleanup, export, and deletion boundaries;
- privacy, rights, honesty, and food-safety boundaries;
- image-specific budgets, credits, quotas, retries, and kill switches;
- observability and cost evidence;
- degraded behavior;
- evaluation dataset and release gates;
- implementation-plan handoff.

### Excluded

- production provider integration;
- live provider calls or benchmarks;
- object-storage implementation;
- frontend implementation;
- database schema implementation;
- budget-ledger implementation;
- moderation-service implementation;
- modifying the merged PLAN-0017 recipe protocol;
- generating images for every compact candidate;
- using private imported photographs for image-to-image generation;
- selecting a permanent provider without evaluation evidence;
- current production-pricing claims.

## Dependency on PLAN-0017

PLAN-0017 already defines the persisted expanded-recipe `thumbnailVisual` field with:

- schema version;
- appearance description;
- visible components;
- dish format;
- plating;
- sauce appearance;
- material-state and texture cues;
- garnish;
- dominant colors;
- excluded elements.

PLAN-0008 does not redefine that recipe contract. It requires implementation plans to enforce its strict schema, validate consistency against the normalized recipe, canonicalize it, and pass only its privacy-safe visual content into `recipe.thumbnail.generate.v1`.

A material recipe edit that changes visible appearance must create a new visual identity. A wording-only edit must not.

## Cache and cost invariants

The implementation must satisfy:

```text
visualIdentityKey = SHA-256(canonicalJson(schemaVersion + normalizedThumbnailVisual))
```

```text
renderCacheKey = SHA-256(canonicalJson(
  visualIdentityKey
  + styleProfileVersion
  + promptTemplateVersion
  + modelPolicyVersion
  + aspectRatio
  + imageSize
  + outputMimeType
))
```

Required invariants:

- cache lookup precedes provider budget reservation;
- a cache hit causes no provider call;
- one render key has at most one active generation job;
- failed or invalid output cannot poison the successful cache;
- unrelated images are never substituted;
- cache identity excludes localized title and free-form summary;
- private context cannot become part of a reusable cache key;
- style, prompt, model policy, size, aspect ratio, MIME, descriptor version, or material visual changes produce a new render key;
- valid existing assets may remain after provider changes until an explicit regeneration policy invalidates them.

## Initial visual direction

The initial style is realistic food photography showing one served plate on a modern wooden table, with a light or predominantly white softly blurred background and little margin outside the plate.

The prompt must exclude:

- cutlery and utensils;
- napkins;
- people and hands;
- visible text and watermarks;
- labels, logos, and packaging;
- invented ingredients, sides, or garnish;
- unsupported safety, nutrition, freshness, authenticity, or quality claims.

The stakeholder-provided Portuguese prompt is retained in the dedicated policy as the first benchmark fixture.

## Budget and entitlement requirements

The image operation requires:

- global daily and monthly limits;
- environment limits;
- user and plan limits;
- operation, provider, and model limits;
- per-recipe and per-visual-identity generation limits;
- concurrency limits;
- output-byte and storage-growth limits;
- one automatic-retry ceiling;
- independent image-generation kill switch;
- continued serving of accepted cached images when new generation is disabled.

Worst-case cost is reserved only after a confirmed cache miss. Actual cost is settled only after valid persistence. Cache hits do not consume an image-generation credit.

## Validation gates

Before production enablement, validate:

1. strict `thumbnailVisual` schema enforcement;
2. canonicalization and stable hashes;
3. cache lookup before reservation and provider invocation;
4. no provider call or generation charge on a cache hit;
5. one job for concurrent identical misses;
6. bounded negative caching and retry behavior;
7. no successful cache record after provider, validation, moderation, or storage failure;
8. MIME signature, dimensions, output size, and decompression limits;
9. semantic fidelity to visible components and material state;
10. absence of invented dominant ingredients;
11. absence of cutlery, napkins, people, text, logos, packaging, and watermarks;
12. crop quality at actual card sizes;
13. consistency across `pt-BR`, `en`, and `es` source recipes;
14. cases that must and must not reuse a visual identity;
15. p50 and p95 latency;
16. cost per valid persisted asset;
17. cache hit ratio and avoided provider cost;
18. storage and delivery cost;
19. timeout, cancellation, quota, kill-switch, and provider-unavailable behavior;
20. deterministic placeholder behavior;
21. export, deletion, retention, and orphan cleanup;
22. privacy-safe logs, traces, metrics, and asset metadata;
23. human-reviewed model and prompt quality thresholds.

## Acceptance criteria

- [x] The image operation and provider-independent ownership boundary are defined.
- [x] PLAN-0017 `thumbnailVisual` is recognized as the existing recipe contract.
- [x] Visual and render cache identities are defined.
- [x] Cache-first budget behavior is defined.
- [x] Asynchronous fail-soft behavior is defined.
- [x] Initial provider and prompt hypotheses are recorded.
- [x] Privacy, safety, rights, storage, provenance, and deletion boundaries are defined.
- [x] Evaluation and cost evidence requirements are defined.
- [x] No production implementation or unsupported benchmark claim is included.
- [x] Documentation accepted and merged through PR #29.
- [ ] PLAN-0008 Phase 1 refreshes provider, price, storage, privacy, and threshold evidence.
- [x] Implementation plan placeholder created (PLAN-0030 Draft); coding remains pending.

## Execution state

- **Current checkpoint:** Documentation accepted and merged through PR #29. Implementation is pending under PLAN-0030. Initial provider candidate remains Nano Banana / Gemini image policy, subject to a current provider and pricing refresh before implementation.
- **Last completed step:** Thumbnail policy documentation merged; PLAN-0030 draft placeholder created during PLAN-0028 roadmap reconciliation.
- **Exact next action:** After PLAN-0028 lands, claim PLAN-0030 and refresh Gemini/Nano Banana availability and pricing before coding.
- **Blockers:** Production work remains blocked on PLAN-0028 AI Gateway, saved recipe revisions, persistent job/object-storage foundations, and current provider evidence.
- **Validation performed:** Documentation consistency review against PLAN-0008, PLAN-0017, ADR-0005, and the current repository registry. No live provider or storage action was performed.

## Progress log

### 2026-08-02 — PR #29 reconciliation

- Preserved the former PR #29 head in `archive/pr29-pre-plan0017-reconcile`.
- Reset `agent/plan-0008-recipe-thumbnail-policy` to the current `main` containing PR #31.
- Removed the broad rewrite of the original PLAN-0008 definition.
- Added this focused amendment.
- Added the durable recipe-thumbnail generation and cache policy.
- Reconciled the registry with PLAN-0017 delivery and PLAN-0008 execution status.
- Kept the change documentation-only and implementation-neutral.

## Implementation handoff

After merge, create separate plans for:

1. strict `thumbnailVisual` backend validation and canonicalization;
2. AI Gateway Gemini adapter and provider policy;
3. image budgets, quotas, and credit ledger integration;
4. persistent generation jobs and idempotent deduplication;
5. cache indexes and invalidation;
6. S3-compatible object storage and lifecycle;
7. image validation, moderation, quarantine, and support tools;
8. recipe-to-asset associations and generic reuse;
9. frontend pending, cached, placeholder, unavailable, and failed states;
10. evaluation execution, cost evidence, and independent acceptance.

No implementation plan may treat the candidate model, exact SDK shape, pricing, or availability as permanently fixed.
