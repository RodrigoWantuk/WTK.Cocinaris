import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { createUnavailablePlanningRepository } from "@/adapters/live/livePlanningRepository";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import {
  planningCatalogs,
  REQUIRED_PLANNING_I18N_KEYS,
} from "@/app/i18n/planningCatalog";
import { SessionProvider } from "@/app/session/SessionProvider";
import type {
  Menu,
  PlanningRepository,
  ShoppingList,
} from "@/contracts/planning";
import type { RecipeRepository } from "@/contracts/recipes";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import {
  createMockInventoryRepo,
  createSessionAdapter,
  sampleLot,
} from "@/features/inventory/testUtils";
import { RecipeProvider } from "@/features/recipes/RecipeProvider";
import {
  executionStubs,
  librarySummary,
} from "@/features/recipes/testExecutionStubs";
import { MenuPage } from "./MenuPage";
import { MenusPage } from "./MenusPage";
import { addDays, daysBetween, localIsoDate } from "./planningDates";
import { PlanningProvider } from "./PlanningProvider";
import { ShoppingPage } from "./ShoppingPage";

const MENU: Menu = {
  menuId: "menu-1",
  name: "Semana",
  startDate: "2026-11-02",
  endDate: "2026-11-04",
  status: "draft",
  acceptedAt: null,
  updatedAt: "2026-10-29T12:00:00Z",
  entries: [
    {
      entryId: "entry-1",
      recipeId: "recipe-1",
      recipeName: "Feijoada",
      revisionNumber: 1,
      revisionPinned: false,
      plannedDate: "2026-11-02",
      mealType: "dinner",
      servings: 4,
      status: "planned",
      executionId: null,
    },
  ],
};

function recipeRepo(overrides: Partial<RecipeRepository> = {}) {
  return {
    ...executionStubs,
    listRecipes: async () => [
      librarySummary({
        recipeId: "recipe-2",
        name: "Salada",
        mealTypes: ["lunch"],
        servings: 1,
        createdAt: "2026-10-01T12:00:00Z",
      }),
    ],
    getRecipe: vi.fn(),
    requestCandidates: vi.fn(),
    getGenerationSession: vi.fn(),
    selectCandidate: vi.fn(),
    ...overrides,
  } as RecipeRepository;
}

function renderAt(
  path: string,
  planning: Partial<PlanningRepository>,
  options: {
    recipes?: RecipeRepository;
    inventory?: ReturnType<typeof createMockInventoryRepo>;
  } = {},
) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider
          repository={options.inventory ?? createMockInventoryRepo()}
        >
          <RecipeProvider repository={options.recipes ?? recipeRepo()}>
            <PlanningProvider
              repository={{
                ...createUnavailablePlanningRepository(),
                ...planning,
              }}
            >
              <MemoryRouter initialEntries={[path]}>
                <Routes>
                  <Route path="/app/cardapio" element={<MenusPage />} />
                  <Route path="/app/cardapio/:menuId" element={<MenuPage />} />
                  <Route
                    path="/app/cardapio/:menuId/compras"
                    element={<ShoppingPage />}
                  />
                  <Route
                    path="/app/receitas/cozinhar/:executionId"
                    element={<p>cooking page</p>}
                  />
                </Routes>
              </MemoryRouter>
            </PlanningProvider>
          </RecipeProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("menu planning (PLAN-0029)", () => {
  it("has every planning key in every locale and walks calendar days", () => {
    for (const locale of ["pt-BR", "en", "es"] as const) {
      for (const key of REQUIRED_PLANNING_I18N_KEYS) {
        expect(planningCatalogs[locale][key], `${locale} ${key}`).toBeTruthy();
      }
    }
    expect(addDays("2026-10-30", 3)).toBe("2026-11-02");
    expect(daysBetween("2026-12-30", "2027-01-01")).toEqual([
      "2026-12-30",
      "2026-12-31",
      "2027-01-01",
    ]);
  });

  it("creates a seven-day draft from today", async () => {
    const user = userEvent.setup();
    const createMenu = vi.fn(async () => MENU);
    renderAt("/app/cardapio", {
      listMenus: async () => [],
      createMenu,
      getMenu: async () => MENU,
    });

    expect(await screen.findByTestId("menus-empty")).toBeInTheDocument();
    await user.type(screen.getByTestId("menu-create-name"), "Semana");
    await user.click(screen.getByTestId("menu-create"));

    const today = localIsoDate();
    expect(createMenu).toHaveBeenCalledWith(
      { name: "Semana", startDate: today, endDate: addDays(today, 6) },
      { csrfToken: expect.any(String), idempotencyKey: expect.any(String) },
    );
    expect(await screen.findByTestId("menu-page")).toBeInTheDocument();
  });

  it("adds a library recipe to a day, skips a meal, and cooks from the menu", async () => {
    const user = userEvent.setup();
    const addEntry = vi.fn(async () => MENU);
    const changeEntry = vi.fn(async () => ({
      ...MENU,
      entries: [{ ...MENU.entries[0], status: "skipped" }],
    }));
    const startExecution = vi.fn(async () => ({
      executionId: "exec-9",
    })) as unknown as RecipeRepository["startExecution"];
    renderAt(
      "/app/cardapio/menu-1",
      { getMenu: async () => MENU, addEntry, changeEntry },
      { recipes: recipeRepo({ startExecution }) },
    );

    const day = await screen.findByTestId("menu-day-2026-11-03");
    await user.click(within(day).getByTestId("menu-add-2026-11-03"));
    await user.click(await within(day).findByTestId("menu-add-recipe"));
    await user.selectOptions(
      within(day).getByTestId("menu-meal-select"),
      "lunch",
    );
    await user.click(within(day).getByTestId("menu-add-submit"));
    expect(addEntry).toHaveBeenCalledWith(
      "menu-1",
      {
        recipeId: "recipe-2",
        plannedDate: "2026-11-03",
        mealType: "lunch",
        servings: 1,
      },
      { csrfToken: expect.any(String), idempotencyKey: expect.any(String) },
    );

    const entry = within(screen.getByTestId("menu-day-2026-11-02")).getByTestId(
      "menu-entry",
    );
    expect(entry).toHaveTextContent("Dinner");
    expect(entry).toHaveTextContent("Planned");
    await user.click(within(entry).getByTestId("menu-entry-skip"));
    expect(changeEntry).toHaveBeenCalledWith(
      "menu-1",
      "entry-1",
      {
        plannedDate: "2026-11-02",
        mealType: "dinner",
        servings: 4,
        status: "skipped",
      },
      { csrfToken: expect.any(String) },
    );
    await waitFor(() =>
      expect(screen.getByTestId("menu-entry")).toHaveTextContent("Skipped"),
    );
  });

  it("starts cooking linked to the meal", async () => {
    const user = userEvent.setup();
    const startExecution = vi.fn(async () => ({
      executionId: "exec-9",
    })) as unknown as RecipeRepository["startExecution"];
    renderAt(
      "/app/cardapio/menu-1",
      { getMenu: async () => MENU },
      { recipes: recipeRepo({ startExecution }) },
    );

    await user.click(await screen.findByTestId("menu-entry-cook"));

    expect(startExecution).toHaveBeenCalledWith("recipe-1", {
      csrfToken: expect.any(String),
      servings: 4,
      menuEntryId: "entry-1",
    });
    expect(await screen.findByText("cooking page")).toBeInTheDocument();
  });

  it("groups the shopping list and records a purchase in the pantry", async () => {
    const user = userEvent.setup();
    const list: ShoppingList = {
      menuId: "menu-1",
      from: localIsoDate(),
      mealCount: 2,
      items: [
        {
          itemKey: "feijao|Gram",
          displayName: "Feijão",
          unit: "Gram",
          category: "required",
          requiredQuantity: 1000,
          availableQuantity: 300,
          toBuyQuantity: 700,
          checked: false,
          uses: [
            {
              entryId: "entry-1",
              recipeName: "Feijoada",
              plannedDate: "2026-11-02",
              quantity: 1000,
              optional: false,
            },
          ],
        },
        {
          itemKey: "sal|Gram",
          displayName: "Sal",
          unit: "Gram",
          category: "available",
          requiredQuantity: 5,
          availableQuantity: 500,
          toBuyQuantity: 0,
          checked: false,
          uses: [],
        },
      ],
    };
    const getShopping = vi.fn(async () => list);
    const setChecked = vi.fn(async () => undefined);
    const inventory = createMockInventoryRepo();
    inventory.createLot = vi.fn(async () => sampleLot);
    renderAt(
      "/app/cardapio/menu-1/compras",
      { getShopping, setChecked },
      { inventory },
    );

    const toBuy = await screen.findByTestId("shopping-required");
    expect(toBuy).toHaveTextContent("Feijão — 700 g");
    expect(toBuy).toHaveTextContent("Need 1,000 g · have 300 g");
    expect(screen.getByTestId("shopping-available")).toHaveTextContent("Sal");
    expect(getShopping).toHaveBeenCalledWith(
      "menu-1",
      localIsoDate(),
      expect.anything(),
    );

    await user.click(within(toBuy).getByTestId("shopping-item-check"));
    expect(setChecked).toHaveBeenCalledWith("menu-1", "feijao|Gram", true, {
      csrfToken: expect.any(String),
    });

    await user.click(within(toBuy).getByTestId("shopping-item-buy"));
    const quantity = within(toBuy).getByTestId("shopping-buy-quantity");
    await user.clear(quantity);
    await user.type(quantity, "1000");
    await user.click(within(toBuy).getByTestId("shopping-buy-confirm"));
    expect(inventory.createLot).toHaveBeenCalledWith(
      {
        productName: "Feijão",
        quantity: { kind: "measured", value: 1000, unit: "Gram" },
        storageLocation: "Pantry",
      },
      { csrfToken: expect.any(String), idempotencyKey: expect.any(String) },
    );
    await waitFor(() => expect(getShopping).toHaveBeenCalledTimes(2));
  });
});
