# ADR-0011 — Shared recipe-idea library: reuse generic AI recipes across people without AI calls

- **Status:** Accepted
- **Date:** 2026-10-30
- **Owners:** Repository owner; Claude Code (implementation)
- **Decision scope:** Recipes module (suggestion and selection), AI cost, privacy, food safety
- **Related:** [PLAN-0055](../../plans/PLAN-0055-ingredient-identity.md) Phase 2, [ADR-0010](0010-ingredient-identity-catalog-embeddings-pgvector.md), [`docs/ai/recipe-idea-library.md`](../../ai/recipe-idea-library.md)
- **Supersedes:** None
- **Superseded by:** None

## Context

Every suggestion request calls the AI for three candidates, and every selection calls it again to expand the chosen one. The owner asked (2026-10-30) for a way to avoid unnecessary AI calls, transparently for the user: keep recipe ideas indexed by main ingredients, score them with our own algorithm, and use the AI only when nothing stored fits.

`docs/ai/overview.md` requires that private context is never reused across users, and AGENTS.md requires deterministic code to own safety decisions.

## Decision

1. **Capture after expansion.** After a successful AI expansion, the recipe is kept as a generic *idea* only when all of these hold:
   - every ingredient resolves to a catalog ingredient (ADR-0010);
   - every equipment item has a stable code;
   - the text contains no private-context term and no name the owner typed outside the catalog.

   Names become catalog names in the output language, and inventory and equipment ids are removed. Ideas live in `recipes.recipe_ideas`, with no owner column.
2. **Restriction rule.** An idea records the restriction codes it was generated under. It is served only to a person whose restriction codes are **all** in that set. This is conservative, uses no allergen data, and does not trust the model.
3. **Variant rule.** An idea is served only for the same output language, instruction detail, and cooking skill it was written for.
4. **Retrieval and scoring.**
   - **Hard filters in SQL:** language, detail, skill, restrictions (`@>`), meal type for menu slots, at least one pantry ingredient (`&&`, GIN index), and equipment the person has (`<@`).
   - **Scoring:** 0.8 × pantry coverage of the required ingredients (catalog ingredient and unit) + 0.2 × a smoothed selection rate.
   - **Exclusions:** coverage below 0.6, ideas whose name the person already has, and strategies not allowed in the mode.
   - **Choice:** three ideas with distinct strategies and dishes.
5. **Rebuild and validate.** The chosen ideas are rebuilt for the person (their lots, names, and equipment ids) and must pass the same protocol validation as AI output, against the request the AI would have received. Choosing one builds the recipe the same way, again with no AI call.
6. **Fallback.** Anything that does not fit (fewer than three ideas, a validation failure, a changed language or restriction) falls back to the AI exactly as before. The library can only remove calls; it never changes what is valid.
7. **Novelty share.** 25 % of suggestion requests (`Recipes:Ideas:NoveltyShare`) go to the AI even when the library could answer, so the library keeps growing and suggestions stay varied.

## Consequences

- When the library answers, a suggestion request costs no AI call and no usage credits, and so does choosing a library idea. Images are already shared by render key, so recipes from the same idea also share their dish image.
- The library grows only from recipes people actually chose.
- A person's own recipes are never shared, only the generic idea. Their saved recipe stays a private, independent copy.
- The privacy guard is strict and drops some recipes (for example any text with "pantry" or "user"). That is acceptable: a dropped recipe only means a later AI call.
- The restriction rule means people with restrictions benefit only from ideas generated under the same or broader restrictions.
- Reported quality signals (cooked, rated) are not used yet; scoring uses selection only.
