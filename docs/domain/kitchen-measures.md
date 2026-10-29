# Kitchen measures and metric amounts

Status: implemented in [PLAN-0054](../plans/PLAN-0054-everyday-cook-realignment.md) Phase 3, on 2026-10-29.

## Owner rule

"Concordo, desde que ambas as opções sempre sejam visíveis e sejam confirmadas serem compatíveis. Conheço gente que só funciona na medida de cozinha, mas eu mesmo, faço tudo na balança."

## Model

- Recipes, the pantry, reservations, and shopping store only canonical amounts in grams, milliliters, or units. Kitchen measures are never stored.
- Wherever a recipe ingredient is shown (the recipe page and the cooking view), the household measure appears next to the metric amount, for example "Arroz — 180 g · ≈ 1 xícara".
- The recipe editor accepts cups, tablespoons, and teaspoons. It shows what the entry is saved as ("Salvo como ≈ 185 g") and saves the canonical amount. For a metric entry it shows the household measure live.
- The conversion is deterministic code in `apps/frontend/src/lib/kitchenMeasures.ts`. No model proposes or checks a measure. Because the arithmetic is deterministic, the plan's "model proposes, code checks" step is unnecessary.

## Compatibility rules

| From | To | Rule |
| --- | --- | --- |
| ml | cups and spoons | Exact (cup 240 ml, tablespoon 15 ml, teaspoon 5 ml) |
| g | cups and spoons | Only for an ingredient class in the curated table (grams per cup); always "≈" |
| units and g | units and g | Only through a curated average piece weight; always "≈" |
| Cups or spoons entered | g or ml | A solid in the table becomes grams through its density (≈), so it still matches the pantry. A liquid or an unknown ingredient becomes milliliters exactly. |

Further rules:

- **Rounding.** A household measure is offered only when its rounded value is within 10% of the amount. Fractions are ¼, ⅓, ½, ⅔, and ¾ cup, half tablespoons, and quarter teaspoons. A cup amount may be combined with up to four tablespoons.
- **Limits.** Above eight cups, below an eighth of a teaspoon (shown as "uma pitada"), or beyond 12 pieces, only the metric amount is shown.
- **Unknown ingredients.** An ingredient with no curated class gets no household measure for grams, because a wrong cup is worse than none.
- **Name matching.** Ingredient names are matched on whole words after removing accents and case, so "salsinha" is not "sal" and "melão" is not "mel".

## Limits

- **Table coverage.** The table covers common Western staples in Portuguese, English, and Spanish names. A missing ingredient simply shows the metric amount alone.
- **Rounding and densities.** Densities and piece weights are averages from common kitchen references, so a converted value is approximate and always marked "≈".
- **Units by region.** Imperial weights (oz, lb) and °F wait for the household region (PLAN-0054 Phase 6). Oven temperatures written inside step text are not converted.
- **Shopping list.** The shopping list stays metric, since stores sell by weight and volume.
