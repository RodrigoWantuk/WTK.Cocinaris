# PLAN-0054: Everyday-Cook Realignment — Design System, Easy Pantry, Dual Measures, Beginner Cooking, Shared Household

- **Status:** In Progress
- **Type:** Implementation (product realignment)
- **Priority:** Highest (runs before the pending phases of PLAN-0029, PLAN-0037, PLAN-0042, PLAN-0045)
- **Owner:** Claude Code (claimed 2026-10-29)
- **Created:** 2026-10-29
- **Last updated:** 2026-10-29
- **Branch:** `agent/plan-0054-everyday-cook-realignment` (Phase 0)
- **Pull request:** Draft, opened after this commit
- **Decisions:** [`docs/product/everyday-cook-realignment-2026-10-29.md`](../product/everyday-cook-realignment-2026-10-29.md)
- **Audit:** [`docs/design/ux-audit-2026-10-29.md`](../design/ux-audit-2026-10-29.md)
- **Design proposal:** design canvas "Cocinaris — nova direção de design" (private claude.ai artifact, owner's account: https://claude.ai/artifact/N7grShNyoauYepVVjN7mEs)

## Objective

Make Cocinaris work for an average person, living alone or in a household, with little cooking experience, in Western countries. Keep the precise path for people who weigh everything.

## Phases (ordered for flow; owner: "faça como achar mais fluido")

| Phase | Outcome | Why in this order |
| --- | --- | --- |
| 0 | Owner decisions recorded, audit, design-direction proposal, two defects fixed | Nothing else should be built before the direction is agreed |
| 1 | Design system and app shell | Every later phase builds screens; doing them in the old look means redoing them |
| 2 | Easy pantry | Exact or approximate amounts, quick add with region staples and several items in a row, places in everyday words; suggestions, menus, reservations and shopping use approximate amounts too |
| 3 | Dual measures | Household measure and metric shown together from one deterministic conversion table (density and piece weight per ingredient class), validated for compatibility; imperial and °F by region; the model proposes a measure, deterministic code checks it |
| 4 | Beginner cooking mode | One step per screen, timers, kept-awake screen, preparation first, "how it should look", one-tap completion with optional edits; help for a missing ingredient or a problem (absorbs the start of PLAN-0045 and the PLAN-0042 Phase 2 substitutions) |
| 5 | Shared household | Superseding ADR for ownership (`AGENTS.md` §4), members with age and restrictions, invitations, a shared pantry, menu, and shopping list for the household (grouped by store section, everything checked put into the pantry at once), and migration of existing single-owner data |
| 6 | Onboarding and region | First value in about two minutes: who eats at home, what you have (staples by region); region in the AI context (cuisine, ingredient availability, meal patterns) |
| 7 | Validation | The owner's exhaustive test with a repository checklist, fixes, then a test script for three external people and the findings |

Phase 5 is large and changes the data model. It follows Phases 1–4 because those change screens the household will reuse, and the ADR can be prepared in parallel. If the owner wants the household earlier, Phase 5 moves up; nothing in Phases 1–4 blocks it.

## Phase 0 scope delivered

- **Decision record.** The owner decisions are recorded verbatim, with their consequences for existing plans.
- **Audit.** Every production screen was captured (phone and desktop) and reviewed. The findings are in the audit document.
- **Design proposal.** A design canvas with the proposed system (palette, type, components, principles) and five phone screens in the new direction: Hoje, Despensa with quick add (amount as Cheio/Metade/Pouco/Exato), Receita with both measures and "Tem", Cozinhar step by step, and the household's Compras grouped by store section.
- **Defect: cooking review.** The cooking proposal now matches ingredients without their own lot to pantry lots by normalized name and unit. Manually written recipes therefore propose the pantry items. The normalization is shared, through `RecipeIngredientReader.NormalizeName`, by the proposal, the shopping list, and reservations.
- **Defect: menu copy.** The "in use" hint no longer says nothing is reserved.

## Acceptance criteria (Phase 0)

- [x] Owner decisions recorded in the repository with their effect on other plans.
- [x] Audit of every production screen with concrete findings.
- [x] Design-direction proposal the owner can react to.
- [x] Both audit defects fixed, with a test for the name match.
- [x] Owner reaction to the design direction. The first direction felt like "um sistema". The owner approved direction 2 (white, near-black pills, Inter, rows instead of boxes, four tabs plus "+", the menu inside Hoje): "Vamos fechar assim por hora, ficou bem melhor já."

## Phase 1 scope delivered

- **Design system.** The rules are written in [`docs/design/design-system.md`](../design/design-system.md).
  - Tokens for light and dark live in `index.css`. Tailwind now has `success` and `warning` colors; the existing `bg-warning/10`-style classes were silently no-ops before.
  - The font is Inter, self-hosted. Lora and Manrope were removed.
  - Buttons are pills with no shadows. Inputs and selects are 44px with 15px text. Cards have no shadow.
- **Shell.**
  - `AppNavigation` is a bottom bar on phones and inline in the header from `md`, with Hoje, Despensa, "+", Receitas, and Compras.
  - `AccountMenu` holds Perfil, the language, and Sair.
  - The header has no blur.
  - The new `/app/compras` route (`ShoppingHubPage`) opens the list of the current menu: the accepted menu covering today, else the next accepted one, else a current draft. With no menu, it links to the menu page.
- **Screens.** The pantry and shopping lists are rows instead of boxes. The pantry attention panel uses the amber tint. All native selects and inputs use the new size.
- **Not yet moved:** Hoje, the recipe page, the menu page, and the profile still use their boxed sections. Phase 2 rewrites the pantry and the add flow, and Phase 4 rewrites recipe and cooking, so those screens move to the row pattern when they are rebuilt, not twice.

## Phase 2 scope delivered (first part: approximate amounts and quick add)

- **Estimated amounts.** A measured lot can be marked as an estimate. The rules are in [`docs/domain/inventory-lifecycle.md`](../domain/inventory-lifecycle.md#implemented-estimated-amounts-plan-0054-phase-2):
  - Full, about half, or a little of a stated package size becomes an estimate.
  - Without a package size, the choice becomes `Available` or `Low`.
  - Consumption beyond an estimate empties the lot.
  - A correction states whether its value is an estimate.
- **Implementation.**
  - Domain: `LotQuantity.Measured.IsEstimate`.
  - Migration: `AddLotQuantityEstimate`, which adds a boolean column defaulting to false and a check constraint.
  - API: `quantity.estimated` on create and on lot responses, and `estimated` on Correct adjustments. The OpenAPI contract and the generated client were regenerated.
- **Add screen.**
  - The questions use everyday words: "O que você tem?", "Quanto?" (Exato, Cheio, Metade, Pouco, Sem quantidade), and "Onde fica?" (Armário, Geladeira, Freezer, Outro).
  - Common items are one tap, with their usual unit and place. The list follows the interface language until Phase 6 adds the region.
  - A live hint shows the estimate ("Cerca de 500 g").
  - "Salvar e adicionar outro" stays on the form for the next item.
  - Package, date, and notes fold under "Mais detalhes".
- **Pantry list.** An estimate shows as "~500 g". The copy says "itens" and "Adicionar" instead of "lote".
- **Completed in the second part:**
  - The lot detail's correction has the checkbox "É uma estimativa (não pesei nem contei)".
  - The shopping list's "tem" shows "~" when a matched lot is an estimate (`availableIsEstimate`).
  - The AI context marks estimated items with `quantityIsEstimate`, and the suggest policy then adds `estimatedQuantities`. Both are written only when an estimate exists, so exact envelopes are unchanged.
- **Not done (accepted):**
  - Menu readiness and reservations use the estimated value without a mark; the lot itself shows "~".
  - Hoje, recipe, and menu screens keep their boxed sections until Phase 4.

## Phase 3 scope delivered: dual measures

- **Rules.** The rules are in [`docs/domain/kitchen-measures.md`](../domain/kitchen-measures.md). The conversion is deterministic (`lib/kitchenMeasures.ts`), uses a curated density and piece-weight table in pt, en, and es, and never stores kitchen measures.
- **Recipe page and cooking view.** Every ingredient shows its metric amount and, when compatible, the kitchen measure ("180 g · ≈ 1 xícara").
- **Editor.** The editor accepts xícara, colher de sopa, and colher de chá. A solid in the table is saved as grams through its density, so it still matches the pantry; a liquid or an unknown ingredient is saved as milliliters. A live hint shows the other measure.
- **Change from the plan's wording.** No model proposes a measure, because the deterministic table makes that unnecessary.
- **Not done:** imperial and °F wait for the region (Phase 6).

## Phase 4 scope delivered: beginner cooking mode

- **Guided cooking.** The cooking view has a preparation screen, then one stage per screen with a progress bar, large text, "Como deve ficar", and a timer that beeps and vibrates. The screen is kept awake during preparation and the steps, and the step is resumed after a reload. The finish takes one tap (the proposal as is, leftover portions, a rating), and "Ajustar" opens the detailed review. The rules are in [`docs/domain/recipes-and-cooking.md`](../domain/recipes-and-cooking.md#guided-cooking-for-beginners-plan-0054-phase-4).
- **Recipe page.** Ingredients are rows with both measures, the page shows the total time, and "Cozinhar agora" is large with the secondary actions smaller.
- **Defect found and fixed.** A missing saved step would have resumed at step 1, because `Number(null)` is 0.
- **Not done:**
  - Help for a missing ingredient or a problem during cooking needs a governed AI operation (PLAN-0042 Phase 2, PLAN-0045).
  - Dish images wait for the owner's provider decision (PLAN-0030).
  - Hoje and the menu page keep their boxed sections.

## Execution state

- **Current run delivery target:** finish Phase 2, then Phases 3 and 4 (owner, 2026-10-29). All three are delivered.
- **Current checkpoint:** Phase 4 complete, with the limits above.
- **Validation:**
  - **Frontend:** typecheck and lint are clean, and Prettier and the guards pass. Vitest passes 461 tests, including the new guided path (preparation, step and timer, one-tap finish with leftover and rating) and the overview and review paths.
  - **Replica:** phone captures of the recipe page, preparation, step, and finish screens.
  - **Backend:** no change in Phases 3–4.
- **Exact next action:**
  - The owner tests Phases 1–4 on production.
  - Then Phase 5, the shared household: the superseding ADR for ownership (`AGENTS.md` §4) first, then members, invitations, and the shared pantry, menu, and shopping. Phase 5 needs owner review of the ADR before implementation.
- **Blockers:** Phase 5 needs the owner's agreement with the ownership ADR.

## Progress log

### 2026-10-29T17:40:00Z — Claude Code

- Recorded the owner's decisions after the flow review, audited every screen, and published the design proposal.
- Fixed the two audit defects.

### 2026-10-29T21:30:00Z — Claude Code

- The owner approved design direction 2.
- Delivered Phase 1: the tokens, Inter, the pill primitives, the bottom and inline navigation, the account menu, and the Compras hub, with the pantry and shopping lists as rows.
- Added the success and warning Tailwind colors and the design-system document.
- Frontend tests pass (447), the guards pass, and the build passes.

### 2026-10-29T23:10:00Z — Claude Code

- Phase 2 first part: approximate pantry amounts end to end (domain, migration, API, client), a quick add screen in everyday words with common items, and save-and-add-another. The pantry list now shows "~" for estimates.
- Backend tests pass: 21 unit and 28 integration. Frontend tests pass: 450.

### 2026-10-29T23:50:00Z — Claude Code

- Phase 2 complete:
  - an estimated correction on the lot detail;
  - "~" in the shopping list;
  - the estimate mark in the AI suggest context.
- Tests pass: backend 21 targeted, frontend 451.

### 2026-10-30T00:30:00Z — Claude Code

- Phase 3: household measures next to every metric recipe amount, and cups and spoons accepted in the editor and saved as grams or milliliters, from a deterministic curated table.
- Frontend tests pass: 460.

### 2026-10-30T01:20:00Z — Claude Code

- Phase 4: guided cooking (preparation, one step per screen with a timer, wake lock, resume, one-tap finish) and the recipe page in the row style.
- Fixed a resume defect found by the new test.
- Frontend tests pass: 461.
