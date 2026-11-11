import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { createUnavailablePlanningRepository } from "@/adapters/live/livePlanningRepository";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import type { Menu, MenuEntry, PlanningRepository } from "@/contracts/planning";
import type { RecipeRepository } from "@/contracts/recipes";
import type {
  ShoppingPlan,
  ShoppingPlanRepository,
} from "@/contracts/shoppingPlan";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import {
  createMockInventoryRepo,
  createSessionAdapter,
} from "@/features/inventory/testUtils";
import { RecipeProvider } from "@/features/recipes/RecipeProvider";
import { executionStubs } from "@/features/recipes/testExecutionStubs";
import { ShoppingPlanProvider } from "@/features/shopping-plan/ShoppingPlanProvider";
import { MenuPage, missingByMeal } from "./MenuPage";
import { MenusPage } from "./MenusPage";
import { addDays, localIsoDate } from "./planningDates";
import { PlanningProvider } from "./PlanningProvider";

const TODAY = localIsoDate();

const entry = (extra: Partial<MenuEntry>): MenuEntry => ({
  entryId: "e",
  recipeId: "recipe-1",
  recipeName: "Prato",
  revisionNumber: 1,
  revisionPinned: false,
  plannedDate: null,
  mealType: "dinner",
  servings: 2,
  status: "planned",
  executionId: null,
  reserveIngredients: true,
  readiness: null,
  mealsCovered: 1,
  suggestedOrder: null,
  cookOnDate: null,
  ...extra,
});

/** A menu in use: one dish today, one in two days, one snack whenever. */
const IN_USE: Menu = {
  menuId: "menu-1",
  name: "",
  startDate: TODAY,
  endDate: addDays(TODAY, 6),
  status: "accepted",
  acceptedAt: "2026-11-01T12:00:00Z",
  updatedAt: "2026-11-01T12:00:00Z",
  entries: [
    entry({
      entryId: "e-today",
      recipeName: "Feijoada",
      plannedDate: TODAY,
      suggestedOrder: 2,
      servings: 4,
      readiness: {
        entryId: "e-today",
        readiness: "missing",
        missing: [{ name: "Feijão", unit: "Gram", quantity: 200 }],
      },
    }),
    entry({
      entryId: "e-later",
      recipeId: "recipe-2",
      recipeName: "Lasanha",
      plannedDate: addDays(TODAY, 2),
      mealType: "lunch",
      suggestedOrder: 3,
    }),
    entry({
      entryId: "e-snack",
      recipeId: "recipe-3",
      recipeName: "Pão de queijo",
      mealType: "snack",
      suggestedOrder: 1,
    }),
  ],
};

const AMENDING: ShoppingPlan = {
  sessionId: "s1",
  menuId: "menu-1",
  status: "amending",
  setup: {
    today: TODAY,
    days: 7,
    mealTypes: ["snack"],
    mealsOut: 0,
    cookingStyle: "mix",
    priority: "none",
    prepDate: null,
    prepMinutes: null,
    anchorIngredient: null,
    people: 2,
    startDate: TODAY,
    endDate: addDays(TODAY, 6),
  },
  progress: {
    mealsTotal: 1,
    mealsFilled: 0,
    listItemCount: 0,
    sessionMinutesUsed: 0,
    sessionMinutesBudget: null,
  },
  queue: [],
  accepted: [],
  groups: [
    {
      mealType: "snack",
      wanted: 1,
      filled: 0,
      options: ["a", "b", "c", "d", "e", "f"].map((id) => ({
        cardId: `card-${id}`,
        source: "saved",
        name: `Tapioca ${id}`,
        summary: "",
        mealType: "snack",
        servings: 2,
        totalMinutes: 15,
        activeMinutes: 10,
        newItemCount: 1,
        reusedNames: [],
        reasonCode: null,
        reasonIngredient: null,
        overBudget: false,
        guardCode: null,
        guardIngredient: null,
        leadIngredient: null,
        ingredients: [],
      })),
    },
  ],
  preferences: { usePreferences: true, usePantry: false, goal: null },
} as ShoppingPlan;

function plansRepo(
  overrides: Partial<ShoppingPlanRepository> = {},
): ShoppingPlanRepository {
  const unused = vi.fn(async () => AMENDING);
  return {
    getStart: vi.fn(),
    create: unused,
    get: unused,
    addBatch: vi.fn(async () => AMENDING),
    decide: unused,
    undo: unused,
    expand: unused,
    retryExpansion: unused,
    removeCard: unused,
    accept: vi.fn(async () => ({ ...AMENDING, status: "finished" })),
    abandon: unused,
    chooseRecipe: unused,
    setPreferences: unused,
    setMealCounts: unused,
    setCardDay: unused,
    setCardYield: unused,
    setCardRepeats: unused,
    setCardPeople: unused,
    chooseMany: vi.fn(),
    distribute: unused,
    getRoute: vi.fn(),
    saveRouteProgress: vi.fn(),
    findRouteLink: vi.fn(async () => null),
    getMenuLink: vi.fn(async () => ({
      sessionId: "s1",
      status: "finished",
      mealCounts: { dinner: 1, lunch: 1, snack: 2 },
    })),
    openAmendment: vi.fn(async () => AMENDING),
    ...overrides,
  } as ShoppingPlanRepository;
}

function render_(
  path: string,
  planning: Partial<PlanningRepository>,
  options: {
    recipes?: Partial<RecipeRepository>;
    plans?: ShoppingPlanRepository;
  } = {},
) {
  const recipes = {
    ...executionStubs,
    listRecipes: async () => [],
    ...options.recipes,
  } as RecipeRepository;
  const page = (
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/app/cardapio" element={<MenusPage />} />
        <Route path="/app/cardapio/:menuId" element={<MenuPage />} />
        <Route
          path="/app/receitas/cozinhar/:executionId"
          element={<p>cooking page</p>}
        />
      </Routes>
    </MemoryRouter>
  );
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider repository={createMockInventoryRepo()}>
          <RecipeProvider repository={recipes}>
            <PlanningProvider
              repository={{
                ...createUnavailablePlanningRepository(),
                ...planning,
              }}
            >
              {options.plans ? (
                <ShoppingPlanProvider repository={options.plans}>
                  {page}
                </ShoppingPlanProvider>
              ) : (
                page
              )}
            </PlanningProvider>
          </RecipeProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("the menu in use (PLAN-0065 Slice 4)", () => {
  it("lists menus and starts a new one on the board, with no form by dates", async () => {
    render_("/app/cardapio", { listMenus: async () => [] });
    expect(await screen.findByTestId("menus-empty")).toBeInTheDocument();
    expect(screen.getByTestId("menus-new")).toHaveAttribute(
      "href",
      "/app/compras/planejar",
    );
    expect(screen.queryByTestId("menu-create")).not.toBeInTheDocument();
  });

  it("shows today, later days, and undated dishes by meal, with Cozinhar only on today's dish", async () => {
    const user = userEvent.setup();
    const startExecution = vi.fn(async () => ({
      executionId: "exec-9",
    })) as unknown as RecipeRepository["startExecution"];
    render_(
      "/app/cardapio/menu-1",
      { getMenu: async () => IN_USE },
      { recipes: { startExecution } },
    );

    const today = await screen.findByTestId("menu-today");
    expect(today).toHaveTextContent("Feijoada");
    expect(
      within(screen.getByTestId("menu-later")).getByTestId(
        `menu-day-${addDays(TODAY, 2)}`,
      ),
    ).toHaveTextContent("Lasanha");
    expect(screen.getByTestId("menu-whenever-snack")).toHaveTextContent(
      "Pão de queijo",
    );
    // Days with nothing on them do not appear.
    expect(
      screen.queryByTestId(`menu-day-${addDays(TODAY, 1)}`),
    ).not.toBeInTheDocument();
    expect(within(today).getByTestId("menu-entry-readiness")).toHaveTextContent(
      "Missing: Feijão 200 g",
    );

    const cookNow = screen.getAllByTestId("menu-entry-cook-next");
    expect(cookNow).toHaveLength(1);
    expect(within(today).getByTestId("menu-entry-cook-next")).toBe(cookNow[0]);
    await user.click(cookNow[0]!);
    expect(startExecution).toHaveBeenCalledWith("recipe-1", {
      csrfToken: expect.any(String),
      servings: 4,
      menuEntryId: "e-today",
    });
    expect(await screen.findByText("cooking page")).toBeInTheDocument();
  });

  it("changes one dish from its sheet: skip it, release its pantry, give it a day", async () => {
    const user = userEvent.setup();
    const changeEntry = vi.fn(async () => IN_USE);
    render_("/app/cardapio/menu-1", {
      getMenu: async () => IN_USE,
      changeEntry,
    });

    const later = await screen.findByTestId(`menu-day-${addDays(TODAY, 2)}`);
    await user.click(within(later).getByTestId("menu-entry-open"));
    const sheet = await screen.findByTestId("menu-entry-sheet");
    await user.click(within(sheet).getByTestId("menu-entry-skip"));
    expect(changeEntry).toHaveBeenLastCalledWith(
      "menu-1",
      "e-later",
      {
        plannedDate: addDays(TODAY, 2),
        mealType: "lunch",
        servings: 2,
        status: "skipped",
      },
      { csrfToken: expect.any(String) },
    );
    await user.click(within(sheet).getByTestId("menu-entry-reserve"));
    expect(changeEntry).toHaveBeenLastCalledWith(
      "menu-1",
      "e-later",
      expect.objectContaining({ reserveIngredients: false }),
      { csrfToken: expect.any(String) },
    );
    await user.click(within(sheet).getByTestId("menu-entry-day-none"));
    expect(changeEntry).toHaveBeenLastCalledWith(
      "menu-1",
      "e-later",
      expect.objectContaining({ plannedDate: null }),
      { csrfToken: expect.any(String) },
    );
    // Without a plan behind the page, swapping is not offered.
    expect(
      within(sheet).queryByTestId("menu-entry-swap"),
    ).not.toBeInTheDocument();
  });

  it("cooks several dishes ahead on one day without moving the day they are eaten", async () => {
    const user = userEvent.setup();
    const setCookDay = vi.fn(async () => ({
      ...IN_USE,
      entries: IN_USE.entries.map((item) =>
        item.entryId === "e-later" || item.entryId === "e-snack"
          ? { ...item, cookOnDate: TODAY }
          : item,
      ),
    }));
    render_("/app/cardapio/menu-1", {
      getMenu: async () => IN_USE,
      setCookDay,
    });

    await user.click(
      within(await screen.findByTestId("menu-more")).getByText("More options"),
    );
    await user.click(screen.getByTestId("menu-prepare"));
    const sheet = await screen.findByTestId("menu-prepare-sheet");
    const dishes = within(sheet).getAllByTestId("menu-prepare-dish");
    expect(dishes).toHaveLength(3);
    await user.click(dishes[1]!);
    await user.click(dishes[2]!);
    await user.click(within(sheet).getByTestId("menu-prepare-confirm"));

    expect(setCookDay).toHaveBeenCalledWith(
      "menu-1",
      TODAY,
      ["e-later", "e-snack"],
      { csrfToken: expect.any(String) },
    );
    await waitFor(() =>
      expect(screen.getAllByTestId("menu-entry-cook-on")).toHaveLength(2),
    );
    // Lasanha is still eaten on its own day.
    expect(
      screen.getByTestId(`menu-day-${addDays(TODAY, 2)}`),
    ).toHaveTextContent("Lasanha");
  });

  it("swaps a dish through the options sheet, which can be undone", async () => {
    const user = userEvent.setup();
    const getMenu = vi.fn(async () => IN_USE);
    const addEntry = vi.fn(async () => IN_USE);
    const plans = plansRepo({
      decide: vi.fn(async () => ({
        ...AMENDING,
        groups: [{ ...AMENDING.groups[0]!, filled: 1, options: [] }],
      })),
    });
    render_("/app/cardapio/menu-1", { getMenu, addEntry }, { plans });

    const snack = await screen.findByTestId("menu-whenever-snack");
    // Two snacks wanted and one on the menu: one is missing.
    expect(within(snack).getByTestId("menu-choose-snack")).toBeInTheDocument();
    await user.click(within(snack).getByTestId("menu-entry-open"));
    await user.click(await screen.findByTestId("menu-entry-swap"));

    expect(plans.openAmendment).toHaveBeenCalledWith(
      "menu-1",
      TODAY,
      "e-snack",
      expect.objectContaining({ csrfToken: expect.any(String) }),
    );
    const sheet = await screen.findByTestId("meal-sheet");
    expect(await screen.findByTestId("menu-swap-undo")).toHaveTextContent(
      "Pão de queijo left the menu.",
    );
    await user.click(within(sheet).getAllByTestId("meal-option-choose")[0]!);
    expect(plans.decide).toHaveBeenCalledWith(
      "s1",
      "card-a",
      { decision: "accept", mealType: "snack" },
      { csrfToken: expect.any(String) },
    );
    // Closing the plan again once the meal is filled, and reading the menu again.
    await waitFor(() =>
      expect(plans.accept).toHaveBeenCalledWith("s1", expect.anything()),
    );
    expect(getMenu.mock.calls.length).toBeGreaterThan(1);
  });

  it("opens the swap of a dish named in the address, from a reminder on Hoje", async () => {
    const plans = plansRepo();
    render_(
      "/app/cardapio/menu-1?trocar=e-snack",
      { getMenu: async () => IN_USE },
      { plans },
    );
    await waitFor(() =>
      expect(plans.openAmendment).toHaveBeenCalledWith(
        "menu-1",
        TODAY,
        "e-snack",
        expect.objectContaining({ csrfToken: expect.any(String) }),
      ),
    );
    expect(await screen.findByTestId("meal-sheet")).toBeInTheDocument();
  });

  it("undoes a swap by putting the same dish back", async () => {
    const user = userEvent.setup();
    const addEntry = vi.fn(async () => IN_USE);
    render_(
      "/app/cardapio/menu-1",
      { getMenu: async () => IN_USE, addEntry },
      { plans: plansRepo() },
    );
    const snack = await screen.findByTestId("menu-whenever-snack");
    await user.click(within(snack).getByTestId("menu-entry-open"));
    await user.click(await screen.findByTestId("menu-entry-swap"));
    await user.keyboard("{Escape}");
    await user.click(await screen.findByTestId("menu-swap-undo-action"));
    expect(addEntry).toHaveBeenCalledWith(
      "menu-1",
      {
        recipeId: "recipe-3",
        plannedDate: null,
        mealType: "snack",
        servings: 2,
      },
      { csrfToken: expect.any(String), idempotencyKey: expect.any(String) },
    );
  });

  it("counts what is missing per meal, with lunch and dinner sharing", () => {
    expect(
      missingByMeal({ lunch: 2, dinner: 1, snack: 3 }, [
        entry({ mealType: "lunch", mealsCovered: 3 }),
        entry({ mealType: "snack" }),
        entry({ mealType: "snack", status: "skipped" }),
      ]),
    ).toEqual({ lunch: 0, dinner: 0, snack: 2 });
  });
});
