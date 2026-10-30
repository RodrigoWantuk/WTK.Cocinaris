# Recipes and Guided Cooking

- **Status:** Accepted
- **Last updated:** 2026-10-29

## Recipe identity

Every saved recipe has a user-owned UUID. A recipe remains private unless the owner explicitly shares or publishes a selected revision snapshot.

The same mutable recipe object is never shared between users.

## Initial recipe origins

- AI generation;
- URL parsing;
- image parsing from a book, notebook, package, or similar source.

All origins produce the same normalized internal structure.

## Normalized recipe

A recipe revision may contain:

- title and description;
- yield and portions;
- structured products or ingredients with quantities;
- preparation and mise en place;
- ordered stages;
- required and optional equipment;
- active and total time estimates;
- storage, preservation, and reheating guidance;
- nutrition estimates;
- safety warnings and uncertainty;
- localization and unit representations;
- change reason and provenance.

## Import analysis

The parser must identify and display issues such as:

- a product used in instructions but absent from the list;
- a listed product never used;
- ambiguous or incompatible units;
- quantities inconsistent with portions;
- missing temperature, time, equipment, or preparation prerequisite;
- contradictory or incomplete order;
- uncertain extraction;
- possible food-safety concern.

AI proposes normalization and repairs. The user reviews uncertain or material changes before saving.

## Source retention

After import, store only:

- normalized recipe data;
- origin type such as generated, imported URL, or imported image;
- import timestamp;
- parser and normalization versions;
- confidence and warnings.

Do not retain the original URL, image, page text, or book photograph.

## Revisions and derived recipes

A revision preserves the same recipe identity when the change is a correction or refinement, such as quantity adjustment, clearer wording, time correction, or modest seasoning change.

A derived recipe receives a new UUID when the method, identity, or intended result changes materially, such as an air-fryer version, vegetarian version, or substantial ingredient structure change.

A derived recipe records parent recipe, parent revision, and derivation reason.

### Implemented library (PLAN-0038)

- **Origins.**
  - **Recipes** are `generated` (cook-now), `manual` (written by the owner), or `derived`.
  - **Revisions** are `generated`, `manual`, `edited`, or `derived`, and may carry a change reason and the completed cooking session they came from.
- **Owner-written content.** Validated and composed deterministically into the same protocol 0.3 shape as generated content, so display and cooking read both the same way.
  - Rows that keep their identifier keep their pantry link.
  - Fields the editor does not show are carried over.
- **Editing.** Every edit appends the next revision. An edit that started from an older revision is refused instead of overwriting the newer one. Earlier revisions stay readable at `/app/receitas/:id/versoes/:n`.
- **Revision or derived recipe.** The owner chooses between "Editar" (a revision) and "Criar variação" (a derived recipe). The application does not infer materiality.
- **Derived recipes.** A derived recipe requires a reason and records its parent recipe and parent revision. The parent is not changed.
- **Favorites and archive.** Both are flags on the recipe and never touch revision content.
  - Archived recipes leave the default list and the home page; their history and executions are kept, and they can be restored.
  - Recipes are not deleted, because executions and derived recipes reference them.
- **History.** The recipe page lists revisions and finished cooking sessions with their date, servings, rating, and notes.

## Favorites and menu references

A favorite points to the logical recipe and follows its current revision.

A menu entry can follow the current revision or remain pinned to a specific revision. Pinning must be explicit and visible.

## Sharing

Sharing priority is:

1. private share link;
2. direct share to another Cocinaris user;
3. unlisted public link;
4. searchable public catalog later.

A share points to an immutable snapshot. Accepting it creates a private copy with a new UUID and provenance. Future changes by either user do not cross account boundaries.

## Execution start and mise en place

Viewing a recipe does not start cooking. The user starts an execution explicitly.

Before start, the product reviews:

- portions;
- products and selected lots;
- quantities and reservations;
- missing required and optional products;
- substitutions;
- equipment;
- thawing and advance preparation;
- expected time;
- relevant restrictions and safety notes.

The checklist informs and offers adaptations but does not arbitrarily block continuation.

### Implemented execution (PLAN-0042 Phase 1)

- **Start.** "Cozinhar agora" on the recipe page creates an execution pinned to the current revision. The servings default to the revision's. A recipe has at most one execution in progress, and starting again returns it. The home page lists executions in progress so the cook can resume.
- **Cooking view.** Ingredients, equipment, and stages with each stage's instructions, timings, and doneness cues, all with checkboxes. Checks are kept only in the browser, per execution.
- **Completion proposal.** Deterministic and recomputed from the pinned revision and the current pantry. Quantities scale with servings cooked over the revision's servings: whole units round up, and grams and milliliters round to whole numbers. Each line is `ready`, `insufficient` (capped at what the lot holds), `lot_unavailable`, or `not_tracked`, and the cook may pick any measured pantry lot for any line.
- **Finalization in Phase 1.** Lot quantities consumed, leftover portions with storage and an optional use-by date, a 1–5 rating, and private notes. Photos and consumed-portion records come in later phases.
- **Saving changes after cooking (Phase 2, with PLAN-0038).** The finished screen offers "Salvar ajustes na receita" (next revision) and "Criar variação" (derived recipe). The editor shows the session's notes and rating, and the result records the session. Nothing changes the saved recipe unless the owner saves.
- **Changes while cooking (PLAN-0042 Phase 2).** "Mudou alguma coisa?" on the cooking page lets the cook say, per recipe ingredient, that they used something else (a name, and optionally the amount used), used a different amount, or left it out. The changes are stored on the session (`PUT /api/v1/recipes/executions/{executionId}/adjustments`, the whole list each time, at most 40, only while the session is in progress) and never change the recipe. The completion proposal follows them: a substitute is looked up in the pantry by its own name and catalog identity (the recipe's linked lot is ignored) and is `not_tracked` when nothing matches; an amount is taken as used for this session, not scaled again; a left-out ingredient is `skipped` and consumes nothing. Review lines show "(no lugar de …)". When the finished screen opens the editor, the changes are already applied to the draft: substitutes rename the ingredient and drop its pantry link, amounts are converted back to the recipe's servings (for grams, milliliters, and units only), and a left-out ingredient becomes optional rather than removed. The editor lists what it changed so the owner reviews before saving.
- **Abandonment.** Ends the execution without touching inventory.

### Guided cooking for beginners (PLAN-0054 Phase 4)

- **Preparation first.** The cooking view opens on "Antes de começar": every ingredient with its metric amount and kitchen measure (see [`kitchen-measures.md`](kitchen-measures.md)), a check for each one set out, and the equipment.
- **One stage per screen.** Each stage has a progress bar ("Passo 2 de 5"), large instruction text, and the doneness cues as "Como deve ficar". A stage with a passive time (or else an active time) offers a timer. The timer counts against a target time, so it stays right in a throttled background tab, and it ends with a beep and a vibration where the device allows them.
- **Screen kept awake.** The Screen Wake Lock is requested during preparation and the steps, and it is requested again when the page becomes visible. Where the browser refuses or lacks it, nothing breaks.
- **Resuming.** The current step is kept in browser storage per execution, like the checklist, and reopening the cooking view resumes there. Both are cleared on completion or abandonment.
- **One-tap finish.** "Bom apetite!" lists what leaves the pantry (the backend proposal as is). It offers "Sobrou?" (no, or 1 to 3 portions, stored in the refrigerator) and "Como ficou?" (rating 2, 4, or 5), and "Concluir" commits through the same atomic completion. "Usei diferente? Ajustar" opens the detailed review (lots, quantities, leftovers with place and date, rating, notes).
- **Full view.** "Ver receita inteira" keeps the earlier checklist view.
- **Recipe page.** Ingredients are rows showing both measures, the page shows the total time (work plus waiting), and "Cozinhar agora" is the main action.
- **Not yet implemented:** help during cooking for a missing ingredient or a problem (substitutions, PLAN-0042 Phase 2 and PLAN-0045). It needs a governed AI operation and comes in a later phase.

## Stage-based instructions

Instructions are divided into stages. The profile determines a default detail level; the recipe AI request carries it as `instructionStyle` (see `docs/ai/recipe-artifact-protocol.md`). Every stage can request more explanation independently.

A beginner may receive sensory references, heat levels, approximate durations, and expected appearance. An experienced user may receive concise instructions for familiar techniques.

## Text troubleshooting

Each active stage can open a textual troubleshooting interaction.

The backend supplies bounded structured context:

- recipe and starting revision;
- current active instructions;
- current and completed stages;
- products and actual substitutions;
- quantities;
- equipment;
- recorded time and temperature when available;
- prior troubleshooting and execution changes.

The model may explain the likely problem, propose a recovery, adjust future stages, or recommend stopping when safety is uncertain.

Material updates to the active instructions require clear user-facing disclosure and an execution log entry.

## Execution-local recipe state

Troubleshooting and spontaneous substitutions update the execution-local recipe representation. They do not silently mutate the saved recipe.

At finalization the user can:

- discard execution changes;
- select changes to save as a new revision;
- create a derived recipe;
- leave notes without changing the recipe.

## Finalization

The completion flow proposes:

- products and lot quantities consumed;
- substitutions, omissions, and additions;
- portions produced and consumed;
- leftover quantity and storage;
- frozen or preserved components;
- waste or discard;
- reservation release;
- rating, difficulty, notes, and optional photos.

The user can edit freely before the authoritative transaction.

## History and learning

Execution history may support future suggestions by recording:

- rating and intention to repeat;
- difficulty and unfamiliar techniques;
- recurring quantity or seasoning corrections;
- successful substitutions;
- cleanup and time observations;
- unused leftovers;
- troubleshooting events and results;
- user photos and private comments.

The application may propose learned preferences but requires confirmation before making important profile changes.

## Community boundary

Searchable community recipes and ranking are deferred. Publication is explicit and revision-specific. Personal notes, household data, inventory, troubleshooting, and photos remain private unless the user separately chooses to publish permitted content.
