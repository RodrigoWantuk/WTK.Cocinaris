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
import { LotCommitmentsPanel } from "./LotCommitmentsPanel";

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
      reserveIngredients: true,
      readiness: {
        entryId: "entry-1",
        readiness: "missing",
        missing: [{ name: "Feijão", unit: "Gram", quantity: 200 }],
      },
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
          storeSection: "dry_goods",
          requiredQuantity: 1000,
          availableQuantity: 300,
          availableIsEstimate: true,
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
          itemKey: "ingredient:milk|Milliliter",
          displayName: "Leite",
          unit: "Milliliter",
          category: "optional",
          storeSection: "dairy_eggs",
          requiredQuantity: 200,
          availableQuantity: 0,
          toBuyQuantity: 200,
          checked: false,
          uses: [],
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

    // Lines to buy are grouped by store section, in shopping-trip order (PLAN-0037 Phase 3).
    const toBuy = await screen.findByTestId("shopping-section-dry_goods");
    expect(toBuy).toHaveTextContent("Dry goods");
    expect(toBuy).toHaveTextContent("Feijão — 700 g");
    const dairy = screen.getByTestId("shopping-section-dairy_eggs");
    expect(dairy).toHaveTextContent("Leite — 200 ml");
    expect(dairy).toHaveTextContent("optional");
    expect(
      dairy.compareDocumentPosition(toBuy) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(toBuy).toHaveTextContent("Need 1,000 g · have ~300 g");
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
    // A dairy purchase suggests the fridge; dry goods stay in the pantry.
    await user.click(within(dairy).getByTestId("shopping-item-buy"));
    expect(within(dairy).getByRole("combobox")).toHaveValue("Refrigerator");
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

  it("shows what a meal lacks and releases its reservations", async () => {
    const user = userEvent.setup();
    const changeEntry = vi.fn(async () => ({
      ...MENU,
      entries: [
        { ...MENU.entries[0], reserveIngredients: false, readiness: null },
      ],
    }));
    renderAt("/app/cardapio/menu-1", {
      getMenu: async () => MENU,
      changeEntry,
    });

    expect(await screen.findByTestId("menu-entry-readiness")).toHaveTextContent(
      "Missing: Feijão 200 g",
    );
    await user.click(screen.getByTestId("menu-entry-edit"));
    await user.click(screen.getByTestId("menu-entry-reserve"));
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(changeEntry).toHaveBeenCalledWith(
      "menu-1",
      "entry-1",
      expect.objectContaining({ reserveIngredients: false }),
      { csrfToken: expect.any(String) },
    );
    await waitFor(() =>
      expect(screen.getByTestId("menu-entry-readiness")).toHaveTextContent(
        "Does not reserve pantry items",
      ),
    );
  });

  it("lists a lot's reservations and protects a minimum quantity", async () => {
    const user = userEvent.setup();
    const reservations = (
      protection: { kind: string; minimumQuantity: number | null } | null,
    ) => ({
      from: localIsoDate(),
      lots: [
        {
          lotId: sampleLot.lotId,
          productName: "Rice",
          unit: "Gram",
          quantity: 500,
          protectedQuantity: protection ? 100 : 0,
          reservedQuantity: 300,
          freeQuantity: protection ? 100 : 200,
          protection,
          reservations: [
            {
              menuEntryId: "entry-1",
              recipeName: "Risoto",
              plannedDate: "2026-11-02",
              quantity: 300,
            },
          ],
        },
      ],
      meals: [],
    });
    const protectLot = vi.fn(async () =>
      reservations({ kind: "minimum", minimumQuantity: 100 }),
    );
    render(
      <ProductionI18nProvider initialLocale="en">
        <SessionProvider adapter={createSessionAdapter()}>
          <PlanningProvider
            repository={{
              ...createUnavailablePlanningRepository(),
              getReservations: async () => reservations(null),
              protectLot,
            }}
          >
            <LotCommitmentsPanel lot={sampleLot} />
          </PlanningProvider>
        </SessionProvider>
      </ProductionI18nProvider>,
    );

    expect(await screen.findByTestId("lot-commitments-meal")).toHaveTextContent(
      "Risoto",
    );
    await user.click(screen.getByTestId("lot-protect-minimum"));
    await user.type(screen.getByTestId("lot-protect-quantity"), "100");
    await user.click(screen.getByTestId("lot-protect-save"));
    expect(protectLot).toHaveBeenCalledWith(
      sampleLot.lotId,
      { kind: "minimum", minimumQuantity: 100 },
      { csrfToken: expect.any(String) },
    );
  });

  it("suggests dishes for a day with AI and adds the chosen one", async () => {
    const user = userEvent.setup();
    const candidate = {
      candidateId: "c1",
      candidateStrategy: "on_hand_first",
      name: "Arroz de forno",
      targetMealType: "lunch",
      dishFormat: "bake",
      primaryTechnique: "bake",
      primaryIngredientRefs: [],
      summary: "Arroz gratinado com o que sobrou.",
      servings: 3,
      activeMinutes: 10,
      passiveMinutes: 30,
      totalMinutes: 40,
      difficulty: "easy",
      requiredEquipmentIds: [],
      requiredCapabilities: [],
    };
    const requestCandidates = vi.fn(async () => ({
      sessionId: "s-1",
      status: "CandidatesReady",
      candidates: [candidate],
      failureReason: null,
    }));
    const selectCandidate = vi.fn(async () => ({ recipeId: "recipe-9" }));
    const addEntry = vi.fn(async () => MENU);
    renderAt(
      "/app/cardapio/menu-1",
      { getMenu: async () => MENU, addEntry },
      {
        recipes: recipeRepo({
          requestCandidates,
          selectCandidate,
        } as unknown as Partial<RecipeRepository>),
      },
    );

    const day = await screen.findByTestId("menu-day-2026-11-03");
    await user.click(within(day).getByTestId("menu-ai-2026-11-03"));
    await user.selectOptions(within(day).getByTestId("menu-ai-meal"), "lunch");
    await user.clear(within(day).getByTestId("menu-ai-servings"));
    await user.type(within(day).getByTestId("menu-ai-servings"), "3");
    await user.click(within(day).getByTestId("menu-ai-ask"));

    expect(requestCandidates).toHaveBeenCalledWith(
      expect.objectContaining({
        menuPlanning: {
          menuId: "menu-1",
          plannedDate: "2026-11-03",
          mealType: "lunch",
          servings: 3,
        },
      }),
    );
    await user.click(await within(day).findByTestId("menu-ai-choose"));
    expect(selectCandidate).toHaveBeenCalledWith(
      "s-1",
      "c1",
      expect.objectContaining({ idempotencyKey: expect.any(String) }),
    );
    await waitFor(() =>
      expect(addEntry).toHaveBeenCalledWith(
        "menu-1",
        {
          recipeId: "recipe-9",
          plannedDate: "2026-11-03",
          mealType: "lunch",
          servings: 3,
        },
        { csrfToken: expect.any(String), idempotencyKey: expect.any(String) },
      ),
    );
  });
});
