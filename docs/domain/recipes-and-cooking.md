# Recipes and Guided Cooking

- **Status:** Accepted
- **Last updated:** 2026-07-28

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

## Stage-based instructions

Instructions are divided into stages. The profile determines a default detail level. Every stage can request more explanation independently.

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
