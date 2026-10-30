# PLAN-0058: Experience Redesign of the Main Screens

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High (owner feedback)
- **Owner:** Claude Code (claimed 2026-10-30)
- **Created:** 2026-10-30
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0058-experience-redesign`
- **Pull request:** Draft, opened with the first commit
- **Related documents:** [`docs/design/design-system.md`](../design/design-system.md), [PLAN-0057](PLAN-0057-registry-and-ux-redesign.md)

## Owner feedback (2026-10-30)

"Cara, quando eu peço para voce rever UX, não é só tema e cor não, a tela de inicio está horrível, as receitas são textos soltos, a tela está toda desorganizada... Custa muito você fazer uma revisão inteira do sistema e melhora da experiencia do usuário, verdadeiramente?"

PLAN-0057 changed the visual language and fixed specific findings, but the page structures stayed as stacks of bordered boxes and text lists. This plan redesigns the structure of the screens people use most, starting from their tasks.

## Phases

| Phase | Outcome | Status |
| --- | --- | --- |
| 1 | Home, recipe library, recipe page, and pantry list rebuilt around tasks, with image cards, carousels, tiles, icons, and grouped lists | Delivered in this branch |
| 2 | The remaining screens rebuilt the same way: pantry entry, menu, shopping, cooking steps, generation and candidates, profile; empty states with illustrations; a first-run welcome | Next |
| 3 | Owner walkthrough on the host, then fixes | After Phase 2 |

## Phase 1 scope delivered

- **Home ("Hoje").** It answers "what do we cook today?" in task order:
  1. **Cooking in progress:** accent cards with the dish image.
  2. **Today's menu:** a card per meal with the image, the meal, the pantry readiness ("Tudo na despensa" or "Faltam N itens"), and a full-width "Cozinhar agora" button.
  3. **Four starting points:** large tiles. "Sugerir receitas" is the accented one; the others are add to pantry, menu, and shopping list.
  4. **"Prepare-se":** one row per task, each with an icon for its kind (buy, thaw, use soon).
  5. **"Use logo":** a horizontal carousel of item cards.
  6. **"Suas receitas":** a carousel of recipe image cards (previously a list of plain links).
- **Recipe library.** A grid of image cards: two per row on phones, three from `sm`, four on wide screens. The favorite toggle sits on the image, and the main actions have icons.
- **Recipe page.**
  - **Facts:** tiles for time, servings, and times cooked.
  - **Ingredients:** a card with the pantry summary ("Você tem 2 de 4"). Each row has a status icon (in the pantry, partly, missing), the household measure under the name, and the amount on the right.
  - **Equipment:** chips.
  - **Steps:** a numbered timeline with one card per stage, time chips with icons, and the "how to tell it worked" cues in an accent box.
  - **History:** collapsed.
- **Pantry list.** Items are grouped by place in the order people look: fridge, freezer, cupboard, other. Each group has an icon and a count, and each row shows the amount on the right and a chevron. The attention box shows its items as white rows.
- **Shared pieces.**
  - `RecipeCard`: an image card with an optional favorite toggle.
  - `HomeSection`: a heading plus an optional "see all" link.
  - Photo-less recipes show their initial on a soft tint picked from the name. The six palettes keep contrast in both themes.

## Acceptance criteria

- [x] Phase 1: home, recipe library, recipe page, and pantry list rebuilt; frontend tests, typecheck, lint, and format pass; replica captures reviewed on phone and desktop in both themes, with axe clean in light.
- [ ] Phase 2: the remaining screens rebuilt to the same standard.
- [ ] Phase 3: the owner's walkthrough and the fixes that follow.

## Execution state

- **Current run delivery target:** Phase 1.
- **Current checkpoint:** Phase 1 delivered.
- **Validation (lean):**
  - **Frontend:** typecheck, ESLint, and Prettier are clean; the `src/features` and `src/app` suites passed 320/320. The home tests were updated for the new structure: reminder rows are links, and the menu card text changed.
  - **Visual:** replica captures of all 19 routes were reviewed at phone and desktop sizes in both themes, and axe reported no violations in light.
  - **Backend:** no change.
- **Not run:** full suites, screen reader.
- **Exact next action:** The owner reviews the draft PR. Phase 2 then rebuilds pantry entry, menu, shopping, cooking steps, generation and candidates, and profile, and adds illustrated empty states and a first-run welcome.
- **Blockers:** None.

## Progress log

### 2026-10-30T23:59:00Z — Claude Code

- Plan created from the owner's feedback. Phase 1 delivered: home, recipe library, recipe page, and pantry list restructured around tasks with image cards, carousels, tiles, icons, and grouped lists.
