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
- [ ] Owner reaction to the design direction (Phase 1 starts from it).

## Execution state

- **Current run delivery target:** Phase 0.
- **Current checkpoint:** Phase 0 delivered as above.
- **Validation:**
  - **Backend unit tests:** the reservation, shopping, execution, and cook-now tests pass (23), including the new name-match test.
  - **Backend integration tests:** `RecipeExecutionApiTests` (7) pass after the proposal change.
  - **Frontend:** a copy change only; no code change.
- **Not run:** the full suites.
- **Exact next action:** The owner reacts to the design canvas. Then Phase 1:
  - Tokens (color, type, spacing, radii, dark mode) in `index.css` and `tailwind.config.js`.
  - Figtree replaces Lora and Manrope.
  - Bottom navigation on phones, with the avatar menu holding profile, household, and language.
  - Button, chip, status pill, sheet, and segmented control primitives.
  - Every existing screen moved to the new primitives without changing its behavior.
- **Blockers:** Phase 1 waits for the owner's reaction to the direction (taste decision); Phases 2–4 do not depend on it for their backend parts.

## Progress log

### 2026-10-29T17:40:00Z — Claude Code

- Recorded the owner's decisions after the flow review, audited every screen, and published the design proposal.
- Fixed the two audit defects.
