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

## Execution state

- **Current run delivery target:** Phase 1.
- **Current checkpoint:** Phase 1 delivered as above, with the limits noted.
- **Validation:**
  - **Frontend:** typecheck is clean. Vitest passes: 55 files, 447 tests. The shell tests were updated for the account menu and the `/app/compras` route. Prettier is clean on `src`.
  - **Guards:** ts-only, interactive-nesting, production-isolation, build-mode, and third-party-html pass.
  - **Build:** passes.
  - **Visual check:** phone and desktop captures through the local proxy replica covered Hoje, Despensa, Receita, the menu, Compras, and the account menu.
- **Not run:** the backend suites (no backend change in Phase 1).
- **Exact next action:** Phase 2, the easy pantry.
  - The amount can be exact or approximate (Cheio, Metade, Pouco). An approximate amount is stored with low confidence and a nominal value derived from the package size.
  - A quick-add sheet behind "+" adds several items in a row, with region staples.
  - Places are named in everyday words.
  - Suggestions, menus, reservations, and shopping treat approximate amounts as estimates.
- **Blockers:** none.

## Progress log

### 2026-10-29T17:40:00Z — Claude Code

- Recorded the owner's decisions after the flow review, audited every screen, and published the design proposal.
- Fixed the two audit defects.

### 2026-10-29T21:30:00Z — Claude Code

- The owner approved design direction 2.
- Delivered Phase 1: the tokens, Inter, the pill primitives, the bottom and inline navigation, the account menu, and the Compras hub, with the pantry and shopping lists as rows.
- Added the success and warning Tailwind colors and the design-system document.
- Frontend tests pass (447), the guards pass, and the build passes.
