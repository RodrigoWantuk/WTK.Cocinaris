# PLAN-0062: Guided Shopping Planning ("Planejar compras")

- **Status:** Ready (owner approved this plan on 2026-11-04; Slice 1 not started)
- **Type:** Implementation
- **Priority:** High (owner request)
- **Owner:** Unassigned; the next execution agent claims it
- **Created:** 2026-11-04
- **Last updated:** 2026-11-04
- **Branch:** Plan publication `agent/plan-0062-guided-shopping-planning`; each slice uses the branch named in its section
- **Pull request:** Plan publication draft PR, opened with this file
- **Related issues:** None
- **Related ADRs:** ADR-0005, ADR-0009, ADR-0011; new ADR-0012 (Slice 3) and ADR-0013 (Slice 7)
- **Dependencies:** PLAN-0023, PLAN-0029, PLAN-0037, PLAN-0042, PLAN-0046, PLAN-0055, PLAN-0060, PLAN-0061 (all merged). This plan absorbs the session-route part of PLAN-0044 and the web-push part of PLAN-0048.

## How to use this plan

The executing model is Opus 5.5 at medium effort. **Every product and technical decision below is final.** Do not reopen, redesign, or ask the owner about a decision recorded here.

- Execute one slice at a time, in order. A slice is one pull request from a freshly fetched `origin/main`.
- Start a slice only after the previous slice is merged by the owner.
- Agents never merge, approve, or enable auto-merge (`AGENTS.md` §11). A green gate is not merge authorization.
- Read the slice's "Hazards" before writing its code. Those are the places most likely to go wrong.
- **Deviation rule:** if the code contradicts a detail here, for example a signature changed, keep the outcome and the invariants, adapt the mechanics, and record the deviation in the progress log. Stop and ask the owner only for the stop conditions below.
- **Stop conditions:** a gate marked "Owner gate" is pending; the code cannot satisfy a section "Invariants" rule; provider credentials are missing for a required evaluation; remote authentication is unavailable; or an unrelated failure blocks the gates.
- Before every commit, update this plan's `Execution state` and `Progress log` and the registry row (`AGENTS.md` §5).

## Objective

Help someone who is going to the market and does not know what to cook. They answer one short setup screen, accept or reject dish cards, and get a shopping list net of their pantry. After shopping, Cocinaris tells them where everything goes, how to portion it, and what to prepare ahead. Then it reminds them before food is lost.

## Context

Today a person must build a menu slot by slot, with one AI request and one wait per slot (`docs/domain/planning-and-shopping.md`). The shopping list works only after that effort. The discovery record names the real pains: unreliable pantries, pack remainders for people living alone, boredom from repeated meals, and freezing strategies "known in theory but not planned in practice".

This module is a guided entry into the existing menu draft, shopping list, pantry, reservations, cooking, and reminders. It is **not** a parallel planner. The page `apps/frontend/src/pages/Plan.tsx` and its sibling `pages/Shopping.tsx` belong to the prototype build. Do not change them.

## Owner decisions (2026-11-04)

| Topic | Decision |
|---|---|
| Main flow | Dish-by-dish cards with Yes, No, and "Yes, twice", and a live "+N new items" count |
| Optimization goal | The person chooses for each plan: less waste, short list, save money, or less time |
| Cooking pattern | Asked in every plan, with the last answer preselected |
| "Yes, twice" | Asks on tap: "Yields 2 meals" or "Cook again another day" |
| List scope | Recipe ingredients only. No manual or recurring items. Watch for a second list during external testing |
| Pre-prep | Raw portioning, reusable components, frozen whole dishes, and a timed session route |
| Days | Suggested order with an advisory cook-by limit; a fixed date is optional |
| Pack size | No curated pack table. No remainder calculation until the person confirms a size |
| Pantry check | "Já tenho" on each list line, no separate review step |
| Preparation timing | Set while building the plan. "Cheguei do mercado" also offers calendar-independent work |
| Missed limit | In-app warning with options, plus opt-in notifications outside the app |
| Storage guidance | A "Cheguei do mercado" step; the list shows only a short hint |
| Market extras | Share the list, offline list, "Não achei este item", split by store |
| Plan extras | Repeat or save a plan, start from an ingredient, containers and labels |
| Delivery order | From plan to list, then market and arrival, then preparation, then session route and outside notifications |
| Out of scope | Budget and prices, curated pack sizes, manual or recurring list items, shared household |

## Invariants

These hold in every slice. A test must protect each rule that can be tested.

1. **AI never mutates state.** Model output is validated, and only deterministic commands change menus, recipes, or the pantry.
2. **Restrictions are hard rules.** The goal, ranking, chips, and anchor ingredient can never relax them. Allergies and medical restrictions are never inferred.
3. **Drafts reserve nothing.** A plan appears on "Hoje" and reserves pantry stock only after explicit acceptance.
4. **Accepted plans are never silently rewritten.** A change touches one entry, and only by explicit command.
5. **Shelf-life output is advisory.** It uses curated rules, never model text, and never claims a guarantee. Without a rule, show nothing.
6. **Pack sizes are never invented.** Only an owner-confirmed size enables package counts and remainder.
7. **Degraded mode works.** With AI off, the flow still runs on saved recipes and library ideas, and the existing menu and list stay intact.
8. **Owner isolation.** Every read and write is owner-scoped, and another owner's identifier returns 404 without disclosure.
9. **Answers do not update the profile.** Setup answers belong to the plan. Reusing the last setup as a default is not a profile change.
10. **Private words stay private.** A recipe influenced by a typed anchor ingredient never reaches the shared idea library (PLAN-0060 rule).
11. **Localization.** All copy lives in catalogs for pt-BR, en, and es, and the backend returns codes, not sentences.

## Scope

### Included

Seven slices, in the owner's order:

| Slice | Owner stage | Outcome |
|---|---|---|
| 1 | From zero to list | Setup, cards, review, "Já tenho", share |
| 2 | Market and arrival | "Não achei", split by store, "Cheguei do mercado", raw portioning, containers and labels |
| 3 | Market and arrival | Offline list |
| 4 | Preparation and continuity | Cook-by limits, in-app warnings, repeat or save a plan |
| 5 | Preparation and continuity | Reusable components and frozen whole dishes |
| 6 | Session and notifications | Timed session route |
| 7 | Session and notifications | Web push |

### Excluded

Everything in the "Out of scope" row above. Also excluded: e-mail notifications (they need a provider decision and stay in PLAN-0048), swipe gestures (buttons only), printing labels, and price estimates in any currency.

## Requirements and constraints

- Backend modules keep ownership: Planning owns plan sessions and ranking, Recipes owns AI generation and expansion, Inventory owns lots, and Ingredients owns curated tables.
- Every AI operation goes through the gateway with a registered definition, closed schema, validator, limits, and evaluation.
- Follow `docs/design/design-system.md`: one primary button per view, 44 px minimum targets, 56 px for the main action, and state in words.
- Use lean validation (`AGENTS.md` §9): targeted tests, one focused boundary test per real boundary, then one gate run on the final head.

## Substantial run delivery target

- **Target outcome:** one complete slice, from migration to browser journey, ready for owner review.
- **Minimum acceptable evidence:** code, tests, documentation, regenerated contracts, and gate results in the PR.
- **Adjacent checkpoints:** none across slices, because the next slice starts from merged `main`.
- **Valid early-stop conditions:** the stop conditions in "How to use this plan".

## Functional specification

### Setup screen (`/app/compras/planejar`)

One screen, no steps, using `ChoiceChips`:

| Row (pt-BR copy) | Options | Default | Rules |
|---|---|---|---|
| "Pra quantos dias?" | 3, 5, 7, outro (1–14) | Last answer, else 5 | Required |
| "Quais refeições?" | Almoço, Jantar, Lanche, Café da manhã (multi) | Last answer, else Almoço and Jantar | At least one |
| "Quantas serão fora de casa?" | Stepper 0 to total − 1 | 0 | Total = days × meal types; "N refeições" updates live |
| "Como quer cozinhar?" | Uma vez e comer vários dias (`once`); Um pouco de cada (`mix`); Cada dia um prato (`daily`) | Last answer, else `mix` | Required |
| "O que pesa mais?" | Desperdiçar menos (`waste`); Lista curta (`short`); Economizar (`save`); Menos tempo (`time`) | Last answer, else `waste` | Required |
| "Quando vai preparar?" | Hoje, Amanhã, Escolher dia (up to 7 days ahead) | Hoje | Hidden for `daily` |
| "Quanto tempo?" | 1 h, 2 h, 3 h ou mais | Last answer, else 2 h | Hidden for `daily` |
| "Quer partir de algum ingrediente?" | Optional text, 60 characters, collapsed | Empty | Suggests catalog names |
| Summary line | "Para 2 pessoas · sem lactose · Alterar" | Profile | People stepper 1–12 for this plan only; restrictions link to the profile |

- `3 h ou mais` is stored as 240 minutes.
- Total meals are limited to 40. Above that, the "Começar" button is disabled and the row explains why.
- A restriction code the guard cannot check adds: "Não consigo conferir '{restriction}' automaticamente. Confira os ingredientes."
- If a plan is in progress, the page opens with "Continuar plano" as the primary action and "Descartar e começar outro" as a ghost action with confirmation.

### Cards (`/app/compras/planejar/:sessionId`)

Top line: "Refeição 2 de 8 · lista: 7 itens". For `once` and `mix`, add "Preparo: 45 de 120 min".

The card shows name, summary, total time, servings, **"+N itens novos"** ("nenhum item novo" when zero), "Usa: frango, tomate" (items already on the list or at home), and the reason when relevant ("Usa o iogurte que precisa de atenção"). A collapsed section lists all ingredients.

| Action | Effect |
|---|---|
| **Sim** (primary, 56 px) | Accepts one meal; the next card appears immediately |
| **Sim, 2 vezes** | Opens a sheet with a stepper (2–4) and "Rende para N refeições" or "Fazer de novo outro dia". The preselected mode is "Rende" for `once` and `mix`, "Fazer de novo" for `daily`. Limited by remaining meals and by servings ≤ 24 |
| **Não** | Rejects; the next card appears, with an optional reason strip: "Não gosto", "Muito trabalho", "Enjoei de {ingredient}" |
| **Desfazer** | Undoes the last decision |
| **Quero outras opções** | Chips for family, cuisine, and time from the existing closed codes. Highlighted after 3 straight rejections |
| **Já está bom, ver a lista** | Goes to review at any time with at least one accepted card |

When all meals are filled, go to review automatically. While AI cards are still being fetched and no card is available, show a skeleton and "Procurando pratos que combinam…", with "Sair" available.

### Review (`/app/compras/planejar/:sessionId/revisar`)

Dishes in suggested order, with "Faça até {date}" when available (Slice 4), meal coverage, and session dishes under "Preparo: {date}". Each row can change servings, set a date, swap ("Trocar"), or remove. Actions: "Ver lista" and primary "Usar este plano". "Usar este plano" accepts the menu.

If any expansion is pending: "Preparando receitas: 3 de 5". A failed one shows "Não consegui preparar {dish}" with "Tentar de novo", "Trocar", and "Remover".

### Exceptions

| Situation | Behavior |
|---|---|
| Empty pantry | Normal flow; "Já tenho" fills the pantry |
| Profile incomplete | People defaults to 2; no restriction is assumed |
| Guard blocks every card | Explain and offer other options; never relax the rule |
| AI unavailable or budget exhausted | Continue with saved and library cards, with one notice. If there are none: empty state linking to recipes |
| No more options (5 batches used) | "Não há mais opções agora"; finish with what was chosen |
| Left mid-flow | Everything saved; resume from setup, "Compras", or "Cardápio" |
| Discard | Archives the menu; cards are deleted; recipes already created stay in the library |
| Undo after expansion | Removes the entries; the created recipe stays in the library as an ordinary recipe |
| Over-budget card | Stays visible, ranked low, labeled "Passa do seu tempo" |

## Architecture and contract impact

### Data model (Slice 1)

Migration `AddShoppingPlans`. Tables belong to Planning.

**`shopping_plan_sessions`**

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `owner_user_id` | uuid | |
| `menu_plan_id` | uuid FK, unique | Cascades on delete |
| `creation_key` | uuid | Unique with owner |
| `status` | text | `building`, `reviewing`, `finished`, `abandoned` |
| `setup_json` | jsonb | Validated setup; kept for defaults |
| `batch_count` | int | Limit 5 |
| `consecutive_rejections` | int | |
| `last_decided_card_id` | uuid null | For undo |
| `private_direction` | bool | True when an anchor was typed |
| `created_at`, `updated_at` | timestamptz | |

Partial unique index: one session per owner with status in (`building`, `reviewing`).

**`shopping_plan_cards`** (at most 60 per session)

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `session_id`, `owner_user_id` | uuid | Cascade on session delete |
| `source` | text | `saved`, `idea`, `ai` |
| `recipe_id` | uuid null | Set for `saved` and after expansion |
| `candidate_json` | jsonb null | Validated `SuggestCandidate`; null for `saved` |
| `snapshot_json` | jsonb | Name, summary, meal type, format, technique, servings, minutes, difficulty, ingredients, guard result |
| `state` | text | `unseen`, `accepted`, `rejected` |
| `reject_reason`, `reject_ingredient_key` | text null | |
| `accept_mode` | text null | `yield`, `again` |
| `repeats` | int | Default 1 |
| `expansion_state` | text | `none`, `pending`, `ready`, `failed` |
| `expansion_key` | uuid | Drives session, selection, and entry idempotency |
| `first_seen_order`, `decided_at` | int, timestamptz null | |

**`menu_entries` additions:** `meals_covered int not null default 1` (1–4) and `suggested_order int null`.

Retention: on acceptance, delete the session's `unseen` and `rejected` cards in the same transaction. On discard, delete all cards. On `POST /shopping-plans`, delete the owner's own `abandoned` sessions older than 30 days. No background job. List this data in `docs/security/privacy-and-data-protection.md`.

### API (Slice 1)

Under `/api/v1/planning/shopping-plans`. CSRF filter and `mutation` limiter as in `PlanningEndpoints`. `batches` and `expansion` use the same rate-limit policy as the existing recipe AI routes. Keep the `Idempotency-Key` header pattern. Regenerate OpenAPI and the client.

| Route | Purpose |
|---|---|
| `GET /start` | Defaults from the last setup, profile people, restrictions, unverifiable restrictions, `hasFreezer`, current session summary |
| `POST /` | Key required. Creates the draft menu and session and inserts instant cards. Returns 422 `plan_in_progress` with `sessionId` when one exists |
| `GET /{id}` | Session, progress, card queue |
| `POST /{id}/batches` | Key required. Body: optional `dishFamilies`, `cuisine`, `time`. Adds up to 6 AI cards |
| `POST /{id}/cards/{cardId}/decision` | Body: `decision`, `mode`, `repeats` |
| `PUT /{id}/cards/{cardId}/reason` | Body: `reason` |
| `POST /{id}/undo` | Undoes `last_decided_card_id`, once |
| `POST /{id}/cards/{cardId}/expansion` | Key required. Expands an accepted card and creates its entries |
| `POST /{id}/review` | Requires no pending expansion, else 422 `expansions_pending`. Computes order; status `reviewing` |
| `POST /{id}/replacements` | Body: `entryId`. Removes that entry; status `building` |
| `POST /{id}/acceptance` | Accepts the menu; status `finished` |
| `POST /{id}/abandon` | Archives the menu; status `abandoned` |

Every mutation returns `ShoppingPlanResponse`: `sessionId`, `menuId`, `status`, `setup`, `progress { mealsTotal, mealsFilled, listItemCount, sessionMinutesUsed, sessionMinutesBudget }`, `queue` (next five unseen cards, with per-card `newItemCount`, `reusedNames`, `reasonCode`, `overBudget`, `guardNote`), `accepted`, `needsBatch`, `offerMoreOptions`, `exhausted`.

### AI operation (Slice 1)

Register `recipe.suggest_plan_deck.v1`:

```csharp
new AiOperationDefinition(SuggestPlanDeck, "recipe-suggest-plan-deck.v1", "0.3",
    PreferNonThinking: true, TimeoutSeconds: 60, MaxRepairAttempts: 1, EstimatedUsageUnits: 6,
    ContextBudgetCharacters: 12_000, MaxOutputTokens: 8192,
    EstimatedCostMicroUsd: 7_000, EstimatedTokens: 26_000)
```

If `AiGatewayHardeningTests` pins the cost or token formula, follow the formula and record the values.

**Why six candidates per call:** the recorded evaluation shows about 10 s for 1,717 output tokens with three candidates. Six keeps the wait near 20 s, and later batches arrive in the background.

**Response schema:** `packages/contracts/ai/recipe/schemas/recipe-plan-deck.response.v0.3.json`. Closed: `operation`, `schemaVersion: "0.3"`, `candidates` with exactly 6 items of the **same candidate shape** as `recipe-suggest-candidates`. Reusing the shape means expansion, `ToSelectedCandidateContext`, library capture, and `RecipeRevision` work unchanged. Add a C# `PlanDeckResponse` in lock-step, mirrored rules in `validate-core.mjs`, and positive and negative fixtures.

**Semantics:** extract the per-candidate checks from `RecipeSuggestSemantics.Validate` into a shared method, so `recipe.suggest_candidates.v1` behavior stays identical. Add `ValidatePlanDeck`: exactly 6; strategies limited to `on_hand_first`, `planned_purchase_reuse`, `exploratory` (repeats allowed); unique fingerprints; `targetMealType` among requested meal types; `totalMinutes` within `maxTotalMinutes` when present. Add a language check through `RecipeOutputQuality`.

**Request envelope:** add `RecipeAiRequestEnvelopes.BuildPlanDeckRequest` and a repair builder, reusing the policy, presets, restrictions, availability, and equipment blocks. `executionContext.executionMode` stays `menu_planning`. Add a `planDeck` object:

- `mealNeeds`, for example `{ "main": 5, "breakfast": 2 }`; `days`; `cookingStyle`; `priority`; `maxActiveMinutes`.
- `chosenDishes`, at most 20 names; `shoppingListIngredients`, at most 40 names; `rejectedDishes`, at most 30 names; `avoidIngredients`, at most 10 names.
- `anchorIngredient`, cleaned by `UntrustedText.Normalize(text, 60)` and described with the same untrusted-data treatment as `hint`.
- `rule`: "Return six dishes a home cook would recognize. Reuse perishable ingredients from shoppingListIngredients and from each other, so little is left over, and make dishes differ in format, technique, and flavor. Rice, chicken, and tomato may repeat if the dishes do not feel the same. Never use avoidIngredients. userPresets.restrictions always win."

Chips reuse `suggestionGuidance`. Do not use a `guidance` object with `requiredLotIds`.

### New services

**Recipes module, `RecipePlanDeckService`:**

- `GenerateBatchAsync(PlanDeckRequest, key, correlationId)`: reserves usage, invokes with repair, and returns validated candidates or the existing failure codes. Uses `RecipeContextAssembler` with a `MenuPlanningTarget`, so only free stock reaches the model.
- `ExpandCardAsync(ownerId, candidate, expansionKey, hint, language)`: claims a `RecipeGenerationSession` with mode `menu_planning` and the hint so `PrivateDirection` is preserved, attaches a snapshot with this **one** candidate, then calls `RecipeCookNowApplicationService.SelectCandidateAsync`.

**Planning module, `ShoppingPlanApplicationService`,** plus pure static classes `ShoppingPlanRanker`, `ShoppingPlanProjection`, and `ShoppingPlanSequencer`. Add a Planning → Profiles project reference to read people, restrictions, and the freezer through `IProfileReadStore`.

**Ingredients module:** `RestrictionGuard` with `Catalog/restriction-rules.json`; `IngredientPerishability` with the rank table below.

### Card sources and order of arrival

1. `POST /` inserts saved recipes first: favorites, then most recently cooked, at most 12, read through `IRecipeLibraryStore` with `RecipeIngredientReader`.
2. Then at most 6 library ideas through a new `RecipeIdeaLibrary.FindPlanDeckAsync`. It uses the same safety rule, language, level, and restriction filters. It does not require pantry coverage, and it permits zero `inventoryUses`. Do not change `TrySuggestAsync` or `Rebuild`; add a `RebuildForPlan`.
3. `needsBatch` is true when fewer than 3 unseen cards remain and `batch_count < 5`. The browser then calls `POST /batches`. With 3 or more cards the deck appears at once, and AI cards join in the background.

Deduplicate across all sources by normalized name and by `dishFormat|primaryTechnique|primaryIngredientRefs`, and against accepted dishes. Saved cards take priority over the same dish from other sources.

### Restriction guard (food-safety critical)

`RestrictionGuard.Check(ingredientNames, keys, restrictionCodes)` returns `Allowed`, `Blocked(code, ingredient)`, or `Unverified(code, ingredient)`.

- `Catalog/restriction-rules.json` maps each curated `DietaryPattern`, `Allergy`, `Intolerance`, and `MedicalRestriction` code in `profileCatalogCodes.ts` to forbidden catalog categories and slugs, and to forbidden name keywords in pt, en, and es. For example, `vegetarian` forbids categories meat, poultry, fish, and seafood, and `lactose_intolerance` forbids category dairy and keywords such as "leite", "queijo", "manteiga", "creme de leite", "iogurte", "milk", "cheese", "butter", "cream", "yogurt", "leche", "queso", "mantequilla", "crema", and "yogur".
- A name resolving to a catalog item is checked by category and slug. Every name is also checked by keyword, because "molho de queijo" may not resolve. Word-boundary, accent-insensitive matching using `IngredientText.Normalize`.
- A rule may list exceptions such as "leite de coco" and "manteiga de cacau".
- An unresolved name with no keyword match, under an `Allergy`, `Intolerance`, or `MedicalRestriction` code, returns `Unverified`.
- A custom restriction code without a rule returns `Unverified` for every card, and the setup screen shows the note.
- `Dislike` codes never block. They apply a −200 rank penalty through the same slug and keyword data.

| Source | Blocked | Unverified |
|---|---|---|
| AI, idea | Dropped, metric `restriction_blocked` | Shown with `guardNote`: "Confira: não consegui verificar {ingredient} para {restriction}" |
| Saved | Hidden from this deck | Shown with `guardNote` |
| After expansion | Check the full recipe again. If blocked, set `expansion_state = failed` with reason `restriction_blocked` and create no entry | Keep, and show the note on review |

**Owner gate G1-A:** the owner must approve `docs/domain/restriction-rules-review.md`, which shows the table in plain language, before Slice 1 merges. Copy must say the check helps but does not replace reading labels.

### Ranking

`ShoppingPlanProjection` and `ShoppingPlanRanker` are pure, deterministic, and read-only.

**Inputs for each card:** ingredient keys from `IngredientMatchKeys.Key(name) + "|" + unit`, excluding `HouseholdBasics`, optional items, assumptions, and `assumptionsUsed`. Quantities scale with `RecipeIngredientReader.Scale` to plan servings.

**Definitions:**

- `Chosen`: ingredients of accepted cards, summing scaled quantities and allocating free stock in accepted order, as `ShoppingProjector` does.
- `listItemCount`: keys with positive remaining need. Includes optional only when required elsewhere.
- `newItems(c)`: keys of `c` with need left to buy that are not already on the list.
- `reused(c)`: keys of `c` on the list or covered by free stock.
- `newPerishable(c)`: subset of `newItems` whose catalog category is perishable. Unresolved names count as non-perishable.
- `rescued(c)`: perishable keys of `c` currently used by exactly one accepted card.
- `attention(c)`: keys of `c` matching lots with attention `use soon`, `use with priority`, or `check condition`.

**Score:** integer arithmetic, highest first; ties by `first_seen_order`.

```text
score = 100 × attention
      + goal
      + diversity
      + feedback
      + budget
goal:
  waste  = 40 × rescued − 30 × newPerishable − 5 × newNonPerishable + 5 × reused
  short  = −30 × newItems + 5 × reused
  save   = 10 × reused − 10 × newItems − 25 × newCostly
           (newCostly: new items in categories meat, poultry, fish, seafood, nut)
  time   = −activeMinutes − (totalMinutes − activeMinutes) / 4 − 2 × newItems
diversity (against accepted cards):
  −40 if the same dishFormat appears; −25 if the same primaryTechnique appears;
  −60 if its first primary ingredient already leads two or more accepted cards
feedback:
  −10 for the format of each card rejected without a reason
  dislike:        −80 for cards sharing the rejected card's first primary ingredient
  too_much_work:  −60 for cards with activeMinutes ≥ the rejected card's activeMinutes
  tired_of:       −150 for cards using reject_ingredient_key
  profile Dislike match: −200
budget:
  −200 and overBudget = true when activeMinutes > remaining session minutes
```

**Meal needs:** lunch and dinner form one `main` group; `breakfast` and `snack` are separate. Cards of a group with no need left stay out of the queue. Each accepted card fills `repeats` meals in its group. `mealsFilled` is the total.

`reusedNames` shows the display names of at most 3 reused keys. `reasonCode` is `uses_attention_item` when `attention > 0`, else `reuses_list_items` when `reused ≥ 2`, else null.

### Accepting a card

Decision `accept` is atomic: `UPDATE … WHERE state = 'unseen'`. Repeating it is a no-op.

| Mode | Entries |
|---|---|
| Plain "Sim" | One entry: `servings = people`, `meals_covered = 1` |
| `yield`, N | One entry: `servings = people × N`, `meals_covered = N` |
| `again`, N | N entries: `servings = people`, `meals_covered = 1` |

Reject when `people × N > 24` with `validation_failed`; the sheet already limits this.

- **Saved cards:** create entries immediately, with `CreationKey` = UUIDv5(`expansion_key`, repeat index). Implement one deterministic helper; do not use random keys here.
- **`idea` and `ai` cards:** set `expansion_state = pending`. Entries are created by the expansion route.
- **Entry fields:** `MealType` = the card's target meal type. `PlannedDate` = the plan's preparation date when mode is `yield` and style is `once` or `mix`; otherwise null.

### Expansion from the browser

No server worker. `ICurrentUserAccessor` needs the request, and the app has no queue (PLAN-0039 not started).

`useExpansionQueue` in the deck:

- Waits 5 s after acceptance, the undo window, then expands one card at a time.
- On `ai_operation_conflict`, retries after 3 s, 6 s, and 12 s, then marks failed.
- On `ai_budget_exhausted` or `ai_capability_unavailable`, stops and shows the failure.
- Reruns when the session resumes with any `pending` card. On resume, pending cards skip the undo window.
- Keeps `batches` and expansion to at most 2 in flight together, matching `UserConcurrencyCeiling: 2`.

Server route:

1. If `recipe_id` is set, skip to step 3.
2. Call `RecipePlanDeckService.ExpandCardAsync`. A generation session expires after 2 hours; on `precondition_failed`, derive a new key from `expansion_key` with an attempt number, stored in the card snapshot, and retry once.
3. Run `RestrictionGuard` on the full recipe.
4. Create entries through the idempotent path from "Accepting a card". The expanded recipe's real ingredients now drive the real shopping list.

If the session was undone or replaced in the meantime, keep the recipe and create no entry.

### Sequencing

`ShoppingPlanSequencer.Order` sets `suggested_order` on `POST /review` and after edits while the status is `building` or `reviewing`. Never after acceptance.

Rank by `IngredientPerishability`:

| Rank | Items |
|---|---|
| 1 | fish, seafood, `ground-beef`, `ground-pork`, `ground-chicken` |
| 2 | poultry, meat |
| 3 | herb |
| 4 | vegetable, fruit, dairy, bakery |
| 5 | egg |
| 9 | everything else |

Each entry's rank is the lowest rank of its required ingredients that are not fully covered by lots already in the freezer. Sort by rank, then by attention (entries using attention items first), then by total minutes, then by `CreatedAt`. Then move a repeated recipe away from an adjacent copy when another entry exists. This table sets an order, not a safety statement, so it does not need the Slice 2 flag.

**Changes to existing code:**

- `ResolveEntriesAsync`: add `.ThenBy(item => item.SuggestedOrder ?? int.MaxValue)` before `CreatedAt`, so shopping and reservation allocation follow the suggested order.
- `GetTodayAsync`: for an entry with `PlannedDate == null` and `SuggestedOrder != null`, show only the lowest-order entry still planned in each plan, plus any in `cooking`. Entries without `SuggestedOrder` keep today's behavior. Without this, "Hoje" would list every flexible meal.
- `MenuEntryView` and the API add `MealsCovered` and `SuggestedOrder`.
- The menu page labels a flexible entry "Próximo" or by position, and `meals_covered > 1` as "Rende N refeições".

**Period:** `StartDate` = the preparation date when set, else the owner's today from the browser. `EndDate` = `StartDate + days − 1`. The browser sends its local date (`localIsoDate`), never server time.

## Execution phases

### Plan publication

- [ ] Fetch `origin`, branch `agent/plan-0062-guided-shopping-planning` from `origin/main`.
- [ ] Add this file as `docs/plans/PLAN-0062-guided-shopping-planning.md`, add the registry row, and link it from `docs/roadmap/initial-release-delivery-map.md`.
- [ ] Commit, push, open a draft PR, and request review from `RodrigoWantuk` when the author differs.

**Exit criteria:** the plan is on the branch, and the registry names Slice 1 as the next action.

### Slice 1: From zero to list

Branch: `agent/plan-0062-s1-plan-to-list`.

**Backend**

- [ ] Migration, records, and store: extend `IMenuPlanStore` or add `IShoppingPlanStore`, implemented in `Cocinaris.Infrastructure/Persistence`.
- [ ] `IngredientPerishability`, `RestrictionGuard`, and `restriction-rules.json`.
- [ ] Schema, C# protocol, semantics refactor, `validate-core.mjs` mirror, fixtures, operation registration.
- [ ] `RecipePlanDeckService`, `RecipeIdeaLibrary.FindPlanDeckAsync` and `RebuildForPlan`.
- [ ] `ShoppingPlanApplicationService`, ranker, projection, sequencer, endpoints, metrics.
- [ ] `GetTodayAsync`, `ResolveEntriesAsync`, and `MenuEntry` changes.

**Frontend**

- [ ] New folder `features/shopping-plan`: `ShoppingPlanSetupPage`, `ShoppingPlanDeckPage`, `ShoppingPlanReviewPage`, `useExpansionQueue`. Add routes under `/app/compras/planejar`.
- [ ] New catalog `shoppingPlanCatalog.ts` for pt-BR, en, es, composed like the others, with a catalog completeness test.
- [ ] Types in `contracts/planning.ts` and methods in `livePlanningRepository.ts`.
- [ ] **"Já tenho"** on to-buy rows in `ShoppingPage`: `inventory.createLot` with `productName = displayName`, the line's unit, `toBuyQuantity`, the estimated flag, and storage from the existing section default. Reload the list. A notice offers "Desfazer" for 8 s, which deletes the lot with its returned version.
- [ ] **Share:** a pure `buildShoppingListText(items, t, locale)` with to-buy lines only, grouped by section. `navigator.share` when available, else a sheet with "WhatsApp" (`https://wa.me/?text=`) and "Copiar".
- [ ] Entry points: `ShoppingHubPage` empty state (primary), `ShoppingPage` header, `MenusPage` (secondary), `QuickActionMenu`. Show "Continuar plano" when a session is in progress.
- [ ] Deck accessibility: focus moves to the new card title; `role="status"` announces "Refeição N de M, lista com X itens"; reduced motion respected; no swipe-only action.

**Hazards**

- `TryClaimSelectionAsync` allows one selection per generation session, and a 3-candidate rule exists only in validation of new output. That is why each card gets its own single-candidate session. Do not reuse one session for the deck.
- `RecipeSuggestSemantics.Validate` enforces exactly 3 and unique strategies. Do not loosen it for the existing operation; extract shared per-candidate checks.
- `RecipeIdeaLibrary.Rebuild` returns null for zero pantry uses and more than 5 additional ingredients, and `TrySuggestAsync` returns null for an empty pantry. Write the plan variant separately.
- An anchor must flow as the session hint so `PrivateDirection` blocks library capture.
- The AI `additionalIngredients` limit is 12 per candidate. The shopping list comes from the expanded recipe, so card and list counts may differ slightly. Show the card count as the estimate and never present it as final.
- `ShoppingProjector.Key` includes the unit, so "frango g" and "frango unit" are separate lines. Reproduce this in the projection instead of merging them.
- `ConservativeToday()` is only for reservations. Use the browser's date for periods and `from`.
- The `mutation` rate limiter may be too tight for quick decisions. Read its configuration in `Program.cs`. If a person tapping once per second would hit it, add a dedicated `shopping-plan-decision` policy of 120 per minute.
- A generated EF migration can lose its UTF-8 BOM after editing, and the format gate rejects it. Run `dotnet format` on it (PLAN-0037 log).
- Keep the new draft out of `pickShoppingMenu`'s way: an accepted menu is picked first, so "Ver lista" links directly to `/app/cardapio/:menuId/compras`.

**Tests**

- Unit: ranker for each goal, each penalty, ties, and determinism across runs; projection with pantry allocation, `HouseholdBasics`, and units; sequencer; meal needs; every entry mode with idempotent keys; guard outcomes, exceptions, keywords in three languages, and accent handling.
- Contract: schema fixtures, including 5 and 7 candidates, an unknown field, a duplicate fingerprint, and a wrong meal type. Run the existing suggest fixtures unchanged.
- Integration: full journey with a stub provider; another owner's session returns 404; one in-progress session per owner; retry of accept and expansion creates no duplicates; no reservation before acceptance; acceptance reserves; "Hoje" shows one flexible entry.
- Frontend: setup defaults and limits; deck actions and undo; failure and retry in the queue; "Já tenho" and undo; share text; catalog completeness.
- Browser: new `scripts/e2e/shopping-plan-journey.mjs`, with a stub or AI-off path using saved recipes: setup, accept, "Sim, 2 vezes", list, "Já tenho", accept plan, "Hoje".

**AI evaluation (Gate G1-B)**

Extend the gateway smoke in `scripts/ai` with `PLAN0062_LIVE_EVAL=1` and a ceiling of US$ 0.05. Use three synthetic scenarios: empty pantry with vegetarian restriction; partial pantry with lactose intolerance and the `short` goal; and an anchor containing an injection attempt. Record in `docs/evidence/plan-0062/`: valid output rate, latency, cost, ingredient overlap, guard blocks, and `markerLeaked`. Never print keys. Without credentials, stop and report it as a blocker.

**Documentation**

- `docs/product/guided-shopping-planning.md` (new): this functional specification and the owner decisions.
- `docs/domain/planning-and-shopping.md`: remove "Planning several slots in one request" from "Not yet"; add sessions, cards, modes, order, and the "Hoje" rule.
- `docs/ai/recipe-artifact-protocol.md`, `docs/ai/overview.md`, `docs/ai/usage-and-cost-governance.md`: the new operation.
- `docs/product/user-journeys.md`, `docs/security/privacy-and-data-protection.md`, `docs/testing/product-foundation-gates.md`, `packages/contracts/ai/recipe/README.md`.
- XML docs on all new public members; TSDoc on exported hooks and functions.

**Gate G1 (all required before requesting merge)**

| ID | Check | Evidence |
|---|---|---|
| G1-A | **Owner gate:** restriction rules approved | Owner comment recorded in the PR |
| G1-B | Live AI evaluation: at least 90% valid after repair, no restricted ingredient reaching a visible card, `markerLeaked=false` | `docs/evidence/plan-0062/` |
| G1-C | First card visible under 1 s when 3+ instant cards exist; first AI batch p95 under 45 s | Evaluation and browser journey |
| G1-D | Decision round trip under 300 ms locally with 60 cards | Integration test timing or a logged measure |
| G1-E | With AI off, the flow finishes with saved recipes | Browser journey |
| G1-F | `scripts/backend/export-openapi.sh`, `lint-openapi.sh`, `scripts/contracts/sync-api-client-to-frontend.mjs`, `check-api-client-drift.mjs`, and `npm test` plus `npm run check:drift` in `packages/contracts/ai/recipe` pass | PR body |
| G1-G | One `scripts/ci/run-local-gates.sh --ref <head>` run passes | Summary in the PR |
| G1-H | Manual check at 320 px and with keyboard only | PR body |

**Exit:** owner merges. Deploy `main` follows the runbook.

### Slice 2: Market and arrival

Branch: `agent/plan-0062-s2-arrival`.

**Storage guidance**

- `Catalog/storage-guidance.json` by catalog category, with slug overrides: `{ "location": "Pantry|Refrigerator|Freezer", "fridgeDays": n|null, "freezable": bool, "freezerDays": n|null, "source": "..." }`. Keep values conservative. Cite sources such as the USDA FoodKeeper in `docs/domain/storage-guidance-review.md`, verifying each one while writing it. Cover raw and cooked state for meat, poultry, and fish.
- `StorageGuidance` in the Ingredients module, behind `Planning:StorageGuidance:Enabled`, default `false`.
- With the flag on, `InventoryAttentionAdvisor` adds a rule after printed date and before "no date": lot creation date + `fridgeDays` with evidence "curated rule" for refrigerated lots with no date. This completes the second part of PLAN-0037 Phase 3. Update that plan.
- **Owner gate G2-A:** the owner reviews `storage-guidance-review.md`. The slice can merge with the flag off. Turn it on only after written approval, as a configuration change recorded in the runbook.

With the flag off, the arrival screen still works: location comes from the store section (the current `COLD_SECTIONS` rule, moved to the backend), there are no freezer suggestions, and nothing shows a date.

**Line marks**

Migration `AddShoppingLineMarks`: table `shopping_line_marks` with (`menu_plan_id`, `item_key`, `state` null or `later`, `substitute_name` null, `place` null, `updated_at`). `PUT /menus/{id}/shopping/marks`.

- **"Não achei este item"** on a to-buy line opens a sheet naming the affected dishes (`uses`):
  - "Trocar ingrediente": a name field, run through `RestrictionGuard`. Blocked names are refused with the reason. The projector replaces the line's display name and match key with the substitute, and keeps the quantity. The recipe does not change. The cooking page's existing "Mudou alguma coisa?" handles it at cook time; show a hint saying so.
  - "Comprar depois": the line moves to a "Comprar depois" block and stops counting as pending.
  - "Trocar prato": goes to `POST /replacements` on a draft. On an accepted menu, links to the menu until Slice 4.
- **Split by store:** a toggle on the list. `place` is a closed code: `market`, `street_market`, `butcher`, `bakery`. The default comes from the section: produce → `street_market`, butcher_fish → `butcher`, bakery → `bakery`, others → `market`. A person can move a line. Grouping changes only when the toggle is on. The shared text follows it.

**Arrival (`/app/cardapio/:menuId/chegada`)**

Pure `ArrivalAdvisor` in Planning. Input: to-buy lines, uses in plan order, plan start, guidance, confirmed sizes.

Lines come from checked to-buy lines, or all to-buy lines when none are checked, with a filter to include others.

```text
bought     = packageCount × packageSize if known, else toBuyQuantity (editable)
expectedDay(use) = its PlannedDate, else start + floor(position × days / meals)
For each use in order, with guidance:
  days until use ≤ fridgeDays             → refrigerator portion
  else freezable                          → freezer portion
  else                                    → refrigerator portion, note "use primeiro"
surplus = bought − sum(uses)
  perishable and freezable                → freezer portions:
      size  = largest use of this item, or surplus when there is none
      count = ceil(surplus / size), maximum 6
  otherwise                               → stays with the main location
Same location, same use day group → one portion. Non-perishable → one lot in its location.
```

For unit lines, quantities stay whole. If the arithmetic gives a fraction, use one group.

Endpoints:

- `GET /menus/{id}/arrival?from=` returns lines with `boughtQuantity`, `needsRealQuantity`, `portions[]`, `containerCount`, and labels.
- `POST /menus/{id}/arrival/preview` is read-only and recomputes for edited quantities.
- `POST /menus/{id}/arrival`, key required, records everything.

`needsRealQuantity` is true when no size is confirmed and the item is perishable. The field is prefilled; the person may ignore it.

**Recording ("Guardar tudo")**

- One lot per portion, through the Inventory application service, with idempotency key UUIDv5(request key, item key, portion index). Bound: 120 lots per request.
- A freezer portion: create in the refrigerator, then `Freeze` the whole lot, so the state is `Frozen` with `FrozenAt`. Creating directly in the freezer would leave `AsAcquired` (domain doc).
- `Notes` = "Para: {dish}" when a portion has one use.
- Not one transaction across up to 120 lots. The response lists `recorded` and `failed` items, and a retry with the same key replays completed ones. This matches the plan's "no double count" rule.
- "Esse é o tamanho do pacote": an unchecked box appearing only when the amount differs from need and no size exists. It calls the existing package-size route.

**Screen:** groups Despensa, Geladeira, Freezer; a line per portion ("3 × 330 g"), location changeable by select; label text "Frango · 330 g · 04/11 · Para: Escondidinho"; summary "Separe 6 potes ou sacos"; primary "Guardar tudo"; a later section "Dá para adiantar agora" lists raw portioning already covered, and, from Slice 5, components.

Entry points: a banner on the list when anything is checked, and "Prepare-se" on "Hoje" for a plan with checked lines. On the list, the short hint "dividir em N porções" appears when the advisor gives more than one portion.

**Hazards**

- Lot names and shopping keys: create with `displayName`, as "Comprei" does, so lists recalculate correctly.
- A reservation is derived, so new lots are reserved automatically on the next read. Do not write reservations.
- Thaw reminders already fire for reserved lots in the freezer; check they appear per portion.
- Never let an AI recipe's `storage` values drive the advisor.
- Substitutes change the shopping key. Add tests that a check mark and package size follow the substitute's key, not the original's.

**Gate G2**

| ID | Check |
|---|---|
| G2-A | **Owner gate:** storage table reviewed before enabling the flag; flag off is acceptable for merge |
| G2-B | Advisor unit tests: use split, surplus, whole units, no guidance, no freezer in profile, flag off |
| G2-C | Integration: retry of "Guardar tudo" creates no duplicates; partial failure reports items; freezer portions are `Frozen`; list recalculates; owner isolation |
| G2-D | `RestrictionGuard` refuses a blocked substitute |
| G2-E | Journey extended: purchase, arrival, pantry shows lots, thaw reminder present |
| G2-F | OpenAPI and client current; one local gate run on the head |

Docs: `planning-and-shopping.md`, `inventory-lifecycle.md`, `operations/linux-host-deployment-runbook.md` (flag), product spec.

### Slice 3: Offline list

Branch: `agent/plan-0062-s3-offline-list`. Write **ADR-0012** first.

**Decisions**

- `vite-plugin-pwa`, exact pinned version, `generateSW`, precache built assets only, `navigateFallback` to the index, denylist `/api` and auth routes, `autoUpdate`. Registered only in the production build. The prototype build has no worker.
- **The service worker never caches API responses.** List data lives in IndexedDB through app code, namespaced by owner id, in a small `offlineListStore` using `idb-keyval` (exact pin). Cleared at logout, and when another owner signs in.
- Stored: the last list per menu, and an outbox of check marks and `later` marks.
- Offline, only check marks and `later` work. "Comprei", "Já tenho", arrival, and substitutes are disabled with "Precisa de internet".
- On `online` and on app open, flush the outbox in order, with a fresh CSRF token. Then reload the list.
- **Conflicts:** the person's own check marks use last write wins, with no prompt. An item key no longer in the list is a conflict: show "N marcações não se aplicam mais", with the names and a dismiss.
- A 401 during flush keeps the outbox for the same owner after sign-in.

**Session bootstrap change:** a **network** failure on session load, with a cached owner id, renders an offline shell allowing only `/app/compras` and `/app/cardapio/:id/compras`, with the banner "Sem internet. Suas marcações serão enviadas depois." Other routes show the same banner. A 401 still goes to sign-in. The cached id is a display hint, never authorization.

**Hazards:** an old worker serving stale assets after deploy; verify `autoUpdate` and that `/api` never reaches the cache. Check `infrastructure/deploy/linux-host/Caddyfile.cocinaris` for CSP; if one is active, add `worker-src 'self'`. `guard:third-party-html` and bundle inspection must stay green. A shared device must not show another owner's list.

**Gate G3:** Playwright `context.setOffline(true)` journey: open the installed shell, mark lines, go online, marks persist on the server. Logout clears storage (asserted). `/api` absent from cache storage (asserted). Second owner sees nothing. One local gate run on the head. Docs: ADR-0012, `docs/operations` runbook note on cache clearing and rollback (unregister worker), product spec.

### Slice 4: Limits, warnings, and repeat

Branch: `agent/plan-0062-s4-limits-and-repeat`. Requires G2-A approved; if not approved, this slice ships repeat only and records the limits work as blocked.

**Cook-by limit:** for each planned entry, `CookBy` = the earliest use-by estimate among its reserved lots that are not in the freezer (from attention). Before purchase, review shows `start + min(fridgeDays)` of its perishable ingredients, labeled "se comprar hoje". No rule, no date.

**Warning:** add reminder kind `cook_by` in `PreparationReminders`, including flexible entries with `SuggestedOrder`. Due the day before `CookBy` and on it. The "Prepare-se" row offers:

- **"Congelar hoje"**, only when guidance says `freezable`: partial `Freeze` transition of the reserved quantity per lot, using the lot's current version.
- **"Trocar de prato"**: extends `POST /replacements` for accepted menus. It removes the entry and opens cards for one meal. The new entry is added without changing other entries' order or dates.
- **"Deixar como está"**: table `reminder_dismissals` (`owner`, `entry_id`, `kind`, `lot_id`, `dismissed_on`). It suppresses this reminder until `CookBy` changes.

Reminders remain derived and disappear when the cause does.

**Repeat or save:** `/start` adds the last 5 finished plans. `POST /` accepts `fromMenuId`: copies the setup, creates `saved` cards with `state = accepted` and entries from that menu's recipes (skipping deleted ones), then opens the deck with the remaining meals. Saving means naming the menu through the existing `PUT`; finished plans stay available.

**Hazards:** partial freeze needs the `If-Match` version and may conflict; on conflict reload and show the row again. `From` boundaries around midnight use the browser date. Do not re-sequence accepted plans.

**Gate G4:** unit tests for `CookBy`, due dates, dismissal, no-rule case, and flag off; integration for partial freeze with reservations, replacing an accepted entry, and repeat with a deleted recipe; journey extended; one local gate run. Docs updated.

### Slice 5: Components and frozen dishes

Branch: `agent/plan-0062-s5-components`.

**AI:** register `recipe.suggest_plan_deck.v2` with schema `recipe-plan-deck.response.v0.4.json`: adds `components`, 0–2 items: `{ candidate: <candidate shape>, output: { name, quantity, unit }, usedBy: [candidateId] }`. Requested only for styles `once` and `mix`; `daily` keeps v1 behavior through an empty `components`. Validation: `usedBy` ids exist; `output.name`, normalized, equals an `additionalIngredients` name in each `usedBy` candidate; output quantity ≥ the sum of those uses. An invalid component is dropped by the service instead of failing the batch. Run a new evaluation (G5-A).

**Model:** `menu_entries` adds `kind` (`meal` default, `component`), `produces_name`, `produces_quantity`, `produces_unit`. Components do not count toward `mealsFilled`.

**Cards:** after accepting a dish that uses a component, the next card is that base: "Base sugerida: Frango desfiado, 1 kg. Serve para: Wrap, Escondidinho." "Não" keeps the item on the list as a required line. It may be bought ready-made.

**Shopping projection:** `ShoppingMeal` gains an optional production. Components sort first. After processing a component, add its output to `remaining[key]` as planned production. Lines satisfied by it get the new category `from_prep`, shown in the owned block as "Vem do pré-preparo". Once the component is cooked, real lots replace the planned production because cooked entries leave the projection. The sequencer puts components first; their date is the preparation date.

**Completing with outputs:** extend the execution completion command with optional `storedOutputs`, up to 4: `{ quantity, unit, storageLocation, useByDate? }`.

- Each becomes a lot in the same preparation batch and transaction.
- For a component, the product name is `produces_name`, and the unit is measured.
- For a dish, the unit is portions, and the total may not exceed the portions cooked.
- The old single-leftover field keeps working.

The finished screen proposes outputs: for a `meals_covered > 1` entry with `FreezeCompatible`, split by guidance and expected day; for a component, measured portions from its uses. Without guidance, it proposes refrigerator only and lets the person choose.

**Ready portions:** reminder kind `ready_portions` for lots created by such entries until consumed, with "Comer uma porção": the existing consume adjustment of 1 unit.

**Hazards:** `InventoryAttentionAdvisor`'s freezer rule may read `FrozenAt`, which is null for a `Prepared` lot stored in the freezer. Check it, use `PreparedAt` as fallback, and add a test. The unique index allows one reconciliation per execution; keep all outputs in that one batch. A component output name that the catalog later resolves changes the key on both sides equally only if both pass through `IngredientMatchKeys`; test it. Do not lose atomicity of completion and inventory.

**Gate G5:** G5-A live evaluation for v2, with at least 80% of batches returning a valid component when requested, and no restricted ingredient. Unit: projection with planned production, dropped invalid components, output limits. Integration: atomic completion with multiple outputs and rollback on a stale lot; component lot matches later dishes. Journey extended. One local gate run. Docs: `recipes-and-cooking.md`, `closed-loop-kitchen-orchestration.md`, protocol.

### Slice 6: Session route

Branch: `agent/plan-0062-s6-session-route`. Update PLAN-0044's registry row to say this slice covers the session route.

**Scheduler:** pure `PrepRouteScheduler`. Input: entries dated on the preparation date, with components first. Tasks: each recipe's `preparations` and `stages`, with `activeMinutes`, `passiveMinutes`, and `dependsOn`. Stages depend on that recipe's preparations; a dish's stages depend on the completion of a component it uses.

```text
One cook. time = 0. Repeat until no task remains:
  ready = tasks whose dependencies have finished by `time`
  choose the ready task with the longest remaining path
        (ties: recipe order, then stage order)
  it starts at `time`; the cook is busy until time + activeMinutes
  its passive part runs in the background; it finishes at start + active + passive
  time = cook's free moment; if nothing is ready, advance to the next finish
A task with activeMinutes = 0 starts without using the cook.
On a dependency cycle, fall back to running the recipes one after another and log a metric.
```

Output per task: `startMinute`, `activeEndMinute`, `endMinute`, `recipeId`, stage reference. Totals: `totalMinutes`, `budgetMinutes`, `overBudget`. **Limitation to state in the UI:** stages have no per-stage equipment, so burner or oven conflicts are not detected: "Confira se dá para usar as bocas e o forno ao mesmo tempo."

**Screen (`/app/compras/planejar/:sessionId/preparo`):** one task per screen with large text, using `CookingGuide` patterns: wake lock, timers for passive parts, "Enquanto isso" with the next task. Progress in `route_progress_json` on the session via `PUT`. A person may skip a task. Starting a recipe's first task starts its execution from the menu entry. At the end, the finished screen from Slice 5 runs for each recipe in turn.

**Gate G6:** scheduler unit tests: parallel passive work, dependencies across component and dish, cycles, determinism, over budget. Browser journey completes a route with two recipes and resumes after reload. Manual check on a phone with wake lock. One local gate run. Docs: `closed-loop-kitchen-orchestration.md` and the spec.

### Slice 7: Web push

Branch: `agent/plan-0062-s7-web-push`. Write **ADR-0013** first. Update PLAN-0048's registry row: web push delivered here, e-mail and the attention dashboard stay there.

**Decisions**

- Web Push with VAPID, using the service worker from Slice 3. VAPID keys come from environment variables documented in the runbook. **Never commit them.** If they are not configured, the feature hides itself.
- Opt-in only, from Settings and after the first accepted plan, with an explicit explanation. Never ask for permission on page load.
- Table `push_subscriptions` (`owner`, `endpoint`, `keys`, `created_at`, `last_success_at`). HTTP 404 or 410 from the push service deletes the row. Export and deletion treat this as personal data.
- `PlanReminderPushService`, a hosted service running hourly. It sends at most one notification per owner per day, at 09:00 in the profile time zone (no profile time zone: no push). It reads the same derived reminders of kinds `cook_by`, `thaw`, and `buy`.
- **Payload contains no food, health, or restriction data:** "Você tem algo do seu plano para resolver hoje" with a link to `/app/hoje`, localized by the profile language. Record `last_sent_on` per owner so a restart does not send twice.
- iPhone delivery needs the installed app. State this in Settings.

**Hazards:** the hosted service has no current user, so read through store ports with an explicit owner id, not `ICurrentUserAccessor`. The service must not fail on one bad subscription. Keep the push library pinned and check it with the vulnerability gate.

**Gate G7:** unit tests for schedule, time zones, one per day, and no eligible reminder; integration for subscribe, unsubscribe, owner isolation, and 410 cleanup; a manual push on a real installed phone, recorded in the PR; secrets scan passes; one local gate run. Docs: ADR-0013, runbook, privacy doc.

## Testing and validation plan

Each slice lists its tests and gate. Across slices: authorization and cross-user isolation for every new route; idempotency for every retryable mutation; pt-BR, en, and es catalog completeness; keyboard, 320 px, and 200% zoom on every new screen; reduced motion. Run deterministic tests once, and flaky suspects at most three times. After all slices, run the external test script with three people (PLAN-0054 decision 8) and record whether they keep a second shopping list.

## Cross-cutting impact

### Security and privacy

Owner-scoped tables and 404 on foreign identifiers. Anchor text and substitute names are untrusted, bounded, and never logged. Card snapshots hold pantry names, so they are purged on acceptance and discard. Offline storage is namespaced and cleared at logout. Push payloads are generic. Export and deletion must include new tables when PLAN-0049 is delivered; until then, record them in the privacy document's data inventory.

### Food safety

`RestrictionGuard` adds the deterministic check that was missing from AI suggestions. It applies only inside this module in this plan. Storage and shelf-life output comes only from the owner-reviewed table and stays advisory. All guidance text says to check the food and read labels. Thawed-food refreezing advice is out of scope and stays with attention rules.

### AI behavior and cost

A typical plan costs 2 batches and about 5 expansions, or 37 usage units, under the 200 daily ceiling. The library and saved recipes reduce this. All failures degrade to non-AI cards. Metrics: `shopping_plan_started`, `shopping_plan_accepted`, `shopping_plan_card_decision{decision,reason}`, `shopping_plan_batch{source,outcome}`, `shopping_plan_time_to_first_card`, `restriction_blocked`, `restriction_unverified`. No ingredient or dish text in metric labels or logs. Use `scripts/ops/ai-usage-report.sh` after the first real plans. If the US$ 5 monthly ceiling is reached, the operation fails with the existing code and the flow keeps working without AI. Raising the ceiling is the owner's choice.

### Localization and accessibility

Every string is in catalogs. Quantities use `formatLocaleDecimal`. Reason codes and places are closed codes. Cards work with buttons only, focus moves predictably, and status updates use polite live regions.

### Operations and observability

Migrations are additive, and rollback means deploying the earlier code with unused columns remaining. New flags and secrets: `Planning:StorageGuidance:Enabled` (Slice 2) and the VAPID variables (Slice 7). Document them in `docs/operations/linux-host-deployment-runbook.md`. Deploy `main` only after the owner merges, using `scripts/deploy/deploy-linux-host.sh`.

## Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Waiting for the first AI batch feels slow | Medium | High | Instant saved and library cards; six-candidate batches; skeleton; G1-C |
| Model repeats similar dishes | Medium | Medium | Fingerprint deduplication, diversity penalties, repair |
| Restricted food reaches a card | Low | Critical | Guard before display and after expansion; owner-reviewed rules; evaluation |
| Keyword rules hide too many dishes | Medium | Medium | Exceptions list; metrics; owner review |
| Card estimate differs from final list | Medium | Low | Label as an estimate; the real list comes from recipes |
| A second shopping list remains in use | Medium | Medium | Owner chose this scope; observe in external test |
| Advice in the arrival step is wrong | Low | High | Conservative table, flag, owner gate, advisory wording |
| Service worker serves a stale app | Medium | High | `autoUpdate`, no API caching, rollback note, G3 |
| Scope is large | High | Medium | Seven independent slices; each has value and its own gate |

## Acceptance criteria

- [ ] **S1:** Someone with an empty pantry reaches a usable list from one setup screen and cards. Accepted dishes show live "+N". Goal changes order deterministically. Restrictions are enforced. AI off still works. Nothing reserves before acceptance.
- [ ] **S2:** Arrival records all purchases with one confirmation, portions follow the advisor, freezer portions are frozen, and a retry never double counts. "Não achei" and store grouping work.
- [ ] **S3:** The list opens and takes marks with no signal, then syncs; no other owner's data appears.
- [ ] **S4:** Limits and warnings appear only from curated rules, with the three options. A plan can be repeated.
- [ ] **S5:** A component reduces purchases and is cooked into stored portions atomically. Frozen dishes are proposed and consumed by portion.
- [ ] **S6:** A session route merges recipes with parallel passive work, can be resumed, and completes executions.
- [ ] **S7:** Opt-in push arrives at most once a day with no food data.
- [ ] Every slice: documentation, XML docs, TSDoc, contracts, and gates are current. No unsupported claim remains.

## Execution state

- **Current run delivery target:** Plan publication.
- **Current checkpoint:** Plan approved by the owner and published in a draft PR.
- **Last completed step:** Functional discovery and technical design.
- **Exact next action:** The owner merges the plan PR. Then claim the plan and start Slice 1 from fresh `origin/main` on `agent/plan-0062-s1-plan-to-list`, beginning with the migration and `RestrictionGuard`.
- **Blockers:** None for Slice 1 implementation. G1-A needs the owner's review before merge. Slice 4 limits need G2-A.
- **Partially modified areas:** None.
- **Documentation delivered:** This plan.
- **Validation performed:** Code reading of the affected modules; no code changed.
- **Known failures or limitations:** Per-stage equipment conflicts are not detected (Slice 6). Latency figures come from one recorded evaluation and must be confirmed in G1-C.
- **Working tree state:** Clean after the publication commit.

## Progress log

### 2026-11-04 — Claude Code

- **Run delivery target:** Publish an executable plan.
- **Checkpoint:** Owner discovery rounds finished; decisions recorded; plan expanded with technical detail and delivery gates at the owner's request.
- **Changes included in the commit:** This plan, the registry row, and the roadmap link.
- **Documentation and code-documentation delivered:** This plan.
- **Validation performed:** Read `MenuPlan`, `ShoppingProjector`, `MenuPlanningApplicationService`, `PlanningEndpoints`, `RecipeCookNowApplicationService`, `RecipeIdeaLibrary`, the suggest and expand protocols, `AiOperationRegistry`, `RecipeContextAssembler`, `ShoppingPage`, and the domain documents.
- **Result:** Plan ready for Slice 1.
- **Next action:** See "Exact next action".
- **Blockers or handoff notes:** Owner gates G1-A and G2-A.

## Completion and handoff checklist

- [ ] All slices and acceptance criteria are resolved truthfully.
- [ ] Each run delivered a substantial coherent outcome or a valid early-stop reason is explicit.
- [ ] Required tests and evaluations pass, or limitations are documented.
- [ ] Durable documentation, contracts, comments, XML documentation, and TSDoc are current.
- [ ] Security, privacy, safety, localization, accessibility, AI, and operational impacts were reviewed.
- [ ] `docs/plan-status.md` matches this plan.
- [ ] Pull requests link this plan and report validation evidence.
- [ ] No hidden or unexplained partial work remains.
- [ ] Exact continuation instructions exist if the work is not complete.
- [ ] Delivery state and branch-cleanup responsibility are recorded.
