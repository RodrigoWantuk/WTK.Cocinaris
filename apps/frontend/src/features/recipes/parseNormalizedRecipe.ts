/**
 * Best-effort parse of backend-normalized recipe JSON for display.
 * Never treats AI content as authoritative beyond the validated string payload.
 *
 * The shape is protocol 0.3 `recipe.expand_selected.v1` (`packages/contracts/ai/recipe`):
 * ingredients carry `requiredQuantity` and a canonical `unit` (`g`, `ml`, `unit`), and each
 * stage carries a `name`, one `instructions` string, timings, and `sensoryCues`. Older or
 * alternative field names are still read so a malformed row degrades instead of failing.
 */
export interface ParsedIngredient {
  ingredientId?: string;
  displayName: string;
  quantity?: number;
  /** Canonical recipe unit: `g`, `ml`, or `unit`; other strings are shown as written. */
  unit?: string;
  optional: boolean;
  /** `inventory`, `additional`, or `assumption`. */
  sourceType?: string;
}

export interface ParsedStage {
  stageId?: string;
  title: string;
  instructions: string[];
  activeMinutes?: number;
  passiveMinutes?: number;
  sensoryCues: string[];
}

export interface ParsedNormalizedRecipe {
  yield?: string;
  ingredients: ParsedIngredient[];
  stages: ParsedStage[];
  equipment: string[];
  assumptions: string[];
}

const text = (value: unknown): string | undefined =>
  typeof value === "string" && value.trim().length > 0 ? value : undefined;

const numberOf = (value: unknown): number | undefined => {
  const n =
    typeof value === "number"
      ? value
      : typeof value === "string"
        ? Number(value)
        : NaN;
  return Number.isFinite(n) ? n : undefined;
};

const strings = (value: unknown): string[] =>
  Array.isArray(value)
    ? value.filter(
        (item): item is string => typeof item === "string" && item.length > 0,
      )
    : [];

export function parseNormalizedRecipeJson(
  raw: string,
): ParsedNormalizedRecipe | null {
  try {
    const root = JSON.parse(raw) as unknown;
    if (!root || typeof root !== "object") return null;
    const recipe =
      "recipe" in root && root.recipe && typeof root.recipe === "object"
        ? (root.recipe as Record<string, unknown>)
        : (root as Record<string, unknown>);

    const ingredients: ParsedIngredient[] = [];
    if (Array.isArray(recipe.ingredients)) {
      for (const item of recipe.ingredients) {
        if (!item || typeof item !== "object") continue;
        const row = item as Record<string, unknown>;
        const displayName =
          text(row.displayName) ?? text(row.name) ?? text(row.ingredientRef);
        if (!displayName) continue;
        ingredients.push({
          ingredientId: text(row.ingredientId),
          displayName,
          quantity: numberOf(row.requiredQuantity) ?? numberOf(row.quantity),
          unit: text(row.unit),
          optional: row.optional === true,
          sourceType: text(row.sourceType),
        });
      }
    }

    const stages: ParsedStage[] = [];
    if (Array.isArray(recipe.stages)) {
      for (const stage of recipe.stages) {
        if (!stage || typeof stage !== "object") continue;
        const row = stage as Record<string, unknown>;
        const instructions: string[] = [];
        // Protocol 0.3 has one `instructions` string per stage; split it into sentences only
        // at line breaks so the model's own punctuation is kept.
        if (typeof row.instructions === "string") {
          instructions.push(
            ...row.instructions
              .split(/\n+/)
              .map((line) => line.trim())
              .filter(Boolean),
          );
        } else if (Array.isArray(row.instructions)) {
          instructions.push(...strings(row.instructions));
        }
        if (Array.isArray(row.steps)) {
          for (const step of row.steps) {
            if (typeof step === "string") instructions.push(step);
            else if (step && typeof step === "object") {
              const stepRow = step as Record<string, unknown>;
              const value = text(stepRow.instruction) ?? text(stepRow.text);
              if (value) instructions.push(value);
            }
          }
        }
        const single = text(row.instruction);
        if (single) instructions.push(single);
        stages.push({
          stageId: text(row.stageId),
          title: text(row.title) ?? text(row.name) ?? "",
          instructions,
          activeMinutes: numberOf(row.activeMinutes),
          passiveMinutes: numberOf(row.passiveMinutes),
          sensoryCues: strings(row.sensoryCues),
        });
      }
    }

    const equipment: string[] = [];
    if (Array.isArray(recipe.equipment)) {
      for (const item of recipe.equipment) {
        if (item && typeof item === "object") {
          const name = text((item as Record<string, unknown>).name);
          if (name) equipment.push(name);
        }
      }
    }

    return {
      yield: text(recipe.yield),
      ingredients,
      stages,
      equipment,
      assumptions: strings(recipe.assumptions),
    };
  } catch {
    return null;
  }
}
