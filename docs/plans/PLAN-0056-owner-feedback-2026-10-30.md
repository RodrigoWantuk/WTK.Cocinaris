# PLAN-0056: Owner Feedback 2026-10-30 — Pantry Entry, Auto-Removal, Guided Suggestions, Saved Candidates, Attention Fix

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High (owner testing feedback)
- **Owner:** Claude Code (claimed 2026-10-30)
- **Created:** 2026-10-30
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0056-owner-feedback-2026-10-30`
- **Pull request:** Draft, opened with the first commit

## Owner reports (2026-10-30, verbatim intent)

1. **Pantry name autocomplete.** When typing "O que você tem?", suggest similar names as the user types, highlighting the match in a combobox below, and let them pick one.
2. **Auto-removal date.** When adding an item, set a date after which it is removed from the pantry automatically ("100 g de requeijão cremoso … depois do dia tal, apague automaticamente").
3. **Guided suggestions.** On "O que cozinhar agora?", let the person steer the AI before the three candidates are generated. Suggested directions: full meal or quick snack; pasta, meat, broth, and so on; a regional cuisine; quick or slow preparation. The owner leaves the design open.
4. **Attention bug.** Only "Arroz Branco Cru", a long-lasting item, kept asking for attention.
5. **Save candidates for later.** On "Escolha um candidato", save one or more candidates for later instead of having to pick one and continue now. Choosing now stays possible.
6. **Required and excluded pantry items.** Before generating, mark pantry items the suggestion **must** use and items it must **not** use. Excluded items are never sent to the AI; the others go as required or optional.

Also reported the same day and handled in PLAN-0055 (PR #63): suggestions sometimes failed on the first request.

## Phases

| Phase | Items | Outcome | Status |
| --- | --- | --- | --- |
| 1 | 4 | Dry foods that keep once opened no longer ask for attention (curated catalog rule) | Delivered |
| 2 | 3, 6 | Suggestion pre-filters: meal kind, dish family, cuisine, time, required and excluded pantry items. Sent to the AI as constraints and applied as hard filters to library ideas (PLAN-0055 Phase 2). Excluded lots are removed from the AI context. | Next |
| 3 | 5 | Saved candidates: keep any candidate for later from a generation session, list them, and expand on demand (a library idea expands without AI) | After Phase 2 |
| 4 | 1, 2 | Pantry entry: name autocomplete from the person's products and the catalog with match highlighting (accessible combobox), plus an optional auto-removal date that discards the lot through the normal lifecycle (recorded, reversible in history) | After Phase 3 |

## Phase 1 delivered (item 4)

- **Root cause.** The rice lot is the only one marked `Opened`, and it has no printed date. The attention rule treats every opened package without a date as `estimate uncertain`, which makes no sense for dry, shelf-stable foods.
- **Fix.**
  - The catalog marks 36 ingredients `keepsWhenOpened`.
  - `IngredientCatalog.KeepsWhenOpened(name)` accepts only a curated name, optionally followed by dry descriptors.
  - The advisor returns `normal` with reason `keeps_when_opened` for such opened lots in the pantry. A close or passed printed date still wins.
- **Documentation.** `docs/domain/inventory-lifecycle.md`, plus the reason label in en, pt-BR, and es.

## Acceptance criteria (Phase 1)

- [x] An opened "Arroz Branco Cru" in the pantry with no date is `normal`.
- [x] "Feijão preto cozido", "Arroz doce", brand names, and anything outside the pantry are unchanged.
- [x] A close printed date on an opened dry food still asks for attention.

## Execution state

- **Current checkpoint:** Phase 1 implemented and tested.
- **Validation (2026-10-30, host `rwantuk`):**
  - Unit tests: 80 passed in the inventory and ingredient suites, including the new advisor and catalog-rule tests.
  - Frontend: inventory and i18n tests, 68 passed. Typecheck is clean.
- **Exact next action:** Phase 2. Design the pre-filter panel on "O que cozinhar agora?" and the request contract (`preferences` and `pantry` selections), with the AI envelope fields and library hard filters to match.
- **Blockers:** none.

## Progress log

### 2026-10-30 — Claude Code

- Recorded the six owner reports and planned them in four phases.
- Delivered Phase 1 (item 4): read-only inspection of the owner's lots confirmed that only the rice is marked opened.
