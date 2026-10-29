# Planning and Shopping

- **Status:** Accepted
- **Last updated:** 2026-10-29

## Optional module rule

Menu planning is optional. A user can start recipe selection and cooking at any time without an active menu plan.

A menu entry is an intention, shortcut, and source of preparation or purchase actions. It is not an obligation.

## Implemented (PLAN-0029 Phase 1)

- **Menus.** A menu covers 1 to 31 days and is a draft, accepted ("em uso"), or archived (read-only). A draft is a simulation: its shopping list works, but its meals are not on the home page. Accepting creates no reservations yet.
- **Meals.** A meal names a recipe, an optional day inside the period (none means "some time in the period"), an optional meal type, and servings. It follows the recipe's current revision; pinning exists in the API only. Every change touches one meal.
- **Cooking from a meal.** Starting from a meal links the cooking session to it. The meal is "cooking" while the session runs and "cooked" once it completes; this state is derived from the session, never stored separately. Completion and inventory reconciliation are unchanged.
- **Home.** "Hoje" lists the accepted menus' meals for the owner's current day (dated that day, or flexible), excluding skipped and cooked meals, as the first suggestion tier.
- **Shopping.** The list is derived from the meals still to cook from the owner's current day and the current pantry.
  - Quantities scale like the cooking proposal.
  - Lots are matched by normalized name and unit, and the pantry is allocated meal by meal in plan order.
  - Lines are required, assumed basics, optional, "you have it but it will run out", or already available.
  - No package size is assumed.
  - A check mark is only remembered. "Comprei" records the purchase as a pantry lot through the normal inventory command, and the list is recalculated.
- **Not yet.** AI-generated plans, reservations, package remainders, planned-versus-actual purchase impact, readiness checks, and reminders.

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
