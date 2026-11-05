# PLAN-0062: Guided Shopping Planning ("Planejar compras")

- **Status:** In Progress (owner approved this plan on 2026-11-04; Slice 1 underway)
- **Type:** Implementation
- **Priority:** High (owner request)
- **Owner:** Claude Code (claimed 2026-11-04)
- **Created:** 2026-11-04
- **Last updated:** 2026-11-05
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
- [ ] **S5 (code delivered; G5-A pending):** A component reduces purchases and is cooked into stored portions atomically. Frozen dishes are proposed and consumed by portion.
- [x] **S6:** A session route merges recipes with parallel passive work, can be resumed, and completes executions. (Phone check with wake lock pending.)
- [ ] **S7:** Opt-in push arrives at most once a day with no food data.
- [ ] Every slice: documentation, XML docs, TSDoc, contracts, and gates are current. No unsupported claim remains.

## Execution state

- **Current run delivery target:** Slice 7, web push.
- **Current checkpoint:** Slice 7 implemented on `agent/plan-0062-s7-web-push`, in a draft pull request. No notification has reached a real phone.
- **Last completed step:** Worker check and signed-in journey on the proxy replica with disposable keys; see the Slice 7 log entry.
- **Exact next action:** The owner merges this pull request (#91), deploys, adds keys with `scripts/ops/generate-vapid-keys.mjs`, turns notifications on from a real installed phone, and confirms one arrives at 09:00. All seven slices are then delivered except the Slice 4 limits, which wait for G2-A.
- **Blockers:** G7 and G6 need the owner's phone. G5-A and G1-B need the provider key. G1-A has no owner comment. G2-A for the Slice 4 limits and for storage guidance. The browser smoke matrix does not finish on this host.
- **Partially modified areas:** Slice 4 limits and warnings are not delivered.
- **Documentation delivered:** ADR-0013 and its index row; runbook, `api.env.example`, privacy, product spec, planning, and gates documents; PLAN-0048 registry row; this plan.
- **Validation performed:** See the Slice 7 entry in the progress log.
- **Known failures or limitations:** See the Slice 7 entry. Earlier limits stand: storage values are not checked against primary sources, and G1-A has no separate owner comment.
- **Working tree state:** Clean after this commit.

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

### 2026-11-04 — Claude Code (Slice 1, backend)

- **Run delivery target:** Slice 1.
- **Checkpoint:** Backend committed.
- **Changes included in the commit:** Migration `AddShoppingPlans`; `RestrictionGuard` and rules; `IngredientPerishability`; `recipe.suggest_plan_deck.v1` with schema, fixtures, and validators; `RecipePlanDeckService`; `RecipeIdeaLibrary.FindPlanDeckAsync`; `ShoppingPlanApplicationService`, ranking, and sequencer; endpoints; "Hoje" and allocation order.
- **Deviations from this plan, keeping the outcome:**
  - The card's lead ingredient is its largest ingredient by weight or volume, not `primaryIngredientRefs[0]`, because model references are not stable across batches and the "tired of" reason needs a real name.
  - `DietaryPattern`, `Intolerance`, and `MedicalRestriction` codes that no word list can express (for example `keto`, `renal_diet`) are marked `unverifiable`, so every card shows the note.
  - Added `POST /{id}/cards/{cardId}/removal`, so "Remover" works on a failed card that has no menu entry.
  - Added `last_batch_key` to the session, so a retried batch does not spend another.
  - A failed batch is given back, so a provider outage does not use up the plan's allowance.
  - Acceptance is two steps, not one transaction, because the menu and the plan use separate stores. Each step is idempotent, so a retry completes it.
- **Validation performed:** See `Execution state`.
- **Next action:** See `Exact next action`.

### 2026-11-04 — Claude Code (Slice 1, frontend and documents)

- **Run delivery target:** Slice 1.
- **Checkpoint:** Slice 1 implemented, awaiting owner gates.
- **Changes included in the commit:** OpenAPI and generated client; `features/shopping-plan` (setup, cards, review, expansion queue); "Já tenho" and sharing on the list; entry points in Compras, Cardápio, and the main button; catalogs in three languages; browser journey; `PLAN0062_LIVE_EVAL` in the gateway smoke; documents. Also added the missing UTF-8 BOM to migration `20261104171014`, which `main` carries and which fails the format gate.
- **Deviations from this plan, keeping the outcome:**
  - The stepper for "Sim, 2 vezes" sits inline under the card rather than in a separate sheet, which needs no focus trap and keeps the card in view.
  - The main button's half moon grew to three actions.
- **Validation performed:**
  - Frontend tests: shopping plan, planning, cooking, quick menu, and i18n, 52/52, including one bug they caught in restriction labels.
  - Browser journey on the proxy replica, AI off, at 390 px: all checks passed. First card 880 ms (G1-C, instant cards). Decision round trip 61 and 71 ms with a small deck (G1-D; not measured with 60 cards).
  - Screens read at 360 px; keyboard and 200% zoom not checked by hand.
- **Gate G1-G not complete:** `run-local-gates.sh` was stopped by the host when memory ran low, during the backend build. Restore, vulnerabilities, and format had passed. The full backend suite, migrations applied twice, OpenAPI drift, frontend gates, and the smoke matrix did not run on this head.
- **Result:** Ready for owner review, not for merge until G1-A, G1-B, and G1-G.
- **Next action:** See `Exact next action`.

### 2026-11-04 — Claude Code (Slice 2)

- **Run delivery target:** Slice 2.
- **Checkpoint:** Slice 2 implemented, with guidance off.
- **Changes included in the commits:** Storage table behind a flag; curated attention rule; line marks; arrival advisor, routes, and page; "Não achei", later block, and split by place on the list; portions hint; entry on "Hoje"; browser journey; documents.
- **Defect found and fixed:** `SaveCreatedAsync` left the new lot tracked, so a freeze in the same request failed with an Entity Framework error surfaced as `domain_rule_violated`. HTTP callers never saw it, because each request has its own context. It now clears the tracker after saving.
- **Deviations from this plan, keeping the outcome:**
  - "Trocar prato" opens the menu instead of calling `POST /replacements`, because the list does not know the plan's session. Slice 4 extends replacement to accepted menus.
  - The "Hoje" entry appears when a reminder says something is to buy, not when lines are checked, which would need a list request per menu on the home page.
  - `GET arrival` takes `all=true`, so the list's portions hint does not vanish once a line is checked.
  - Surplus portions are whole grams, with the last taking the rest, so the total is exact.
  - An empty equipment list does not rule out a freezer; only a list without one does.
- **Validation performed:**
  - Backend: unit suite 350/350; architecture 14/14; integration filters for arrival, marks, guidance on, transitions, and purchase 10/10; format clean.
  - Frontend: typecheck clean; lint and format clean; planning, shopping plan, and i18n tests 57/57.
  - Contracts: recipe AI tests passed; OpenAPI exported and client regenerated.
  - Browser journey on the proxy replica, flag off, at 390 px: all checks passed.
  - The journey needed two fixes of its own: this run's menu is counted alone, and the fixture user's pantry is cleared first.
- **Not run:** the full local gates (G2-F), because the host has too little free memory (about 700 MB at the last check, and the earlier run was stopped for this). The full integration suite did not run. Flag on is covered by one integration test, not by the browser.
- **Result:** Ready for owner review. G2-A remains open.
- **Next action:** See `Exact next action`.

### 2026-11-05 — Claude Code (Slice 3)

- **Run delivery target:** Slice 3, offline list.
- **Checkpoint:** The list opens and takes marks with no connection, and they reach the server afterward. Owner merged #85 before this run ("merge feito, continue").
- **Changes included in the commit:**
  - Frontend: precache-only service worker in the production build; `features/planning/offline` (store, outbox delivery, provider, notices, route gate); session state `offline` through `withOfflineSession`; `ShoppingPage` and `ShoppingHubPage` read the saved list and disable what needs the server.
  - Operations: `sw-unregister.js`, installed by `--rollback` when the target release has no worker.
  - Dependencies: `idb-keyval` 6.3.0 and `vite-plugin-pwa` 2.0.0 pinned; `resolutions` for `glob` 14.0.3 under `workbox-build` and `minimatch` 11.0.4 under `typescript-estree`, because the audit policy refused the vulnerable versions. The `minimatch` advisory also affected `main`.
- **Documentation and code-documentation delivered:** ADR-0012 and its index row; `guided-shopping-planning.md`, `planning-and-shopping.md`, `privacy-and-data-protection.md`, `product-foundation-gates.md`, runbook; TSDoc on exported functions.
- **Defects found and fixed:** The plugin's `autoUpdate` setting alone left a new worker waiting, so a deploy never reached a visitor with the app open; `skipWaiting` and `clientsClaim` are now explicit.
- **Deviations from this plan, keeping the outcome:**
  - Marks also retry when the page becomes visible and every 30 seconds while pending, because `navigator.onLine` stays true on a weak signal.
  - A 401 also closes the offline list until the same owner signs in again, so an ended session does not keep opening private data. The outbox stays.
  - "Comprei" and similar actions share one note at the top of the list instead of one per line.
  - Places and package sizes are also disabled offline, since only check marks and "later" work.
  - A rejected mark (400, 409, 422) is reported as a conflict, so one bad mark cannot block the queue.
  - The registration script does not reload an open page, so a form is never discarded.
  - The smoke matrix blocks service workers, because its API answers are simulated.
- **Validation performed:**
  - Frontend: typecheck, lint, and format clean; full test suite 555/555; production and prototype builds (the prototype has no `sw.js`); bundle inspection, third-party, build-mode, and isolation guards, and audit policy passed.
  - Browser, proxy replica, 390 px: `offline-list-journey.mjs` 25/25, covering G3 (offline open, marks delivered, no `/api` in cache storage, logout erases, second owner sees nothing, 401 closes the list). `shopping-plan-journey.mjs` passed, confirming sign-in still works through the worker.
  - One-off simulated deploy: the new `index.html` appears on the second load. One-off rollback: `sw-unregister.js` leaves no registration and no cache, and the page renders.
- **Not run or failing:**
  - **Browser smoke matrix fails:** `page.goto` on the prototype server times out at 15 s before any check runs, twice on this branch and once on unmodified `main`, so this branch did not cause it. Loaded alone, the same page takes 3.2 s. The cause is not established. Its production part, including the `serviceWorkers: "block"` change, has therefore not run.
  - Full local gates (G3's gate run) and backend tests: not run. No backend code changed.
  - By hand: keyboard only and 320 px. A real phone and a real installed app were not tested; "installed" in the journey is a browser with the worker active.
  - `deploy-linux-host.sh --rollback` itself was not run; only the script it installs was exercised.
- **Correction:** The first Slice 3 commit deleted `browser-smoke-report.schema.example.json` by mistake, through a cleanup of smoke output. The next commit restores it unchanged.
- **Result:** Ready for owner review.
- **Next action:** See `Exact next action`.

### 2026-11-05 — Claude Code (Slice 4, repeat only)

- **Run delivery target:** Slice 4. This plan ships repeat only while G2-A is open, and the owner has not approved the storage table. Owner merged #86 before this run ("merge feito, continue").
- **Checkpoint:** A finished plan can be named and repeated.
- **Changes included in the commit:** `POST /shopping-plans` accepts `fromMenuId`; `GET /start` returns `recent`; `ShoppingPlanRecentList` on the setup screen; store queries for finished plans; regenerated OpenAPI and client. No migration.
- **Documentation and code-documentation delivered:** `guided-shopping-planning.md`, `planning-and-shopping.md`, XML docs, TSDoc.
- **Defect found and fixed before commit:** The list first rendered inside the setup form, so saving a name submitted the outer form and started a plan. A frontend test caught it. The list now sits outside, and the test asserts no plan is created.
- **Deviations from this plan, keeping the outcome:**
  - The repeated plan reads the old menu's entries, not its cards, because the menu can change after acceptance.
  - `RestrictionGuard` runs again and meal needs cap the copies, which the plan did not list. Invariant 2 requires the first.
  - The new plan always starts today. The old preparation date has passed, and the review screen can set another.
  - Nothing names a dish left out; the person sees fewer meals filled.
- **Validation performed:**
  - Backend: the four shopping plan integration tests passed, including the new one (archived and newly restricted dishes left out, another owner gets 404, an open plan cannot be repeated, retry adds no meal, draft stays a draft). OpenAPI exported and client regenerated; drift check passed.
  - Backend format reported nothing on the changed files; shopping plan unit tests 58/58.
  - Frontend: typecheck, lint, and format clean; full suite 558/558; production build passed.
  - Browser journey on the proxy replica at 390 px: passed, now through naming and repeating.
- **Host incident:** The host killed the development Keycloak container for lack of memory (OOM, exit 137) while this run was building and testing, which failed two journey attempts. I restarted that container; the journey then passed. The production service was not touched.
- **Not run:** Full local gates and the smoke matrix (it fails on unmodified `main` here). The full integration suite. Keyboard only and 320 px by hand.
- **Result:** Ready for owner review. Slice 4 limits and warnings are not delivered.
- **Next action:** See `Exact next action`.

### 2026-11-05 — Claude Code (Slice 5)

- **Run delivery target:** Slice 5. Owner merged #87 before this run ("ja fiz o merge").
- **Checkpoint:** Bases and stored portions work end to end with the fake provider. The real model has not produced a base yet.
- **Changes included in the commit:**
  - AI: `recipe.suggest_plan_deck.v2` and schema v0.4, `RecipeSuggestSemantics.ValidComponents`, its JavaScript mirror, fixtures, and the fake provider. `daily` plans keep v1.
  - Planning: migration `AddMenuEntryComponents`; base cards; projection, ranking, and sequencing; `StoredOutputAdvisor`; `ExecutionPlanLookup`; reminder `ready_portions`.
  - Recipes: completion takes `storedOutputs` through a port Planning answers, since Recipes does not read Planning's tables.
  - Inventory: `ListOutputBatchesAsync`; a prepared lot in the freezer ages from its creation.
  - Frontend: base card, review label, `from_prep`, storage editor on both finish screens, "Comer uma porção".
- **Documentation and code-documentation delivered:** See `Documentation delivered`; XML docs and TSDoc.
- **Deviations from this plan, keeping the outcome:**
  - The limit `MaxOutputTokens` is 10,240 and the estimates are 7 units, US$ 0.009, and 32,000 tokens, because v2 returns up to two more candidates. The plan did not set these.
  - With guidance on, a dish freezes past the table's `cooked.fridgeDays`. It uses no per-dish rule, since a dish has no catalog category.
  - A repeated plan brings dishes but not bases, because a base has no meal type to copy. Its item stays on the list.
  - A reminder for what a base makes is hidden by matching key; the plan named only the list.
  - A base with an output name the real expansion spells differently would leave both on the list. The expanded recipe is not checked against the output name.
  - "Comer uma porção" uses one unit, as the plan says, even though a stored meal for two people holds two portions.
- **Validation performed:**
  - Backend: unit suite 370/370 (20 new); architecture 14/14; the `RecipeExecutionApiTests` and `PlanningApiTests` classes 37/37, whose 2 new tests cover atomic completion, rollback on a stale lot, retry, limits, a base lot matching a later dish, and portions counted down.
  - Frontend: typecheck, lint, and format clean; see the pull request for the final suite count.
  - Contracts: recipe AI tests and drift check passed; OpenAPI exported and client regenerated.
  - Browser journey on the proxy replica, flag off, 390 px: passed through cooking, storing, and eating a portion.
- **Not run or not covered:**
  - **G5-A:** the real model was never asked for components; the valid-component rate is unknown. Without the key, the 80% criterion is unverified.
  - **The base card in a browser:** the replica has no AI, so it is covered by frontend tests and the API test, not by the journey.
  - Flag on in a browser; full local gates and smoke matrix; full integration suite; keyboard only and 320 px by hand.
- **Result:** Ready for owner review.
- **Next action:** See `Exact next action`.

### 2026-11-05 — Claude Code (Slice 6)

- **Run delivery target:** Slice 6, timed session route.
- **Checkpoint:** Scheduler, route, progress, screen, entry points, and browser journey delivered in one draft pull request.
- **Changes included in the commit:**
  - **Scheduler and route.** Pure `PrepRouteScheduler` and `PrepRouteBuilder`; `ShoppingPlanRouteService`; `GET /{id}/route`, `PUT /{id}/route/progress`, `GET /route-link`; migration `AddShoppingPlanRoute` adds a nullable `RouteProgressJson`.
  - **Screen.** `ShoppingPlanRoutePage` at `/app/compras/planejar/:sessionId/preparo`, `SessionRouteLink` on the menu page and "Hoje", and `?rota=&concluir=1` on `CookingPage`.
  - **Regenerated.** OpenAPI and the API client.
- **Deviations, keeping the slice's outcome:**
  - A later stage that states no dependency follows the previous stage, because an empty `dependsOn` often means the writer left it out. Treating it as parallel could tell someone to serve before cooking. The cost is less parallel work for such recipes.
  - A preparation's `minimumLeadMinutes` counts as passive time, so a marinade gets its lead. The plan did not mention it.
  - The route opens only after acceptance, returning 422 before, because cooking uses the pantry and a draft must reserve nothing. The plan did not say.
  - Added `GET /route-link` and the links on the menu page and "Hoje", because the plan named a screen but no way to reach it.
  - Each task can show the dish's scaled ingredients, because stage text often has no quantities.
- **Documentation and code-documentation delivered:** See "Documentation delivered"; XML docs and TSDoc on new members.
- **Validation performed:**
  - **Backend.** Unit 391/391, including 21 new for the scheduler and builder (parallel passive work, dependencies across base and dish, cycles, determinism). Architecture 14/14. `RecipeExecutionApiTests` and `PlanningApiTests` 38/38, including a new journey over HTTP (not before acceptance, resume, another owner gets 404, cooked tasks read as done). Over budget is covered by a frontend test, with the budget flag computed in the service.
  - **Frontend.** 575/575, typecheck, lint, format, and build.
  - **Contracts.** `export-openapi`, the client sync, and the drift check passed. `lint-openapi.sh` exited 0.
  - **Browser.** `scripts/e2e/session-route-journey.mjs`, 15 checks at 390 px on the proxy replica, including a reload, focus moving to the new task, and no sideways scroll at 320 px. `shopping-plan-journey.mjs` still passes, 18 checks.
- **Result:** G6 passed except the phone check. **Intermittent journey failures:** the session journey failed 4 of 15 runs, all while it was being run back to back with the other journey, and passed the last 6 in a row.
  - The first failure is most likely earlier runs' dishes crowding the seeded ones out of the first five cards. The journey now archives them and stops if a dish is missing.
  - The other three are most likely the 60-per-minute `mutation` limit, since they followed the heavier journey. That is not proven: no 429 was captured, because the check that reports it was added afterward and has not fired. Only the journey's seed reports it. A person on the route uses that limit only to start and finish dishes.
- **Not run:** the phone check with wake lock (the owner's); a route with a base in a browser, because the replica has no AI (covered by unit tests only); a route using AI-written dependencies; screen reader; 200% zoom; one local gate run, because the owner decides when to rerun the gates; the full integration suite.
- **Limitations:**
  - Burner and oven conflicts are not detected, as planned, and the overview says so.
  - The order does not adapt when someone runs late.
  - A timer's alarm sounds only with the page open; push does not cover it.
  - A dish with no date is not on the route, so a plan of only plain "Sim" dishes has no session.
  - Two entries of one recipe on the same day share the first cooking session until it finishes; the finish step then starts the second.
  - Skipping every task of a dish still leaves its finish step.
- **Next action:** See `Exact next action`.
- **Blockers or handoff notes:** See `Blockers`. The development database has this migration; production gets it on deploy. Dev Keycloak and the replica are stopped.

### 2026-11-05 — Claude Code (deploy and first full gate run)

- **Run delivery target:** Publish `main` on the test host at the owner's request, and repair what the first full gate run found.
- **Deploy:** `8ddec16` active as release `20261105T102018Z-8ddec165110f`. Migration history went from 28 to 32 rows, `cocinaris_app` has access to the new tables, `/health/ready` returns 200, and the service log shows no errors in the first minutes. Sign-in, the new screens, and any plan on the deployed site were not exercised, because the agent has no account there.
- **Worker caching:** the CDN cached `/sw.js` because of its extension. The agent's own check, made before the first release with a worker, stored the `index.html` fallback there, and that copy still answered after the deploy. The Caddy block now sends `Cache-Control: no-cache` on the path, and a fresh URL at the CDN answered `REVALIDATED` on its second read. The CDN still rewrites the browser header to `max-age=14400`, which does not delay worker updates because browsers skip the HTTP cache for the worker script by default. The stale copy stays until it expires or the owner purges it; the agent has no CDN credentials.
- **First full gate run on `8ddec16`, result Failed:**
  - `backend-format`: a real defect. `AddShoppingPlanRoute.cs` had a UTF-8 BOM that `.editorconfig` rejects. Repaired here. No effect on behavior.
  - `backend-dependencies`: the agent's development PostgreSQL held port 55432, so the gate's PostgreSQL and Keycloak did not start.
  - `backend-tests`: 14 architecture and 391 unit tests passed; integration 217 of 228. The 11 failures are login and logout routes answering 500, which fits the missing Keycloak; this is confirmed only if they pass on the rerun.
  - Every remaining frontend, contract, and OpenAPI step failed on `ENOSPC`, because the gate worktree lived in the 2 GB RAM-backed `/tmp`. The script now builds in `/var/tmp`, as the deploy script does.
- **Changes included in the commit:** `Caddyfile.cocinaris`, the runbook, `run-local-gates.sh`, the migration's encoding, this plan, and the registry.
- **Next action:** See "Exact next action".

- **Second full gate run on `d0face5`, result Failed in one step:** 32 of 33 steps passed, including format, the 14 architecture, 391 unit, and 228 integration tests (the 11 login and logout failures are gone, so they came from the missing Keycloak), migrations applied twice, OpenAPI drift, contracts, frontend tests and builds, audit, and the secrets scan.
- **`frontend-smoke` does not finish, cause not established:**
  - In the full run it failed after 44 s, on `page.goto: net::ERR_INSUFFICIENT_RESOURCES` for `/app/hoje`.
  - Run alone with only `frontend,smoke`, it hung with no output for 25 minutes and was stopped by hand.
  - Both stopped right after the CSS zoom check, whose next step is the touch check in a new browser context. Six checks passed before that; everything after, including the production part with `serviceWorkers: "block"`, has never run since Slice 3.
  - Earlier runs failed with a 15 s `page.goto` timeout, also on unmodified `main`. A smoke run has no overall time limit, so the gate script can wait forever.
  - The partial run overwrote the full run's summary under `artifacts/local-gates/d0face5…`; the table is in the pull request.

### 2026-11-05 — Claude Code (Slice 7)

- **Run delivery target:** Slice 7, opt-in web push, from fresh `origin/main` after the owner asked to continue. PR #90 is still open, so its gate script and encoding fixes are repeated here unchanged to keep this branch's gates honest; they merge cleanly in either order.
- **Checkpoint:** Implemented and validated up to the point a real push service and phone are needed.
- **Changes:**
  - Backend: `PlanReminderPush.cs` (rules, schedule, services, ports), `WebPushSender`, `PlanReminderPushWorker`, `PushNotificationEndpoints`, `PostgreSqlPushSubscriptionStore`, additive migration `AddPushSubscriptions`, `Lib.Net.Http.WebPush` 3.3.1, and `GetPreparationForOwnerAsync` so the same reminders work with no signed-in user.
  - Frontend: `public/push-sw.js` imported by the generated worker, `features/notifications`, the option on the profile page, the invitation on accepted menus, logout cleanup, and catalogs.
  - Tooling: `scripts/ops/generate-vapid-keys.mjs`, `scripts/e2e/push-worker-check.mjs`, `scripts/e2e/push-opt-in-journey.mjs`.
  - OpenAPI and generated client.
- **Deviations from the plan:**
  - **Sentence in the worker, not the backend.** The server sends a closed type and language, which keeps invariant 11.
  - **Closed list of push hosts, not in the plan.** The endpoint is an address the server calls, so this prevents server-side request forgery. A browser using another push service cannot subscribe and sees "Não consegui alterar os avisos".
  - **Logout ends the subscription, not in the plan.** The person turns notifications on again after signing back in, and a shared device stays clean.
  - **`cook_by` is accepted and never occurs yet,** because that kind waits for G2-A. Today only `buy` and `thaw` trigger a notification.
  - **Claim before sending,** so a crash loses that day's notification rather than risking a second one.
  - **Invitation placement:** on the accepted menu page, at any time until answered, rather than only after the first plan.
- **Validation performed:**
  - Backend unit: 39 new tests passed. Integration: 8 new tests covering isolation, CSRF, validation, cap, one per day, restart, no time zone, nothing due, a failing owner, concurrent claims, and the real sender's encryption, VAPID header, and 404/410/500 handling against a local handler.
  - Frontend: full suite 594 of 594, typecheck, lint, and format.
  - `push-worker-check.mjs`: 6 of 6, in a real browser with the production build. The push reached the worker through the debugging protocol, and calls to `showNotification` were recorded inside it.
  - `push-opt-in-journey.mjs` on the proxy replica with disposable keys: 10 of 10, including 320 px and removal on logout. The browser's permission and push service are stand-ins here.
  - Vulnerable package listing for the API: none. OpenAPI regenerated and client drift passed.
  - Full gate result: recorded in the pull request.
- **Not run:**
  - **G7: a real notification on a real phone.** Nothing here has spoken to a real push service, and the production site has no keys. This remains the main risk of the slice, including iPhone.
  - The notification's actual display and click on a device. `headless_shell` listed no notifications, and the click handler has only a unit test.
  - Invitation on an accepted menu in a browser (unit tests only). Screen reader and 200% zoom.
- **Limitations:**
  - A person without a time zone in the profile gets nothing, and only a line on the option explains it.
  - The 09:00 hour is fixed. A service outage during it loses that day.
  - On the very first page load the worker may not be registered yet, so the option appears only after the next navigation.
  - The session can expire without a logout, and then the subscription stays until another owner takes it or the push service removes it. The message names nothing about the person.
- **Next action:** See "Exact next action".

### 2026-11-05 — Claude Code (merge of `main` into Slice 7)

- **Correction:** the Slice 7 entry says #90 and this branch "merge cleanly in either order". That was wrong and was never tried: after #90 merged, this branch conflicted in the plan and the registry.
- **Changes:** merged `origin/main` at `41d597a`, kept both progress log sections in order, and updated the state and registry to say #90 is merged.
- **Validation:** the diff against the gated commit `e4d4fa3` is limited to the files #90 changed and this plan's records, so the gates were not rerun.

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
