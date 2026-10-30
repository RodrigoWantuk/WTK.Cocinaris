# PLAN-0030: Implement Recipe Thumbnail Generation

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** Medium
- **Owner:** Claude Code
- **Created:** 2026-08-05
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0054-everyday-cook-realignment`. It is delivered with PLAN-0054 because it rebuilds the same recipe screens, and stacking a second branch on an unmerged PR would conflict.
- **Pull request:** #58 (draft). It was pushed to the #57 branch after #57 was squash-merged at an earlier head, so it is delivered in #58 together with PLAN-0054 Phases 2b–4.
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

## Owner decision (2026-10-30)

"Para a imagem, vamos usar o FLUX 3 Image Klein do OpenRouter, mas você precisa especificar que queremos a imagem em 16:9, 1024px × 576px, por causa do preço."

- **Provider and model.** OpenRouter, model `black-forest-labs/flux-3-image-klein` (provider Black Forest Labs). This replaces the Gemini candidate in the policy.
- **Size.** The OpenRouter Images API (`POST /api/v1/images`) takes a size tier (`resolution`) and an `aspect_ratio`, not pixels. The tiers are `768`, `1K`, `1.5K`, `2K`, and `4K`; the endpoint listing was read on 2026-10-30. Tier `768` is about 768 × 768 pixels of area, and 768² = 589,824 = 1024 × 576, so `resolution: "768"` with `aspect_ratio: "16:9"` asks for exactly 1024 × 576. It is the cheapest tier: US$ 0.008 per image (1K: US$ 0.009; 2K: US$ 0.014). The job reads the real size from the returned bytes and records any difference (`size_WxH`) in the asset and the ledger. The first live image confirms the size.
- **Billing.** OpenRouter bills images all-or-nothing, so a failed generation costs nothing.

## Delivered (2026-10-30)

- **Operation.** `recipe.thumbnail.generate.v1` is registered in the AI Gateway: style and prompt `recipe-thumbnail-style-v2` (natural photography; `v1` before 2026-10-30), one image per call, no text tokens.
- **Provider.**
  - The adapter is `OpenRouterImageProvider` (`Cocinaris.Modules.Ai/Images`). It makes one call with no retries, reads a bounded response (at most 12 MB), and maps failures to transient, refused, or invalid.
  - It is enabled with `Ai__Images__Enabled=true` and `Ai__Providers__OpenRouter__ApiKey`. The key is server-side only.
  - The Testing environment uses `FakeImageProvider` (a blank 1024 × 576 PNG).
- **Descriptor and cache keys.** `RecipeThumbnailDescriptor` validates the stored `thumbnailVisual` against the protocol bounds and canonicalizes it: trimmed, lower case, lists sorted and deduplicated.
  - `visualIdentityKey` is the SHA-256 of the canonical descriptor.
  - `renderKey` is the SHA-256 of the visual identity plus the style version, model, tier, ratio, and size.
  - Recipes written by hand (`{}`) and invalid descriptors have no image.
  - The prompt is the policy's canonical template.
- **Cache-first queue.**
  - Reading a recipe (the list or the detail) resolves each image by render key and queues a missing one as `Pending` (`INSERT … ON CONFLICT DO NOTHING`, so identical misses collapse).
  - Identical appearance shares one image across recipes. The descriptor holds no private context.
  - Nothing is queued while generation is disabled.
  - Recipes and every recipe action never wait for an image.
- **Job.** `RecipeThumbnailWorker` processes one image at a time. Each image:
  1. **Claim.** `FOR UPDATE SKIP LOCKED`, with a lease, so a crashed step is retried.
  2. **Quotas.** At most 10 images per owner and 100 for the deployment per UTC day (`Ai:Images:UserDailyImages`, `GlobalDailyImages`). Over a quota, the job is deferred to the next day, not failed.
  3. **Budget.** The image price is reserved in the AI usage ledger, so images count toward the monthly AI ceiling (US$ 5) and the per-owner concurrency limit. A rejected reservation defers the job by one hour.
  4. **Call.** One provider call.
  5. **Validation.** The file signature (PNG, JPEG, or WebP) and dimensions are read from the bytes, the reported media type is ignored, the image is at most 4 MB, and it is 16:9 within 2%.
  6. **Store and settle.** The file is stored, the asset is marked `Ready`, and the reservation is settled at the cost OpenRouter reported.

  On failure:
  - A transient failure is retried once 10 minutes later and then becomes `Failed`.
  - A refused or invalid result becomes `Failed`, and its billed cost is still recorded.
  - `Failed` keeps the placeholder and is not retried automatically.
- **Storage.** Images are files named `<renderKey>.<ext>` in the service's state directory (`/var/lib/wtk-cocinaris/media/thumbnails`), or in `Ai:Images:StoragePath`. They are written atomically through `IRecipeThumbnailFileStore`, and an S3-compatible store can replace it later.
  - The `recipes.recipe_thumbnails` table (migration `AddRecipeThumbnails`) holds the state, keys, canonical descriptor, size, media type, and the owner charged.
  - This is a change from the policy's S3-compatible path, chosen because the deployment is one host. The owner can still revisit it.
- **Serving.** `GET /api/v1/recipes/{recipeId}/thumbnail[?revision=n]&v=<key prefix>` checks ownership first (another owner gets 404), sends `nosniff`, and allows `private, max-age=30 days, immutable`. It is the only API response exempt from the `no-store` rule, through `Program.PrivateCacheableItem`. Recipe list and detail responses carry `thumbnail: { state: ready | pending | none, url }`.
- **Frontend.**
  - The recipe list shows a 16:9 thumbnail per row, and the detail page shows the image with the caption "Imagem ilustrativa gerada por IA".
  - The placeholder appears while pending or when there is none. A broken image falls back to it.
  - The client accepts only this API's thumbnail path as an image URL.

## Not done

- **Hand-written recipes.** They have no descriptor, so they get no image. Generating a descriptor for them with the text model is a later step.
- **Evaluation and retention.** There is no human-reviewed image evaluation set yet, no regeneration action, and no cleanup of images no recipe uses any more.
- **Ops report.** `scripts/ops/ai-usage-report.sh` lists the thumbnail operation like any other, with no image-specific view.

## Acceptance criteria (draft)

- [x] Cache hits charge no image-generation credit (resolution is a lookup; only the job reserves).
- [x] Concurrent identical misses collapse to one job (primary key on the render key, `ON CONFLICT DO NOTHING`, `SKIP LOCKED` claims).
- [x] Recipe validity is independent of thumbnail availability.
- [x] Provider remains replaceable (`IImageGenerationProvider`, model and tier in configuration).

## Execution state

- **Current checkpoint:** implemented and tested, deployed disabled until the owner adds the OpenRouter key. The prompt is `recipe-thumbnail-style-v2`, which asks for a natural photograph; it is committed but not yet deployed.
- **Validation:**
  - **Unit tests:** canonical keys, prompt, invalid descriptors, and image inspection (6).
  - **Integration test (PostgreSQL):** an image is queued on read, two recipes with the same look share one generation, the ledger is settled, and the image is served to the owner as `image/png` with a private cache and refused to another owner.
  - **Backend suites:** unit and architecture tests pass.
- **Exact next action:** the owner adds `Ai__Providers__OpenRouter__ApiKey` and `Ai__Images__Enabled=true` to `/etc/wtk-cocinaris/api.env` and restarts the service. The first generated image then confirms the 1024 × 576 size, and the ledger cost in `scripts/ops/ai-usage-report.sh` confirms US$ 0.008.
- **Blockers:** the OpenRouter key (owner).

## Progress log

### 2026-10-30 — Claude Code

- Implemented recipe thumbnails with OpenRouter FLUX 3 Image Klein at 16:9, tier 768 (1024 × 576), following the owner's decision. Delivered the cache, queue, job, budget, storage, serving, and frontend.
- Changed the prompt to `recipe-thumbnail-style-v2` at the owner's request. It now asks for a natural, photorealistic photograph of home-cooked food with soft daylight, true colors, and real textures, and it rules out illustrated, 3D-rendered, over-saturated, or artificially perfect looks. The style version is part of the render key, so no `v1` image is reused. Validation: `RecipeThumbnailTests` (unit) pass. Next: deploy with the branch, then the owner adds the OpenRouter key.
- Delivery moved to draft PR #58, because PR #57 was squash-merged before these commits were pushed. The content is unchanged.

### 2026-10-30 — Claude Code (served size)

- The owner reported that the recipe image was slow to load. Three causes were found and fixed:
  - **File size.** The provider's 230–330 KB JPEG was served everywhere, including list cards. Images are now served as WebP: 1024 × 576 at quality 80 (46–75 KB) on the recipe page and 480 × 270 at quality 75 (13–19 KB) in lists (`smallUrl`, `?size=small`), encoded with SkiaSharp 4.153.1 (MIT). The original is kept, and older images are encoded on first read.
  - **Lazy loading.** The recipe page lazy-loaded its main image; it now loads eagerly with high fetch priority.
  - **Waiting for generation.** A new image only appeared after a manual reload, up to 10 s of job polling plus generation later. The job now polls every 3 s, and the page and list refresh quietly while an image is pending.
- URLs carry `v=<key>.w1`; the frontend's strict URL check accepts the suffix and the small URL.
- Validation:
  - `RecipeThumbnailApiTests` (integration) pass: WebP full and small sizes served, the original kept, and lazy re-encoding after deleting the WebP files.
  - Frontend: recipe feature and live repository tests pass (35), including a page that shows the image after it is generated without a reload.
  - Typecheck, lint, format, OpenAPI export and API client drift, `dotnet format`, and the vulnerability check are clean.
- Next: the owner merges this PR and deploys `main`. Existing images are converted on their first read.

### 2026-10-30 — Claude Code (deploy)

- The first deploy of `main` with the WebP change failed before touching the service: `ENOSPC` while installing the frontend in the build worktree under `/tmp`. On the owner host `/tmp` is a 2 GB tmpfs, while the root disk has 25 GB free. The deploy script now builds in `/var/tmp` (`COCINARIS_DEPLOY_BUILD_ROOT`), and the runbook says so.
- Next: deploy `main` after this fix is merged, or deploy this branch, which contains `main` plus the fix.

