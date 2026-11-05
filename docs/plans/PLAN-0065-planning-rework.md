# PLAN-0065: Planning Rework ("Montar o cardápio")

- **Status:** Proposed; waits for the owner's answers to D1 to D4
- **Type:** Implementation
- **Priority:** High (owner request)
- **Owner:** Unassigned; the next execution agent claims it
- **Created:** 2026-11-05
- **Last updated:** 2026-11-05
- **Branch:** Plan publication `agent/plan-0065-planning-rework`; each slice uses the branch named in its section
- **Pull request:** Plan publication draft PR, opened with this file
- **Related issues:** None
- **Related ADRs:** ADR-0009, ADR-0012, ADR-0013
- **Dependencies:** PLAN-0062, PLAN-0063, PLAN-0064 (all merged). This plan replaces the deck and review screens of PLAN-0062 Slice 1 and the panel of PLAN-0064.

## How to use this plan

Execute one slice at a time, in order. A slice is one pull request from a freshly fetched `origin/main`, and the next starts only after the owner merges the previous one. Agents never merge, approve, or enable auto-merge.

- **Slice 0 needs no owner decision.** Slices 1 to 5 start only after the owner answers D1 to D4.
- **Deviation rule:** if the code contradicts a detail here, keep the outcome and the invariants, adapt the mechanics, and record the deviation in the progress log.
- **Stop conditions:** a decision below is unanswered, the code cannot satisfy an invariant, or an unrelated failure blocks the gates.
- Before every commit, update `Execution state`, `Progress log`, and the registry row.

## Objective

Let someone build a week of meals on a phone in about a minute, asking for exactly what they want, letting Cocinaris suggest the rest, and always seeing the whole week. The person reaches a usable list without answering abstract questions first and without refusing dishes one by one.

## Why: what the review found

The review ran on 2026-11-05 at `fc17bfe`. It read every screen and the backend, walked the flow in a browser at 320, 360, and 390 px with measurements, and read the owner's real session on the test host. Headless Chromium was used; no real phone and no WebKit.

### The owner's real session

| Measure | Value |
|---|---|
| Questions on the setup screen | 9, before any dish appears |
| Dishes offered | 31 |
| Refused / accepted | 27 / 4 |
| Refusals in a row at the end | 13 |
| Refusals between 15:16:02 and 15:16:32 UTC | 12, clearing old dishes to reach the rest |
| Batches used | 5 of 5, so no more could be asked for |
| Dishes with "picante", "apimentado", "pimenta", or "jerk" in the name | 23 of 31; the profile has `spicy_food`, and the prompt treats it as a rule for every dish |
| Near-duplicates | 3 variations of pão de queijo; formats are free text, so `bolinho frito` and `bolinho assado` count as different |
| Typed hint | "Jerk beef, cozido e com desidratador": a specific recipe, sent as a soft wish |
| Result | Plan stopped in `Reviewing` with 4 of 7 meals filled, never accepted, all 4 dishes undated |
| AI time | Batch 21 s on average, 32 s at most; recipe expansion 17 s each, in series |
| AI cost of the session | About US$ 0.09, so cost is not the constraint |

### Structural problems

1. **Decisions come before their consequences.** Style, priority, preparation day, and preparation time are asked before any dish exists, and on the phone the setup takes 1.8 screens, or 3 at 320 px with recent plans.
2. **Two questions do nothing for most dishes.** "Quando vai preparar?" and "Quanto tempo?" only reach a dish through "Sim, 2 vezes" with "Rende". A plain "Sim" has no date, so the route link never appeared in the test.
3. **There is no way to ask for a dish.** Search exists only for saved recipes, and only on the menu page. The anchor field accepts one ingredient, and the hint is a soft wish.
4. **Yes or no on one dish at a time.** Nothing can be compared, and the chosen dishes are not visible while choosing. "Refeição 1 de 5" does not say which day.
5. **Three screens for one object, three ways to build it.** Setup, cards, and review lead to a separate menu page, alongside the manual menu form and "Sugerir com IA" per slot.
6. **The week is hidden.** Without dates, an accepted menu opens with "Nada planejado" on every day and the dishes at the bottom under "Sem dia definido".
7. **Names.** "Plano", "cardápio", "lista", and "o que comprar" describe overlapping things.

### Confirmed defects

| # | Severity | Defect | Evidence |
|---|---|---|---|
| B1 | High | "Escolher mais pratos" returns to the review at once: the deck redirects any plan in `reviewing`. A plan can be stuck below its meals | Browser, and `ShoppingPlanDeckPage.tsx` load effect |
| B2 | High | On a list line at 360 px, "800 g" sits under "Já tenho"; the dish name wraps over 5 lines; each line takes 193 to 225 px | Browser measurement and screenshot |
| B3 | High | After a request, old dishes stay queued, so the person refuses them all to get through | 12 refusals in 30 s in the real session |
| B4 | High | The model applies one taste to almost every dish and offers near-duplicates | Real session |
| B5 | Medium | "Usar este plano" accepts 3 of 7 meals with no word about the rest | Browser |
| B6 | Medium | Expansion runs per page, so the deck and the review race: 12 duplicate-key errors in the log during the session. Leaving the screen stops it | `journalctl`; `useExpansionQueue` |
| B7 | Medium | Controls under 44 px on the phone: chips 36 to 40, "Trocar" and "Remover" 36, "Não achei" 24, "Voltar" 20, the package link 16 | Measurement |
| B8 | Medium | From the draft list, "Voltar para o cardápio" leaves the flow for a page with its own accept button | Browser |
| B9 | Low | The review shows a number field and a date field per dish: 2.6 screens for 4 dishes | Measurement |
| B10 | Low | The menu title repeats the period; at 320 px the "+" and AI buttons wrap; "1º a cozinhar" means little; "+1 itens novos" | Browser |
| B11 | Unverified | `datalist` does nothing on iOS Safari, and native date inputs may overflow there | Known platform behavior; to be checked with WebKit and a real iPhone |

## Owner decisions to confirm

The plan is written for the recommended answer to each.

| # | Decision | Recommended | Reverses |
|---|---|---|---|
| D1 | Main way to build | **A board of the week, with a list of options per meal.** Dish-by-dish yes or no goes away | PLAN-0062 "Dish-by-dish cards" |
| D2 | Days | **Every dish sits on a day and meal**, chosen by where it was tapped, and can be moved | PLAN-0062 "a fixed date is optional" |
| D3 | First screen | **Only days, start, and meals.** People stays as one line. Cooking style and meals eaten out leave; priority becomes a filter; preparation moves to after the menu is in use | PLAN-0062 "Asked in every plan" |
| D4 | Names | **"Cardápio" everywhere.** "Plano" leaves the interface; the tab stays "Planejar" | PLAN-0063 copy "Começar um plano" |

If D1 is refused, only Slice 0 applies, and the plan must be rewritten.

## Invariants

These hold in every slice, and tests protect each that can be tested.

1. **AI never mutates state.** Output is validated, and only deterministic commands change menus, recipes, or the pantry.
2. **Restrictions are hard rules.** No filter, typed text, or request relaxes them. A typed request whose result is blocked says why.
3. **Drafts reserve nothing.** Only "Usar este cardápio" reaches "Hoje" and reserves stock.
4. **An accepted menu is never silently rewritten.** Each change is one explicit command on one entry.
5. **Private words stay private.** A dish made under typed text carries it through expansion and never reaches the shared idea library.
6. **Typed text is untrusted,** bounded, in its own prompt field, and never logged.
7. **Degraded mode works.** With AI off, saved recipes, ideas, and search still fill a week, and typed requests say AI is unavailable.
8. **Owner isolation.** Another owner's identifier returns 404.
9. **The offline list keeps working** (ADR-0012), including being saved before the market without having been opened.
10. **Localization.** All copy in pt-BR, en, and es; the backend returns codes.
11. **Answers do not update the profile.**

## The new flow

### Decision moments

| Decision | Today | New moment |
|---|---|---|
| Days, start, meals | Setup, with 7 other questions | First screen, alone |
| People | Setup | One line on the first screen and the board |
| What to eat at a meal | One yes or no at a time | Tap that meal, then type or tap from a list |
| What matters (use the pantry, fewer purchases, quick, cheap) | Setup, before any dish | A filter above the options, changeable at any time |
| Meals eaten out | Setup stepper | Tap the meal: "Não preciso desta refeição" |
| Cook once, eat twice | "Sim, 2 vezes", then a sheet | On a chosen dish: "Rende mais refeições" |
| Which day | Native date field per dish, optional | Implied by the tap; "Mudar de dia" to move |
| Preparing several together | Setup, before any dish | After the menu is in use: "Preparar vários de uma vez" |
| Pack size, store | On each line | Inside the line's menu |

### Screens

**First screen (`/app/compras/planejar`).** Fits one phone screen at 360 × 740 without scrolling: "Pra quantos dias?" (3, 5, 7, outro), "Começando" (hoje, amanhã, outro dia), "Quais refeições?", the line "Para 2 pessoas · Alterar", and primary "Montar cardápio". Below, "Repetir um cardápio" as a disclosure. Last answers are preselected. With a menu in progress it shows "Continuar cardápio".

**Board (`/app/compras/planejar/:sessionId`).** One row per meal, grouped by day, on one surface.

- An empty meal is a full-width row: "Escolher jantar".
- A chosen meal shows the name (at most 2 lines), "35 min · +3 itens", and a state when its recipe is being prepared. Tapping it opens its actions.
- The bottom tab bar is hidden on phones while building, as in guided cooking. A close control at the top says "Sair (fica salvo)".
- A fixed bottom bar shows "3 de 7 · 9 itens" and the primary action: "Completar" while meals are empty, "Usar este cardápio" when none are. "O que comprar" is a secondary link above the bar.

**Options sheet.** A bottom sheet at 90 dvh, opened from an empty meal. Title: "Jantar · sex., 7 nov".

- **Text box first:** "O que você quer comer?", placeholder "feijoada, algo com frango, jerk beef…", up to 120 characters.
- **While typing:** saved recipes that match, then always one row: "Pedir à IA: {text}".
- **Without text:** a filter row (Usa o que tenho, Menos compras, Rápido, Econômico; none chosen by default), "Já neste cardápio" to repeat a dish, and six options at once. Each shows name, time, "+N itens", and what it reuses.
- **Tap an option** to choose it. A notice offers "Desfazer" for 5 seconds, and the sheet moves on to the next empty meal.
- **"Ver ingredientes"** on a row expands it, and offers "Quero parecido" and "Não sugerir isso".
- **"Ver outras"** at the end fetches six more.
- A wait shows skeleton rows in the list's shape, and existing options stay usable.

**Actions on a chosen meal.** A sheet: Trocar, Mudar de dia, Rende mais refeições, Porções, Ver receita, Remover. "Não preciso desta refeição" appears on an empty meal.

**Completar.** A sheet proposing one dish for each empty meal, with a swap control per row, and "Usar estes". Nothing changes until confirmed.

**Accepting with empty meals.** Asks once: "Faltam 2 refeições. Usar assim?", with "Usar assim" and "Continuar escolhendo".

### What goes away

`ShoppingPlanDeckPage`, `ShoppingPlanReviewPage` (its route redirects to the board), the setup questions in D3, the "Quero outras opções" panel, the manual menu form on `MenusPage`, "Montar refeição por refeição", and `AiMealSuggestion` (replaced in Slice 4). PLAN-0064's three answers survive: pantry as a filter, similarity as "Quero parecido" and "Ver outras", and the hint as the text box.

## Technical design

### Data

Migration `AddPlanningBoard` (Slice 1). All additive and nullable, so an older release still runs.

| Table | Column | Purpose |
|---|---|---|
| `menu_entries` | `LeftoverSlotsJson` jsonb | `[{date, mealType}]` a yielding dish also covers. `MealsCovered` stays `1 + count` |
| `menu_entries` | `CookOnDate` date | Cook ahead of the day it is eaten (Slice 4). The route selects `CookOnDate ?? PlannedDate` |
| `shopping_plan_sessions` | `SkippedSlotsJson` jsonb | Meals marked as not needed |

`DeckPreferenceJson` changes meaning: it keeps only the filter (`priority`, or none). Typed text no longer persists; it belongs to one request and to the cards made from it through `PrivateHint`.

### API

Under `/api/v1/planning/shopping-plans`:

| Route | Change |
|---|---|
| `POST /` | Adds `startDate`. `cookingStyle`, `priority`, `prepDate`, `prepMinutes`, and `mealsOut` become optional with fixed defaults: `mix`, no priority, none, none, 0. `ValidateSetup` stops requiring preparation for `mix` |
| `POST /{id}/cards/{cardId}/decision` | Adds `plannedDate` and `mealType`. Accepting a card for a meal stores them in the snapshot, and expansion creates the entry there. `mode` and `repeats` remain for repeating a saved plan |
| `GET /{id}` | Adds `queues` by meal group (`main`, `breakfast`, `snack`), six each, and `slots` |
| `POST /{id}/batches` | Adds `request` (text, up to 120) and `mealType`; replaces `hint`, `pantry`, and `similarity` with `request`, `priority`, and `likeCardId` |
| `PUT /{id}/filter` | Sets or clears the filter, and re-ranks |
| `POST /{id}/slots/skip` | Marks or restores a meal as not needed |
| `PUT /{id}/entries/{entryId}/slot` | Moves a dish; swaps when the target is taken. Allowed on a draft, and on an accepted menu as an explicit command |
| `PUT /{id}/entries/{entryId}/yield` | Sets what it also covers; scales servings to `people × (1 + count)` |
| `POST /{id}/reopen` | Slice 0, fixes B1 |

Keep the `Idempotency-Key` pattern on `batches`, and the `shopping-plan-decision` limiter on decisions. Regenerate OpenAPI and the client.

### Ranking

`ShoppingPlanRanker` keeps its integer arithmetic. No filter means `PantryWeight = 1` and no goal term, so food needing attention can rise without dominating. Filters map to today's goals: Usa o que tenho → `waste` at weight 3; Menos compras → `short`; Rápido → `time`; Econômico → `save`. `ConsecutiveRejections` no longer sets the weight, because refusing is no longer the way through.

### Prompt

In `RecipeAiRequestEnvelopes.BuildPlanDeckEnvelope`:

- **Taste rule, for every batch:** "userPresets.preferences are tastes, not requirements. At most 2 of the 6 dishes may lead with any one taste or flavor, and vary cuisines and main ingredients." Restrictions remain absolute.
- **Default rule:** without a filter, stop asking for reuse: "Offer dishes a person would be glad to cook this week. Use availabilitySnapshot when it fits, but do not build dishes around it."
- **`planDeck.requestedDish`:** `{ text, treatment }`, with text cleaned to 120 characters by `UntrustedText.Normalize`, and the untrusted-text rule. "The person asked for this. Candidate 1 must be this dish, made the way a home cook would recognize it, even if it needs ingredients not at home; list them. The others are close alternatives. If it is ambiguous, make the most common version." With a request, `availabilitySnapshot.items` stays but is not to be favored.
- **`planDeck.likeDish`:** replaces `direction`, same untrusted treatment.

A repeat of the schema version is not needed, because only request fields change.

### Near-duplicates

`AddCandidatesAsync` gains a pure `DishSimilarity.IsSameDish(a, b)`: normalized name tokens without stop words and cooking words (`de`, `com`, `no`, `na`, `air`, `fryer`, `frigideira`, `assado`, `picante`, `apimentado`), same when Jaccard ≥ 0.6. It applies only to cards from the deck, never to a requested dish's first candidate.

### A typed request

1. The sheet shows saved matches at once, with no AI.
2. "Pedir à IA" calls `batches` with `request` and `mealType`. Six cards come back in about 20 s, while the rest of the sheet stays usable.
3. Answers come first for that meal, under "Para {text}", through the existing `FromOrder`.
4. `RestrictionGuard` runs as always. If every card is blocked, the response is 422 `request_blocked` with the restriction code, and the sheet says: "Essa receita conflita com {restrição}." It never offers a way around.
5. Each card keeps the text in `PrivateHint`, which also keeps it from the idea library.

### Expansion

A single `PlanExpansionProvider` inside `ShoppingPlanProvider` replaces the per-page hook, so it survives navigation and there is one queue. It starts 5 seconds after a choice, for undo. "Usar este cardápio" stays disabled while any dish is being prepared, with "Preparando receitas: 3 de 5". A failed dish shows on its row, with "Tentar de novo" and "Trocar". A server queue stays out of scope (PLAN-0039).

### Sheets and phone layout

- Use the existing `vaul` drawer (`components/ui/drawer.tsx`), with `max-h-[90dvh]`, a sticky search box, `env(safe-area-inset-bottom)`, focus trapped, Escape closing it, and focus returned to the row. Reduced motion is respected.
- A fixed bottom bar needs `padding-bottom` on the page equal to its height, and must rise above the keyboard. Test with `visualViewport`.
- The shell uses `min-h-dvh` rather than `min-h-screen`.
- `Button size="sm"` and `ChoiceChips` reach 44 px under `@media (pointer: coarse)`. This one rule fixes most of B7.
- Replace `<input type="date">` on planning screens with chips of the menu's days, and replace the `datalist`.

## Slices

### Slice 0: Fix what is broken today

Branch: `agent/plan-0065-s0-fixes`. Needs no owner decision and is useful even if D1 is refused.

- [ ] **Mobile audit gate.** New `scripts/e2e/mobile-audit.mjs`: walks the planning routes with seeded data at 320, 360, and 390 px, and fails on horizontal overflow, text or control overlap (`elementsFromPoint` at each control's center and each text's corners), clipped text, and main controls under 44 px, with a short documented allowlist. It stores screenshots under `artifacts/`. Install Playwright WebKit and run the iPhone profile too. Every later slice runs it.
- [ ] **B1:** add `POST /{id}/reopen`, setting `building`; "Escolher mais pratos" calls it.
- [ ] **B2:** rebuild the list line. First line: checkbox, name (wraps freely), amount. Second line: dishes clamped to one line, plus "tem {x}" only when something is at home. All actions move behind one 44 px "⋯" control opening a sheet: Comprei, Já tenho, Não achei, Comprar em, Tamanho do pacote. The whole row toggles the check, except the "⋯". Keep every test id working inside the sheet, and keep offline rules.
- [ ] **B3:** an answered request deletes the earlier unseen cards of that meal group. Raise `MaxBatches` to 12 and `MaxCards` to 120.
- [ ] **B4:** the taste rule and `DishSimilarity`.
- [ ] **B5:** the confirmation with empty meals.
- [ ] **B6:** `PlanExpansionProvider`.
- [ ] **B7:** the coarse-pointer rule; "Voltar" links gain a 44 px target.
- [ ] **B8:** from a draft reached through a plan, "Voltar" returns to the review.
- [ ] **B10:** the menu title shows the name only when there is one; dated dishes first, then undated, then empty days collapsed into one line; plurals with `.one` and `.other`.

**Hazards:** the list line is used offline, and the journeys click buttons that now sit in a sheet, so update `shopping-plan-journey`, `offline-list-journey`, and `session-route-journey` in the same PR. Deleting unseen cards must not delete a base waiting on an accepted dish.

**Gate S0:** mobile audit passes at all widths; B1 and B3 have integration tests; `DishSimilarity` has unit tests using the real session's names; journeys pass; `frontend,backend,contracts,secrets` gates pass. The owner tries the list on a phone.

### Slice 1: The engine

Branch: `agent/plan-0065-s1-engine`. Backend only, and the current screens keep working.

- [ ] Migration, records, store.
- [ ] Optional setup fields, `startDate`, `slots`, and `queues`.
- [ ] Decision with a meal; expansion and saved cards create the entry there.
- [ ] `filter`, `slots/skip`, `slot`, `yield`.
- [ ] `request` and `likeCardId` on batches, the prompt changes, `request_blocked`, and removal of hint persistence.
- [ ] Ranking with no filter. Keep `ConsecutiveRejections` in the data, unused.
- [ ] OpenAPI, client, protocol and privacy documents.
- [ ] `GetTodayAsync`: a leftover slot shows as "Sobra de {dish}", with no cook action.

**Hazards:**

- `MenuEntry.Create` and `MealsCovered` feed the projector, route, and reminders. A yield keeps one entry, with scaled servings, so the list stays right. Test it.
- Moving a dish onto a taken slot must swap in one transaction.
- `PreparationReminders` only checks dated meals. With every dish dated, reminders start firing for all of them, including notifications. This is the intended behavior; test the volume.
- `GetTodayAsync` and allocation still handle old flexible entries with `SuggestedOrder`. Keep them.
- A repeated plan (`fromMenuId`) must place dishes on slots in order.
- Keep the old batch fields until Slice 2 merges, then remove them.

**Gate S1:** integration tests for each route, isolation, idempotency, restriction on a request, privacy on a requested dish, swap, and yield on the list. Unit tests for ranking with each filter. Contract tests pass.

**AI evaluation (Gate S1-AI):** extend the gateway smoke with `PLAN0065_LIVE_EVAL=1` and a US$ 0.05 limit, with these scenarios: a specific request ("jerk beef cozido"), a request that conflicts with a restriction, a profile with `spicy_food` (at most 2 of 6 spicy), and an injection attempt in the text. It needs the provider key, so the owner runs it. Results go in `docs/evidence/plan-0065/`.

### Slice 2: First screen, board, options

Branch: `agent/plan-0065-s2-board`.

- [ ] First screen with `startDate`; recent plans in a disclosure.
- [ ] `ShoppingPlanBoardPage`, with slots from the server and the fixed bar.
- [ ] `MealOptionsSheet`: text box, saved matches, "Pedir à IA", filters, six options, "Ver outras", "Ver ingredientes", move to next empty meal, undo.
- [ ] Hidden tab bar while building; "Sair (fica salvo)".
- [ ] Redirect `/revisar` to the board. Remove the deck, the review, the panel, and their copy. Update hub links.
- [ ] Copy under D4 in three languages.
- [ ] Accessibility: rows are buttons named with day, meal, and dish; `role="status"` for progress; focus returns to the row when the sheet closes.

**Hazards:**

- A `vaul` sheet with an on-screen keyboard is the most likely thing to break on iOS. Check on WebKit and a real iPhone before calling it done.
- A plan in progress at deploy has cards and undated entries. The board must show undated entries in a "Sem dia" group with "Colocar em um dia", so nothing is lost. The owner's stuck plan is the test case.
- Saved recipes load once per sheet; keep that list in the provider so typing filters instantly.
- A batch can return after the sheet closed or moved to another meal. Results belong to their meal group, not to the open sheet.
- The page must not call a batch by itself for each sheet. One automatic batch when a group has fewer than 6 options and no batch is running.

**Gate S2:** new `planning-board-journey.mjs`: a 5-dinner menu using saved recipes, at 360 px, in at most 12 taps, with a typed search and undo; mobile audit including the open sheet and keyboard; AI failure path; three languages; keyboard only. Owner check on a phone: **"Consigo pedir o que eu quero?"**

### Slice 3: Acting on a chosen dish

Branch: `agent/plan-0065-s3-slot-actions`.

- [ ] Actions sheet: Trocar, Mudar de dia (a list of the menu's meals, showing what is there), Rende mais refeições (stepper 2 to 4, filling the next empty meals, shown as "Sobra de {dish}"), Porções, Ver receita, Remover.
- [ ] "Não preciso desta refeição", and its undo.
- [ ] "Completar": proposals for all empty meals, with swap, and one confirmation.
- [ ] Bases as a quiet row on the board, not an interruption: "Base sugerida: Frango desfiado serve para Wrap e Escondidinho. Adicionar". Style `mix` keeps requesting components.
- [ ] "Organizar pelo que estraga primeiro" in the board's menu, using `ShoppingPlanSequencer` to reorder dates.

**Hazards:** Completar sends one decision per meal, so handle a partial failure by naming what stayed empty. Removing a dish with leftovers frees those meals too.

**Gate S3:** journey extended; integration for partial Completar and removal with leftovers; mobile audit.

### Slice 4: The menu in use

Branch: `agent/plan-0065-s4-in-use`.

- [ ] `/app/cardapio/:menuId`: heading, next step as on the hub, today first, past days collapsed, one row per meal using the same row as the board. "Cozinhar" as the primary only on today's or the next dish; others keep it in the row's sheet. Archive moves to the page's menu.
- [ ] Options sheet on accepted menus, for an empty meal or "Trocar", as explicit single-entry commands. An accepted session reopens only for this; cards are purged as usual. A menu without a session gets one on first use. Remove `AiMealSuggestion`.
- [ ] `MenusPage`: list of menus only, with "Novo cardápio".
- [ ] List header: one summary line with progress, a share icon, "Por seção / Por lugar" chips. A fixed bar "Cheguei do mercado (3)" when something is marked.
- [ ] "Preparar vários de uma vez": choose a day and dishes (the next 3 days by default). It sets `CookOnDate`, and the route selects by it. Remove the session's `PrepDate` from new plans. `prepMinutes` becomes an optional note on the route.

**Hazards:** `CookOnDate` must not move the eating day, the reservation, or "Hoje"; a cooked-ahead dish on its eating day shows the stored portions, and otherwise "Já preparado". Old accepted menus with no slots show their entries as they are. Offline, the list header must work without the server.

**Gate S4:** journeys; route with `CookOnDate`; offline journey; mobile audit; owner phone check with the route and a screen kept on (also closes G6 of PLAN-0062).

### Slice 5: Finish

Branch: `agent/plan-0065-s5-finish`.

- [ ] Screen reader pass (VoiceOver and TalkBack) and 200% zoom on board, sheet, list. Both themes.
- [ ] en and es walked in a browser.
- [ ] Remove dead code: legacy deck copy, `MoreOptionsAfterRejections`, unused API fields, unused prompts.
- [ ] Documents: rewrite `guided-shopping-planning.md`; update `planning-and-shopping.md`, the design system (sheets, fixed bars, coarse pointer), user journeys, and privacy.
- [ ] Metrics: `planning.meal_filled{source=saved|idea|ai|requested}`, `planning.request{outcome}`, time from start to acceptance. No dish or typed text in labels.
- [ ] Update PLAN-0062's acceptance criteria to state what was replaced.

**Gate S5:** full gates; external test with three people (PLAN-0054 decision 8), recording whether they reach a list unaided and whether they keep a second list.

## Success measures

| Measure | Today | Target |
|---|---|---|
| Questions before the first dish | 9 | 3 |
| Taps to a 5-dinner menu from saved recipes | About 30, including setup and review | At most 12 |
| Steps to ask for a specific dish | Not possible | 2: tap the meal, type |
| Refusals needed to reach a menu | 27 in the real session | 0 |
| Dishes leading with one taste, per batch | 23 of 31 | At most 2 of 6 |
| Screen height of the first screen at 360 px | 1.8+ screens | 1 |
| List line height at 360 px | 193 to 225 px | At most 72 px |
| Main controls under 44 px on planning screens | Many | 0 |

"About 30" is an estimate from the screens, to be measured when Slice 0 adds the audit.

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Sheet and keyboard break on real iPhones | Medium | High | WebKit in the audit, real device check before each merge, plain full-screen page as fallback |
| Six options still miss what the person wants | Medium | Medium | The text box is always first |
| The model ignores a specific request | Medium | High | Candidate 1 rule, live evaluation, and the person sees alternatives |
| Dated dishes feel like an obligation | Low | Medium | Moving takes two taps; "Hoje" keeps offering other things; a menu entry stays an intention |
| More reminders once every dish has a date | Medium | Low | Still at most one notification a day; check "Prepare-se" length |
| Plans in progress at deploy | Certain, small | Medium | The board shows undated entries and offers placing them |
| Scope is large | High | Medium | Each slice ships on its own; Slice 0 is valuable alone |

## Excluded

Prices and budgets, curated pack sizes, loose list items (a decision of PLAN-0062 that stays), shared household, drag and drop, a server queue for expansion (PLAN-0039), and offline building of a menu.

## Execution state

- **Current run delivery target:** Publish the review and this plan.
- **Current checkpoint:** Plan written, in a draft pull request.
- **Last completed step:** Review of screens, code, and the owner's session.
- **Exact next action:** The owner answers D1 to D4. Slice 0 can start from fresh `origin/main` on `agent/plan-0065-s0-fixes` once the owner agrees, with no need to wait for those answers.
- **Blockers:** D1 to D4 for Slices 1 to 5.
- **Partially modified areas:** None.
- **Documentation delivered:** This plan and the registry row.
- **Validation performed:** Browser walk with Playwright at 320, 360, and 390 px on the proxy replica, at `fc17bfe`; read-only queries on the test host's database and log. No code changed.
- **Known failures or limitations:** Not checked on a real phone or WebKit. The replica has no AI, so AI behavior comes from the owner's session of one person and one plan. The "about 30 taps" figure is an estimate.
- **Working tree state:** Clean after this commit.

## Progress log

### 2026-11-05 — Claude Code

- **Run delivery target:** A review of the planning feature and an executable plan, at the owner's request.
- **Checkpoint:** Findings recorded; four product decisions proposed for the owner.
- **Changes included in the commit:** This plan and the registry row.
- **Documentation and code-documentation delivered:** This plan.
- **Validation performed:** See `Execution state`. B1, B2, B5, B7, B8, B9, and B10 were reproduced in a browser. B3, B4, and B6 come from the owner's session and the service log. B11 is unverified.
- **Result:** Ready for the owner's decisions.
- **Next action:** See "Exact next action".
- **Blockers or handoff notes:** The owner's plan on the test host is stuck in `Reviewing` with 4 of 7 meals. It is the test case for Slice 2's handling of plans in progress; do not delete it.

## Completion and handoff checklist

- [ ] All slices and success measures are resolved truthfully.
- [ ] Required tests and evaluations pass, or limitations are documented.
- [ ] Durable documentation, contracts, comments, XML documentation, and TSDoc are current.
- [ ] Security, privacy, safety, localization, accessibility, AI, and operational impacts were reviewed.
- [ ] `docs/plan-status.md` matches this plan.
- [ ] Pull requests link this plan and report validation evidence.
- [ ] Exact continuation instructions exist if the work is not complete.
