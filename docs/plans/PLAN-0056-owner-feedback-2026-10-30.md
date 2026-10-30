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
| 3 | 5 | Saved candidates: keep any candidate for later from a generation session, list them, and expand on demand (a library idea expands without AI) | Delivered |
| 4 | 1, 2 | Pantry entry: name autocomplete from the person's products and the catalog with match highlighting (accessible combobox), plus an optional auto-removal date that discards the lot through the normal lifecycle (recorded, reversible in history) | Item 1 delivered; item 2 next |

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

## Phase 3 delivered (item 5)

- **Candidates screen.** Each candidate has "Salvar para depois", next to "Escolher". Several can be saved, and choosing stays optional.
- **Recipes page.** A "Ideias salvas" section offers "Cozinhar esta" and "Remover". It is hidden when nothing is saved.
- **API.**
  - `POST /generation-sessions/{id}/saved-candidates` keeps a candidate. It is idempotent per owner, session, and candidate.
  - `GET /saved-candidates` lists the owner's saved candidates.
  - `DELETE /saved-candidates/{id}` removes one.
  - `POST /saved-candidates/{id}/recipe` cooks one and requires an Idempotency-Key.
- **Cooking.**
  - A saved candidate opens a new generation session holding just that candidate, keyed by the request, and goes through the normal selection.
  - Selection keeps its claims, replay, usage ledger, idea-library shortcut (no AI for an `idea-` candidate), and capture.
  - Inventory uses of lots no longer in the pantry become ingredients to buy.
  - On success the entry is marked cooked, leaves the list, and still answers retries with the same recipe.
- **Storage.** Table `recipes.saved_candidates`, owner-scoped, migration `AddSavedCandidates`.

## Phase 3 acceptance criteria

- [x] Several candidates of one session can be saved without choosing one; saving again does not duplicate.
- [x] Saved candidates are listed, removable, and visible only to their owner.
- [x] Cooking one later creates a recipe against the current pantry; a lot that is gone is not referenced.
- [x] A retried cook request returns the same recipe.

## Phase 4 item 1 delivered (name autocomplete)

- **Endpoint.** `GET /api/v1/inventory/name-suggestions?q=` returns up to 8 names, owner-scoped.
  - The person's own product names come first (500 most recent, non-deleted), then curated catalog names in the `Accept-Language` language.
  - Matching ignores case and accents.
  - Ranking puts names that start with the text first, then names with a word that starts with it, then names that only contain it. Shorter names come first within each group.
- **Field.** "O que você tem?" is now a combobox following the ARIA 1.2 pattern:
  - typing is debounced by 150 ms and the previous request is cancelled;
  - the matching part is highlighted, accent-aware;
  - names used before are labelled "já usado";
  - ArrowDown and ArrowUp move, Enter picks, Escape closes, and a click or tap picks.
  - When the repository has no `suggestNames`, or a request fails, it is a plain text field.

## Execution state

- **Current checkpoint:** Phases 1–3 merged and deployed. Phase 4 item 1 (name autocomplete) is on `agent/plan-0056-pantry-name-autocomplete`.
- **Validation (2026-10-30, host `rwantuk`):**
  - Backend unit tests: 176 passed, including 6 suggestion tests.
  - Architecture tests: 14 passed.
  - Integration test `NameSuggestionsOfferTheOwnersNamesFirstAndNeverAnotherPersons` passed.
  - Frontend inventory, i18n, and adapter tests: 126 passed, including the combobox tests (highlighting, keyboard, mouse, Escape, no-suggestion fallback).
  - Typecheck, lint, and format are clean. The OpenAPI and API client drift check is clean.
- **Exact next action:** Phase 4 item 2, the auto-removal date. Add an optional `autoRemoveOn` date on lot creation and editing. A daily job removes lots whose date has passed in the owner's calendar, through the normal lifecycle, recording the reason in the history.
- **Blockers:** none.

## Progress log

### 2026-10-30 — Claude Code

- Recorded the six owner reports and planned them in four phases.
- Delivered Phase 1 (item 4): read-only inspection of the owner's lots confirmed that only the rice is marked opened.

### 2026-10-30 — Claude Code (Phase 2)

- Delivered the suggestion guidance panel and contract (items 3 and 6), as listed above.

### 2026-10-30 — Claude Code (Phase 3)

- Delivered saved candidates (item 5).
- A replay first returned 404, because the entry was deleted after cooking. Entries are now marked cooked instead, so retries replay.

### 2026-10-30 — Claude Code (Phase 4, item 1)

- Delivered pantry name autocomplete. Product search names keep their accents in storage, so the owner's names are filtered in memory with the catalog normalization instead of SQL `LIKE`.

