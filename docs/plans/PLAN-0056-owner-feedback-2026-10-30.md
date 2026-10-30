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
| 2 | 3, 6 | Suggestion pre-filters: meal kind, dish family, cuisine, time, required and excluded pantry items. Sent to the AI as constraints and applied as hard filters to library ideas (PLAN-0055 Phase 2). Excluded lots are removed from the AI context. | Delivered |
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

## Phase 2 delivered (items 3 and 6)

- **Screen.** "O que cozinhar agora?" has a collapsible "Direcionar as sugestões (opcional)" panel. It offers:
  - meal kind (full meal, something light, quick snack);
  - up to 3 dish families out of 12;
  - a cuisine out of 12;
  - time (up to 20 min, up to 45 min);
  - each measured pantry item as "Usar", "Talvez" (default), or "Não usar", with at most 5 required.

  It is labelled in en, pt-BR, and es. The chips use `aria-pressed`, and each item is a native radio group.
- **API.** `guidance` on `POST /api/v1/recipes/generation-sessions` takes closed codes only and returns 400 for unknown codes, limits, conflicts, or an unavailable required item.
- **AI request.** Excluded lots are removed during context assembly. Required lots carry `mustUse` and are kept first. `suggestionGuidance` carries the preferences and the hard rules ([protocol](../ai/recipe-artifact-protocol.md#suggestion-guidance-plan-0056-phase-2)).
- **Deterministic validation.** A candidate that skips a required item or exceeds the time limit is rejected and repaired, in both the .NET and the contract validators.
- **Idea library.** Requests with style preferences go to the AI. Required items and the time limit filter the ideas.

## Phase 2 acceptance criteria

- [x] Excluded items never appear in the AI request, neither id nor name.
- [x] Every candidate uses each required item, and the time limit holds (enforced by validation, not trust).
- [x] Unknown codes and inconsistent choices are rejected; nothing typed reaches the model.
- [x] Without any choice, the request and the AI envelope are unchanged.

## Execution state

- **Current checkpoint:** Phases 1 (merged #64, deployed) and 2 (this branch) are delivered.
- **Validation (2026-10-30, host `rwantuk`):**
  - Backend unit tests: 170 passed.
  - Integration tests: 17 passed, including `GuidanceReachesTheAiExcludedItemsNeverDoAndBadCodesAreRejected`.
  - Architecture tests: 14 passed.
  - Contract tests: passed.
  - Frontend recipe, i18n, and repository tests: 53 passed. Typecheck, lint, and format are clean. OpenAPI export and API client drift checks are clean.
- **Exact next action:** Phase 3 (item 5): save candidates for later. The design is a per-owner saved-candidate list holding the session's validated candidate snapshot; expanding later uses the normal selection path, so a library idea costs nothing.
- **Blockers:** none.

## Progress log

### 2026-10-30 — Claude Code

- Recorded the six owner reports and planned them in four phases.
- Delivered Phase 1 (item 4): read-only inspection of the owner's lots confirmed that only the rice is marked opened.

### 2026-10-30 — Claude Code (Phase 2)

- Delivered the suggestion guidance panel and contract (items 3 and 6), as listed above.

