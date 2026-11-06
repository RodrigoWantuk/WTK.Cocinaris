# Storage Guidance: Owner Review (Gate G2-A)

- **Status:** Accepted by the owner on 2026-11-06, in a written instruction to the agent: "Assuma as revisões como aceitas e siga o trabalho." The owner accepted the pending reviews as a whole. **The values marked unverified below were not checked against a primary source by the owner or the agent**; acceptance does not change that, and the advice stays advisory. `Planning:StorageGuidance:Enabled` may now be set to `true` on a host, following the runbook.
- **Source of truth:** `apps/backend/src/Cocinaris.Modules.Ingredients/Catalog/storage-guidance.json`. Change values there and update this document in the same pull request.
- **Last updated:** 2026-11-06

## What this controls

With the flag on:

- "Cheguei do mercado" (PLAN-0062) suggests the refrigerator or the freezer for each meal and portions a surplus.
- A refrigerated lot with no date of its own gets an advisory use-by estimate, counted from the day it was recorded, with reason `curated_fridge_days`. A printed date always wins.

With the flag off, a purchase stays whole in the place its store section suggests, and nothing mentions freezing or dates. All output is advisory, says so, and tells the person to check the food. Model text never feeds this table.

## How the values were chosen

Where a source gives a range, the **shorter** time is used. `fridgeDays` counts from purchase. `freezerDays` is about quality, not safety.

## Verification status: read this first

**No value was checked directly against a primary page.** FSIS and FoodSafety.gov refused automated access, so:

| Level | Items | Basis |
|---|---|---|
| Search summary of USDA pages | poultry, fish, seafood, ground meats, eggs, cooked leftovers | Summary of [FSIS](https://www.fsis.usda.gov/food-safety/safe-food-handling-and-preparation/food-safety-basics/refrigeration) and the [FoodSafety.gov chart](https://www.foodsafety.gov/food-safety-charts/cold-food-storage-charts): raw poultry and ground meat 1–2 days refrigerated; ground meat 3–4 months frozen; poultry pieces about 9 months frozen; fish 1–2 days, 3–8 months frozen; eggs in the shell 3–5 weeks, not frozen in the shell; leftovers 3–4 days, 2–3 months frozen |
| Agent's general knowledge, **unverified** | steaks, cured meats, dairy, produce, fruit, herbs, bakery, tofu, freezer times not listed above | To be checked against the [USDA FoodKeeper](https://www.foodsafety.gov/keep-food-safe/foodkeeper-app) or a Brazilian source (ANVISA) by the owner |

## Cooked food

| Refrigerator days | Freezer days |
|---|---|
| 3 | 60 |

## By catalog category

| Category | Location | Refrigerator days | Worth freezing | Freezer days |
|---|---|---|---|---|
| `poultry` | Refrigerator | 1 | yes | 270 |
| `meat` | Refrigerator | 3 | yes | 120 |
| `fish` | Refrigerator | 1 | yes | 90 |
| `seafood` | Refrigerator | 1 | yes | 90 |
| `egg` | Refrigerator | 21 | no | — |
| `dairy` | Refrigerator | 5 | no | — |
| `vegetable` | Refrigerator | 3 | no | — |
| `fruit` | Refrigerator | 3 | no | — |
| `herb` | Refrigerator | 3 | no | — |
| `bakery` | Pantry | — | yes | 60 |
| `grain` | Pantry | — | no | — |
| `pasta` | Pantry | — | no | — |
| `baking` | Pantry | — | no | — |
| `sweetener` | Pantry | — | no | — |
| `oil` | Pantry | — | no | — |
| `spice` | Pantry | — | no | — |
| `condiment` | Pantry | — | no | — |
| `legume` | Pantry | — | no | — |
| `nut` | Pantry | — | no | — |
| `beverage` | Pantry | — | no | — |

## Overrides by ingredient

| Ingredient | Location | Refrigerator days | Worth freezing | Freezer days |
|---|---|---|---|---|
| `ground-beef` | Refrigerator | 1 | yes | 90 |
| `ground-pork` | Refrigerator | 1 | yes | 90 |
| `ground-chicken` | Refrigerator | 1 | yes | 90 |
| `sausage` | Refrigerator | 1 | yes | 30 |
| `bacon` | Refrigerator | 7 | yes | 30 |
| `ham` | Refrigerator | 3 | yes | 30 |
| `hot-dog-sausage` | Refrigerator | 7 | yes | 30 |
| `salami` | Refrigerator | 7 | no | — |
| `tuna-canned` | Pantry | — | no | — |
| `sardine-canned` | Pantry | — | no | — |
| `salt-cod` | Refrigerator | 7 | no | — |
| `butter` | Refrigerator | 30 | yes | 180 |
| `yogurt` | Refrigerator | 7 | no | — |
| `parmesan` | Refrigerator | 21 | no | — |
| `cheddar` | Refrigerator | 21 | no | — |
| `condensed-milk` | Pantry | — | no | — |
| `garlic` | Pantry | — | no | — |
| `onion` | Pantry | — | no | — |
| `red-onion` | Pantry | — | no | — |
| `potato` | Pantry | — | no | — |
| `sweet-potato` | Pantry | — | no | — |
| `cassava` | Pantry | — | no | — |
| `pumpkin` | Pantry | — | no | — |
| `tomato` | Pantry | — | no | — |
| `carrot` | Refrigerator | 14 | no | — |
| `beet` | Refrigerator | 14 | no | — |
| `cabbage` | Refrigerator | 7 | no | — |
| `red-cabbage` | Refrigerator | 7 | no | — |
| `banana` | Pantry | — | no | — |
| `avocado` | Pantry | — | no | — |
| `orange` | Pantry | — | no | — |
| `lemon` | Pantry | — | no | — |
| `lime` | Pantry | — | no | — |
| `pineapple` | Pantry | — | no | — |
| `mango` | Pantry | — | no | — |
| `apple` | Refrigerator | 21 | no | — |
| `raisins` | Pantry | — | no | — |
| `grated-coconut` | Pantry | — | no | — |
| `oregano` | Pantry | — | no | — |
| `bay-leaf` | Pantry | — | no | — |
| `tofu` | Refrigerator | 3 | no | — |

## Known limits, for the owner to accept or change

- **Raw vegetables are marked "not worth freezing"** because most need blanching first. A surplus of them stays in the refrigerator.
- **Pantry items have no clock.** They get no date from this table.
- **Tomato, onion, potato, and similar are placed in the pantry,** which suits a typical kitchen but not a very hot one.
- **Refreezing is not handled here.** It stays with the attention rules.
- **Brazilian conditions.** The sources are American. A warmer kitchen or a fridge above 4 °C shortens these times.

## Owner decision

- [x] Accepted as written on 2026-11-06 ("Assuma as revisões como aceitas e siga o trabalho."), recorded in the PLAN-0065 Slice 3 pull request. An operator may now set `Planning__StorageGuidance__Enabled=true`, following the runbook. Checking the unverified values against FoodKeeper or ANVISA remains open.
