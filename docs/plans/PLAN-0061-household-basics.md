# PLAN-0061: Salt and Water Are Assumed at Home

- **Status:** Validating
- **Type:** Implementation
- **Priority:** High (owner request)
- **Owner:** Claude Code (claimed 2026-11-04)
- **Created:** 2026-11-04
- **Last updated:** 2026-11-04
- **Branch:** `agent/plan-0061-household-basics`
- **Pull request:** Draft, opened with this delivery
- **Related documents:** [`docs/ai/ingredient-identity.md`](../ai/ingredient-identity.md), [PLAN-0029](PLAN-0029-implement-sequential-planning-menu-and-shopping.md)

## Owner request (2026-11-04)

"Sal e água são dois ingredientes que não precisam aparecer como 'indisponíveis' na despensa ou que precisa comprar, assuma que todo mundo tem isso em casa."

## Scope

- `HouseholdBasics.Is`, one deterministic rule for names, with a closed list of qualifiers.
- `RecipeIngredientReader` reads them as `assumption`, which already removes them from menu readiness, the cook page's counts, and recommendations. Stored recipes need no migration.
- Recipe availability omits them, so the recipe page shows no status for them.
- The shopping list leaves them out, including the "assumed" group, unless a tracked one still has stock.
- "Adaptar ao que tenho" does not count them as purchases.

## Decisions

- **Only salt and water,** as asked. Oil and other basics a recipe marks as assumed keep today's behavior.
- **Variants that are other products stay ingredients:** "água de coco", "água com gás", "flor de sal", "sal e pimenta". An unknown qualifier makes a name a real ingredient, so the rule errs toward showing something rather than hiding it.
- **Tracking still works.** Someone who keeps "Sal" as a pantry item still sees it proposed and consumed when cooking.
- **The cooking review still lists them,** because that step asks which pantry item was used. Its text is neutral ("Não veio da despensa"), not a shortage.

## Acceptance criteria

- [x] Salt and water never appear as unavailable on a recipe, or as something to buy.
- [x] They do not lower the cook page's "Você tem X de Y" or make a menu meal "missing".
- [x] Other products with similar names still appear.

## Execution state

- **Current run delivery target:** the request above.
- **Current checkpoint:** implemented and validated as below.
- **Validation (lean):** unit filters `HouseholdBasics`, `ShoppingProjector`, `ReservationProjector`, `RecipeAiDirection`, `RecipeExecution`, and `RecipeRecommend`, 68/68; integration tests for salt and water, recipe availability, and the cook page, 3/3.
- **Not run:** browser check, full suites. No frontend change.
- **Exact next action:** Merged via #82 and deployed with `7333b25` (registry reconciled 2026-11-11). The owner checks a recipe with salt and water on the host; then mark the plan completed.
- **Blockers:** None.

## Progress log

- 2026-11-04: Delivered from a branch from `main`.
