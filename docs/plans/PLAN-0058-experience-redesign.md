# PLAN-0058: Experience Redesign of the Main Screens

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High (owner feedback)
- **Owner:** Claude Code (claimed 2026-10-30)
- **Created:** 2026-10-30
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0058-experience-redesign` (Phase 1, merged via #74); `agent/plan-0058-phase-2` (Phase 2)
- **Pull request:** Draft, opened with the first commit
- **Related documents:** [`docs/design/design-system.md`](../design/design-system.md), [PLAN-0057](PLAN-0057-registry-and-ux-redesign.md)

## Owner feedback (2026-10-30)

"Cara, quando eu peço para voce rever UX, não é só tema e cor não, a tela de inicio está horrível, as receitas são textos soltos, a tela está toda desorganizada... Custa muito você fazer uma revisão inteira do sistema e melhora da experiencia do usuário, verdadeiramente?"

PLAN-0057 changed the visual language and fixed specific findings, but the page structures stayed as stacks of bordered boxes and text lists. This plan redesigns the structure of the screens people use most, starting from their tasks.

## Phases

| Phase | Outcome | Status |
| --- | --- | --- |
| 1 | Home, recipe library, recipe page, and pantry list rebuilt around tasks, with image cards, carousels, tiles, icons, and grouped lists | Merged via #74; deployed `7003eff` |
| 2 | The remaining screens rebuilt the same way: pantry entry, menu, shopping, cooking steps, generation and candidates, profile; empty states with illustrations; a first-run welcome | Merged via #75; deployed `5cdadb3` (cooking steps and profile were already restructured in PLAN-0054 and PLAN-0057, so they are unchanged here) |
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

## Phase 2 scope delivered

- **Shopping list.**
  - A summary card at the top: items to buy, how many are checked off, and a progress bar.
  - Store sections carry an icon (produce, meat and fish, dairy, bakery, dry goods, drinks) and a count, and each section's rows sit on one surface.
  - Each row shows the name and amount on one line, the need, pantry, and meals below it, and a larger checkbox.
  - What the pantry already covers is folded into "Você já tem (N)".
- **Menus list.** The menus come first, as cards with a calendar leaf (month and first day), the period, the meal count, and a status pill. "Novo cardápio" is a disclosure that opens by itself when there is no menu yet.
- **Menu page.** Each day has a calendar leaf (weekday and day), with the accent on today; meals show the dish image; the back link has a chevron.
- **"O que cozinho agora?"**
  - An accent hero card states the promise.
  - While suggestions are being made, skeleton cards in the shape of the result show.
  - The candidates are image cards with the meal type (now translated; before, the raw code was shown) and time, servings, and difficulty chips, plus "Quero esta" and "Guardar para depois" with a bookmark icon.
- **Pantry entry.** Three numbered steps on surfaces (what, how much, where) and a details surface. The chips use the same style as `ChoiceChips`.
- **Empty states.** A shared `EmptyState` (icon in an accent circle, title, detail, first action) on the pantry and recipe lists.
- **First run.** While the person has no recipes, "Hoje" shows a welcome with three linked steps (pantry, suggestions, menu) instead of empty sections.

## Acceptance criteria

- [x] Phase 1: home, recipe library, recipe page, and pantry list rebuilt; frontend tests, typecheck, lint, and format pass; replica captures reviewed on phone and desktop in both themes, with axe clean in light.
- [x] Phase 2: the remaining screens rebuilt to the same standard.
- [ ] Phase 3: the owner's walkthrough and the fixes that follow.

## Execution state

- **Current run delivery target:** Phase 2 (owner: "Ja fiz o merge, pode seguir").
- **Current checkpoint:** Phases 1 and 2 delivered; Phase 2 awaits review.
- **Validation (Phase 2, lean):**
  - **Frontend:** typecheck, ESLint, and Prettier are clean; the `src/features` and `src/app` suites passed 322/322. New: first-run welcome and recipe carousel tests. Updated: the shopping amount tests.
  - **Visual:** replica captures of all 19 routes reviewed in both themes; axe reported no violations in light.
  - **Backend:** no change.
- **Validation (Phase 1):** recorded in PR #74.
- **Not run:** full suites, screen reader.
- **Exact next action:** Phase 3: the owner walks through the app on the host and reports what still bothers them; fixes follow. The owner's mid-run requests (install, quick actions, cook page) are PLAN-0059.
- **Blockers:** None.

## Progress log

### 2026-10-30T23:59:00Z — Claude Code

- Plan created from the owner's feedback. Phase 1 delivered: home, recipe library, recipe page, and pantry list restructured around tasks with image cards, carousels, tiles, icons, and grouped lists.

### 2026-10-31T00:45:00Z — Claude Code

- Phase 1 merged via #74 and deployed at `7003eff`. Phase 2 delivered on a new branch:
  - shopping summary and section icons;
  - menu calendar leaves and meal images;
  - suggestion hero, skeletons, and candidate cards;
  - pantry entry steps;
  - `EmptyState`;
  - a first-run welcome.

### 2026-10-31T01:30:00Z — Claude Code

- Phase 2 merged via #75 and deployed at `5cdadb3`. The owner's new requests during this run went to PLAN-0059.
