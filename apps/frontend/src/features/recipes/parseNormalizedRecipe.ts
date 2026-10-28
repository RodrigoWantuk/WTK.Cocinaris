/**
 * Best-effort parse of backend-normalized recipe JSON for display.
 * Never treats AI content as authoritative beyond the validated string payload.
 */
export interface ParsedNormalizedRecipe {
  yield?: string;
  ingredients: Array<{ displayName: string; quantityLabel?: string }>;
  stages: Array<{ title: string; instructions: string[] }>;
  assumptions: string[];
}

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

    const ingredients: ParsedNormalizedRecipe["ingredients"] = [];
    if (Array.isArray(recipe.ingredients)) {
      for (const item of recipe.ingredients) {
        if (!item || typeof item !== "object") continue;
        const row = item as Record<string, unknown>;
        const displayName =
          typeof row.displayName === "string"
            ? row.displayName
            : typeof row.name === "string"
              ? row.name
              : typeof row.ingredientRef === "string"
                ? row.ingredientRef
                : null;
        if (!displayName) continue;
        const quantity =
          typeof row.quantity === "number" || typeof row.quantity === "string"
            ? String(row.quantity)
            : undefined;
        const unit = typeof row.unit === "string" ? row.unit : undefined;
        ingredients.push({
          displayName,
          quantityLabel:
            quantity && unit
              ? `${quantity} ${unit}`
              : quantity
                ? quantity
                : undefined,
        });
      }
    }

    const stages: ParsedNormalizedRecipe["stages"] = [];
    if (Array.isArray(recipe.stages)) {
      for (const stage of recipe.stages) {
        if (!stage || typeof stage !== "object") continue;
        const row = stage as Record<string, unknown>;
        const title =
          typeof row.title === "string"
            ? row.title
            : typeof row.name === "string"
              ? row.name
              : "Stage";
        const instructions: string[] = [];
        if (Array.isArray(row.steps)) {
          for (const step of row.steps) {
            if (typeof step === "string") {
              instructions.push(step);
              continue;
            }
            if (step && typeof step === "object") {
              const stepRow = step as Record<string, unknown>;
              if (typeof stepRow.instruction === "string") {
                instructions.push(stepRow.instruction);
              } else if (typeof stepRow.text === "string") {
                instructions.push(stepRow.text);
              }
            }
          }
        }
        if (typeof row.instruction === "string") {
          instructions.push(row.instruction);
        }
        stages.push({ title, instructions });
      }
    }

    const assumptions = Array.isArray(recipe.assumptions)
      ? recipe.assumptions.filter(
          (item): item is string => typeof item === "string",
        )
      : [];

    return {
      yield: typeof recipe.yield === "string" ? recipe.yield : undefined,
      ingredients,
      stages,
      assumptions,
    };
  } catch {
    return null;
  }
}
