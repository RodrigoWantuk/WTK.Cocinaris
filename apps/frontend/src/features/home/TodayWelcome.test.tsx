import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { createUnavailablePlanningRepository } from "@/adapters/live/livePlanningRepository";
import type { RecipeRepository } from "@/contracts/recipes";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import {
  createMockInventoryRepo,
  createSessionAdapter,
} from "@/features/inventory/testUtils";
import { PlanningProvider } from "@/features/planning/PlanningProvider";
import { RecipeProvider } from "@/features/recipes/RecipeProvider";
import {
  executionStubs,
  librarySummary,
} from "@/features/recipes/testExecutionStubs";
import { TodayPage } from "./TodayPage";

function renderHome(recipes: Partial<RecipeRepository>) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider
          repository={createMockInventoryRepo({
            listAttention: async () => [],
          })}
        >
          <RecipeProvider
            repository={
              {
                ...executionStubs,
                getRecipe: vi.fn(),
                requestCandidates: vi.fn(),
                getGenerationSession: vi.fn(),
                selectCandidate: vi.fn(),
                listRecipes: async () => [],
                ...recipes,
              } as RecipeRepository
            }
          >
            <PlanningProvider
              repository={{
                ...createUnavailablePlanningRepository(),
                getToday: async () => [],
              }}
            >
              <MemoryRouter>
                <TodayPage />
              </MemoryRouter>
            </PlanningProvider>
          </RecipeProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("home layout (PLAN-0058)", () => {
  it("welcomes a new cook with three steps instead of empty sections", async () => {
    renderHome({});
    const welcome = await screen.findByTestId("today-welcome");
    const links = within(welcome).getAllByRole("link");
    expect(links.map((link) => link.getAttribute("href"))).toEqual([
      "/app/despensa/novo",
      "/app/receitas/gerar",
      "/app/cardapio",
    ]);
    expect(screen.queryByTestId("today-recipes")).toBeNull();
    // The start tiles stay available; suggesting recipes is the main one.
    expect(screen.getByTestId("today-generate")).toHaveAttribute(
      "href",
      "/app/receitas/gerar",
    );
  });

  it("shows saved recipes as image cards once there are any", async () => {
    renderHome({
      listRecipes: async () => [
        librarySummary({
          recipeId: "recipe-9",
          name: "Moqueca",
          mealTypes: ["dinner"],
          servings: 4,
          createdAt: "2026-10-01T12:00:00Z",
        }),
      ],
    });
    const recipes = await screen.findByTestId("today-recipes");
    expect(
      within(recipes).getByRole("link", { name: /Moqueca/ }),
    ).toHaveAttribute("href", "/app/receitas/recipe-9");
    expect(screen.queryByTestId("today-welcome")).toBeNull();
  });
});
