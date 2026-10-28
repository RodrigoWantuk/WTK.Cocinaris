# PLAN-0030: Implement Recipe Thumbnail Generation

- **Status:** Draft
- **Type:** Implementation
- **Priority:** Medium
- **Owner:** Unassigned AI media / operations agent
- **Created:** 2026-08-05
- **Last updated:** 2026-08-05
- **Branch:** `agent/plan-0030-recipe-thumbnail-generation` (when claimed)
- **Pull request:** Not opened
- **Policy:** [`docs/ai/recipe-thumbnail-generation.md`](../ai/recipe-thumbnail-generation.md)
- **Related amendment:** [`PLAN-0008-amendment-recipe-thumbnail-policy.md`](PLAN-0008-amendment-recipe-thumbnail-policy.md)

## Objective

Implement the complete optional recipe thumbnail vertical slice as specified by the durable thumbnail policy, including cache-first asynchronous generation through the AI Gateway.

## Dependencies

- PLAN-0028 AI Gateway;
- saved recipe revisions with validated `thumbnailVisual`;
- persistent job and object-storage foundations;
- current Gemini / Nano Banana availability and pricing verification before production enablement.

## Included scope

- operation `recipe.thumbnail.generate.v1`;
- validated `thumbnailVisual` input only;
- cache-first behavior (`visualIdentityKey` / `renderCacheKey`);
- asynchronous persistent job;
- idempotent deduplication of identical cache misses;
- S3-compatible permanent storage;
- immutable provenance;
- frontend states: pending, ready, unavailable, failed;
- Nano Banana / Gemini image models as an **evaluated candidate**, not a permanent dependency.

## Excluded scope

- blocking recipe save/open/cook on image availability;
- embedding private pantry/profile context in image prompts;
- cook-now text generation (PLAN-0028);
- sequential planning (PLAN-0029).

## Acceptance criteria (draft)

- [ ] Cache hits charge no image-generation credit.
- [ ] Concurrent identical misses collapse to one job.
- [ ] Recipe validity is independent of thumbnail availability.
- [ ] Provider remains replaceable after pricing/availability refresh.

## Execution state

- **Current checkpoint:** Draft placeholder created during PLAN-0028 roadmap reconciliation.
- **Blockers:** PLAN-0028 gateway + saved revisions; job/storage foundations; provider pricing refresh.
- **Exact next action:** After dependencies land, claim and verify current Nano Banana / Gemini terms before implementation.
