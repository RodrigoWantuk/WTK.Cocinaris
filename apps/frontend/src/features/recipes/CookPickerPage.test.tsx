import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import type { RecipeExecution, RecipeRepository } from "@/contracts/recipes";
import { createSessionAdapter } from "@/features/inventory/testUtils";
import { CookPickerPage } from "./CookPickerPage";
import { RecipeProvider } from "./RecipeProvider";
import { executionStubs, librarySummary } from "./testExecutionStubs";

const summary = (recipeId: string, name: string) =>
  librarySummary({
    recipeId,
    name,
    mealTypes: ["dinner"],
    servings: 2,
    createdAt: "2026-10-01T12:00:00Z",
  });

function renderPage(overrides: Partial<RecipeRepository>) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={createSessionAdapter()}>
        <RecipeProvider
          repository={
            {
              ...executionStubs,
              getRecipe: vi.fn(),
              requestCandidates: vi.fn(),
              getGenerationSession: vi.fn(),
              selectCandidate: vi.fn(),
              listRecipes: async () => [],
              ...overrides,
            } as RecipeRepository
          }
        >
          <MemoryRouter initialEntries={["/app/cozinhar"]}>
            <Routes>
              <Route path="/app/cozinhar" element={<CookPickerPage />} />
              <Route
                path="/app/receitas/cozinhar/:executionId"
                element={<p>cooking</p>}
              />
            </Routes>
          </MemoryRouter>
        </RecipeProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("cook page (PLAN-0059)", () => {
  it("orders saved recipes by what the pantry covers and starts cooking in one tap", async () => {
    const startExecution = vi.fn(
      async () => ({ executionId: "exec-7" }) as RecipeExecution,
    );
    renderPage({
      listRecipes: async () => [
        summary("cake", "Bolo"),
        summary("rice", "Arroz com feijão"),
        summary("risotto", "Risoto"),
      ],
      listRecipeReadiness: async () => [
        { recipeId: "rice", available: 2, partial: 0, missing: 0 },
        { recipeId: "risotto", available: 1, partial: 0, missing: 1 },
        { recipeId: "cake", available: 0, partial: 0, missing: 2 },
      ],
      startExecution,
    });

    const rows = await screen.findAllByTestId("cook-recipe");
    expect(rows.map((row) => row.textContent)).toEqual([
      expect.stringContaining("Arroz com feijão"),
      expect.stringContaining("Risoto"),
      expect.stringContaining("Bolo"),
    ]);
    expect(
      within(rows[0]!).getByTestId("cook-recipe-readiness"),
    ).toHaveTextContent("You have everything");
    expect(
      within(rows[1]!).getByTestId("cook-recipe-readiness"),
    ).toHaveTextContent("You have 1 of 2");
    expect(screen.getByTestId("cook-suggest")).toHaveAttribute(
      "href",
      "/app/receitas/gerar",
    );
    expect(screen.getByTestId("cook-write")).toHaveAttribute(
      "href",
      "/app/receitas/nova",
    );

    await userEvent.setup().click(
      within(rows[0]!).getByRole("button", {
        name: "Cook: Arroz com feijão",
      }),
    );
    expect(startExecution).toHaveBeenCalledWith("rice", {
      csrfToken: expect.any(String),
      servings: 2,
    });
    expect(await screen.findByText("cooking")).toBeInTheDocument();
  });

  it("invites to create the first recipe when there is none", async () => {
    renderPage({});
    expect(await screen.findByTestId("cook-empty")).toHaveTextContent(
      "No saved recipes yet",
    );
  });
});
