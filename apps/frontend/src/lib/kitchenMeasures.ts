/**
 * Household measures (cups, spoons, pieces) shown next to the metric amount of a recipe
 * ingredient (PLAN-0054 Phase 3). The owner's rule: "ambas as opções sempre sejam visíveis e sejam
 * confirmadas serem compatíveis". Recipes store only grams, milliliters, and units, and this module
 * derives the other measure deterministically. Nothing here is stored, and no model is involved.
 *
 * Compatibility rules:
 * - Milliliters convert to cups and spoons exactly. A household measure is offered only when its
 *   rounded value is within 10% of the amount.
 * - Grams convert to volume only for an ingredient class in the curated table below, which gives
 *   the grams in one cup. Grams of an unknown ingredient get no volume, because a wrong cup is
 *   worse than none.
 * - Units and grams convert into each other only through a curated average piece weight.
 * - Every derived value from a density or piece weight is marked approximate ("≈").
 *
 * Measures follow the Brazilian and metric kitchen standard: cup 240 ml, tablespoon 15 ml,
 * teaspoon 5 ml. Imperial weights and °F wait for the household region (PLAN-0054 Phase 6).
 */

export const CUP_ML = 240;
export const TABLESPOON_ML = 15;
export const TEASPOON_ML = 5;

/** One part of a household measure, such as "1 ½ cups" or "2 tablespoons". */
export type MeasurePart = {
  unit: "cup" | "tbsp" | "tsp" | "pinch" | "piece" | "g";
  amount: number;
};

/** The household equivalent of one metric amount. */
export type HouseholdMeasure = {
  parts: MeasurePart[];
  /** True when the value relies on a density or piece weight, or on noticeable rounding. */
  approximate: boolean;
};

type IngredientClass = {
  /** Normalized name fragments in pt, en, and es; the first match in table order wins. */
  match: string[];
  /** Grams in one 240 ml cup, for converting grams to cups and spoons. */
  gramsPerCup?: number;
  /** Average grams of one piece, for converting between units and grams. */
  gramsPerPiece?: number;
  /** A liquid is measured in milliliters; a cup of it stays a volume. */
  liquid?: boolean;
};

// Curated averages from common kitchen references. More specific entries come before broader
// ones, for example brown sugar before sugar and powdered milk before milk. Values are averages
// shown as approximate, never as precise conversions.
const CLASSES: IngredientClass[] = [
  {
    match: ["farinha de rosca", "breadcrumb", "pan rallado"],
    gramsPerCup: 110,
  },
  { match: ["farinha de mandioca", "farofa"], gramsPerCup: 150 },
  { match: ["farinha", "flour", "harina"], gramsPerCup: 120 },
  { match: ["fuba", "cornmeal", "polenta"], gramsPerCup: 150 },
  {
    match: ["amido de milho", "maisena", "cornstarch", "maicena"],
    gramsPerCup: 130,
  },
  {
    match: ["acucar mascavo", "brown sugar", "azucar moreno"],
    gramsPerCup: 180,
  },
  {
    match: [
      "acucar de confeiteiro",
      "powdered sugar",
      "icing sugar",
      "azucar glas",
    ],
    gramsPerCup: 120,
  },
  { match: ["acucar", "sugar", "azucar"], gramsPerCup: 200 },
  {
    match: ["leite em po", "milk powder", "powdered milk", "leche en polvo"],
    gramsPerCup: 120,
  },
  { match: ["cacau", "chocolate em po", "cocoa", "cacao"], gramsPerCup: 90 },
  { match: ["aveia", "oats", "avena"], gramsPerCup: 90 },
  { match: ["arroz", "rice"], gramsPerCup: 185 },
  {
    match: [
      "feijao",
      "lentilha",
      "grao de bico",
      "beans",
      "lentils",
      "chickpeas",
      "frijol",
      "lenteja",
      "garbanzo",
    ],
    gramsPerCup: 190,
  },
  {
    match: ["queijo ralado", "parmesao", "parmesan", "queso rallado"],
    gramsPerCup: 100,
  },
  {
    match: ["manteiga", "margarina", "butter", "mantequilla"],
    gramsPerCup: 227,
  },
  { match: ["mel", "honey", "miel"], gramsPerCup: 340 },
  { match: ["sal", "salt"], gramsPerCup: 290 },
  {
    match: ["oleo", "azeite", "oil", "aceite"],
    gramsPerCup: 220,
    liquid: true,
  },
  {
    match: ["creme de leite", "cream", "nata"],
    gramsPerCup: 240,
    liquid: true,
  },
  {
    match: [
      "leite",
      "milk",
      "leche",
      "agua",
      "water",
      "caldo",
      "broth",
      "stock",
      "suco",
      "juice",
      "jugo",
      "zumo",
      "vinagre",
      "vinegar",
    ],
    gramsPerCup: 240,
    liquid: true,
  },
  {
    match: ["dente de alho", "garlic clove", "diente de ajo"],
    gramsPerPiece: 5,
  },
  { match: ["ovo", "egg", "huevo"], gramsPerPiece: 50 },
  { match: ["cebola", "onion", "cebolla"], gramsPerPiece: 150 },
  { match: ["batata doce", "sweet potato", "batata"], gramsPerPiece: 170 },
  { match: ["potato", "papa", "patata"], gramsPerPiece: 170 },
  { match: ["tomate", "tomato"], gramsPerPiece: 120 },
  { match: ["cenoura", "carrot", "zanahoria"], gramsPerPiece: 80 },
  { match: ["pimentao", "bell pepper", "pimiento"], gramsPerPiece: 160 },
  { match: ["abobrinha", "zucchini", "calabacin"], gramsPerPiece: 200 },
  { match: ["banana", "platano"], gramsPerPiece: 120 },
  { match: ["maca", "apple", "manzana"], gramsPerPiece: 180 },
  { match: ["limao", "lemon", "lime", "limon"], gramsPerPiece: 60 },
];

/** Lower case, accents removed, single spaces, as the backend matches pantry names. */
export function normalizeIngredientName(name: string): string {
  return name
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase()
    .replace(/\s+/g, " ")
    .trim();
}

function classFor(name: string): IngredientClass | undefined {
  const normalized = ` ${normalizeIngredientName(name)} `;
  // Word-boundary match, so "sal" does not match "salsinha" or "mel" "melao".
  return CLASSES.find((entry) =>
    entry.match.some((fragment) =>
      new RegExp(`[^a-z]${fragment}(s|es)?[^a-z]`).test(normalized),
    ),
  );
}

const CUP_FRACTIONS = [0, 1 / 4, 1 / 3, 1 / 2, 2 / 3, 3 / 4];
const MAX_ERROR = 0.1;

type Candidate = { parts: MeasurePart[]; ml: number };

function volumeCandidates(ml: number): Candidate[] {
  const candidates: Candidate[] = [];
  const cups = ml / CUP_ML;
  const whole = Math.floor(cups);
  for (const base of [whole, whole + 1]) {
    for (const fraction of CUP_FRACTIONS) {
      const value = base + fraction;
      // Beyond eight cups the metric amount is the clearer measure.
      if (value <= 0 || value > 8) continue;
      candidates.push({
        parts: [{ unit: "cup", amount: value }],
        ml: value * CUP_ML,
      });
      // A cup amount plus up to four tablespoons covers awkward amounts like 100 ml.
      const rest = ml - value * CUP_ML;
      const tablespoons = Math.round((rest / TABLESPOON_ML) * 2) / 2;
      if (tablespoons > 0 && tablespoons <= 4) {
        candidates.push({
          parts: [
            { unit: "cup", amount: value },
            { unit: "tbsp", amount: tablespoons },
          ],
          ml: value * CUP_ML + tablespoons * TABLESPOON_ML,
        });
      }
    }
  }
  const tablespoons = Math.round((ml / TABLESPOON_ML) * 2) / 2;
  if (tablespoons >= 1 && tablespoons <= 6) {
    candidates.push({
      parts: [{ unit: "tbsp", amount: tablespoons }],
      ml: tablespoons * TABLESPOON_ML,
    });
  }
  const teaspoons = Math.round((ml / TEASPOON_ML) * 4) / 4;
  if (teaspoons >= 0.25 && teaspoons < 3) {
    candidates.push({
      parts: [{ unit: "tsp", amount: teaspoons }],
      ml: teaspoons * TEASPOON_ML,
    });
  }
  return candidates;
}

/**
 * Best household volume for an amount in milliliters, or null when no simple measure is within
 * 10%. Among close candidates, fewer parts win, since "½ cup" reads better than "7 tablespoons".
 */
export function householdVolume(ml: number): HouseholdMeasure | null {
  if (!(ml > 0)) return null;
  // Under an eighth of a teaspoon, spoons stop being meaningful.
  if (ml < TEASPOON_ML / 8) {
    return { parts: [{ unit: "pinch", amount: 1 }], approximate: true };
  }
  let best: { candidate: Candidate; error: number; score: number } | null =
    null;
  for (const candidate of volumeCandidates(ml)) {
    const error = Math.abs(candidate.ml - ml) / ml;
    if (error > MAX_ERROR) continue;
    // Fewer parts read better, and on a near tie fewer spoonfuls win ("⅓ cup + 1 ½ tbsp" over
    // "¼ cup + 2 ½ tbsp").
    const spoons = candidate.parts
      .filter((part) => part.unit !== "cup")
      .reduce((sum, part) => sum + part.amount, 0);
    const score = error + 0.03 * (candidate.parts.length - 1) + 0.002 * spoons;
    if (!best || score < best.score) best = { candidate, error, score };
  }
  return best
    ? { parts: best.candidate.parts, approximate: best.error > 0.02 }
    : null;
}

/**
 * The household equivalent of a recipe amount in its canonical unit (`g`, `ml`, `unit`), or null
 * when no compatible measure exists.
 *
 * @param name Ingredient name, matched against the curated classes.
 * @param quantity Amount in `unit`.
 * @param unit `g`, `ml`, or `unit`.
 */
export function householdMeasure(
  name: string,
  quantity: number,
  unit: string,
): HouseholdMeasure | null {
  if (!(quantity > 0)) return null;
  const entry = classFor(name);
  if (unit === "ml") return householdVolume(quantity);
  if (unit === "g") {
    if (entry?.gramsPerPiece) {
      const pieces = Math.round((quantity / entry.gramsPerPiece) * 2) / 2;
      const error =
        Math.abs(pieces * entry.gramsPerPiece - quantity) / quantity;
      return pieces >= 0.5 && pieces <= 12 && error <= 0.25
        ? { parts: [{ unit: "piece", amount: pieces }], approximate: true }
        : null;
    }
    if (!entry?.gramsPerCup) return null;
    const volume = householdVolume((quantity / entry.gramsPerCup) * CUP_ML);
    return volume ? { ...volume, approximate: true } : null;
  }
  if (unit === "unit" && entry?.gramsPerPiece) {
    const grams = quantity * entry.gramsPerPiece;
    const rounded = grams >= 100 ? Math.round(grams / 10) * 10 : grams;
    return { parts: [{ unit: "g", amount: rounded }], approximate: true };
  }
  return null;
}

const FRACTION_GLYPHS: Array<[number, string]> = [
  [1 / 4, "¼"],
  [1 / 3, "⅓"],
  [1 / 2, "½"],
  [2 / 3, "⅔"],
  [3 / 4, "¾"],
];

/** "1 ½", "⅓", "2": whole number and kitchen fraction glyph. */
export function formatKitchenAmount(amount: number): string {
  const whole = Math.floor(amount + 1e-9);
  const rest = amount - whole;
  const glyph = FRACTION_GLYPHS.find(
    ([value]) => Math.abs(value - rest) < 0.01,
  )?.[1];
  if (!glyph) return String(Math.round(amount * 100) / 100);
  return whole > 0 ? `${whole} ${glyph}` : glyph;
}

/** A kitchen measure the recipe editor accepts besides grams, milliliters, and units. */
export type KitchenUnit = "cup" | "tbsp" | "tsp";

const KITCHEN_ML: Record<KitchenUnit, number> = {
  cup: CUP_ML,
  tbsp: TABLESPOON_ML,
  tsp: TEASPOON_ML,
};

/**
 * Converts an amount entered in cups or spoons to the canonical unit a recipe stores. A solid in
 * the curated table becomes grams through its density, so it still matches the pantry, which
 * holds it in grams; that conversion is approximate. A liquid or an unknown ingredient becomes
 * milliliters exactly.
 *
 * @returns The canonical amount, rounded to whole grams or milliliters (one decimal below 10).
 */
export function toCanonicalAmount(
  name: string,
  amount: number,
  unit: KitchenUnit,
): { quantity: number; unit: "g" | "ml"; approximate: boolean } {
  const ml = amount * KITCHEN_ML[unit];
  const entry = classFor(name);
  const gramsPerCup = entry && !entry.liquid ? entry.gramsPerCup : undefined;
  const value = gramsPerCup ? (ml / CUP_ML) * gramsPerCup : ml;
  const quantity = value < 10 ? Math.round(value * 10) / 10 : Math.round(value);
  return {
    quantity: Math.max(quantity, 0.1),
    unit: gramsPerCup ? "g" : "ml",
    approximate: Boolean(gramsPerCup),
  };
}
