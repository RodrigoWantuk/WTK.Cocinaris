import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import type { RecipeRepository } from "@/contracts/recipes";
import { RecipeProvider } from "@/features/recipes/RecipeProvider";
import {
  executionStubs,
  librarySummary,
} from "@/features/recipes/testExecutionStubs";
import { localIsoDate } from "@/features/planning/planningDates";
import { ForYouNow, mealForHour } from "./ForYouNow";

const recipes = [
  librarySummary({
    recipeId: "r1",
    name: "Strogonoff",
    mealTypes: ["dinner"],
    servings: 2,
    createdAt: "2026-10-01T12:00:00Z",
  }),
];

describe("for you now (PLAN-0021)", () => {
  it("asks for the meal and time, never stores them, and explains each pick", async () => {
    const listRecommendations = vi.fn(async () => [
      {
        recipeId: "r1",
        score: 80,
        totalMinutes: 25,
        available: 1,
        total: 2,
        reasons: [{ code: "uses_soon", item: "Creme de leite" }],
      },
    ]);
    render(
      <ProductionI18nProvider initialLocale="en">
        <RecipeProvider
          repository={
            {
              ...executionStubs,
              listRecommendations,
            } as unknown as RecipeRepository
          }
        >
          <MemoryRouter>
            <ForYouNow recipes={recipes} />
          </MemoryRouter>
        </RecipeProvider>
      </ProductionI18nProvider>,
    );

    const item = await screen.findByTestId("for-you-item");
    expect(item).toHaveTextContent("Strogonoff");
    expect(item).toHaveTextContent("Uses Creme de leite, due soon");
    expect(within(item).getByRole("link")).toHaveAttribute(
      "href",
      "/app/receitas/r1",
    );
    expect(listRecommendations).toHaveBeenLastCalledWith(
      expect.objectContaining({
        today: localIsoDate(),
        mealType: mealForHour(new Date().getHours()),
        maxMinutes: null,
      }),
      expect.anything(),
    );

    const user = userEvent.setup();
    await user.click(screen.getByTestId("for-you-time-20"));
    await user.click(screen.getByTestId("for-you-meal-lunch"));
    await waitFor(() =>
      expect(listRecommendations).toHaveBeenLastCalledWith(
        expect.objectContaining({ mealType: "lunch", maxMinutes: 20 }),
        expect.anything(),
      ),
    );
    expect(screen.getByTestId("for-you-time-20")).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    // Nothing is persisted: the chooser keeps no storage.
    expect(localStorage.length).toBe(0);
  });

  it("presets the meal from the local hour", () => {
    expect([7, 12, 16, 21, 2].map(mealForHour)).toEqual([
      "breakfast",
      "lunch",
      "snack",
      "dinner",
      "dinner",
    ]);
  });
});
