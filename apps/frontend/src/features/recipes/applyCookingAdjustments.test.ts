import type { RecipeExecution } from "@/contracts/recipes";
import { applyCookingAdjustments } from "./RecipeEditorPage";

type Draft = Parameters<typeof applyCookingAdjustments>[0];

const row = (
  ingredientId: string,
  displayName: string,
  quantity: string,
  unit = "g",
) => ({
  key: ingredientId,
  ingredientId,
  displayName,
  quantity,
  unit,
  optional: false,
  fromPantry: true,
});

const draft: Draft = {
  name: "Arroz soltinho",
  servings: "2",
  mealTypes: [],
  yield: "",
  ingredients: [
    row("ing-rice", "Arroz", "180"),
    row("ing-salt", "Sal", "5"),
    row("ing-garlic", "Alho", "1", "clove"),
    row("ing-oil", "Azeite", "10", "ml"),
  ],
  equipment: "",
  stages: [],
  reason: "",
};

describe("carrying cooking adjustments into the editor", () => {
  it("renames swaps, converts amounts to the recipe portions, and marks skipped items optional", () => {
    // Cooked for 4 people from a 2-serving recipe: amounts used are halved back.
    const execution = {
      servings: 4,
      recipeServings: 2,
      adjustments: [
        {
          ingredientId: "ing-rice",
          kind: "substitute",
          substituteName: "Quinoa",
          quantity: 300,
        },
        {
          ingredientId: "ing-salt",
          kind: "skip",
          substituteName: null,
          quantity: null,
        },
        {
          ingredientId: "ing-garlic",
          kind: "quantity",
          substituteName: null,
          quantity: 3,
        },
        {
          ingredientId: "ing-oil",
          kind: "quantity",
          substituteName: null,
          quantity: 25,
        },
      ],
    } as unknown as RecipeExecution;

    const { draft: next, changed } = applyCookingAdjustments(
      draft,
      execution,
      "pt-BR",
    );

    expect(changed).toEqual(["Arroz", "Sal", "Alho", "Azeite"]);
    expect(next.ingredients[0]).toMatchObject({
      displayName: "Quinoa",
      quantity: "150",
      fromPantry: false,
    });
    expect(next.ingredients[1]).toMatchObject({
      displayName: "Sal",
      optional: true,
      quantity: "5",
    });
    // A household unit the adjustment cannot express keeps the recipe's amount.
    expect(next.ingredients[2]).toMatchObject({ quantity: "1", unit: "clove" });
    expect(next.ingredients[3]).toMatchObject({ quantity: "12,5" });
  });
});
