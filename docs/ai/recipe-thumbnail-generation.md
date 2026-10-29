# Recipe Thumbnail Generation and Cache Policy

- **Status:** Implemented in PLAN-0030 (2026-10-30), with the owner's provider decision below
- **Last updated:** 2026-10-30
- **Parent plan:** [`PLAN-0008`](../plans/PLAN-0008-define-lean-launch-ai-economics.md)
- **Plan amendment:** [`PLAN-0008 recipe-thumbnail amendment`](../plans/PLAN-0008-amendment-recipe-thumbnail-policy.md)
- **Recipe contract owner:** [`PLAN-0017`](../plans/PLAN-0017-define-ai-recipe-artifact-protocol.md), merged through PR #31
- **Related ADR:** ADR-0005 — AI Gateway and Usage Governance
- **Operation:** `recipe.thumbnail.generate.v1`

## Purpose

Cocinaris may generate a realistic landscape thumbnail for a persisted recipe. The thumbnail improves recognition and browsing, but it is an optional presentation asset rather than authoritative recipe content.

Thumbnail generation must never become a prerequisite for:

- selecting or expanding a recipe;
- saving a recipe;
- adding it to a menu;
- generating shopping or preparation data;
- opening guided cooking;
- completing or reconciling a cooking execution.

The operation is governed by four primary constraints:

1. all provider access remains behind the backend-owned AI Gateway;
2. cache lookup occurs before budget reservation and before every billable provider request;
3. visually equivalent, non-private recipes may reuse an eligible generated asset;
4. provider, validation, storage, quota, or moderation failure degrades to a deterministic placeholder without failing the recipe workflow.

## Ownership boundary

PLAN-0017 already defines the versioned expanded-recipe `thumbnailVisual` contract. This policy consumes that validated descriptor and owns the image-generation side of the boundary.

### PLAN-0017 owns

- producing `thumbnailVisual` during selected-recipe expansion;
- visible-component and material-state semantics;
- excluding private or non-visual context;
- contract-level field limits and schema versioning;
- recipe validity independently from image availability.

### PLAN-0008 and this policy own

- AI Gateway operation registration;
- provider/model policy;
- prompt and style policy;
- cache canonicalization and render keys;
- asynchronous jobs and idempotency;
- image validation, moderation, storage, provenance, retention, and deletion;
- budgets, credits, quotas, retries, kill switches, and cost evidence;
- degraded and placeholder behavior.

Provider names, SDK types, prompt templates, render keys, storage metadata, and model-policy fields must not leak into the recipe artifact or frontend domain contracts.

## Recipe visual descriptor

The input is the persisted expanded-recipe field defined by PLAN-0017:

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

### Descriptor acceptance rules

Before cache lookup, the backend must validate and normalize the descriptor.

The descriptor must:

- describe only visible properties of the finished dish;
- include only components present in the normalized recipe and expected to remain visible;
- preserve material state such as cooked, roasted, sautéed, blended, sliced, whole, or melted;
- use bounded, canonicalizable fields;
- keep `appearanceDescription` concise;
- represent garnish only when the recipe actually includes it;
- distinguish material visual changes from wording-only edits.

The descriptor must not include:

- user identity or account identifiers;
- pantry quantities or inventory history;
- restrictions, private notes, household history, or preference narratives;
- source URLs or imported page text;
- provider instructions or prompt injection content;
- safety, nutrition, freshness, authenticity, medical, or guaranteed-result claims;
- invented garnish, side dishes, brands, packaging, tableware, or restaurant styling.

A descriptor rejected by deterministic validation cannot be sent to the image provider. The recipe remains valid and uses the placeholder path.

## Provider decision (owner, 2026-10-30)

The owner chose OpenRouter's **FLUX 3 Image Klein** (`black-forest-labs/flux-3-image-klein`) at 16:9, 1024 × 576, "por causa do preço". It replaces the Gemini candidate below, which is kept for history.

| Parameter | Value | Rule |
|---|---|---|
| Endpoint | `POST https://openrouter.ai/api/v1/images` | Through `OpenRouterImageProvider` only |
| Model | `black-forest-labs/flux-3-image-klein` | Configuration (`Ai:Images:Model`) |
| Size tier | `768` | About 768 × 768 pixels of area: 768² = 1024 × 576 |
| Aspect ratio | `16:9` | Together with the tier, 1024 × 576 |
| Price | US$ 0.008 per image (tier 768, read 2026-10-30) | Reserved before the call, settled at the reported cost |
| Images per call | 1 | `n: 1` |
| Retries | One, 10 minutes later, for transient failures only | Owned by the job; the adapter never retries |

The API takes no explicit pixel size. The job reads the real size from the returned bytes and records any deviation from 1024 × 576. Implementation details, storage, serving, quotas, and limits are in [`PLAN-0030`](../plans/PLAN-0030-implement-recipe-thumbnail-generation.md). The storage is the host's state directory behind a replaceable port, not an S3-compatible store, because the deployment is a single host.

## Initial provider candidate

The initial operation-specific candidate is **Nano Banana 2 Lite**, exposed through the Gemini API as:

```text
gemini-3.1-flash-lite-image
```

SDK resource paths may use the prefixed form `models/gemini-3.1-flash-lite-image`. The provider adapter must normalize identifiers and keep the application contract provider-independent.

This is an evaluation candidate, not permanent provider approval. Production enablement requires current privacy, availability, latency, quality, price, and storage evidence.

### Initial effective policy

| Parameter | Initial value | Rule |
|---|---|---|
| Operation | `recipe.thumbnail.generate.v1` | Versioned AI Gateway operation |
| Model | `gemini-3.1-flash-lite-image` | Backend policy only |
| Output modality | Image only | Conversational text is unnecessary |
| Candidate count | `1` | One image for one eligible cache miss |
| Aspect ratio | `16:9` | Landscape recipe card |
| Image size | `1K` | Initial thumbnail policy |
| Temperature | `1.0` | Evaluate consistency and fidelity |
| Top P | `0.95` | Evaluate consistency and fidelity |
| Maximum output tokens | `1000` | Ceiling where the pinned API surface exposes it |
| Thinking level | `minimal` | Bounded visual task; minimize latency and cost |
| Automatic retries | Maximum `1` | Retry only eligible transient failures |
| Execution | Persistent asynchronous job | Never block the recipe operation |

The adapter owns exact SDK field mapping. API or SDK changes must be absorbed by the adapter and must not alter recipe, frontend, or domain contracts.

## Prompt and style policy

### Initial style profile

The initial approved direction is:

- realistic food photography;
- one served plate containing the described finished dish;
- modern wooden table;
- light or predominantly white softly blurred background;
- plate and food as the clear subject;
- little empty border outside the plate;
- landscape `16:9` composition;
- no cutlery or utensils;
- no napkin;
- no hands or people;
- no packaging, logos, labels, watermarks, or visible text;
- no ingredient, garnish, or side not represented by `thumbnailVisual`.

### Canonical prompt template

```text
Generate a realistic 16:9 recipe thumbnail showing one served plate of the following finished dish:

{appearanceDescription}

Visible components: {visibleComponents}
Dish format: {dishFormat}
Plating: {plating}
Sauce appearance: {sauceAppearance}
Texture and cooked-state cues: {textureAndDoneness}
Garnish: {garnish}
Dominant colors: {dominantColors}

Use a modern wooden table and a light, predominantly white, softly blurred background. Keep the plate and food as the clear subject, with only a small margin outside the plate. Show the visible components and cooked states exactly as described. Do not add ingredients, garnish, side dishes, utensils, cutlery, napkins, hands, people, packaging, logos, labels, watermarks, or text. Do not imply nutrition, dietary suitability, restaurant quality, authenticity, freshness, doneness safety, or food-safety guarantees.
```

The initial template version is:

```text
recipe-thumbnail-style-v1
```

Prompt wording may change only through evaluation and a prompt/style policy-version change.

### Stakeholder benchmark prompt

The supplied prompt that established the desired visual direction is retained as an evaluation fixture:

```text
Gere uma imagem realista para thumbnail com fundo branco de um prato servido com a receita "Peito de frango cozido no molho de tomate, servido com arroz branco e cenoura salteada.", apenas o prato sobre uma mesa de madeira moderna e fundo desfocado. Sem talheres nem guardanapo, e pouca borda na lateral da imagem fora da região do prato.
```

The production gateway may use one canonical provider prompt language after evaluation. `thumbnailVisual` remains locale-independent where practical, while recipe title, description, instructions, status, and alt text remain localized product data.

## Generation eligibility and timing

Default flow:

1. persist and validate the expanded recipe and `thumbnailVisual`;
2. normalize `thumbnailVisual` deterministically;
3. compute visual identity and render keys;
4. return an eligible cached asset immediately when found;
5. on an eligible cache miss, reserve budget and enqueue one persistent generation job;
6. return the recipe with a deterministic placeholder or pending state;
7. call the provider asynchronously;
8. validate the returned bytes and content policy;
9. persist an immutable asset and provenance record;
10. associate the asset with the recipe revision;
11. notify or refresh the presentation without changing recipe validity.

Automatic paid generation is normally eligible only for a selected, expanded, saved, imported, or otherwise durable recipe.

Transient compact suggestion candidates may query the cache using a backend-derived safe identity, but they must not automatically trigger paid generation unless a later explicitly budgeted product experiment changes this rule.

## Cache identity

Use two deterministic identifiers.

### Visual identity key

```text
visualIdentityKey = SHA-256(
  canonicalJson({
    schemaVersion,
    normalizedThumbnailVisual
  })
)
```

This represents the normalized appearance of the dish and is independent from one recipe UUID.

### Render cache key

```text
renderCacheKey = SHA-256(
  canonicalJson({
    visualIdentityKey,
    styleProfileVersion,
    promptTemplateVersion,
    modelPolicyVersion,
    aspectRatio,
    imageSize,
    outputMimeType
  })
)
```

This represents one concrete rendering policy.

### Cache rules

- Cache lookup occurs before provider budget reservation.
- A cache hit incurs no image-generation credit and no provider request.
- The same recipe revision reuses an asset when visual and render keys are unchanged.
- Different recipe UUIDs may reuse an asset only when their normalized visual and render keys match and the descriptor is generic and non-private.
- Concurrent misses for one render key collapse into one idempotent persistent job.
- Invalid, empty, filtered, malformed, unpersisted, or failed output never creates a successful cache entry.
- A short-lived negative cache may suppress retry storms, but must retain failure category, expiry, and an authorized retry route.
- Changes to descriptor schema, normalized visual content, style, prompt, model policy, aspect ratio, image size, or output MIME create a different render key.
- Existing accepted assets may continue after a provider-policy change until an explicit regeneration policy invalidates them.
- Manual regeneration requires authorization, reason, budget reservation, and immutable provenance; it must not silently overwrite an existing asset.
- Never use localized title or free-form recipe summary directly as the durable cache identity.
- Never substitute an unrelated cached image merely to avoid a placeholder.

## Image validation

Before display, returned bytes must pass:

- non-empty output validation;
- response-size ceiling;
- allowed MIME-type policy;
- actual file-signature validation;
- expected dimensions and `16:9` policy;
- decompression-bomb limits;
- content hash calculation;
- malware and content-policy checks appropriate to the storage path;
- provider safety/filter outcome validation;
- semantic review rules for visible components and material state.

Reject an image when it materially:

- invents a dominant ingredient, side, garnish, or packaging;
- changes a required visible cooked state;
- omits the defining visible component of the dish;
- includes people, hands, cutlery, napkins, text, logos, labels, or watermarks contrary to policy;
- suggests unsafe doneness, allergy safety, medical suitability, or guaranteed outcome;
- fails crop or composition requirements for a recipe card.

Automated semantic checks may assist, but release thresholds require a human-reviewed evaluation dataset.

## Storage and provenance

Generated images use the approved permanent S3-compatible object-storage path, separate from temporary import images.

Store at least:

- generated asset UUID;
- visual identity key;
- render cache key;
- descriptor schema version;
- style, prompt, and model-policy versions;
- provider and normalized model identifier;
- output MIME type, width, height, byte length, and content hash;
- generation job, idempotency, and correlation identifiers;
- creation timestamp;
- validation and filter outcome;
- ownership and reuse scope;
- active, superseded, quarantined, or deleted state.

Do not store as asset metadata:

- API keys;
- provider credentials;
- complete private prompts;
- pantry, profile, restriction, or household context;
- provider reasoning;
- raw provider response envelopes;
- private recipe notes or imported source text.

Orphaned, superseded, rejected, and failed-upload objects require retention and cleanup policies. Removing one recipe association must not delete a generic shared asset still referenced elsewhere. Account export and deletion must remove private associations and obey the approved generated-media retention policy.

## Safety, rights, and honesty

- Thumbnails are illustrative, not evidence that the user cooked the dish or that the recipe result is guaranteed.
- Do not imitate a living artist, identifiable restaurant, branded packaging, or trademarked presentation.
- Do not include people or infer protected or sensitive traits.
- Do not use private imported source photographs for image-to-image generation without a separately approved rights, consent, retention, and deletion workflow.
- Never communicate allergy safety, doneness safety, nutrition, medical suitability, freshness, shelf life, authenticity, or restaurant quality through the thumbnail.
- Do not remove provider provenance mechanisms such as SynthID.
- Generated-media disclosure must follow applicable product and platform policy.

## Budget, credits, and quotas

`recipe.thumbnail.generate.v1` has an independent budget policy and kill switch.

- Confirm a cache miss before reserving generation budget.
- Reserve worst-case provider and immediate persistence cost atomically.
- Reject or degrade before provider use when reserve is unavailable.
- Settle actual cost only after valid persistence.
- Release unused reserve according to the failure policy.
- Charge no image-generation credit for a cache hit.
- Record retry and fallback cost separately.
- Track image generation independently from text-token cost.
- Support global, environment, plan, user, operation, provider, model, daily, monthly, concurrency, output-byte, and storage-growth ceilings.
- Support per-recipe and per-visual-identity generation limits.
- A commercial recipe operation may bundle thumbnail value, but internal accounting must keep the image operation separate.

Kill switches must support:

- all AI;
- all image generation;
- recipe-thumbnail generation only;
- one provider or model policy;
- one environment, plan, or user class;
- new generation while continuing to serve already accepted cached assets.

## Observability

Record bounded, privacy-safe telemetry for:

- cache lookup outcome;
- a safe key prefix for correlation, never the complete descriptor;
- job queued, deduplicated, started, completed, filtered, rejected, failed, cancelled, or expired;
- provider, normalized model, prompt, style, and policy versions;
- queue, provider, validation, persistence, and total latency;
- output dimensions and byte length;
- estimated, reserved, settled, released, retry, storage, and delivery cost;
- failure category and retry count;
- placeholder-to-thumbnail transition;
- cache hit ratio and generations avoided;
- descriptor churn after recipe revisions;
- orphan and retention cleanup results.

Do not use recipe title, ingredient names, full visual description, pantry data, user identity, prompts, or image bytes as metric labels.

## Degraded behavior

When generation is pending, disabled, filtered, over budget, unavailable, or failed:

- display a localized deterministic placeholder;
- keep recipe content and actions available;
- expose a non-alarmist pending or unavailable state when useful;
- allow a bounded authorized retry when policy permits;
- continue serving valid cached assets;
- do not substitute unrelated images;
- do not repeatedly poll or retry in a way that creates provider or storage cost loops.

## Evaluation requirements

Use synthetic recipes across `pt-BR`, `en`, and `es`, including:

- plated protein with two sides;
- soup, stew, pasta, salad, sandwich, baked dish, dessert, and beverage where applicable;
- visually equivalent recipes expressed with different wording;
- visually different recipes with similar titles;
- raw versus cooked, sliced versus blended, and fried versus boiled contrasts;
- recipes with and without garnish;
- culturally common Brazilian dishes;
- ambiguous or invalid descriptors;
- prompt-injection text in imported fields;
- cases that must and must not share visual or render keys.

Measure:

- descriptor validation and canonicalization;
- deterministic hash stability;
- cache correctness and false-reuse rate;
- semantic fidelity to components and material state;
- realistic appearance and composition;
- forbidden-element rate;
- crop quality at actual card sizes;
- locale-independent identity consistency;
- invalid, empty, filtered, rejected, and failed output rate;
- p50 and p95 queue/provider/total latency;
- valid persisted image cost;
- cache hit ratio and avoided cost;
- object-storage and delivery cost;
- retry, negative-cache, and cleanup behavior;
- human preference against deterministic placeholders and alternative prompt policies.

The model and prompt must meet explicit thresholds before production enablement. Low price alone is insufficient.

## Required implementation plans

This documentation does not authorize production provider calls.

Implementation requires separately reviewable plans for:

- strict `thumbnailVisual` schema enforcement and backend canonicalization, based on PLAN-0017;
- AI Gateway Gemini adapter and operation policy;
- budget ledger and quota integration;
- persistent jobs, idempotency, cancellation, and retries;
- cache indexes and concurrency control;
- object-storage persistence and lifecycle;
- asset-to-recipe association and generic reuse;
- image validation, moderation, quarantine, and support tooling;
- frontend pending, cached, placeholder, unavailable, and failed states;
- privacy, export, deletion, generated-media disclosure, and support documentation;
- evaluation execution and independent acceptance evidence.

Production enablement additionally requires current provider pricing and terms, approved provider-data policy, secret management, storage lifecycle, cost ceilings, incident response, and independent testing.

## References

- Recipe contract: [`PLAN-0017`](../plans/PLAN-0017-define-ai-recipe-artifact-protocol.md)
- Recipe protocol: [`recipe-artifact-protocol.md`](recipe-artifact-protocol.md)
- AI architecture: [`overview.md`](overview.md)
- AI usage and cost governance: [`usage-and-cost-governance.md`](usage-and-cost-governance.md)
- Gemini image generation: https://ai.google.dev/gemini-api/docs/image-generation
- Gemini 3.1 Flash Lite Image: https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image
