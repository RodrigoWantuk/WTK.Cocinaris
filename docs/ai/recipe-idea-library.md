# Recipe-Idea Library

- **Plan:** [PLAN-0055](../plans/PLAN-0055-ingredient-identity.md) Phase 2
- **Decision:** [ADR-0011](../architecture/decisions/0011-shared-recipe-idea-library.md)
- **Code:** `Cocinaris.Modules.Recipes/Ideas` (`RecipeIdeaLibrary`), `PostgreSqlRecipeIdeaStore`, table `recipes.recipe_ideas`

## What it does

The library keeps generic versions of recipes the AI already wrote, and it answers later suggestion requests and selections with them, for anyone they fit, without calling the AI. Nothing changes in the interface.

## Flow

```text
Suggestion request
  ├─ library: hard filters → score → 3 distinct ideas → rebuild for the person → protocol validation
  │     └─ valid → session with 3 "idea-<id>" candidates (no AI call, no usage)
  └─ otherwise, or 25 % of the time (novelty) → AI as before

Choosing a candidate
  ├─ "idea-<id>" and the same language, detail, skill, and restrictions → rebuild the recipe → validation → saved (no AI)
  └─ otherwise → AI expansion as before → the result is offered to the library (capture)
```

## Capture rules

An expanded recipe becomes an idea only when all of these hold:

- every ingredient name resolves to a catalog ingredient;
- every equipment item maps to a profile item with a stable code;
- its text (candidate name and summary, recipe name, yield, stages, cues, preparations, notes, assumptions, hints) contains:
  - no private-context term (`inv-1`, user, allerg…, pantry, restriction…, household, owner, URLs), using the same patterns as the thumbnail privacy check;
  - no name the owner typed that is not a curated catalog name (product or equipment custom names used in the recipe);
- it has at least one required, non-assumed ingredient.

When stored:

- ingredients get catalog names in the output language;
- `inventory` becomes `additional`;
- inventory ids are removed;
- equipment ids become stable codes, and a custom equipment name becomes its code.

The idea records:

- language, instruction detail, cooking skill, and meal type;
- strategy;
- the restriction codes it was generated under;
- the slugs of its required ingredients and its equipment codes.

Duplicates are recognized by their signature (language, level, restrictions, meal type, dish format, ingredient slugs, and name), and only the first is kept.

## Serving rules

| Step | Rule |
| --- | --- |
| Language, detail, skill | Must match the person's |
| Restrictions | The person's restriction codes must all be among the idea's (`Restrictions @> user`) |
| Meal type | Must match the target slot when planning a menu |
| Pantry | The idea uses at least one ingredient the person has |
| Equipment | The idea needs only equipment codes the person has |
| Coverage | At least 0.6 average coverage of required ingredients by catalog ingredient and unit (`Recipes:Ideas:MinimumCoverage`) |
| Score | 0.8 × coverage + 0.2 × (selected + 1) / (offered + 2) |
| Already owned | Ideas whose name matches one of the person's recipes are skipped |
| Choice | Three ideas with distinct strategies and distinct dishes (format, technique, main ingredients), as the protocol requires |
| Validation | The rebuilt answer must pass `RecipeProtocolValidator` against the request the AI would have received |

A rebuilt candidate takes the person's largest lot for each ingredient (by catalog ingredient and unit). Ingredients they lack become ingredients to buy (at most 5). Quantities stay those of the idea. The cooking proposal scales them to the portions chosen when cooking.

## Configuration

| Key | Default | Meaning |
| --- | --- | --- |
| `Recipes:Ideas:Enabled` | `true` | Capture and serve |
| `Recipes:Ideas:NoveltyShare` | `0.25` | Share of suggestion requests sent to the AI anyway |
| `Recipes:Ideas:MinimumCoverage` | `0.6` | Lowest pantry coverage to suggest an idea |
| `Recipes:Ideas:CandidatePoolSize` | `40` | Ideas read before scoring |

## Observability

- `recipes.recipe_ideas.TimesOffered` and `TimesSelected` count reuse.
- A library answer leaves no row in `ai.usage_ledger`, so the calls avoided equal the library sessions. Find them as generation sessions whose candidates start with `idea-`.
- Recipes built from an idea have `SourceCandidateId = idea-<id>`.

## Privacy and safety

- No owner, lot id, equipment id, or owner-typed word is stored.
- Saved recipes remain private copies.
- The restriction rule is deterministic and conservative. Allergen data is not used yet.
- See the privacy record: [Shared recipe ideas](../security/privacy-and-data-protection.md#shared-recipe-ideas).
