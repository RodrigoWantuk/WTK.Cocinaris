# PLAN-0065: Planning Rework ("Montar o cardápio")

- **Status:** In Progress; Slices 0 to 6 merged (#95 to #100, #102); owner checks remain (see Execution state)
- **Type:** Implementation
- **Priority:** High (owner request)
- **Owner:** Claude Code
- **Created:** 2026-11-05
- **Last updated:** 2026-11-11
- **Branch:** Plan publication `agent/plan-0065-planning-rework`; each slice uses the branch named in its section
- **Pull request:** #94 (plan publication, merged); #95 to #100 (Slices 0 to 5, merged); #102 (Slice 6, merged); the byte-order-mark fix rides on the PLAN-0049 Slice 1 pull request
- **Related issues:** None
- **Related ADRs:** ADR-0009, ADR-0012, ADR-0013
- **Dependencies:** PLAN-0062, PLAN-0063, PLAN-0064 (all merged). This plan replaces the deck and review screens of PLAN-0062 Slice 1 and the panel of PLAN-0064.

## How to use this plan

Execute one slice at a time, in order. A slice is one pull request from a freshly fetched `origin/main`, and the next starts only after the owner merges the previous one. Agents never merge, approve, or enable auto-merge.

- **Slice 0 needs no owner decision.** Slices 1 to 5 start only after the owner confirms D1 and D2.
- **Deviation rule:** if the code contradicts a detail here, keep the outcome and the invariants, adapt the mechanics, and record the deviation in the progress log.
- **Stop conditions:** a decision below is unanswered, the code cannot satisfy an invariant, or an unrelated failure blocks the gates.
- Before every commit, update `Execution state`, `Progress log`, and the registry row.

## Objective

Let someone say how many days and how many of each meal they want, in any mix, such as only 7 snacks for 14 days with no fixed days, and get dishes that fit each meal. They choose by typing what they want or by comparing options, deciding per choice whether to use their preferences, their pantry, or their own words. Dates stay optional, and the whole set stays visible.

## Why: what the review found

The review ran on 2026-11-05 at `fc17bfe`. It read every screen and the backend, walked the flow in a browser at 320, 360, and 390 px with measurements, and read the owner's real session on the test host. Headless Chromium was used; no real phone and no WebKit.

### The owner's real session

The owner was building a menu of snacks only, for 7 days, with no fixed days.

| Measure | Value |
|---|---|
| Questions on the setup screen | 9, before any dish appears |
| Dishes offered | 31 |
| Refused / accepted | 27 / 4 |
| Refusals in a row at the end | 13 |
| Refusals between 15:16:02 and 15:16:32 UTC | 12, clearing old dishes to reach the rest |
| Batches used | 5 of 5, so no more could be asked for |
| AI dishes labeled `snack` | All 30, including "Espaguete Agridoce", "Macarrão Picante", "Frango Agridoce", and "Tigela de Carne Bovina" |
| Dishes with "picante", "apimentado", "pimenta", or "jerk" in the name | 23 of 31; the profile has `spicy_food`, and the prompt treats it as a rule for every dish |
| Near-duplicates | 3 variations of pão de queijo; formats are free text, so `bolinho frito` and `bolinho assado` count as different |
| Typed hint | "Jerk beef, cozido e com desidratador": a specific recipe, sent as a soft wish |
| Result | Plan stopped in `Reviewing` with 4 of 7 meals filled, never accepted |
| AI time | Batch 21 s on average, 32 s at most; recipe expansion 17 s each, in series |
| AI cost of the session | About US$ 0.09, so cost is not the constraint |

### Structural problems

1. **Meal type is only a label.** The prompt never defines a snack, and validation only checks that `targetMealType` is one the plan asked for. The model writes `snack` on a plate of spaghetti and it passes.
2. **Meals are a grid, not quantities.** The total is `days × meal types`, so "3 lunches and 7 snacks in 14 days" cannot be said. Lunch and dinner also share one `main` group.
3. **Decisions come before their consequences.** Style, priority, preparation day, and preparation time are asked before any dish exists, and on the phone the setup takes 1.8 screens, or 3 at 320 px with recent plans.
4. **Two questions do nothing for most dishes.** "Quando vai preparar?" and "Quanto tempo?" only reach a dish through "Sim, 2 vezes" with "Rende".
5. **There is no way to ask for a dish.** Search exists only for saved recipes, and only on the menu page. The anchor field takes one ingredient, and the hint is a soft wish.
6. **Pantry and tastes cannot be switched off where the choice happens.** PLAN-0064 added a pantry answer behind a panel, and tastes cannot be switched off at all.
7. **Yes or no on one dish at a time.** Nothing can be compared, and the chosen dishes are not visible while choosing.
8. **Three screens for one object, three ways to build it.** Setup, cards, and review lead to a separate menu page, alongside the manual menu form and "Sugerir com IA" per slot.
9. **A menu without dates looks empty.** An accepted menu opens with "Nada planejado" on every day and the dishes at the bottom under "Sem dia definido". "Hoje" shows one flexible dish per plan, whatever the meal.
10. **Names.** "Plano", "cardápio", "lista", and "o que comprar" describe overlapping things.

### Confirmed defects

| # | Severity | Defect | Evidence |
|---|---|---|---|
| B1 | High | "Escolher mais pratos" returns to the review at once: the deck redirects any plan in `reviewing`. A plan can be stuck below its meals | Browser, and `ShoppingPlanDeckPage.tsx` load effect |
| B2 | High | On a list line at 360 px, "800 g" sits under "Já tenho"; the dish name wraps over 5 lines; each line takes 193 to 225 px | Browser measurement and screenshot |
| B3 | High | After a request, old dishes stay queued, so the person refuses them all to get through | 12 refusals in 30 s in the real session |
| B4 | High | The model applies one taste to almost every dish and offers near-duplicates | Real session |
| B12 | High | Full meals are offered as snacks | Real session, query on `shopping_plan_cards` |
| B5 | Medium | "Usar este plano" accepts 3 of 7 meals with no word about the rest | Browser |
| B6 | Medium | Expansion runs per page, so the deck and the review race: 12 duplicate-key errors in the log during the session. Leaving the screen stops it | `journalctl`; `useExpansionQueue` |
| B7 | Medium | Controls under 44 px on the phone: chips 36 to 40, "Trocar" and "Remover" 36, "Não achei" 24, "Voltar" 20, the package link 16 | Measurement |
| B8 | Medium | From the draft list, "Voltar para o cardápio" leaves the flow for a page with its own accept button | Browser |
| B9 | Low | The review shows a number field and a date field per dish: 2.6 screens for 4 dishes | Measurement |
| B10 | Low | The menu title repeats the period; at 320 px the "+" and AI buttons wrap; "1º a cozinhar" means little; "+1 itens novos" | Browser |
| B11 | Unverified | `datalist` does nothing on iOS Safari, and native date inputs may overflow there | Known platform behavior; to be checked with WebKit and a real iPhone |

## Decisions

### Already decided by the owner (2026-11-05)

| Topic | Decision |
|---|---|
| Dates | Optional. A menu can be a set of dishes to eat within a period, with no fixed days |
| How much | The person says how many days, and how many breakfasts, lunches, dinners, and snacks, independently |
| Compatibility | Dishes must fit the meal: a snack is a snack, not a full meal |
| What drives suggestions | Choosable when picking dishes: preferences, current pantry, or manual parameters, each on or off |

### To confirm

| # | Decision | Recommended | Reverses |
|---|---|---|---|
| D1 | Main way to build | **One screen grouped by meal type, with a sheet of options to compare.** Dish-by-dish yes or no goes away | PLAN-0062 "Dish-by-dish cards" |
| D2 | Starting state of the switches | **Preferences on, pantry off,** then each plan starts from the last answers. Cooking style and meals eaten out leave the first screen; priority becomes a filter; preparation moves to after the menu is in use | PLAN-0062 "Asked in every plan" |

Names follow the owner's decision to keep "cardápio": "plano" leaves the interface, and the tab stays "Planejar".

If D1 is refused, only Slice 0 applies, and the plan must be rewritten.

**Owner confirmation (2026-11-05):** after merging #95 the owner wrote "Merge feito, confio nas suas recomendações, pode seguir." D1 and D2 are confirmed as recommended.

## Invariants

These hold in every slice, and tests protect each that can be tested.

1. **AI never mutates state.** Output is validated, and only deterministic commands change menus, recipes, or the pantry.
2. **Restrictions are hard rules.** No switch, filter, typed text, or request relaxes them. Turning preferences off removes tastes only: allergies, intolerances, medical restrictions, diets, and dislikes stay. They already travel apart from tastes (`RecipeContextAssembler`).
3. **Drafts reserve nothing.** Only "Usar este cardápio" reaches "Hoje" and reserves stock.
4. **An accepted menu is never silently rewritten.** Each change is one explicit command on one entry.
5. **The list is always net of the pantry.** The pantry switch changes what is suggested, never what is deducted.
6. **Private words stay private.** A dish made under typed text carries it through expansion and never reaches the shared idea library.
7. **Typed text is untrusted,** bounded, in its own prompt field, and never logged.
8. **Shelf-life output comes only from curated rules.** The model never states dates, and without an approved table nothing claims how long food lasts.
9. **Degraded mode works.** With AI off, saved recipes, ideas, and search still fill a menu, and typed requests say AI is unavailable.
10. **Owner isolation.** Another owner's identifier returns 404.
11. **The offline list keeps working** (ADR-0012), including being saved before the market without having been opened.
12. **Localization.** All copy in pt-BR, en, and es; the backend returns codes.
13. **Answers do not update the profile.**

## The new flow

### Decision moments

| Decision | Today | New moment |
|---|---|---|
| Period | Setup | First screen: days and start |
| How many of each meal | Not possible | First screen: four counters, changeable later on the board |
| People | Setup | One line on the first screen and the board |
| What to eat | One yes or no at a time | Tap "Escolher lanche", then type or tap from a list |
| Use my preferences, use my pantry | Tastes always; pantry behind a panel | Switches above the options, changeable at any time |
| Quick, fewer purchases, cheap | Setup, before any dish | Filter above the options |
| Cook once, eat several times | "Sim, 2 vezes", then a sheet | On a chosen dish: "Rende mais" |
| Which day | Native date field | Only if wanted: "Marcar um dia" on a dish |
| Preparing several together | Setup, before any dish | After the menu is in use: "Preparar vários de uma vez" |
| Pack size, store | On each line | Inside the line's menu |

### Screens

**First screen (`/app/compras/planejar`).** Fits one phone screen at 360 × 740 without scrolling.

- **"Por quantos dias?"** 3, 5, 7, 14, outro (1 to 14). This is the period to eat within.
- **"Começando"** hoje, amanhã, outro dia.
- **"Quantas refeições?"** Four rows with a counter, 44 px controls: Café da manhã, Almoço, Jantar, Lanche, each 0 to 40, at most 40 in total. Counters are independent of the days, so 7 snacks in 14 days is valid.
- **"Para 2 pessoas · Alterar".**
- **Primary:** "Montar cardápio (7)", disabled at zero.
- **Disclosure:** "Repetir um cardápio".

Defaults: the last answers, else dinner equal to the days and the rest zero. While a counter is untouched and above zero, it follows the days. With a menu in progress the screen shows "Continuar cardápio".

**Board (`/app/compras/planejar/:sessionId`).** One surface per meal type that has a count above zero, in meal order.

- **Group header:** "Lanches · 3 de 7", with a compact counter to change the quantity.
- **A chosen dish:** the name (at most 2 lines), "35 min · +3 itens", and a state when its recipe is being prepared. If it has a day, a small tag such as "sex., 7"; without one, no tag and no warning. Tapping opens its actions.
- **What is missing:** one row, not one per meal: "Escolher lanche · faltam 4". This keeps 7 snacks on one screen.
- **Chrome:** the tab bar is hidden on phones while building, as in guided cooking. A control at the top says "Sair (fica salvo)".
- **Fixed bottom bar:** "3 de 7 · 9 itens" with the link "O que comprar", and the primary action: "Completar" while something is missing, "Usar este cardápio" when nothing is.

**Options sheet.** A bottom sheet at 90 dvh. Title: "Lanche · faltam 4".

- **Text box first:** "O que você quer?", placeholder "pão de queijo, algo com frango, jerk beef…", up to 120 characters. While typing, saved recipes that match appear, then always one row: "Pedir à IA: {text}".
- **Switches:** "Minhas preferências" and "O que tenho e já escolhi". Changing one asks for new dishes at once, because existing ones were made under the other answer.
- **Filter, one at a time:** Rápido, Menos compras, Econômico.
- **"Já neste cardápio":** tap to repeat a dish.
- **Six options at once.** Each shows name, time, "+N itens", and what it reuses. Tap to choose: a notice offers "Desfazer" for 5 seconds, and the sheet stays open for the same meal type until none is missing.
- **"Ver ingredientes"** on a row expands it and offers "Quero parecido" and "Não sugerir isso".
- **"Ver outras"** at the end fetches six more, different from those shown.
- **Waiting** shows skeleton rows in the list's shape, and existing options stay usable.

**Actions on a chosen dish.** A sheet: Trocar, Marcar um dia (chips of the period's days, and "Sem dia"), Rende mais (2 to 4, counts toward its meal type), Porções, Ver receita, Remover.

**Completar.** A sheet proposing a dish for everything missing, with a swap control per row, and "Usar estes". Nothing changes until confirmed.

**Accepting with something missing.** Asks once: "Faltam 2 lanches. Usar assim?", with "Usar assim" and "Continuar escolhendo".

**Menu in use (`/app/cardapio/:menuId`).** Heading, the next step, then dishes dated today, later dated dishes, and **"Quando quiser"**, grouped by meal type. Empty days do not appear. A line near the end of the period says "Faltam 2 dias · 3 lanches por fazer".

**"Hoje".** Dated dishes as today, plus the next undated dish of **each meal type**, under "Quando quiser".

### What goes away

`ShoppingPlanDeckPage`, `ShoppingPlanReviewPage` (its route redirects to the board), the setup questions in D2, the "Quero outras opções" panel, the manual menu form on `MenusPage`, "Montar refeição por refeição", and `AiMealSuggestion` (replaced in Slice 4). PLAN-0064's answers survive: pantry as a switch, similarity as "Quero parecido" and "Ver outras", and the hint as the text box.

## Technical design

### Data

Migration `AddPlanningBoard` (Slice 1): `menu_entries.CookOnDate date null` (Slice 4 uses it; the route selects `CookOnDate ?? PlannedDate`). Additive, so an older release still runs.

`ShoppingPlanSetup` (JSON, no migration) gains `MealCounts`: `{ breakfast, lunch, dinner, snack }`, and `StartDate` as a stored value.

- `TotalMeals` is the sum. `MealTypes` is the keys above zero.
- A setup without `MealCounts` derives it: `Days` for each of its `MealTypes`, minus `MealsOut` from lunch and dinner. The owner's stuck plan reads as 7 snacks, and is the test case.
- Validation: each 0 to 40, sum 1 to 40, `Days` 1 to 14.

`DeckPreferenceJson` changes meaning: `{ usePreferences: bool, usePantry: bool, goal: quick|short|save|null, likeDish: string|null }`. Typed text no longer persists: it belongs to one request and to the cards it made, through `PrivateHint`. The first screen's next-plan defaults reuse the last preference, and never touch the profile.

### API

Under `/api/v1/planning/shopping-plans`:

| Route | Change |
|---|---|
| `POST /` | Adds `mealCounts` and `startDate`. `cookingStyle`, `priority`, `prepDate`, `prepMinutes`, and `mealsOut` become optional, with fixed defaults `mix`, none, none, none, 0. `ValidateSetup` stops requiring preparation for `mix` |
| `POST /{id}/cards/{cardId}/decision` | Adds `mealType` and optional `plannedDate`; expansion creates the entry with them. `mode` and `repeats` remain for a repeated plan |
| `GET /{id}` | Adds `groups` (`mealType`, `wanted`, `filled`) and `queues` per meal type, six each |
| `POST /{id}/batches` | Takes `mealType`, optional `request` (text, up to 120), `likeCardId`, and the chip codes. Removes `hint`, `pantry`, and `similarity` |
| `PUT /{id}/preferences` | Sets the switches and the goal, and re-ranks |
| `PUT /{id}/meal-counts` | Changes quantities. It cannot go below what is chosen; the screen asks to remove a dish first |
| `PUT /{id}/entries/{entryId}/day` | Sets or clears a day inside the period. On an accepted menu it is an explicit single-entry command |
| `PUT /{id}/entries/{entryId}/yield` | Sets how many meals it covers (1 to 4); scales servings to `people × N` |
| `POST /{id}/reopen` | Slice 0, fixes B1 |

Keep the `Idempotency-Key` pattern on `batches`, and the `shopping-plan-decision` limiter on decisions. Regenerate OpenAPI and the client.

### Meal compatibility

Two layers, because the model's own label cannot be trusted.

**Prompt.** Each batch asks for **one** meal type, with `planDeck.mealGuide`:

| Type | Guide |
|---|---|
| `snack` | "A small bite between meals: finger food, baked goods, a small sandwich, something to nibble. Never a plated main course, pasta dish, stew, or bowl." |
| `breakfast` | "What people eat in the morning: bread, eggs, fruit, cereal, tapioca, cuscuz. Never a lunch or dinner main." |
| `lunch`, `dinner` | "A complete main meal. Never only a snack or a dessert." |

**Deterministic check.** A pure `MealFit` in Planning with `Catalog/meal-fit-rules.json`, run on every AI and library card before it is shown:

- **Name and format keywords** per type, in pt, en, and es, using `IngredientText.Normalize` with word boundaries. For `snack` and `breakfast`, it blocks words such as macarrão, espaguete, lasanha, risoto, ensopado, feijoada, strogonoff, moqueca, escondidinho, tigela, bowl, "prato". For `lunch` and `dinner`, it blocks pipoca, biscoito, bolacha, and similar.
- **Time:** above 60 total minutes blocks a `snack`.

A blocked card is dropped, with the metric `cocinaris.shopping_plan.meal_fit_dropped{mealType}`. If fewer than 3 survive a batch, the service calls the model once more, with the dropped names in `rejectedDishes`. Saved recipes use the meal types the person gave them, with no `MealFit`, because they are the person's own. A **requested dish skips `MealFit`:** if someone asks for feijoada as a snack, that is their choice.

Mass per serving is deliberately not used. The first version of this plan proposed 250 g, and the real session disproved it: recipes count frying oil and water, so "Frango Crocante", which the owner accepted, reaches 696 g, and three of the four accepted dishes would be blocked.

Tests use the real session's names as fixtures: "Espaguete Agridoce", "Macarrão Picante", "Frango Agridoce" (through its format, `salteado em frigideira`), "Tigela de Carne", "Escondidinho", and "Batata Assada Recheada" (70 minutes) are blocked; the four dishes the owner accepted, "Pão de Queijo", "Bolinho de Polvilho", "Pipoca", and "Tapioca Recheada" pass.

### Ranking

`ShoppingPlanRanker` keeps its integer arithmetic. `usePantry` on means `PantryWeight = 3` with the `waste` goal; off means 0. Goals: `quick` → `time`, `short` → `short`, `save` → `save`. `ConsecutiveRejections` no longer sets the weight, because refusing is no longer the way through. An undated plan keeps `SuggestedOrder`, so what spoils first is still offered first on "Hoje".

### Prompt

In `RecipeAiRequestEnvelopes.BuildPlanDeckEnvelope`:

- **Taste rule, for every batch:** "userPresets.preferences are tastes, not requirements. At most 2 of the 6 dishes may lead with any one taste or flavor; vary cuisines and main ingredients." Restrictions remain absolute.
- **`usePreferences` off:** `userPresets.preferences` is sent empty. `restrictions`, which also carries dislikes, stays.
- **`usePantry` off:** `availabilitySnapshot.items` and `shoppingListIngredients` are sent empty, and `rule` stops asking for reuse. **On:** "Build dishes mainly from availabilitySnapshot and shoppingListIngredients, adding as few new ingredients as possible."
- **`planDeck.timing`:** `{ windowDays, fixedDays: false }` with "These dishes will be eaten at some point in this period, on no fixed day. When the period is over a week, prefer ingredients that keep or dishes that freeze or store well." It must state no dates or durations.
- **`planDeck.requestedDish`:** `{ text, treatment }`, cleaned to 120 characters by `UntrustedText.Normalize`, with the untrusted-text rule. "The person asked for this. Candidate 1 must be this dish, made the way a home cook would recognize it, even if it needs ingredients not at home; list them. The others are close alternatives. If it is ambiguous, make the most common version."
- **`planDeck.likeDish`:** replaces `direction`, same untrusted treatment.

The response schema does not change.

### Near-duplicates

`AddCandidatesAsync` gains a pure `DishSimilarity.IsSameDish(a, b)`: normalized name tokens without stop words and cooking words (`de`, `com`, `no`, `na`, `air`, `fryer`, `frigideira`, `assado`, `picante`, `apimentado`), same when Jaccard ≥ 0.6. It never applies to a requested dish's first candidate.

### A typed request

1. The sheet shows saved matches at once, with no AI.
2. "Pedir à IA" calls `batches` with `request` and `mealType`. Six cards come back in about 20 s, while the rest of the sheet stays usable.
3. Answers come first for that meal type, under "Para {text}", through the existing `FromOrder`.
4. `RestrictionGuard` runs as always. If every card is blocked, the response is 422 `request_blocked` with the restriction code, and the sheet says: "Essa receita conflita com {restrição}." It never offers a way around.
5. Each card keeps the text in `PrivateHint`, which also keeps it from the idea library.

### Expansion

A single `PlanExpansionProvider` inside `ShoppingPlanProvider` replaces the per-page hook, so it survives navigation and there is one queue. It starts 5 seconds after a choice, for undo. "Usar este cardápio" stays disabled while any dish is being prepared, with "Preparando receitas: 3 de 5". A failed dish shows on its row, with "Tentar de novo" and "Trocar". A server queue stays out of scope (PLAN-0039).

### Sheets and phone layout

- Use the existing `vaul` drawer (`components/ui/drawer.tsx`), with `max-h-[90dvh]`, a sticky text box, `env(safe-area-inset-bottom)`, focus trapped, Escape closing it, and focus returned to the row. Reduced motion is respected.
- A fixed bottom bar needs `padding-bottom` on the page equal to its height, and must rise above the keyboard. Test with `visualViewport`.
- The shell uses `min-h-dvh` rather than `min-h-screen`.
- `Button size="sm"` and `ChoiceChips` reach 44 px under `@media (pointer: coarse)`. This one rule fixes most of B7.
- Replace `<input type="date">` on planning screens with chips of the period's days, and replace the `datalist`.

## Slices

### Slice 0: Fix what is broken today

Branch: `agent/plan-0065-s0-fixes`. Needs no owner decision and is useful even if D1 is refused.

- [x] **Mobile audit gate.** New `scripts/e2e/mobile-audit.mjs`: walks the planning routes with seeded data at 320, 360, and 390 px, and fails on horizontal overflow, text or control overlap (`elementsFromPoint` at each control's center and each text's corners), clipped text, and main controls under 44 px, with a short documented allowlist. It stores screenshots under `artifacts/`. Install Playwright WebKit and run the iPhone profile too. Every later slice runs it.
- [x] **B12:** `MealFit`, its catalog, and the `mealGuide`, with `mealNeeds` narrowed to the types still missing.
- [x] **B1:** add `POST /{id}/reopen`, setting `building`; "Escolher mais pratos" calls it.
- [x] **B2:** rebuild the list line. First line: checkbox, name (wraps freely), amount. Second line: dishes clamped to one line, plus "tem {x}" only when something is at home. All actions move behind one 44 px "⋯" control opening a sheet: Comprei, Já tenho, Não achei, Comprar em, Tamanho do pacote. The whole row toggles the check, except the "⋯". Keep every test id working inside the sheet, and keep offline rules.
- [x] **B3:** an answered request deletes the earlier unseen cards of that meal group. Raise `MaxBatches` to 12 and `MaxCards` to 120.
- [x] **B4:** the taste rule and `DishSimilarity`.
- [x] **B5:** the confirmation when something is missing.
- [x] **B6:** `PlanExpansionProvider`.
- [x] **B7:** the coarse-pointer rule; "Voltar" links gain a 44 px target.
- [x] **B8:** from a draft reached through a plan, "Voltar" returns to the review.
- [x] **B10:** the menu title shows the name only when there is one; dated dishes first, then undated, then empty days collapsed into one line; plurals with `.one` and `.other`.

**Hazards:** the list line is used offline, and the journeys click buttons that now sit in a sheet, so update `shopping-plan-journey`, `offline-list-journey`, and `session-route-journey` in the same PR. Deleting unseen cards must not delete a base waiting on an accepted dish. Keyword rules can hide good dishes ("pão de queijo" must pass, and "prato" must match only as a word), so test the rules against the session's 31 names. A library idea has the same check; `RecipeIdeaLibrary` needs no change, because the filter runs on what it returns.

**Gate S0:** mobile audit passes at all widths; B1, B3, and B12 have integration tests; `MealFit` and `DishSimilarity` have unit tests using the real session's names; journeys pass; `frontend,backend,contracts,secrets` gates pass. The owner tries a snack menu on a phone.

### Slice 1: The engine

Branch: `agent/plan-0065-s1-engine`. Backend only, and the current screens keep working.

- [x] Records and store (no migration; see the Slice 1 log).
- [x] `MealCounts`, deriving it for existing plans, with optional setup fields and `groups` (each with its options).
- [x] Meal needs per type. Lunch and dinner count separately, and a card targeting either can fill either, because both are main meals.
- [x] Decision with `mealType` and optional day; expansion and saved cards create the entry there.
- [x] `preferences`, `meal-counts`, `day`, `yield`.
- [x] `request`, `likeCardId`, single-type batches, the prompt changes, and `request_blocked`. Hint persistence stays until Slice 2 removes the old panel.
- [x] Ranking from the switches. `ConsecutiveRejections` no longer affects a plan by counts.
- [x] `GetTodayAsync`: the next undated dish per meal type instead of per plan.
- [x] OpenAPI, client, protocol and privacy documents.

**Hazards:**

- `MealNeeds` feeds the queue, `TotalMeals`, and the review. Change them together, and test a plan with 0 lunches and 7 snacks, and one with 3 lunches and 2 dinners.
- `PreparationReminders` only checks dated meals, so an undated plan raises no "buy" reminder and no notification. This is accepted for now, and the board and hub show what to buy. Say so in the product document.
- A period of 14 days with undated dishes means `ArrivalAdvisor` estimates each use with `start + floor(position × days / meals)`. Keep that.
- A repeated plan (`fromMenuId`) must copy counts, and keep dishes undated unless the original had dates.
- Keep the old batch fields until Slice 2 merges, then remove them.

**Gate S1:** integration tests for each route, isolation, idempotency, restriction on a request, privacy on a requested dish, each switch's effect on the payload (including that dislikes stay when preferences are off), and meal counts. Unit tests for ranking with each switch. Contract tests pass.

**AI evaluation (Gate S1-AI):** extend the gateway smoke with `PLAN0065_LIVE_EVAL=1` and a US$ 0.05 limit, with these scenarios: 6 snacks (how many `MealFit` drops), 6 breakfasts, a specific request ("jerk beef cozido"), a request that conflicts with a restriction, a profile with `spicy_food` (at most 2 of 6 spicy), preferences off, and an injection attempt in the text. It needs the provider key, so the owner runs it. Results go in `docs/evidence/plan-0065/`.

### Slice 2: First screen, board, options

Branch: `agent/plan-0065-s2-board`.

- [x] First screen with the counters; recent plans in a disclosure.
- [x] `ShoppingPlanBoardPage`, with groups from the server and the fixed bar.
- [x] `MealOptionsSheet`: text box, saved matches, "Pedir à IA", switches, filter, six options, "Ver outras", "Ver ingredientes", undo.
- [x] Hidden tab bar while building; "Sair (fica salvo)".
- [x] Redirect `/revisar` to the board. Remove the deck, the review, the panel, and their screens. Update hub links. Unused copy keys go in Slice 5 with the rest of the dead code.
- [x] Copy without "plano" on the building screens, in three languages.
- [x] Accessibility: rows are buttons named with meal and dish; `role="status"` for progress; focus returns to the row when the sheet closes; switches are real switches with `role="switch"`.

**Hazards:**

- A `vaul` sheet with an on-screen keyboard is the most likely thing to break on iOS. Check on WebKit and a real iPhone before calling it done.
- Plans in progress at deploy have undated entries and rejected cards, and must simply appear on the board. The owner's stuck plan is the test case.
- Saved recipes load once per sheet; keep that list in the provider so typing filters instantly.
- A batch can return after the sheet closed or moved to another meal type. Results belong to their meal type, not to the open sheet.
- Toggling switches quickly must not fire one batch per tap: wait 600 ms, and send only the last state.
- One automatic batch when a type has fewer than 6 options and no batch is running; never one per sheet opening.

**Gate S2:** new `planning-board-journey.mjs`: 7 snacks with no days, using saved recipes, at 360 px, in at most 12 taps, with a typed search and undo; a mixed menu of 3 lunches and 2 dinners; mobile audit including the open sheet and keyboard; AI failure path; three languages; keyboard only. Owner check on a phone: **"Consigo pedir o que eu quero, e só lanches?"**

### Slice 3: Acting on a chosen dish

Branch: `agent/plan-0065-s3-dish-actions`.

- [x] Actions sheet: Trocar, Marcar um dia, Rende mais, Porções, Ver receita, Remover.
- [x] "Completar": proposals for everything missing, with swap, and one confirmation.
- [x] Group counters on the board.
- [x] Bases as a quiet row on the board, not an interruption: "Base sugerida: Frango desfiado serve para Wrap e Escondidinho. Adicionar". Style `mix` keeps requesting components, except for a menu of only snacks or breakfasts.
- [x] "Distribuir nos dias", optional, in the board's menu: dates undated dishes across the period using `ShoppingPlanSequencer`, what spoils first coming first.

**Hazards:** Completar sends one decision per dish, so handle a partial failure by naming what stayed missing. `Rende mais` counts toward its type, so it cannot pass what is wanted without raising the count; ask first.

**Gate S3:** journey extended; integration for partial Completar and `yield`; mobile audit.

### Slice 4: The menu in use

Branch: `agent/plan-0065-s4-in-use`.

- [x] `/app/cardapio/:menuId`: heading, next step as on the hub, today's dishes, later dated dishes, "Quando quiser" by meal type, and the line near the end of the period. Empty days disappear. "Cozinhar" as the primary only on today's or the next dish; others keep it in the row's sheet. Archive moves to the page's menu.
- [x] "Hoje": "Quando quiser" with the next dish per meal type.
- [x] Options sheet on accepted menus, for something missing or "Trocar", as explicit single-entry commands. An accepted session reopens only for this; cards are purged as usual. A menu without a session gets one on first use. Remove `AiMealSuggestion`.
- [x] `MenusPage`: list of menus only, with "Novo cardápio".
- [x] List header: one summary line with progress, a share icon, "Por seção / Por lugar" chips. A fixed bar "Cheguei do mercado (3)" when something is marked.
- [x] "Preparar vários de uma vez": choose a day and dishes. It sets `CookOnDate`, and the route selects by it. Remove the session's `PrepDate` from new plans. `prepMinutes` becomes an optional note on the route.

**Hazards:** `CookOnDate` must not move the eating day, the reservation, or "Hoje". A cooked-ahead dish shows its stored portions, and otherwise "Já preparado". Old accepted menus keep their entries as they are. Offline, the list header must work without the server.

**Gate S4:** journeys; route with `CookOnDate`; offline journey; mobile audit; owner phone check with the route and a screen kept on (also closes G6 of PLAN-0062).

### Slice 5: Finish

Branch: `agent/plan-0065-s5-finish`.

- [ ] Screen reader pass (VoiceOver and TalkBack) and 200% zoom on board, sheet, list. Both themes. *Automated part done: `planning-a11y-audit.mjs` runs axe-core in both themes and checks 200% zoom. VoiceOver and TalkBack need a phone and stay open.*
- [x] en and es walked in a browser.
- [x] Remove dead code: legacy deck copy, `MoreOptionsAfterRejections`, unused API fields, unused prompts.
- [x] Documents: rewrite `guided-shopping-planning.md`; update `planning-and-shopping.md`, the design system (sheets, fixed bars, coarse pointer), user journeys, and privacy.
- [x] Metrics: `planning.meal_filled{source=saved|idea|ai|requested}`, `planning.request{outcome}`, time from start to acceptance. No dish or typed text in labels.
- [x] Update PLAN-0062's acceptance criteria to state what was replaced.

**Gate S5:** full gates; external test with three people (PLAN-0054 decision 8), recording whether they reach a list unaided and whether they keep a second list.

### Slice 6: "Repetir" the dish chosen last

Branch: `agent/plan-0065-s6-repeat`. Owner request of 2026-11-07: "Na escolha dos itens do cardápio, quero poder pedir pra repetir 1 ou n vezes o item anterior ao invés de ter que escolher um próximo." On 2026-11-11 the owner accepted both ways of repeating, with one bigger cooking first when the dish keeps.

- [x] `PUT /shopping-plans/{id}/cards/{cardId}/repeats` with `mode` and `meals`, as the whole state: `yield` 1 to 4 meals and 24 portions, `again` 1 to 14, null for one. Room of the meal group, idempotent, menu entries follow (removed, rescaled, or created by entry key); a dish still being prepared gets them on expansion.
- [x] Migration `RaiseShoppingPlanRepeatLimit`: the cards' check constraint allows 1 to 14. Entry lookups read the menu once instead of once per possible cooking.
- [x] Options sheet: "Repetir {prato}" for the dish chosen last in the meal type, with "Fazer uma vez e render" and "Fazer de novo", a counter, and one action; "Desfazer" reverts only the repeat.
- [x] Dish sheet: "Fazer quantas vezes?" for a dish made again each time; the board says "3 vezes" for it instead of "rende 3 refeições".
- [x] Copy in pt-BR, en, and es; product spec and planning domain updated; metric `cocinaris.planning.repeat{mode}`.

**Which way comes first.** No dish says how well it keeps, so the first choice shown follows the meal type: lunch and dinner start on one bigger cooking, the way leftovers work, while snacks and breakfasts start on making it again, because they are usually eaten fresh. A dish already repeated keeps its way. One cooking stays at 4 meals at most, as before.

**Gate S6:** targeted backend and frontend tests, the planning integration tests, and one browser check of the sheet at 360 px.

## Success measures

| Measure | Today | Target |
|---|---|---|
| Can say "7 snacks in 14 days, no days" | No | Yes |
| Full meals among offered snacks | Many in the real session, including spaghetti and bowls | None after `MealFit`, on the session's names |
| Questions before the first dish | 9 | 3 (days, start, how many) |
| Taps to a 7-snack menu from saved recipes | About 30, including setup and review | At most 12 |
| Steps to ask for a specific dish | Not possible | 2: tap the meal, type |
| Refusals needed to reach a menu | 27 in the real session | 0 |
| Dishes leading with one taste, per batch | 23 of 31 | At most 2 of 6 |
| Screen height of the first screen at 360 px | 1.8+ screens | 1 |
| List line height at 360 px | 193 to 225 px | At most 72 px |
| Main controls under 44 px on planning screens | Many | 0 |

"About 30" is an estimate from the screens, to be measured when Slice 0 adds the audit.

**Measured by the end of Slice 5 (2026-11-06), on the proxy replica:** "7 snacks in 14 days, no days" is a valid menu; the first screen asks 3 questions and fits 360 × 740; 7 snacks from saved recipes take 11 taps on the board; a specific dish is asked for in 2 steps; no refusal is needed to reach a menu; list lines stay within the audit's 90 px limit except one fixture name at 320 px; the phone audit finds no control under 44 px. Full meals among offered snacks were checked on the real session's names with `MealFit`, not on the live model, and the share of dishes leading with one taste needs S1-AI.

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Sheet and keyboard break on real iPhones | Medium | High | WebKit in the audit, real device check before each merge, plain full-screen page as fallback |
| `MealFit` hides good dishes or misses bad ones | Medium | Medium | Rules in a reviewable catalog, tests on real names, a metric per type; a typed request is never filtered |
| Six options still miss what the person wants | Medium | Medium | The text box is always first |
| The model ignores a specific request | Medium | High | Candidate 1 rule, live evaluation, and the person sees alternatives |
| Food bought for 14 days spoils unnoticed | Medium | High | Without the owner-reviewed storage table (G2-A), the app says nothing about how long food lasts. The prompt prefers what keeps, and what spoils first is offered first. Reviewing the table is what would allow a warning |
| Undated plans get no reminders or notifications | Certain | Low | "Quando quiser" on "Hoje", and the line near the end of the period |
| Plans in progress at deploy | Certain, small | Medium | Setup without counts is derived, and undated dishes are the normal state |
| Scope is large | High | Medium | Each slice ships on its own; Slice 0 is valuable alone |

## Excluded

Prices and budgets, curated pack sizes, loose list items (a decision of PLAN-0062 that stays), shared household, drag and drop, a calendar view, a server queue for expansion (PLAN-0039), and offline building of a menu.

## Execution state

- **Current run delivery target:** Slice 6, "Repetir".
- **Current checkpoint:** Slice 6 merged via #102 (`7c769b4`). The local gates on its head failed one step, `backend-format`, because the generated migration had a byte-order mark; the fix is a commit on the PLAN-0049 Slice 1 branch.
- **Last completed step:** Repeat endpoint, migration, options-sheet panel, dish-sheet counter, copy, and documents; see the Slice 6 log entry.
- **Exact next action:** Deploy `main` (the migration runs on deploy); the formatting fix reaches `main` with PLAN-0049 Slice 1. The plan then stays In Progress only for owner checks: S1-AI with the provider key (`PLAN0065_LIVE_EVAL=1`), a phone check of the sheets with the keyboard and of the route with the screen kept on, VoiceOver and TalkBack on the board, sheet, and list, and Gate S5's external test with three people. When those are recorded, mark the plan Completed.
- **Blockers:** S1-AI needs the provider key; the phone, screen reader, and external test need the owner.
- **Partially modified areas:** None.
- **Documentation delivered:** This plan and registry row; product spec rewritten; planning domain, design system, user journeys, privacy, and AI protocol documents; PLAN-0062's acceptance criteria; Slice 6 in the product spec and planning domain.
- **Validation performed:** See the Slice 6 entry in the progress log.
- **Known failures or limitations:** See the Slice 6 entry.
- **Working tree state:** Clean after this commit.

## Progress log

### 2026-11-05 — Claude Code

- **Run delivery target:** A review of the planning feature and an executable plan, at the owner's request.
- **Checkpoint:** Findings recorded; product decisions proposed for the owner.
- **Changes included in the commit:** This plan and the registry row.
- **Documentation and code-documentation delivered:** This plan.
- **Validation performed:** See `Execution state`. B1, B2, B5, B7, B8, B9, and B10 were reproduced in a browser. B3, B4, B6, and B12 come from the owner's session and the service log. B11 is unverified.
- **Result:** Ready for the owner's decisions.
- **Next action:** See "Exact next action".
- **Blockers or handoff notes:** The owner's plan on the test host is stuck in `Reviewing` with 4 of 7 snacks. It is the test case for plans in progress; do not delete it.

### 2026-11-05 — Claude Code (owner's correction)

- **Run delivery target:** Rewrite the plan after the owner's correction.
- **Checkpoint:** The owner wants to stock food to eat within up to 14 days with no fixed days, for example only snacks; to say how many days and how many of each meal; to get dishes that fit each meal; and to choose per selection whether preferences, the pantry, or manual parameters apply.
- **Changes included in the commit:** The first version made every dish dated. That recommendation is withdrawn: dates are optional, and the board groups by meal type. Slots, skipped slots, and leftover slots leave the design, which makes the data smaller. Added `MealCounts`, `MealFit`, the switches, and `planDeck.timing`. Added B12.
- **Validation performed:** Query confirming all 30 AI dishes were labeled `snack`; reading `RecipeContextAssembler`, which keeps dislikes apart from tastes, so turning preferences off can keep them.
- **Result:** Ready for the owner's confirmation of D1 and D2.
- **Next action:** See "Exact next action".

### 2026-11-05 — Claude Code (Slice 0)

- **Run delivery target:** Slice 0, after the owner said to begin. This branch started with the plan's two commits while #94 was open; #94 has since merged, and `main` was merged in.
- **Changes:**
  - **Backend:** `MealFit` with `Catalog/meal-fit-rules.json`, `DishSimilarity`, `planDeck.mealGuide`, the taste rule, one retry when fewer than 3 dishes survive, `POST /{id}/reopen`, deletion of earlier unseen AI and library dishes after an answer, 12 batches and 120 cards, order numbers that survive deletions, and migration `RaiseShoppingPlanBatchLimit`.
  - **Frontend:** `PlanExpansionRunner` (one queue for the app), the new list line, the menu with planned dishes first and empty days folded, the confirmation when meals are missing, "Escolher mais pratos" through `reopen`, "Voltar" from a draft's list, 44 px small buttons and chips on phones, and plurals.
  - **Tooling:** `scripts/e2e/mobile-audit.mjs`. `planning-journey.mjs` was stale since an earlier list redesign. Its menu and list selectors were repaired, and it still stops later, on the cooking screen, which this slice did not touch.
- **Deviations from this plan:**
  - **No mass per serving in `MealFit`.** The real session disproved it; see "Meal compatibility".
  - **Width rule instead of `pointer: coarse`.** The media query did not apply in the audit's browser, so small buttons are `h-11 md:h-9`. It also covers real browsers that misreport the pointer, and small buttons grow on narrow desktop windows.
  - **List actions open inside the line, not in a sheet.** This keeps every control and offline rule where it was, without the sheet and keyboard risk, which Slice 2 takes on.
  - **Saved recipes survive an answer;** only AI and library dishes are deleted, because the person's own recipes must not vanish.
  - **A batch asked for with typed words skips `MealFit`,** as a requested dish does.
- **Defect the tests caught:** the database limited batches to 5 by a check constraint, so raising only the code would have failed with a 500 on the sixth. The migration fixes it.
- **Validation performed:** Local gates `backend,contracts,frontend,secrets` on `b5eaf62`: all 32 steps passed, with 14 architecture, 482 unit, 247 integration, and 633 frontend tests. The next commit changes only this plan. `mobile-audit.mjs` on the proxy replica: 48 screens at 320, 360, and 390 px, 0 problems. `shopping-plan-journey`: 19 checks. `planning-entry-journey`: 16. `offline-list-journey`: 25. `session-route-journey`: 15. `planning-journey`: 5 checks through menu, list, and purchase, then it stops at a stale cooking selector, so the rest did not run. List and menu screenshots read at 360 px. Rules tested against the names of the real session, including all 4 dishes the owner accepted.
- **Not run:**
  - **WebKit.** It installed but does not start here: the host lacks system libraries such as GTK 4 and GStreamer, and installing system packages was not asked for. The audit runs it only with `COCINARIS_AUDIT_WEBKIT=1`. B11 stays unverified.
  - **The real model.** How well it follows `mealGuide` and the taste rule is unknown, and G1-B and G5-A have not run.
  - A real phone, screen reader, and 200% zoom. `frontend-smoke`, which does not finish on this host.
- **Limitations:** the deck, review, and setup screens remain until Slice 2, including the native date field. "Plano" stays in the copy until D1 and D2. `MealFit` can still miss a full meal whose name and format have none of its words, and the prompt is the only protection there.
- **Next action:** See "Exact next action".

### 2026-11-05 — Claude Code (Slice 1)

- **Run delivery target:** Slice 1, after the owner merged #95 and confirmed D1 and D2.
- **Changes:**
  - **Setup.** `ShoppingPlanSetup` gains `MealCounts` and `PeriodStart`. `Counts()` derives counts for older plans (meals eaten out come off lunch, then dinner). `TotalMeals`, `MealNeeds`, and `StartDate` read the counts. A plan by counts validates each count (0 to 40, 1 to 40 in all), days 1 to 14, and a first day from today to 7 days ahead; style is `mix`, goal `none`, and there is no preparation day. A repeated plan copies the counts. The setup is now saved on every change.
  - **Switches.** `ShoppingPlanDeckPreference` gains `UsePreferences` and `Goal`. A plan by counts starts with tastes on and the pantry off, or with the last plan's switches; `/start` returns them. `PUT /{id}/preferences`.
  - **Choosing.** The decision takes `mealType` and `plannedDate`, saved in the card snapshot. `PUT /{id}/meal-counts`, `PUT /{id}/cards/{cardId}/day`, and `PUT /{id}/cards/{cardId}/yield`. Undo clears the day.
  - **Asking.** A batch can name one `mealType`, a `request` (120 characters), a `likeCardId` of the same plan, and `replaceOffers`. Requested dishes skip `MealFit` and `DishSimilarity`, come first in their meal, keep the words on the card, and stay out of the shared library. `request_blocked` names the restriction when a request is fully blocked.
  - **Prompt.** `planDeck.requestedDish`, `planDeck.timing`, empty `userPresets.preferences` with tastes off, single-meal `mealTypes`/`mealNeeds`, dishes on screen added to `rejectedDishes`, which now lists the newest first, and no base for a plan without lunch or dinner.
  - **Ranking.** The goal switch maps to `time`, `short`, or `save`; the pantry on with no goal means `waste`. A chosen goal counts in full with the pantry off.
  - **View.** `groups` (wanted, filled, six options), `preferences`, and `requestText` on cards. "Hoje" shows the next flexible dish of each meal type.
  - **Evaluation.** `PLAN0065_LIVE_EVAL=1` in `scripts/ai/RecipeGatewayLiveSmoke` runs the seven S1-AI scenarios with a US$ 0.05 limit and writes `docs/evidence/plan-0065/s1-live-eval.json`.
- **Deviations from this plan:**
  - **No migration.** `CookOnDate` moves to Slice 4, which uses it. Everything else lives in JSON columns that already exist.
  - **`groups` carry their options** instead of a separate `queues` object, so the board reads one list.
  - **Day and yield are per card, not per entry,** because a dish chosen and not yet expanded has no entry. Each route updates the entry too when it exists.
  - **Counts are checked per group.** Lunch and dinner are interchangeable, so lowering lunches below the lunches chosen is accepted while the main meals still fit, and a dish yielding more than its meal fills the other. Choosing still needs room in the named meal.
  - **Hint persistence stays** until Slice 2 removes the old panel, because the current screens still send it.
- **Defects caught:** a new plan's switches were not written on creation, so the first batch still sent the pantry; the store now writes them. The first gate run (`78451e2`) failed `frontend-typecheck` because `replaceOffers` had a default and became required in the contract; it is now optional (`bool?`, false when absent). Every other step of that run passed: 14 architecture, 487 unit, 263 integration, and 633 frontend tests.
- **Validation performed:** Unit `ShoppingPlanTests` (63) and the planning integration tests (34, of which 16 are new) pass, and the 18 affected ones again after the contract fix. OpenAPI exported, client regenerated and mirrored, drift check passed, frontend typecheck passed, lint with the same 9 warnings as `main`. The live evaluation script builds. One more local gate run on the pull request head; results are in the pull request.
- **Not run:** the live evaluation (needs the provider key); browser journeys, because no screen changed.
- **Next action:** See "Exact next action".

### 2026-11-05 — Claude Code (Slice 2)

- **Run delivery target:** Slice 2, after the owner merged #96.
- **Changes:**
  - **First screen.** Days (3, 5, 7, 14, other), start (today, tomorrow, or a chip for one of the next days, never a date field), four counters, people, and "Montar cardápio (N)". Untouched counters follow the days. Nothing about cooking. "Repetir um cardápio" folded underneath. It fits 360 × 740 above the tab bar.
  - **Board.** Groups from the server, chosen dishes with time, coverage, an optional day tag, and preparation state; one "Escolher … · faltam N" row per meal type; a fixed bar with progress, "O que comprar", and "Usar este cardápio" (confirmation when meals are missing, disabled while recipes are prepared). A chosen dish opens "Ver receita", "Tentar de novo", and "Remover". The tab bar hides on phones while building.
  - **Options sheet.** Text box with the person's recipes matching as they type (loaded once per visit) and "Pedir à IA"; the two switches, debounced 600 ms, then one replacing request; the goal filter; six options with ingredients, "Quero parecido", and "Não sugerir isso"; "Já neste cardápio"; "Ver outras"; skeleton rows while waiting; undo for 5 seconds; it closes when the meal type is complete. Requests run one at a time through `useMealBatches`, and a result belongs to its meal type. One automatic request when a type has fewer than six options, at most two per visit.
  - **Backend.** `POST /{id}/recipes/{recipeId}/choice` chooses a saved recipe for any meal, with the restriction guard; the accepted view adds `plannedDate` and `totalMinutes`.
  - **Removed.** `ShoppingPlanDeckPage`, `ShoppingPlanReviewPage`, their tests, and `more-options-journey.mjs`. `/revisar` redirects to the board.
  - **Tooling.** New `planning-board-journey.mjs`; `shopping-plan-journey.mjs` and `mobile-audit.mjs` walk the board and sheets.
- **Deviations from this plan:**
  - **Radix Dialog instead of `vaul`.** A drag drawer fights the on-screen keyboard on iOS, the hazard this slice names; a dialog styled as a bottom sheet keeps focus trap, Escape, and focus return without gestures.
  - **Choosing a saved recipe needed a route,** because the sheet finds recipes saved for other meals, which have no card.
  - **"Completar" and the actions sheet's day, yield, and portions** stay in Slice 3 as planned; Slice 2's dish sheet has only what the old review had (see the recipe, retry, remove).
  - **Copy keys of the removed screens** are left for Slice 5's dead-code pass, so this diff stays reviewable.
- **Validation performed:** Frontend typecheck, lint, format, and the shopping plan, planning, and app suites (91 tests in the two feature folders). Backend: the new route's integration test and the affected ones. OpenAPI and client regenerated, drift check passed. Proxy replica at 360 px: `planning-board-journey` 18 of 18 (7 snacks with no dates in 11 taps on the board, typed search, undo, 3 lunches and 2 dinners, AI failure, keyboard only, en and es); `shopping-plan-journey` 19 of 19; `planning-entry-journey` 16 of 16; `session-route-journey` passed; `mobile-audit` 57 screens at 320, 360, and 390 px with 0 problems, including the open sheet, typed text, details, and confirmation. Local gates run on the pull request head; results are in the pull request.
- **Not run:** WebKit and a real iPhone with the keyboard (WebKit does not start on this host), the real model, a screen reader.
- **Next action:** See "Exact next action".

### 2026-11-06 — Claude Code (Slice 3)

- **Run delivery target:** Slice 3, after the owner merged #97 and wrote "Assuma as revisões como aceitas e siga o trabalho."
- **Changes:**
  - **Backend.** `PUT /{id}/cards/{cardId}/people` (people for one dish, 1 to 12, stored on the card as `People`; entries, ranking, and list scale to it; at most 24 portions with "Rende mais"; undo forgets it). `POST /{id}/choices` chooses 1 to 40 offered cards in one request, each as its own decision, and returns the plan plus `failed`. `POST /{id}/distribution` sequences the draft and spreads each meal type's undated entries over the period, or with `clear` removes every day; it refuses with `expansions_pending` while a recipe is being prepared. The accepted view adds `people`.
  - **Frontend.** `DishActionsSheet` (Ver receita, Trocar, day chips with "Sem dia", "Rende quantas refeições?" with a confirmation before raising the count, people counter, Tentar de novo, Remover). `CompleteSheet` (one proposal per missing meal, never the same dish twice, "Trocar" per row, one automatic request per meal type short of offers, "Usar estes (N)", and the names of what failed with what is still missing). A compact counter on each group heading, debounced 600 ms. Suggested bases as a quiet row with "Adicionar" and "Agora não". "Mais opções", folded, with "Adicionar refeição", "Distribuir nos dias", and "Tirar os dias". The bar's main action is "Completar (N)" while meals are missing. `Counter` moved to its own module.
  - **Tooling.** `planning-board-journey.mjs` covers Completar, a day, "Rende mais" with its confirmation, the group counter, distribution and its undoing, and "Trocar". `mobile-audit.mjs` audits the Completar sheet, and its overlap rule now ignores content scrolling under a fixed bar; instead it fails anything still under a bottom bar with the page scrolled to its end, which is the real defect.
  - **Owner gates of PLAN-0062.** G1-A and G2-A recorded as accepted; see PLAN-0062's log of this day.
- **Deviations from this plan:**
  - **"Completar" is one request, not one decision per dish.** A server route makes the partial outcome testable, keeps the order of choices, and spends one rate-limit token instead of up to 40; each dish is still its own decision, so the hazard's rule holds.
  - **"Porções" is people per dish,** because portions on the board follow people times the meals a dish covers; a separate portions number would contradict "Rende mais".
  - **"Trocar" removes the dish first,** then opens its meal's options. A full meal type has no offers to swap to, so keeping the old dish until a new one is chosen would show an empty sheet.
  - **Group counters cannot go below what is chosen in that meal type** on screen, which is stricter than the server's rule for lunch and dinner; the server still answers `meal_count_below_chosen` for the rest.
  - **"Distribuir nos dias" and adding a meal type live in a folded "Mais opções"** at the end of the board, the board's menu in this plan's words, so the board keeps to its dishes.
- **Validation performed:** Integration: the three new tests (partial and repeated Completar with isolation, people with yield limits, distribution with pending refusal and clearing) and the three affected ones pass. Unit: 67 planning, restriction, and storage tests. Frontend: typecheck, lint, format, and 39 shopping-plan tests (5 new) plus the planning suite. OpenAPI exported, client regenerated, drift check passed. Proxy replica at 360 px: `planning-board-journey` 28 of 28; `shopping-plan-journey` 19 of 19; `planning-entry-journey` 16 of 16; `session-route-journey` passed; `mobile-audit` 60 screens with 1 problem. The first runs of the shopping-plan and entry journeys failed on the `mutation` limit after back-to-back runs, and passed when rerun after the window.
- **Local gates on `4b7a7e1`:** 31 of 32 steps passed (14 architecture, 487 unit, 267 integration, and 628 frontend tests). `backend-format` failed: the edit script added a UTF-8 BOM to `ShoppingPlanEndpoints.cs`, `ShoppingPlanApplicationService.cs`, and `ShoppingPlan.cs`, which `main` has without one. The next commit removes the three BOMs and nothing else; `dotnet format whitespace --verify-no-changes` passes on the three projects. The gates run once more on that head.
- **Not run or not passing:**
  - `mobile-audit`: the list at 320 px has a 110 px line, because a fixture name left by `planning-entry-journey`, "Item Entrada E2E 1793901123754", wraps on its 13-digit number. The list did not change in this slice; real names do not have that shape. Left as a known fixture limit.
  - WebKit and a real iPhone with the keyboard (WebKit does not start on this host), the real model, a screen reader.
- **Next action:** See "Exact next action".

### 2026-11-06 — Claude Code (Slice 4)

- **Run delivery target:** Slice 4, the menu in use, after the owner merged #98 ("Merge feito, continue").
- **Changes:**
  - **Backend.** Migration `AddCookOnDateAndAmendments`: `menu_entries.CookOnDate` (date, null) and the plan status `Amending`; both additive. `PUT /menus/{id}/cook-day` ("Preparar vários de uma vez") sets the cooking day of the listed dishes and clears it from dishes that had that day and were left out; the day is at most 7 days before the menu, inside the period, and never after the day a dish is eaten, and only a menu in use accepts it. `POST /shopping-plans/amendments` opens an accepted menu for choosing again, optionally swapping one dish (`replaceEntryId`); a menu built by hand gets a plan there; chosen cards are rebuilt from the menu's dishes on every opening; the owner's recipes and library ideas are offered at once. While amending, counts, review, distribution, replacements, and discarding are refused; added dishes take the next places in the order; `acceptance` closes the change. `GET /shopping-plans/menu-link`. `recipes/{id}/choice` takes an optional `plannedDate`. The preparation route's day comes from the cooking days.
  - **Frontend.** `MenuPage` rewritten: next step as on "Planejar", "Hoje", "Próximos dias", "Quando quiser" by meal type with "Escolher … · faltam N", folded past days, no empty days, "Cozinhar" in the row only for the next dish, the end-of-period line, and "Mais opções" with "Preparar vários de uma vez" and "Arquivar cardápio". `MenuEntrySheet` (cook, recipe, swap, day chips, portions, pantry reservation, skip, remove). `CookAheadSheet`. `MenuChoosing` opens the options sheet on a menu in use and closes the change when the sheet closes and nothing is being prepared. The options sheet's logic moved out of the board into `useMealChooser`, shared by both. `MenusPage` is the list and "Novo cardápio". "Hoje" shows undated dishes under "Quando quiser". The list header is one summary line with a share icon and "Por seção / Por lugar" chips, and a bar "Cheguei do mercado (N)" stays above the tab bar once something is marked. `AiMealSuggestion` removed.
  - **Tooling.** `planning-board-journey.mjs` walks the menu in use (sections, swap and its undo, swap for another dish, cooking ahead and its route, "Hoje"). `mobile-audit.mjs` audits the dish sheet, the cook-ahead sheet, and the options sheet on the menu in use. `planning-journey.mjs` makes its hand-built menu through the API, because the form is gone. `shopping-plan-journey.mjs` and the audit use the new chips.
- **Deviations from this plan:**
  - **"Reopens" is a status of its own, `Amending`,** because the one-plan-in-progress rule must not block a swap while a new menu is being built, and the route and repeat must still see the plan as accepted. Every opening rebuilds the chosen cards from the menu, so dishes removed or skipped on the menu page count as they are; cards are purged when the change is closed.
  - **"Trocar" removes the dish when the sheet opens,** as on the board, in the same request as opening the change; the page offers to put it back ("Desfazer"), which adds the same dish again. The new dish keeps the old one's day.
  - **"Preparar vários de uma vez" is one request for several dishes.** It says which dishes are cooked on that day; it changes only `CookOnDate`, never the eating day, the reservation, the order, or "Hoje", so invariant 4 holds. The route reads `CookOnDate`; `CookOnDate ?? PlannedDate` applies only to plans whose setup has a preparation day, so a dish eaten that day is not pulled into a session the person did not ask for.
  - **No time note on the route for new plans.** Plans by counts ask for no preparation time, and the route already shows its length; older plans keep their budget.
  - **A cooked-ahead dish reads "Já preparado".** Its stored portions are already on "Hoje" through the `ready_portions` reminder (PLAN-0062 Slice 5), so the menu row does not repeat them.
  - **"Cozinhar" is the main action only when there is no next step.** When something is left to buy, put away, or prepare, that step is the main action and "Cozinhar" is secondary, so a screen keeps one main action.
  - **A menu built by hand gets no new dishes by hand.** The day-by-day "Adicionar refeição" form went with `AiMealSuggestion`; such a menu can still be swapped from once in use, and new menus are built on the board.
  - **The arrival bar is sticky, not fixed,** above the tab bar, so it rests after the list at its end and never covers the last line.
- **Validation performed:** Integration: the three new tests (cooking ahead with its route and limits and isolation; filling and swapping on a menu in use with order, day, closing, and refusals; a menu built by hand) and the whole planning class, 80 tests. Unit: 488, with a new domain test for the cooking day. Frontend: typecheck, lint, format, and the full suite, 631 tests (7 new in `MenuInUse.test.tsx` and one for "Hoje"; five tests of the removed form, day boxes, and AI suggestion were replaced). OpenAPI exported, client regenerated and mirrored, drift check passed. Proxy replica at 360 px, on the final build: `planning-board-journey` passed all 38 checks, 10 of them new; `shopping-plan-journey`, `offline-list-journey`, `session-route-journey`, and `planning-entry-journey` (16 of 16) passed. `mobile-audit`: 69 screens at 320, 360, and 390 px, including the dish sheet, the cook-ahead sheet, and the options sheet on the menu in use, with 1 problem: the known 110 px list line at 320 px from the fixture name of `planning-entry-journey`. The first audit run found a row where "Cozinhar" beside the dish squeezed its name and a summary line whose two parts wrapped into each other at 320 px; both were fixed, and the audit's overlap rule now treats a sticky bar like a fixed one. The first `shopping-plan-journey` run missed G1-C's 1 s target (1583 ms) while another journey ran in parallel, and passed alone. The first run of the user-B journeys failed at sign-in because the runner passed user A's password; rerun with the right one.
- **Not passing, outside this slice:** `planning-journey` passes the menu, list, and purchase steps and stops at the cooking screen, as since Slice 0. `reservations-journey` passes the menu's readiness checks and stops on the lot page, whose reservations sit in a folded section the journey does not open; that page did not change here.
- **Not run:** WebKit and a real iPhone with the keyboard; the route with the screen kept on, on the owner's phone (Gate S4 and PLAN-0062 G6); the real model; a screen reader.
- **Environment note:** the dev database needed the new migration (`dotnet ef database update` with `COCINARIS_DB_CONNECTION`) before the replica could load menus.
- **Next action:** See "Exact next action".

### 2026-11-06 — Claude Code (Slice 5)

- **Run delivery target:** Slice 5, finish, after the owner merged #99 ("Pode seguir").
- **Changes:**
  - **Dead code.** Routes `PUT …/cards/{id}/reason`, `POST …/review`, `POST …/replacements`, and `POST …/reopen` removed with their service methods; the view's `needsBatch`, `offerMoreOptions`, `exhausted`, and `canUndo` removed with `MoreOptionsAfterRejections`; the PLAN-0064 batch fields (families, cuisine, time, pantry, similarity, reference card, hint), `ShoppingPlanDirection`, and the plan deck's `suggestionGuidance` and `personHint` removed. The frontend contract, adapter, and test doubles follow; 128 copy keys no code used any more (deck, review, panel, AI per slot, form by dates) left the three languages.
  - **Metrics.** `cocinaris.planning.meal_filled{source}`, `cocinaris.planning.request{outcome}`, and `cocinaris.planning.time_to_acceptance`, with closed codes only.
  - **Accessibility.** New `scripts/e2e/planning-a11y-audit.mjs`: axe-core (WCAG 2.0 to 2.2, A and AA) on the board, options sheet, menu in use, dish sheet, list, and hub, in pt-BR light and dark, en, and es; copy keys on screen; and 200% zoom emulated as 640 × 360 at a device scale of 2.
  - **Journeys.** `planning-journey` and `reservations-journey`, broken before this plan by guided cooking and a folded lot section, reach the cooking review through the guided steps and "Ajustar", and open the folded reservations; `reservations-journey` ends its cooking session through the API, because guided cooking has no "abandon" on the review.
  - **Documents.** Product spec rewritten for the current flow; planning domain (removed routes, batch fields, metrics, order, slot suggestions), design system (sheets, fixed and sticky bars, both audits), user journeys, privacy (typed hint, metrics), AI protocol (`personHint` and `suggestionGuidance` no longer sent by plan decks), and PLAN-0062's acceptance criteria.
- **Deviations from this plan:**
  - **Batches without a meal type stay valid.** They ask for whatever is still missing, and PLAN-0062's tests and older clients use them; only the panel's fields went.
  - **Tests of removed behavior were removed or moved.** `ShoppingPlanMoreOptionsApiTests` became `ShoppingPlanAskingApiTests`, keeping nothing new, a failed request, meal fit, near-duplicates, and an answer replacing what waited, now asked by meal type; the tests of the panel, its hint, and reopening the review went with them. Review steps in other tests became acceptance, which also sets the order.
  - **The screen reader pass is automated only.** VoiceOver and TalkBack need a phone; the box stays open.
  - **`ConsecutiveRejections` stays** as stored data that older plans' pantry weight reads; nothing shows it any more.
- **Validation performed:** Integration: 74 planning tests (6 tests of removed behavior gone). Unit: 488. Frontend: typecheck, lint, format, and 631 tests. OpenAPI exported, client regenerated and mirrored, drift check passed. Proxy replica: `planning-a11y-audit` 30 screens with 0 violations, 0 copy keys, and no sideways scroll at 200%; `mobile-audit` 69 screens with 0 problems; `planning-board-journey`, `shopping-plan-journey`, `planning-entry-journey` (16 of 16), `planning-journey`, and `reservations-journey` passed. The first `planning-entry-journey` run failed because the first version of the accessibility pass left a menu being built for the same user; the pass now discards it, and both passed on rerun.
- **Not run:** VoiceOver and TalkBack; WebKit and a real iPhone; the real model (S1-AI); the external test with three people (Gate S5).
- **Next action:** See "Exact next action".

### 2026-11-11 — Claude Code (Slice 6)

- **Run delivery target:** Slice 6, "Repetir", from the owner's request of 2026-11-07 ("quero poder pedir pra repetir 1 ou n vezes o item anterior ao invés de ter que escolher um próximo") and the owner's answer of 2026-11-11 ("Siga conforme sua sugestão": both ways, one bigger cooking first when the dish keeps). Registry reconciled in the same run.
- **Changes:**
  - **Backend.** `SetCardRepeatsAsync` and `PUT /shopping-plans/{id}/cards/{cardId}/repeats` (`mode`, `meals`), as the whole state; `yield` 1 to 4 and 24 portions, `again` 1 to 14 (`MaxCookings`), room of the meal group, idempotent; entries past the count are removed, the first is rescaled, and missing ones are created by entry key; a pending dish gets them on expansion. `DecideAsync` and repeated plans accept the same limits. Entry lookups by key (`CardEntriesAsync`) read the menu once. Migration `RaiseShoppingPlanRepeatLimit` widens `ck_shopping_plan_cards_repeats` to 1 to 14. Metric `cocinaris.planning.repeat{mode}`.
  - **Frontend.** `RepeatPanel` in the options sheet for the dish chosen last in the meal type ("Fazer uma vez e render" or "Fazer de novo", "Quantas vezes a mais", "Repetir mais N vezes"); "Desfazer" after a repeat sends the earlier state instead of the server's undo; "Fazer quantas vezes?" in the dish sheet for a dish made again; the board says "N vezes" for it. Repository `setCardRepeats`; limits shared by the dish sheet and the panel. Copy in pt-BR, en, and es.
  - **Documents.** Product spec (options sheet, dish sheet), planning domain (route, limits, migration rollback note, metric), this plan, and the registry.
  - **Registry reconciled.** PLAN-0059, 0061, 0062, 0063, 0064, and 0021 were listed as draft pull requests that the owner had merged (#77, #82, #101, #92, #93, #78); their rows and "Exact next action" lines now say merged and deployed with `7333b25`. PLAN-0037's next action now says the table was accepted (PLAN-0062 G2-A) and only the flag remains.
- **Deviations from this plan:** None from the slice as written; the way shown first follows the meal type because no dish carries how well it keeps.
- **Validation performed:** Integration: 77 planning tests, 2 of them new (both ways, switching, limits, idempotency, other owner, a pending dish, removal past the fourth key). Unit: 489. Architecture: 14. Frontend: typecheck, lint, format, and 636 tests (3 new). OpenAPI exported, client regenerated and mirrored, drift check passed. Proxy replica at 360 px: `planning-board-journey` 40 of 40, now building the seven snacks with five choices and one "Repetir" (11 taps); the panel is 326 px wide. `mobile-audit`: 69 screens with only the known 110 px fixture line at 320 px; its "sheet-chosen" screen shows the panel on a dinner at 320, 360, and 390 px. The first audit runs failed before any screen: the wrong fixture user, then the rate limit right after the journey; neither was a product failure.
- **Not run:** the full local gates (one run on the pull request head follows); the real model; a real phone.
- **Rollback:** the migration's rollback fails while any card repeats more than 4 times; repair forward.
- **Next action:** See "Exact next action".

### 2026-11-11 — Claude Code (Slice 6 gates)

- **Result:** Owner merged #102 ("Merge feito, continue"). The local gates on `1a01a7f` failed `backend-format` with `CHARSET` on `20261111121111_RaiseShoppingPlanRepeatLimit.cs`, which `dotnet ef` wrote with a byte-order mark. The mark is removed in a commit on `agent/plan-0049-privacy-center`; nothing else changed. The remaining gate steps are reported on #102.
- **Next action:** See "Exact next action".

## Completion and handoff checklist

- [ ] All slices and success measures are resolved truthfully.
- [ ] Required tests and evaluations pass, or limitations are documented.
- [ ] Durable documentation, contracts, comments, XML documentation, and TSDoc are current.
- [ ] Security, privacy, safety, localization, accessibility, AI, and operational impacts were reviewed.
- [ ] `docs/plan-status.md` matches this plan.
- [ ] Pull requests link this plan and report validation evidence.
- [ ] Exact continuation instructions exist if the work is not complete.
