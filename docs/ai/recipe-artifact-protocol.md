# AI Recipe Artifact Protocol

- **Status:** Revised — protocol `0.3`
- **Disposition:** Revised (from `0.3-draft`) under PLAN-0022 lean evaluation
- **Plan:** [`PLAN-0022`](../plans/PLAN-0022-evaluate-and-finalize-recipe-ai-contracts.md) · predecessor [`PLAN-0017`](../plans/PLAN-0017-define-ai-recipe-artifact-protocol.md)
- **Strict schemas:** [`../../packages/contracts/ai/recipe/`](../../packages/contracts/ai/recipe/)
- **Decisions:** [`decisions/ingredient-identity-2026-08-05.md`](decisions/ingredient-identity-2026-08-05.md), [`decisions/package-confidence-threshold-2026-08-05.md`](decisions/package-confidence-threshold-2026-08-05.md), [`decisions/model-routing-cook-now-menu-planning-2026-08-05.md`](decisions/model-routing-cook-now-menu-planning-2026-08-05.md)
- **Thumbnail policy:** [`PLAN-0008`](../plans/PLAN-0008-define-lean-launch-ai-economics.md)
- **Updated:** 2026-08-05
- **Text-model baseline:** `deepseek-v4-flash` (thinking-high quality baseline; non-thinking fallback for synchronous `cook_now`)

## Boundary

Cocinaris uses AI to propose culinary artifacts. Application code remains authoritative for identity, authorization, inventory, quantities, expiration, reservations, shopping aggregation, package arithmetic, scheduling, concurrency and mutations.

Every model response is parsed, schema-validated and semantically validated. It may be rejected or repaired and never mutates product state directly.

The initial flow generates exactly **three compact candidates in one call** and expands only the selected candidate. The selected expansion also returns a bounded `thumbnailVisual` description. Image generation is a separate PLAN-0008 operation and never blocks recipe use.

## Operations

### `recipe.suggest_candidates.v1`

Returns exactly three compact candidates for either:

- `cook_now`: immediate meal using current physical availability and a hard lead window;
- `menu_planning`: dated meal using physical stock, reservations, planned purchases and reliable projected package surplus.

Candidates contain comparison and validation fields, not detailed cooking steps. Transient candidates may reuse an existing cached image, but do not automatically trigger paid image generation.

Strict response schema: `packages/contracts/ai/recipe/schemas/recipe-suggest-candidates.response.v0.3.json` with `schemaVersion: "0.3"`.

### `recipe.suggest_plan_deck.v1`

Returns exactly six compact candidates for a shopping plan (PLAN-0062). The candidate shape is the same as `recipe.suggest_candidates.v1`, so expansion and the idea library work on them unchanged. Six keeps the wait near 20 s; later batches arrive in the background.

Strict response schema: `packages/contracts/ai/recipe/schemas/recipe-plan-deck.response.v0.3.json`.

- **Request.** The suggestion envelope (policy, presets, restrictions, equipment, and free stock only) plus `planDeck`: `days`, `cookingStyle`, `priority`, `mealNeeds`, `mealTypes`, `maxActiveMinutes`, `chosenDishes` (20), `shoppingListIngredients` (40), `rejectedDishes` (30), `avoidIngredients` (10), and a closed `rule` asking the model to reuse perishables and vary format, technique, and flavor.
- **The anchor.** `planDeck.anchorIngredient.text` is the person's typed ingredient, cleaned to 60 characters, with the same untrusted treatment as `personHint`. A session with an anchor is private: its recipes never reach the shared idea library.
- **The person's answers (PLAN-0064).** With `free`, `availabilitySnapshot.items` and `shoppingListIngredients` are sent empty, `inventoryUsageMode` is `ignore_inventory`, and `rule` stops asking for reuse. With `use`, `rule` asks for few new ingredients. `planDeck.direction` carries `mode` (`similar` or `different`) and `referenceDish`, which is untrusted data and must be a card of the same plan. `planDeck.personHint.text` is typed text, cleaned to 40 characters, with the same untrusted treatment as the anchor. The response schema is unchanged, and restrictions are sent and enforced as before.
- **Options.** "More options" use `suggestionGuidance` closed codes only.
- **Validation.** Exactly six; strategies limited to `on_hand_first`, `planned_purchase_reuse`, and `exploratory`, repeats allowed; unique fingerprints; every `targetMealType` requested; and the per-candidate rules shared with suggestions. Summary language is a soft check.
- **After validation.** The application drops near-duplicates and runs `RestrictionGuard`. Model output never changes a menu directly.

### `recipe.suggest_plan_deck.v2`

The same six cards plus `components`, 0 to 2 reusable bases, for plans that cook ahead (`once` and `mix`; PLAN-0062 Slice 5). A `daily` plan keeps v1.

Strict response schema: `packages/contracts/ai/recipe/schemas/recipe-plan-deck.response.v0.4.json`, with `schemaVersion: "0.4"`.

- **Component.** `{ candidate, output: { name, quantity, unit }, usedBy: [candidateId] }`. `candidate` has the usual shape, so expansion works unchanged.
- **Rules.** Every `usedBy` id exists; `output.name`, normalized, is an `additionalIngredients` name of each of those dishes, in `output.unit`; `output.quantity` covers all of them; the candidate passes every candidate rule.
- **A bad component is dropped, not repaired.** The six dishes are useful without it. More than two components fails the schema, as usual.
- **After validation.** `RestrictionGuard` also checks what the base makes. A base is offered only after a dish that uses it is accepted.
- **Evaluation.** Gate G5-A: `PLAN0062_LIVE_EVAL=1` prints returned and valid components per scenario. It has not run yet; it needs the provider key.

### `recipe.expand_selected.v1`

Produces one persisted recipe revision containing ingredients, equipment, preparation tasks, stages, dependencies, relative lead times, sensory cues, storage/freezing guidance, produced components, reconciliation hints and `thumbnailVisual`.

The backend converts relative dependencies into concrete reminders and validates/canonicalizes the visual descriptor.

Strict response schema: `packages/contracts/ai/recipe/schemas/recipe-expand-selected.response.v0.3.json` with `schemaVersion: "0.3"`.

### `recipe.thumbnail.generate.v1`

A separate PLAN-0008 operation. It receives only a validated visual descriptor plus versioned render policy. Provider, model, prompt, cache job, budget, storage, moderation and fallback behavior do not belong to the recipe artifact.

## Required request context

Every suggestion request includes:

```json
{
  "executionContext": {
    "executionMode": "cook_now",
    "localDateTime": "2026-08-01T18:10:00-03:00",
    "timeZone": "America/Sao_Paulo",
    "targetMealType": "dinner",
    "targetMealTime": null,
    "availableLeadMinutes": 55
  }
}
```

Both operations also state the output language and the instruction style (added 2026-10-29 after owner feedback: candidates arrived in English for a Portuguese user, and the steps were bare titles for a profile that asked for detailed steps):

```json
{
  "outputLanguage": {
    "languageTag": "pt-BR",
    "languageName": "Brazilian Portuguese",
    "rule": "Write every human-readable string in Brazilian Portuguese (pt-BR): ... Keep stable ids, enum values, and units exactly as the schema defines them."
  },
  "instructionStyle": { "detailLevel": "detailed", "cookingSkill": "beginner", "rule": "..." }
}
```

- **Language source, in order:** the profile language; else the interface locale the frontend sends in `Accept-Language`; else `pt-BR`. Only `pt-BR`, `en`, and `es` are accepted, so a header can never place free text in the envelope. Inventory user names stay verbatim (`preserveInventoryUserNames`).
- **Detail source:** the profile's preferred instruction detail (`minimal`, `standard`, `detailed`). When none is declared, `beginner` and `developing` cooks get `detailed` and everyone else gets `standard`. `detailed` asks for every action in order with quantities, utensil, heat, time, and doneness signs, using up to the 700 characters a stage allows.
- The DeepSeek system prompt names both fields. Language and detail cannot be validated deterministically; they are evaluation criteria for the AI evaluation set (PLAN-0040).

The model never guesses timezone or silently changes the requested meal. A `cook_now` candidate is invalid when soaking, thawing, marinating, fermentation, cooling or another blocking preparation exceeds the available lead window.

## Menu planning requests (PLAN-0029 Phase 2)

For `menu_planning`, `executionContext` adds:
- `plannedDate`;
- `targetMealType`;
- `alreadyPlannedRecipeNames`, at most 20;
- a `planningRule` asking for dishes unlike the planned ones.

The recommended strategy set is `on_hand_first`, `planned_purchase_reuse`, and `exploratory`. `availabilitySnapshot` quantities already exclude protections and other meals' reservations.

## Suggestion guidance (PLAN-0056 Phase 2)

Before asking for three candidates, the person may steer them. The HTTP body field `guidance` accepts closed codes only:
- `mealKind`: `full_meal`, `light_meal`, or `snack`;
- `dishFamilies`: up to 3 of 12 families;
- `cuisine`: one of 12 cuisines;
- `time`: `quick` (20 min or less) or `medium` (45 min or less);
- `requiredLotIds`: up to 5 pantry lots;
- `excludedLotIds`: up to 50 pantry lots.

Unknown codes, more than the limits, a lot both required and excluded, or a required lot that is not in the measured pantry return `400 validation_failed`. No free text is accepted, so nothing the person writes reaches the model as an instruction.

- **Excluded lots** are removed while the context is assembled. They never appear in the request, not even their names.
- **Required lots** stay first in `availabilitySnapshot.items`, so budget truncation never drops them, and carry `mustUse: true`.
- **The request** carries `suggestionGuidance` with:
  - `rules`;
  - `mealKind`, `dishFamilies`, and `cuisine` (preferences the model follows);
  - `maxTotalMinutes` and `requiredInventoryItemIds` (hard rules).

  It is written only when something is set, so unguided requests are unchanged.
- **Deterministic validation** (`RecipeSuggestSemantics`, mirrored in `packages/contracts/ai/recipe/lib/validate-core.mjs`) rejects a candidate that does not list every required item in `inventoryUses` or whose `time.totalMinutes` exceeds `maxTotalMinutes`. A rejected answer goes through the normal repair.

## Output language and detail checks (PLAN-0040)

The request's `outputLanguage` and `instructionStyle` are enforced as far as deterministic code can, after schema and semantic validation (`RecipeOutputQuality`).

- **Language.** Distinctive function words of Portuguese, Spanish, and English are counted in candidate summaries, and in stage instructions and sensory cues. Recipe and ingredient names are excluded, since they often come from the pantry. A clear majority of another language's function words is a `language` issue; texts under 25 words are not judged.
- **Detail.** Stage instruction length is checked against the level.
  - `detailed`: every stage at least 120 characters, with an average of at least 200.
  - `standard`: every stage at least 50 characters.
  - `minimal`: never flagged.
- **Effect.** An issue uses the operation's single repair, with the issue as the repair instruction. The first valid answer is kept when the repair is not clean, and the flags are recorded on the usage ledger. A valid answer is never discarded for a soft issue.

## Candidate strategies

One call returns one candidate for each strategy:

| Mode | Strategy | Intent |
|---|---|---|
| both | `on_hand_first` | prioritize current stock and minimize additions |
| `cook_now` | `on_hand_flexible` | reuse stock with bounded additions |
| `menu_planning` | `planned_purchase_reuse` | reuse existing shopping lines or reliable projected surplus |
| both | `exploratory` | meaningfully different but still practical option |

All candidates must be recognizable dishes or conventional techniques. Across a batch they must differ materially in dish format, primary technique and main ingredient structure. Renaming, seasoning changes, sauce swaps or side changes alone are not sufficient diversity.

The backend computes authoritative semantic fingerprints and rejects near-duplicates across current and later rounds.

## Generation sessions

Cocinaris owns generation-session state. Another batch receives current canonical state plus at most nine compact prior semantic summaries/fingerprints and explicit rejection reasons. Full model responses, provider conversation history and reasoning are not replayed.

Provider cache/state is an optimization only; correctness never depends on it.

## Planning availability and shopping

The model consumes backend-computed `availableForPlanning` values. Availability source is one of:

```text
on_hand
planned_purchase
prepared_component
```

The model must not calculate balances, sufficiency, reservations, remaining quantities, shortfalls, package counts, percentages or shopping quantities.

Projected package surplus enters AI context only at confidence `high` ([decision](decisions/package-confidence-threshold-2026-08-05.md)).

When a recipe is accepted, deterministic code:

1. validates current revisions;
2. expands or reuses a persisted equivalent;
3. validates the complete recipe and `thumbnailVisual`;
4. reserves physical availability;
5. aggregates remaining demand into one shopping plan;
6. resolves package quantity using user choice, preference, purchase history, catalog, bounded hint, then exact-demand fallback;
7. reserves recipe demand against the planned purchase;
8. exposes reliable unused package quantity to later menu slots.

Physical stock is not increased until purchase confirmation. Planned and physical stock remain separate ledgers.

## Ingredient state and preparation

Availability explicitly declares states such as `raw`, `cooked`, `frozen`, `thawed`, `soaked`, `chopped`, `prepared_component` or `unknown`.

The model cannot assume a prepared state. Required transformations appear as advance preparations or recipe stages.

Canonical ingredient identity is backend-owned and reference-preserving ([decision](decisions/ingredient-identity-2026-08-05.md)).

Candidates expose:

- whether advance preparation is required;
- minimum lead minutes;
- blocking preparation codes;
- whether reusable components may be produced.

Expanded tasks return relative dependencies only. Concrete timestamps, reminders, conflict handling and rescheduling remain backend-owned.

## Constraints and exact references

Hard constraints are absolute: restrictions, required/excluded ingredients, equipment/capabilities, target meal, lead time, maximum time, inventory mode, ingredient state and explicit user constraints.

For supplied items, the model copies exact stable references, names, units, states and availability sources. Unit conversion and renaming are forbidden. Canonical units in responses are `g`, `ml`, and `unit`.

`assumptionsUsed` lists only authorized assumptions actually used. An assumption cannot also appear as an additional ingredient.

Unsupported claims such as `homemade`, `fresh`, `healthy`, `authentic`, `traditional` or `high-protein` are forbidden unless supported by deterministic input.

## Candidate artifact

A compact candidate contains:

- stable ID and strategy;
- name and target meal type;
- dish format, primary technique and primary ingredient references;
- summary of two short inviting sentences, at most 320 characters; the backend fits a longer one at a sentence end, so length alone never rejects an answer (PLAN-0060);
- servings, active/passive/total time and stable difficulty;
- complete equipment IDs and capabilities;
- exact inventory uses and bounded additional ingredients;
- preparation profile and assumptions used.

Detailed steps, troubleshooting, shopping/package arithmetic, nutrition prose and the full visual descriptor are excluded.

## Expanded recipe artifact

A persisted expansion must support cards, shopping, reminders, guided cooking, storage, reconciliation and thumbnail-cache lookup without another text-generation call.

In addition to normal recipe fields, it includes:

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

### Visual descriptor rules

- Describe only visible finished-dish properties.
- Include only ingredients/components expected to be visible.
- Preserve material state: cooked, roasted, sautéed, blended, sliced, whole, melted and similar distinctions.
- Do not invent garnish, sides, brands, packaging, tableware or restaurant styling.
- Do not include user identity, pantry quantities, restrictions, private notes, imported source text, URLs or household history.
- Do not make safety, nutrition, freshness, authenticity or guaranteed-result claims.
- Keep fields bounded and canonicalizable.
- Material visual changes create a new descriptor; wording-only edits do not.
- Do not emit cache hashes, provider names, model names, or render policy inside the descriptor.

The backend validates consistency with normalized ingredients and produced states, canonicalizes accepted JSON and computes the semantic visual identity. The model never authors hashes or cache decisions.

PLAN-0008 separates:

```text
visualIdentityKey = hash(descriptor version + normalized descriptor)
renderCacheKey = hash(visual identity + style/prompt/model/format policy)
```

Only the semantic descriptor belongs to the recipe artifact. Localized title and free-form summary are not durable image cache keys.

Thumbnail generation is asynchronous and fail-soft. Saving, planning, opening and cooking continue with a deterministic placeholder when no image is available.

## Person's direction and AI revision (PLAN-0060)

`suggestionGuidance` may carry two more fields:

- **`ingredientApproach`**: `code` (`adapt_to_pantry` or `stay_faithful`) and a fixed `rule`. The expansion receives it too. A candidate may list up to 12 `additionalIngredients`. Under `adapt_to_pantry`, more than 2 required ones raises a soft `adaptation` quality flag and one repair, and the answer is kept.
- **`personHint`**: `text` (at most 300 characters, cleaned by `RecipeUntrustedText`) and a fixed `treatment` rule. It is an untrusted wish. No validator depends on it, the expansion does not receive it, and restrictions always win over it. Sessions it shaped carry `PrivateDirection`, so their recipes never enter the shared idea library, and hinted requests are not served from that library.

`recipe.revise.v1` writes a complete new version of an owned recipe:

- **Request:** `currentRecipe`, `revisionRequest.text` (at most 300 characters, untrusted, cleaned the same way), `revisionPolicy`, the current pantry, equipment, and presets.
- **Response:** the same recipe shape as an expansion ([`recipe-revise.response.v0.3.json`](../../packages/contracts/ai/recipe/schemas/recipe-revise.response.v0.3.json), a test keeps it equal to the expansion schema apart from `operation`).
- **Validation:** the same as an expansion, plus every `inventoryItemId` must be in the snapshot even when the snapshot is empty. The stored `recipeId` and `revision` are set by the backend.
- **Effect:** none until the owner accepts. The answer is stored as a proposal, and acceptance appends a revision with origin `AiRevised` while the recipe is still at the base revision.

For all operations, names, summaries, and recipe text with a link or markup are rejected.

## Model, latency and token policy

Candidate generation uses compact fields, no detailed steps, no previous reasoning and no full historical replay. Ordinary recipe generation does not use web/Google search.

The visual descriptor is generated only during selected-recipe expansion and must not inflate candidate responses.

Routing policy ([decision](decisions/model-routing-cook-now-menu-planning-2026-08-05.md)):

- synchronous `cook_now`: prefer non-thinking fallback;
- `menu_planning`: prefer thinking-high behind progressive UI;
- maximum automatic repair attempts = 1;
- empty/truncated JSON is a failure after repair.

Latency targets remain product goals:

```text
p50 <= 15 seconds
p95 <= 25 seconds
```

Statistical p50/p95 characterization is **deferred** until a larger evaluation sample exists. Lean PLAN-0022 observations and PLAN-0017 provisional runs are recorded as point samples only.

Never expose or persist reasoning content. Record token/cache usage, time to first token, total latency, repair rate, semantic failures and cost per accepted recipe. Track visual-descriptor failures separately from image-generation metrics.

## Validation

Application validation covers:

- closed field types, limits and enums via the versioned JSON Schemas;
- exact references, names, units, states and sources;
- hard constraints and revision freshness;
- equipment/capability and lead-time feasibility;
- assumption duplication;
- culinary plausibility and diversity;
- visual-component fidelity and material state;
- absence of private context in `thumbnailVisual`;
- deterministic visual canonicalization;
- independence of recipe success from thumbnail availability.

All strings from users, inventory, OCR, imports, URLs and notes are untrusted data. Instructions remain separate from data, schemas are closed and model output is never executed directly.

Deterministic validators and fixtures live in `packages/contracts/ai/recipe/` and are the default CI-oriented check for contract changes. Live provider campaigns remain opt-in and cost-bounded.

## Finalized by PLAN-0022

- strict candidate and expansion response schemas (`0.3`);
- collection and string bounds;
- exact-reference and canonical-unit rules;
- ingredient identity treatment;
- package-confidence threshold (`high` only for surplus context);
- `cook_now` / `menu_planning` model policy and repair/fallback behavior;
- protocol disposition: **Revised** to `0.3`.

## Remaining implementation work (not this protocol)

- production AI Gateway and provider adapter;
- food-safety/storage authority and disclaimer policy UI;
- slow-response progress UI;
- PLAN-0008 image generation and cache infrastructure;
- larger-sample latency/cost characterization.
