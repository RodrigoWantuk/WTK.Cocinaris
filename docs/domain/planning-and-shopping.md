# Planning and Shopping

- **Status:** Accepted
- **Last updated:** 2026-10-30

## Optional module rule

Menu planning is optional. A user can start recipe selection and cooking at any time without an active menu plan.

A menu entry is an intention, shortcut, and source of preparation or purchase actions. It is not an obligation.

## Implemented (PLAN-0029 Phase 1)

- **Menus.** A menu covers 1 to 31 days and is a draft, accepted ("em uso"), or archived (read-only). A draft is a simulation: its shopping list and readiness work, but its meals are not on the home page and it reserves nothing.
- **Meals.** A meal names a recipe, an optional day inside the period (none means "some time in the period"), an optional meal type, and servings. It follows the recipe's current revision; pinning exists in the API only. Every change touches one meal.
- **Cooking from a meal.** Starting from a meal links the cooking session to it. The meal is "cooking" while the session runs and "cooked" once it completes; this state is derived from the session, never stored separately. Completion and inventory reconciliation are unchanged.
- **Home.** "Hoje" lists the accepted menus' meals for the owner's current day (dated that day, or flexible), excluding skipped and cooked meals, as the first suggestion tier.
- **Shopping.** The list is derived from the meals still to cook from the owner's current day and the current pantry.
  - Quantities scale like the cooking proposal.
  - Lots are matched by catalog ingredient ("Peito de frango" matches "chicken breast", PLAN-0055) or, failing that, normalized name, and by unit. The pantry is allocated meal by meal in plan order.
  - Lines are required, assumed basics, optional, "you have it but it will run out", or already available.
  - No package size is assumed. The owner may confirm one per item (Phase 4, below).
  - A check mark is only remembered. "Comprei" records the purchase as a pantry lot through the normal inventory command, and the list is recalculated.
- **Reservations and readiness (Phase 3).** A menu in use reserves pantry quantity for its meals still to cook. The reservations are derived, and a meal's reservations can be released. Each meal shows whether its pantry ingredients are covered or what it lacks; a draft shows the same as a simulation. A menu's shopping list counts only stock that is neither protected nor reserved by other menus in use. See `inventory-lifecycle.md` for protections.
- **AI suggestions per slot (Phase 2; screen removed in PLAN-0065 Slice 4).** The menu page no longer offers this: the options sheet of a meal type replaces it, on a draft and on a menu in use. The recipe API still accepts a menu-planning target. When it was offered, on a menu day the AI suggested three dishes for that date, meal, and servings. It sees only pantry stock that no other meal counts on and the names of meals already planned, so the suggestions differ from them. Nothing enters the menu until the owner chooses. Slots are planned one at a time, so each choice updates the reservations the next suggestion sees.
- **Package sizes and remainder (Phase 4).** The owner can confirm the package size they buy an item in, from the shopping line or with one tap after buying more than the menu needed. The size is the owner's own, applies to every menu's list, is keyed by the line's item key (catalog ingredient or normalized name, and unit), and is in that unit. A line with a size shows the whole packages that cover what is left to buy and the expected remainder after the menu, and "Comprei" defaults to those packages. Without a size the line is the net quantity, as before. The source of a size is always the owner; receipts and package confidence from imports (PLAN-0046) are not used yet.
- **Planned versus actual purchase (Phase 4).** After "Comprei" the list is recalculated and the page says what the purchase means: it covers the menu; it leaves named meals short by a quantity (each meal's shortfall comes from allocating the pantry in plan order); or it is more than the menu needs, with a freezing suggestion for produce, meat and fish, dairy, and bakery items, and the option to remember the amount bought as the package size. The rest of a short item stays on the list. The page points to the menu for changing servings or skipping a meal, and nothing in the menu or the pantry changes by itself.
- **Pre-meal readiness and reminders (Phase 4).** "Prepare-se" on "Hoje" lists, for the dated meals of menus in use today and tomorrow, what to buy (a required ingredient the reservations do not cover), what to thaw (a reserved lot stored in the freezer or frozen; a lot already thawing needs nothing), and a reserved lot whose date falls before the meal (advisory: cook earlier or use another lot). Buying and thawing are due the day before the meal (or today, if the meal is today); a date reminder is due on the lot's date. Reminders are derived on every read (`GET /api/v1/planning/preparation?from=&days=`, 1–7 days), so each disappears once its cause does. Only meals that reserve ingredients are checked. Reminders are shown in the app only; there are no push or e-mail notifications and no per-meal reminder policy yet.
- **Not yet.** Package sizes from receipts or imports. Notifications outside the app exist from PLAN-0062 Slice 7, as one generic daily message ([ADR-0013](../architecture/decisions/0013-web-push-plan-reminders.md)).
- **Repeating a plan.** `POST /shopping-plans` with `fromMenuId` copies an accepted plan's answers and creates accepted saved cards and entries on a new draft, reading the old **menu**, because the person may have changed it after accepting. It never changes the old plan. Archived recipes, skipped entries, and meal types outside the answers are left out. `RestrictionGuard` runs again, and meal needs cap the copies. Only an accepted plan of the same owner can be repeated; anything else returns 404.
- **Bases (PLAN-0062).** A menu entry has a kind: `meal` or `component`. A component has what it makes (`produces_name`, `produces_quantity`, `produces_unit`), fills no meal, and comes first in the suggested order. The projection processes components first and adds their output to what later dishes can use, so those lines become `from_prep`. Once cooked, a component leaves the projection, and its real lots take over. Reminders do not ask to buy what a planned base makes. Reminder kind `ready_portions` lists portions stored by an entry that covers several meals, until the lot is used up.
- **Offline list (ADR-0012).** The derived list may be read from the device and marks queued there; the server stays the only authority. A queued mark is sent only if its line still exists when it arrives, and a mark is last write wins with no prompt. Nothing that changes the pantry works offline.

## Shopping plans (PLAN-0062 Slice 1)

A shopping plan fills a draft menu through dish cards ([product behavior](../product/guided-shopping-planning.md)). Since PLAN-0065 the cards are the options of each meal type on the board; the deck, the review, and their routes (`reason`, `review`, `replacements`, `reopen`) were removed in PLAN-0065 Slice 5. The plan and its cards belong to Planning; meals stay ordinary menu entries.

- **One at a time.** An owner has at most one plan in progress, enforced by a unique index. A plan is `building`, `reviewing` (only plans reviewed before Slice 5 of PLAN-0065), `finished`, `amending` (a menu in use being changed), or `abandoned`.
- **Asking for dishes.** `POST …/batches` takes an optional `mealType`, a `request` (a dish in the person's words), a `likeCardId`, and `replaceOffers`. The "Quero outras opções" fields of PLAN-0064 (families, cuisine, time, pantry, similarity, and hint) were removed in PLAN-0065 Slice 5; the switches carry the pantry choice, and typed words go in `request`.
- **Metrics (PLAN-0065 Slice 5).** `cocinaris.planning.meal_filled{source}` counts meals filled by a chosen dish, by where it came from (`saved`, `idea`, `ai`, `requested`); `cocinaris.planning.request{outcome}` counts typed requests (`answered`, `blocked`, `nothing_new`, `ai_unavailable`, `failed`); `cocinaris.planning.time_to_acceptance` records the seconds from starting a plan to using its menu. Labels are closed codes: never a dish name or typed words.
- **Cards.** Saved recipes first (favorites, then most recently cooked, at most 12), then at most 6 library ideas, then AI batches of 6 (at most 12 batches and 120 cards). A batch whose call fails is given back. Cards are deduplicated by name and by format, technique, and main ingredients.
- **Meal fit (PLAN-0065).** The model labels its own dishes and once called spaghetti a snack, so `MealFit` checks each AI and library dish against `Catalog/meal-fit-rules.json`: words in the name and the format, and at most 60 total minutes for a snack or breakfast. Lunch and dinner reject snack words. Mass per serving is not used, because recipes count frying oil and water. A dropped dish raises `cocinaris.shopping_plan.meal_fit_dropped`. If fewer than 3 dishes of a batch survive, the model is asked once more with the dropped names. Saved recipes are the person's own and are not checked, and a batch asked for with typed words skips the check.
- **Near-duplicates (PLAN-0065).** `DishSimilarity` compares names without cooking words and flavors, and drops a dish that matches one already offered. "Pão de queijo de frigideira" and "Pão de queijo no air fryer" are one dish.
- **An answer replaces what was waiting (PLAN-0065).** When the person asks for options and new dishes come, earlier unseen AI and library dishes are deleted, so nobody refuses them all to get past them. Saved recipes and bases stay.
- **Restrictions.** `RestrictionGuard` checks every AI and library card before it is shown, and the full recipe after expansion. A blocked card is dropped; a blocked expansion fails with `restriction_blocked` and creates no meal. See [`restriction-rules-review.md`](restriction-rules-review.md).
- **Ranking.** `ShoppingPlanRanker` is pure integer arithmetic: the same input gives the same order. Attention comes first (100 per ingredient), then the goal, variety, the person's feedback, and the session's time. The goal formulas are in PLAN-0062.
- **Pantry weight (PLAN-0064, then PLAN-0065).** Attention and everything in the goal that depends on the pantry or the list are scaled by a weight of 0 to 3 thirds. The switch "O que tenho e já escolhi" sets 3 when on and 0 when off; on plans from before the switch, each consecutive refusal removes a third. The time a dish takes is never scaled. Dishes answering the latest request come before all others, after any base a chosen dish needs.
- **Meal counts (PLAN-0065).** A plan created with `mealCounts` says how many of each meal it wants (each 0 to 40, 1 to 40 in all) within a period of 1 to 14 days that starts on a chosen day, today or later. Style, goal, and preparation day are no longer asked: style is `mix`, goal `none`, and there is no preparation day. `Counts()` reads an older plan as its days for each meal type, with meals eaten out taken from lunch, then dinner. Counts can change while the plan is open, never below what is chosen in a group (`meal_count_below_chosen`).
- **One meal per dish (PLAN-0065).** Choosing a dish names its meal type, which must be in the same group as the card and have room; with none named, the card's own meal is used, or another of its group with room. Lunch and dinner are interchangeable, so a dish that yields more than its meal asks for counts toward the other. The view lists each meal type with what is wanted, what is chosen, and its next six options.
- **Days are optional (PLAN-0065).** A dish may get a day inside the period when chosen or later (`PUT …/cards/{id}/day`), and most have none. "Fazer de novo" dates only the first of its meals. A chosen dish can cover 1 to 4 meals of its group (`PUT …/cards/{id}/yield`), scaling its portions up to 24; this works before and after expansion. A base cannot be changed this way.
- **Asking for one meal (PLAN-0065).** A batch may name one meal type. It then asks the model for that meal only, never offers again what is on screen, and can carry a specific dish (`request`, up to 120 characters, for that call only) or a dish of the plan to stay close to (`likeCardId`). The dishes of a request skip the meal and near-duplicate checks, come first in their meal, and keep the person's words on the card so the dish never reaches the shared library. If every dish of a request is blocked by a restriction, the answer is `request_blocked` with the restriction's code. `replaceOffers` makes the batch replace the earlier unseen AI and library dishes of that meal, which is what a switch does; a plain "Ver outras" or a request adds.
- **People per dish (PLAN-0065 Slice 3).** `PUT …/cards/{id}/people` sets how many people one chosen dish is for (1 to 12), stored on the card and never on the plan or the profile. Its meals are cooked for those people, times the meals it covers under "Rende mais", at most 24 portions; the ranking and the list scale to it. Undo forgets it.
- **Choosing several at once (PLAN-0065 Slice 3).** `POST …/choices` takes 1 to 40 different offered cards, each with its meal, for "Completar". Each is an ordinary decision, so a card that no longer fits (its meal filled up, or it is gone) fails alone; the response is the plan plus `failed`, the cards not chosen. Repeating the request chooses nothing twice. It uses the decision rate limit.
- **Spreading over the days (PLAN-0065 Slice 3).** `POST …/distribution` gives each undated meal entry of the draft a day: the plan is sequenced first (what spoils first first), then each meal type's undated entries are spread evenly over the period, the i-th of n on day `start + floor(i × days / n)`. Dishes that have a day keep it. It refuses with `expansions_pending` while a recipe is being prepared, because the order needs real ingredients. With `clear: true` every chosen dish loses its day instead. It changes only the draft, and nothing reserves until acceptance.
- **Choosing a saved recipe by name (PLAN-0065 Slice 2).** `POST …/recipes/{recipeId}/choice` with a meal type puts one of the owner's active recipes on the menu as a saved card and chooses it, whatever meal types the recipe was saved with; an unseen offer of the same recipe for that meal is chosen instead of copied. `RestrictionGuard` still runs, and a blocked recipe returns `request_blocked` with the restriction. Another owner's recipe is not found. Undo works as for any choice.
- **Switches (PLAN-0065).** `PUT …/preferences` sets three answers that belong to the plan: whether the profile's tastes reach the model (restrictions and dislikes always do), whether to build on the pantry and the chosen dishes, and an optional goal (`quick`, `short`, `save`). A new plan starts with tastes on and the pantry off, or with the last plan's answers. With the pantry on and no goal, the ranking uses the less-waste goal. A chosen goal counts in full even with the pantry off, so "Menos compras" still means fewer purchases, while attention to the pantry stays scaled by the pantry weight.
- **Meal needs.** Lunch and dinner are one `main` group; breakfast and snack are separate. Meals eaten out come from `main` first. Cards of a group with no need left stay out of the queue.
- **Repeats.** `yield` is one entry with `servings = people × N` and `mealsCovered = N`, dated on the preparation day for the `once` and `mix` styles. `again` is N entries.
- **Expansion.** The browser requests it, one card at a time, 5 seconds after acceptance (the undo window). Each card has its own single-candidate generation session, because a session allows one selection. Keys derive from the card, so a retry creates no second recipe or meal. If the card was undone meanwhile, the recipe stays in the library and no meal is created.
- **Order.** `ShoppingPlanSequencer` sets `suggestedOrder` when a plan is accepted or its dishes are spread over the days, never afterward; a dish added to a menu in use takes the next place. It ranks by `IngredientPerishability`, ignoring ingredients already covered by frozen stock. This sets an order, not a safety statement.
- **Allocation.** Among flexible meals, shopping and reservations follow `suggestedOrder`, so the pantry goes to what spoils first.
- **Home.** For a flexible entry with `suggestedOrder`, "Hoje" shows only the lowest still planned of each meal type in each plan, plus any being cooked (one meal type per plan before PLAN-0065). Entries without it behave as before.
- **Period.** The menu starts on the chosen first day for a plan by counts, else on the preparation day when there is one, else on the browser's local day, and covers the chosen days (1 to 14; at most 40 meals).
- **Reminders.** Buy and cook reminders come only from dated meals, so a plan whose dishes have no day raises none. The board and the hub still show what to buy.
- **Acceptance.** Accepting the menu and closing the plan are two idempotent steps, so a retry completes them. Unseen and rejected cards are deleted on acceptance, and all cards on discard. The owner's abandoned plans older than 30 days are deleted when they start a new plan.

## The menu in use (PLAN-0065 Slice 4)

- **Choosing again (`Amending`).** `POST /shopping-plans/amendments` with `menuId`, the owner's `today`, and an optional `replaceEntryId` opens an accepted menu's plan for choosing a dish again, with status `amending`. A menu built by hand gets a plan here that wants what the menu already has, so only a swap leaves something missing. Every opening reads the menu as it is: chosen cards are rebuilt from the menu's dishes, so a dish removed or skipped on the menu page stops filling its meal; a chosen card whose recipe is still being prepared is kept. When nothing is offered yet, the owner's own recipes and library ideas are added at once, so a swap works without AI. `replaceEntryId` removes that one dish first, and only a dish still to cook, with its meal, can be swapped. A draft or archived menu is refused (`domain_rule_violated`), another owner's menu is not found.
- **What changes while amending.** Choosing, undoing, removing a chosen card, preparing its recipe, the switches, and requests for dishes work as on a draft. Counts, distribution, and discarding need a draft and are refused. A dish chosen here is added with the next places in the menu's order, after every dish already there, and nothing else moves; a swap can give the new dish the old one's day (`plannedDate` on the decision or on `recipes/{id}/choice`). `POST …/acceptance` closes the change: the plan is `finished` again, unseen and rejected cards are deleted, and the menu is not touched. An amendment left open keeps its options until it is opened or closed again.
- **The plan behind a menu.** `GET /shopping-plans/menu-link?menuId=` returns the plan's id, state, and meal counts, or no plan, also for another owner's menu. The menu page reads what is missing from it and from the dishes; lunch and dinner share what passes one of them.
- **Cooking ahead (`CookOnDate`).** `PUT /menus/{id}/cook-day` with a `date` and the `entryIds` cooked that day ("Preparar vários de uma vez") sets `menu_entries.CookOnDate` on those dishes and clears it from the dishes that had that day and were left out, so the same request twice changes nothing and an empty list clears the day. The day is at most 7 days before the menu, inside the period, and never after the day the dish is eaten; a skipped dish is refused, and only a menu in use accepts it. Moving a dish to an earlier eating day clears a cooking day that would fall after it. `CookOnDate` never changes the eating day, the reservation, the shopping order, or "Hoje".
- **The route's day.** The preparation route of a plan is the dishes cooked ahead on its day: the earliest cooking day with something still to cook, else the last one, so a finished session stays visible. A plan from before cooking days existed keeps the preparation day of its setup, with the dishes dated that day, until a cooking day is set. Its time budget is the setup's, when it has one; new plans have none, and the route shows only its own length.
- **The menu page.** Next step as on "Planejar" (buy, put away, prepare), then today's dishes, later dated dishes by day, and "Quando quiser": undated dishes by meal type, with a row for what is missing. Days with nothing on them do not appear; past days are folded. "Cozinhar" is in the row only for today's first dish, else the next in the menu's order. Every other change is in the dish's sheet and touches that dish only. Near the end of the period a line says how many days and dishes are left. A dish cooked ahead for a later day reads "Já preparado".
- **"Hoje".** Dated dishes are today's; undated ones appear under "Quando quiser", the next of each meal type.
- **"Faça até" (PLAN-0062 Slice 4).** A dish with no day on a menu in use gets a `cook_by` reminder in `GET /preparation`: the earliest advisory use-by day among the lots it reserves that are not frozen, from each lot's attention (a printed or recorded date, then the owner-reviewed storage table when it is on). It is due the day before and on that day, never earlier than the owner's day; a dish whose lots say nothing gets none. Dated dishes keep the `use_by` reminder, which says the same for them. The reminder lists, as `freezeLots`, what it reserves that the storage table says can be frozen, so "Congelar hoje" freezes that quantity through the ordinary freeze transition (a lot reserved whole is frozen whole). "Trocar de prato" opens the swap on the menu (`/app/cardapio/{id}?trocar={entryId}`). "Deixar como está" (`PUT /reminders/dismissal` with the entry, `cook_by`, and the day) is kept in `planning.reminder_dismissals` and hides the reminder until the day it names changes. Reminders stay derived: each disappears when its cause does.

## Shopping line marks and arrival (PLAN-0062 Slice 2)

- **Marks.** `planning.shopping_line_marks` keeps what the owner said about a line, by its key before any substitution (`markKey`): `later`, a substitute name, or a place. All empty removes the row. The line stays derived.
- **Substitutes.** A substitute takes over the line's name and key, so stock, check marks, and package sizes follow the product bought. The quantity and the recipe stay. `RestrictionGuard` refuses a blocked one with `restriction_blocked`.
- **Later.** A later line leaves the pending lines and the arrival proposal.
- **Places.** Closed codes: `market`, `street_market`, `butcher`, `bakery`. Default from the section: produce goes to `street_market`, butcher and fish to `butcher`, bakery to `bakery`, the rest to `market`.
- **`ArrivalAdvisor`** is pure. A use whose day is within `fridgeDays` goes to the refrigerator; otherwise to the freezer when freezable and possible; otherwise to the refrigerator marked "use first". A flexible meal's day is `start + floor(position × days / meals)`. A perishable, freezable surplus becomes at most 6 even portions about the size of the largest use, in whole grams, with the last taking the rest so the total is exact. Unit lines that do not divide evenly stay together.
- **Without a rule,** or with `Planning:StorageGuidance:Enabled` off, a line stays whole in the section's default place.
- **Recording.** One lot per portion, each with a key derived from the request key, item key, and position. A freezer portion is created in the refrigerator and then frozen, so it is `Frozen` with `FrozenAt`. This is not one transaction: the result lists `recorded` and `failed`, and a retry with the same key replays what was done. Reservations are derived, so new lots are reserved on the next read.
- **At most** 120 lots per request.

## Preparation session (PLAN-0062 Slice 6)

`PrepRouteScheduler` is pure and deterministic. It serves one cook: hands-on parts never overlap, and passive parts run in the background. Among the ready tasks it takes the one with the longest remaining chain; ties follow recipe order and then task order.

`PrepRouteBuilder` reads tasks from the revision each entry cooks:

- A stage waits for its stated `dependsOn` and `dependencies`. A later stage that states none follows the previous one, because an empty list often means the writer left it out. So only stated dependencies create parallel work.
- The first stages wait for the recipe's preparations. A preparation's `minimumLeadMinutes` counts as waiting.
- A dish's first stages wait for all tasks of a base it uses, matched through `IngredientMatchKeys`.
- Circular dependencies, which model output can contain, fall back to the recipes one after another and raise the metric `cocinaris.shopping_plan.route_cycle_fallback`.

The dishes on the route are those cooked ahead on its day (PLAN-0065 Slice 4; see "The menu in use"). Routes: `GET /shopping-plans/{id}/route` (422 before acceptance, because a draft reserves nothing), `PUT /shopping-plans/{id}/route/progress` (the whole state, so a retry is harmless, and keys no longer on the route are dropped), and `GET /shopping-plans/route-link?menuId=|date=`. Progress is stored in `shopping_plan_sessions.RouteProgressJson`. Tasks of a cooked dish always read as done.

## Planning states

- **Simulation:** calculates possibilities and shopping impact without persistent reservations, tasks, or active-plan mutation.
- **Draft:** stores an editable candidate plan but does not become the operating plan.
- **Accepted:** may create flexible reservations, preparation actions, reminders, and shopping requirements.

## Planning input

The user chooses any relevant subset of:

- planning period;
- meal count and optional dates or times;
- number of people and portions;
- maximum cooking sessions;
- inventory inclusion;
- products or quantities to lock;
- products that should be prioritized;
- additional purchase permission or limits;
- equipment or technique;
- time, effort, cleanup, skill, variety, nutrition, and cost priorities;
- preference for leftovers, batch preparation, component preparation, or freezing.

The system may use profile defaults but must display or allow overrides when they materially affect the result.

## Suggestion and selection

The AI may generate many normalized recipe possibilities. The user can:

- accept all or individual recipes;
- reject and replace one recipe;
- lock accepted recipes;
- change portions;
- request an ingredient or technique adaptation;
- regenerate only the remaining candidates;
- prepare immediately;
- schedule;
- save;
- favorite;
- discard.

A localized change must not silently regenerate the entire accepted plan.

## Contextual home suggestion priority

The authenticated home uses the user's current local date and daypart to organize preparation possibilities. It derives local context from an explicit saved IANA timezone when available, then the browser-reported IANA timezone for the current session, and otherwise uses neutral presentation. It must not treat the server timezone as the user's meal context or require precise geolocation.

Suggestion sources appear in this order when they contain relevant and valid candidates:

1. accepted menu entries relevant to the current local date, daypart, or flexible meal window;
2. suggestions based on usable inventory, prioritizing products or lots needing attention soon;
3. suggestions based on the user's confirmed profile;
4. a visible quick chooser that asks one or two material questions before producing or narrowing suggestions.

A higher source tier appears before a lower source tier, but source priority does not make any suggestion mandatory. The user remains free to ignore a planned meal, choose a non-urgent product, search manually, select a saved recipe, or cook something unrelated to current inventory.

Within a source tier, ordering may consider:

- readiness and required advance action;
- shelf-life attention and confidence;
- accepted reservations and locked quantities;
- current meal context;
- preference and restriction fit;
- available time, effort, skill, equipment, and cleanup tolerance;
- additional purchase requirements;
- waste reduction and preservation alternatives;
- variety and recent history.

The interface must explain the material reasons for a suggestion. An urgency-based suggestion identifies the products or lots that influenced it and preserves correction, preservation, and alternative-selection paths. Urgency remains advisory and cannot silently override reservations, accepted plans, user restrictions, or explicit choices.

The quick chooser asks only questions that can materially reduce current uncertainty and does not repeat sufficiently known context. One question is preferred; two are allowed when necessary. Answers apply to the current request and do not silently update the durable profile. Saving an answer as a preference requires explicit confirmation.

Missing or failed sources degrade independently:

- no accepted menu skips the first source without error;
- empty or unavailable inventory skips or explains the second source and may offer inventory entry;
- incomplete profile uses confirmed context only and may offer progressive completion;
- unavailable AI preserves accepted menu entries, deterministic readiness, deterministic inventory attention, saved recipes, favorites, and deterministic filtering where supported;
- one source failure must not blank successful source tiers.

Displaying, filtering, or ranking a suggestion does not mutate the accepted menu, reservations, inventory, profile, shopping list, or cooking execution. Such changes require their normal explicit commands and validation.

## Menu entry

A planned entry can contain:

- recipe UUID;
- current or pinned revision;
- date and time, when fixed;
- flexible period, when unscheduled;
- portions;
- expected product requirements;
- inventory reservations;
- preparation, thawing, or preservation actions;
- required and optional missing products;
- readiness state;
- reminder policy.

Before the intended time, the system checks current inventory and may propose a purchase, substitution, recipe adaptation, localized replacement, reschedule, or preparation action.

## Shopping requirements

Shopping is derived from actual requirements rather than a concatenated ingredient list.

The calculation considers:

- selected recipe portions;
- available and reserved lots;
- products that must be preserved for another use;
- package sizes and units;
- expected product remainder;
- future uses for the remainder;
- time until the next intended purchase;
- perishability and storage capability;
- required versus optional products;
- substitutions and user constraints.

## Shopping item categories

- required;
- recommended;
- optional;
- already available;
- expected to run out;
- preservation or operational item.

Operational items may include storage materials required by an accepted freezing or portioning strategy.

## Package remainder example

```text
Planned recipe need: 600 g chicken
Current usable lot: 200 g
Net purchase need: 400 g
Available package: 1,000 g
Expected remainder: 600 g
Suggested handling:
- reserve 200 g for another selected meal
- freeze 400 g in two portions
```

The system must not assume a package size without a source or user confirmation.

## Planned versus actual purchase

When a purchase differs from the plan, Cocinaris records the real product and quantity, then proposes impact:

- keep the recipe and adapt the product;
- change portions;
- add the original product to a later purchase;
- replace only affected recipes;
- preserve accepted meals and repurpose the extra product;
- make no plan change.

The actual purchase becomes inventory only after confirmation. The accepted plan is not silently rewritten.

## Cancellation and change

When a planned meal is not prepared, the system may:

- mark it skipped or cancelled;
- ask whether to reschedule;
- release or retain reservations;
- recalculate shelf-life attention;
- propose preservation;
- update future shopping requirements.

Automation follows explicit user policy and remains reversible.

### Store sections (PLAN-0037 Phase 3)

Lines still to buy (required, basics, optional) are grouped by store section in shopping-trip order: produce, meat and fish, dairy and eggs, bakery, dry goods, drinks, other. The section comes from the curated catalog category of the line's ingredient (PLAN-0055); a name the catalog does not know goes to "other". Basics and optional lines carry a small label inside their section.

Lines the pantry covers ("runs out", "already have") keep their own blocks below. Recording a purchase from meat and fish or dairy and eggs suggests the refrigerator; the person can change it.

