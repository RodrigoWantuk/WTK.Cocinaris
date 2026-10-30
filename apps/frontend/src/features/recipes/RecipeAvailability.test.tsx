import { render, screen, within } from "@testing-library/react";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { RecipeContent } from "./RecipeContent";
import { parseNormalizedRecipeJson } from "./parseNormalizedRecipe";

const RECIPE = parseNormalizedRecipeJson(
  JSON.stringify({
    recipe: {
      ingredients: [
        {
          ingredientId: "i1",
          sourceType: "additional",
          displayName: "Arroz",
          requiredQuantity: 300,
          unit: "g",
          optional: false,
        },
        {
          ingredientId: "i2",
          sourceType: "additional",
          displayName: "Frango",
          requiredQuantity: 400,
          unit: "g",
          optional: false,
        },
        {
          ingredientId: "i3",
          sourceType: "additional",
          displayName: "Cebola",
          requiredQuantity: 1,
          unit: "unit",
          optional: false,
        },
        {
          ingredientId: "i4",
          sourceType: "assumption",
          displayName: "Sal",
          requiredQuantity: 5,
          unit: "g",
          optional: false,
        },
      ],
      stages: [],
    },
  }),
)!;

function renderContent(
  availability: Parameters<typeof RecipeContent>[0]["availability"],
) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <RecipeContent recipe={RECIPE} availability={availability} />
    </ProductionI18nProvider>,
  );
}

describe("pantry status on the recipe page (PLAN-0057 H2)", () => {
  it("says what the pantry covers, never for assumed basics", () => {
    renderContent([
      {
        ingredientId: "i1",
        status: "available",
        unit: "Gram",
        missingQuantity: 0,
      },
      {
        ingredientId: "i2",
        status: "partial",
        unit: "Gram",
        missingQuantity: 250,
      },
      {
        ingredientId: "i3",
        status: "missing",
        unit: "Unit",
        missingQuantity: 1,
      },
      {
        ingredientId: "i4",
        status: "missing",
        unit: "Gram",
        missingQuantity: 5,
      },
    ]);
    const rows = screen.getAllByTestId("recipe-ingredient");
    expect(
      within(rows[0]!).getByTestId("recipe-ingredient-pantry"),
    ).toHaveTextContent("In your pantry");
    expect(
      within(rows[1]!).getByTestId("recipe-ingredient-pantry"),
    ).toHaveTextContent("250 g missing");
    expect(
      within(rows[2]!).getByTestId("recipe-ingredient-pantry"),
    ).toHaveTextContent("Not in your pantry");
    expect(
      within(rows[3]!).queryByTestId("recipe-ingredient-pantry"),
    ).toBeNull();
  });

  it("shows no pantry status until the pantry has been read", () => {
    renderContent(null);
    expect(screen.queryByTestId("recipe-ingredient-pantry")).toBeNull();
    expect(screen.queryByText(/buy or have at home/i)).toBeNull();
  });
});
