import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import {
  shoppingPlanCatalogs,
  REQUIRED_SHOPPING_PLAN_I18N_KEYS,
} from "@/app/i18n/shoppingPlanCatalog";
import { SessionProvider } from "@/app/session/SessionProvider";
import { RecipeApiError, type RecipeRepository } from "@/contracts/recipes";
import type { ShoppingItem } from "@/contracts/planning";
import type {
  ShoppingPlan,
  ShoppingPlanAccepted,
  ShoppingPlanCard,
  ShoppingPlanRepository,
  ShoppingPlanStart,
} from "@/contracts/shoppingPlan";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import {
  createMockInventoryRepo,
  createSessionAdapter,
} from "@/features/inventory/testUtils";
import { buildShoppingListText } from "@/features/planning/shoppingListText";
import { createUnavailablePlanningRepository } from "@/adapters/live/livePlanningRepository";
import { PlanningProvider } from "@/features/planning/PlanningProvider";
import { RecipeProvider } from "@/features/recipes/RecipeProvider";
import { localIsoDate, addDays } from "@/features/planning/planningDates";
import { ShoppingPlanBoardPage } from "./ShoppingPlanBoardPage";
import { ShoppingPlanProvider } from "./ShoppingPlanProvider";
import { ShoppingPlanSetupPage } from "./ShoppingPlanSetupPage";
import { UNDO_WINDOW_MS, useExpansionQueue } from "./useExpansionQueue";

const SETUP = {
  today: "2026-11-04",
  days: 14,
  mealTypes: ["snack"],
  mealsOut: 0,
  cookingStyle: "mix",
  priority: "none",
  prepDate: null,
  prepMinutes: null,
  anchorIngredient: null,
  people: 1,
  mealCounts: { snack: 7 },
  startDate: "2026-11-04",
  endDate: "2026-11-17",
};

const card = (
  id: string,
  extra: Partial<ShoppingPlanCard> = {},
): ShoppingPlanCard => ({
  cardId: id,
  source: "ai",
  name: `Lanche ${id}`,
  summary: "Um lanche simples.",
  mealType: "snack",
  servings: 1,
  totalMinutes: 20,
  activeMinutes: 10,
  newItemCount: 2,
  reusedNames: ["Tomate"],
  reasonCode: null,
  reasonIngredient: null,
  overBudget: false,
  guardCode: null,
  guardIngredient: null,
  leadIngredient: "Queijo",
  ingredients: [
    { name: "Queijo", quantity: 100, unit: "Gram", optional: false },
  ],
  ...extra,
});

const chosen = (
  id: string,
  extra: Partial<ShoppingPlanAccepted> = {},
): ShoppingPlanAccepted => ({
  cardId: id,
  name: `Lanche ${id}`,
  mealType: "snack",
  repeats: 1,
  mode: null,
  expansionState: "ready",
  failureCode: null,
  recipeId: `r-${id}`,
  guardCode: null,
  guardIngredient: null,
  plannedDate: null,
  totalMinutes: 25,
  ...extra,
});

const OPTIONS = ["a", "b", "c", "d", "e", "f"].map((id) => card(id));

const plan = (extra: Partial<ShoppingPlan> = {}): ShoppingPlan => ({
  sessionId: "s1",
  menuId: "m1",
  status: "building",
  setup: SETUP,
  progress: {
    mealsTotal: 7,
    mealsFilled: 0,
    listItemCount: 0,
    sessionMinutesUsed: 0,
    sessionMinutesBudget: null,
  },
  queue: [],
  accepted: [],
  groups: [{ mealType: "snack", wanted: 7, filled: 0, options: OPTIONS }],
  preferences: { usePreferences: true, usePantry: false, goal: null },
  ...extra,
});

/** A menu with some snacks chosen. */
const withChosen = (count: number, wanted = 7): ShoppingPlan =>
  plan({
    accepted: Array.from({ length: count }, (_, index) => chosen(`x${index}`)),
    progress: {
      mealsTotal: wanted,
      mealsFilled: count,
      listItemCount: 3,
      sessionMinutesUsed: 0,
      sessionMinutesBudget: null,
    },
    groups: [
      {
        mealType: "snack",
        wanted,
        filled: count,
        options: count >= wanted ? [] : OPTIONS,
      },
    ],
  });

const START: ShoppingPlanStart = {
  lastSetup: null,
  people: 2,
  restrictions: [{ code: "peanut_allergy", category: "Allergy" }],
  unverifiableRestrictions: ["keto"],
  hasFreezer: true,
  current: null,
  recent: [],
};

function repository(
  overrides: Partial<ShoppingPlanRepository> = {},
): ShoppingPlanRepository {
  const unused = vi.fn(async () => plan());
  return {
    getStart: vi.fn(async () => START),
    create: unused,
    get: unused,
    addBatch: unused,
    decide: unused,
    chooseRecipe: unused,
    setPreferences: unused,
    setMealCounts: unused,
    setCardDay: unused,
    setCardYield: unused,
    setCardRepeats: unused,
    setCardPeople: unused,
    chooseMany: vi.fn(async () => ({ plan: plan(), failed: [] })),
    distribute: unused,
    undo: unused,
    expand: unused,
    removeCard: unused,
    accept: unused,
    abandon: unused,
    getRoute: vi.fn(async () => {
      throw new Error("unused");
    }),
    saveRouteProgress: vi.fn(async () => undefined),
    findRouteLink: vi.fn(async () => null),
    openAmendment: vi.fn(async () => {
      throw new Error("unused");
    }),
    getMenuLink: vi.fn(async () => null),
    ...overrides,
  };
}

const SAVED = [
  { recipeId: "r-pq", name: "Pão de queijo da vó", mealTypes: ["dinner"] },
  { recipeId: "r-bolo", name: "Bolo de fubá", mealTypes: ["snack"] },
];

function recipeRepository(): RecipeRepository {
  return {
    listRecipes: vi.fn(async () => SAVED),
  } as unknown as RecipeRepository;
}

function renderAt(repo: ShoppingPlanRepository, path: string) {
  return render(
    <ProductionI18nProvider initialLocale="pt-BR">
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider repository={createMockInventoryRepo()}>
          <PlanningProvider repository={createUnavailablePlanningRepository()}>
            <RecipeProvider repository={recipeRepository()}>
              <ShoppingPlanProvider repository={repo}>
                <MemoryRouter initialEntries={[path]}>
                  <Routes>
                    <Route
                      path="/app/compras/planejar"
                      element={<ShoppingPlanSetupPage />}
                    />
                    <Route
                      path="/app/compras/planejar/:sessionId"
                      element={<ShoppingPlanBoardPage />}
                    />
                    <Route
                      path="/app/cardapio/:menuId"
                      element={<p>cardápio aceito</p>}
                    />
                    <Route path="*" element={<p>outra tela</p>} />
                  </Routes>
                </MemoryRouter>
              </ShoppingPlanProvider>
            </RecipeProvider>
          </PlanningProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

afterEach(() => vi.useRealTimers());

describe("first screen of a menu (PLAN-0065)", () => {
  it("counts meals apart from the days, and an untouched counter follows the days", async () => {
    const create = vi.fn(async () => plan());
    renderAt(repository({ create }), "/app/compras/planejar");
    const user = userEvent.setup();

    await screen.findByTestId("plan-setup");
    expect(screen.getByTestId("plan-count-dinner")).toHaveTextContent("7");
    await user.click(screen.getByTestId("plan-days-14"));
    expect(screen.getByTestId("plan-count-dinner")).toHaveTextContent("14");

    // Seven snacks and no dinners, within fourteen days.
    for (let index = 0; index < 14; index += 1)
      await user.click(screen.getByTestId("plan-count-dinner-less"));
    for (let index = 0; index < 7; index += 1)
      await user.click(screen.getByTestId("plan-count-snack-more"));
    await user.click(screen.getByTestId("plan-days-7"));
    expect(screen.getByTestId("plan-count-snack")).toHaveTextContent("7");
    expect(screen.getByTestId("plan-meal-total")).toHaveTextContent(
      "7 em 7 dias",
    );

    await user.click(screen.getByTestId("plan-begin"));
    await screen.findByTestId("plan-board");
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({
        days: 7,
        people: 2,
        mealCounts: { breakfast: 0, lunch: 0, dinner: 0, snack: 7 },
        startDate: localIsoDate(),
      }),
      expect.objectContaining({ idempotencyKey: expect.any(String) }),
    );
  });

  it("asks nothing about cooking and refuses no meals or too many", async () => {
    renderAt(repository(), "/app/compras/planejar");
    const user = userEvent.setup();
    await screen.findByTestId("plan-setup");
    expect(screen.queryByTestId("plan-style")).toBeNull();
    expect(screen.queryByTestId("plan-priority")).toBeNull();
    expect(screen.getByTestId("plan-begin")).toHaveTextContent(
      "Montar cardápio (7)",
    );

    for (let index = 0; index < 7; index += 1)
      await user.click(screen.getByTestId("plan-count-dinner-less"));
    expect(screen.getByTestId("plan-begin")).toBeDisabled();

    for (let index = 0; index < 40; index += 1)
      await user.click(screen.getByTestId("plan-count-lunch-more"));
    await user.click(screen.getByTestId("plan-count-snack-more"));
    expect(screen.getByRole("alert")).toHaveTextContent("até 40");
    expect(screen.getByTestId("plan-begin")).toBeDisabled();
  });

  it("starts on another day chosen from chips, never in the past", async () => {
    const create = vi.fn(async () => plan());
    renderAt(repository({ create }), "/app/compras/planejar");
    const user = userEvent.setup();
    await screen.findByTestId("plan-setup");
    await user.click(screen.getByTestId("plan-start-other"));
    const later = addDays(localIsoDate(), 3);
    await user.click(screen.getByTestId(`plan-start-day-${later}`));
    await user.click(screen.getByTestId("plan-begin"));
    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({ startDate: later }),
        expect.anything(),
      ),
    );
  });

  it("starts from the last menu's counts and people", async () => {
    renderAt(
      repository({
        getStart: vi.fn(async () => ({
          ...START,
          lastSetup: {
            ...SETUP,
            days: 14,
            people: 1,
            mealCounts: { snack: 7, lunch: 3 },
          },
        })),
      }),
      "/app/compras/planejar",
    );
    await screen.findByTestId("plan-setup");
    expect(screen.getByTestId("plan-count-snack")).toHaveTextContent("7");
    expect(screen.getByTestId("plan-count-lunch")).toHaveTextContent("3");
    expect(screen.getByTestId("plan-count-dinner")).toHaveTextContent("0");
    expect(screen.getByTestId("plan-people-line")).toHaveTextContent(
      "Para 1 pessoa",
    );
  });

  it("continues a menu in progress on its board", async () => {
    renderAt(
      repository({
        getStart: vi.fn(async () => ({
          ...START,
          current: { sessionId: "s1", menuId: "m1", status: "reviewing" },
        })),
      }),
      "/app/compras/planejar",
    );
    expect(await screen.findByTestId("plan-continue")).toHaveAttribute(
      "href",
      "/app/compras/planejar/s1",
    );
  });

  it("repeats a menu from the disclosure", async () => {
    const create = vi.fn(async () => plan());
    renderAt(
      repository({
        create,
        getStart: vi.fn(async () => ({
          ...START,
          recent: [
            {
              menuId: "old",
              name: "",
              startDate: "2026-10-01",
              endDate: "2026-10-07",
              mealsTotal: 7,
              dishes: ["Bolo"],
            },
          ],
        })),
      }),
      "/app/compras/planejar",
    );
    const user = userEvent.setup();
    await user.click(
      (await screen.findByTestId("plan-recent-disclosure")).querySelector(
        "summary",
      )!,
    );
    await user.click(screen.getByRole("button", { name: "Repetir" }));
    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({ fromMenuId: "old" }),
        expect.anything(),
      ),
    );
  });
});

describe("menu board (PLAN-0065)", () => {
  it("shows each meal type with what is chosen and one row for what is missing", async () => {
    const value = withChosen(2);
    value.accepted[0] = chosen("x0", { plannedDate: "2026-11-06", repeats: 2 });
    renderAt(
      repository({ get: vi.fn(async () => value) }),
      "/app/compras/planejar/s1",
    );

    const group = await screen.findByTestId("board-group-snack");
    expect(within(group).getByRole("heading")).toHaveTextContent(
      "Lanches · 2 de 7",
    );
    expect(within(group).getAllByTestId("board-dish")).toHaveLength(2);
    expect(within(group).getByTestId("board-choose-snack")).toHaveTextContent(
      /Escolher lanche · faltam 5/,
    );
    expect(within(group).getAllByTestId("board-dish")[0]).toHaveTextContent(
      "rende 2 refeições",
    );
    // A dish without a day has no tag and no warning.
    expect(within(group).getAllByTestId("board-dish")[1]).not.toHaveTextContent(
      "nov",
    );
    expect(screen.getByTestId("board-progress")).toHaveTextContent(
      "2 de 7 · 3 itens",
    );
  });

  it("chooses an option for its meal, offers undo, and keeps the sheet open", async () => {
    const decide = vi.fn(async () => withChosen(1));
    const undo = vi.fn(async () => plan());
    renderAt(
      repository({ get: vi.fn(async () => plan()), decide, undo }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-choose-snack"));
    const sheet = await screen.findByTestId("meal-sheet");
    expect(within(sheet).getByRole("heading", { level: 2 })).toHaveTextContent(
      "Lanche · faltam 7",
    );
    await user.click(
      within(sheet).getByRole("button", {
        name: "Escolher Lanche a para Lanche",
      }),
    );

    expect(decide).toHaveBeenCalledWith(
      "s1",
      "a",
      { decision: "accept", mealType: "snack" },
      expect.anything(),
    );
    expect(await within(sheet).findByTestId("board-undo")).toHaveTextContent(
      "Lanche a escolhido.",
    );
    expect(screen.getByTestId("meal-sheet")).toBeInTheDocument();
    await user.click(within(sheet).getByTestId("board-undo-action"));
    expect(undo).toHaveBeenCalled();
  });

  it("repeats the dish chosen last instead of choosing another, and undoes only the repeat", async () => {
    const repeated = withChosen(4);
    repeated.accepted = [chosen("x0", { repeats: 4, mode: "again" })];
    const setCardRepeats = vi
      .fn()
      .mockResolvedValueOnce(repeated)
      .mockResolvedValueOnce(withChosen(1));
    const undo = vi.fn(async () => plan());
    renderAt(
      repository({
        get: vi.fn(async () => withChosen(1)),
        setCardRepeats,
        undo,
      }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-choose-snack"));
    const panel = await within(
      await screen.findByTestId("meal-sheet"),
    ).findByTestId("meal-sheet-repeat");
    expect(panel).toHaveTextContent("Repetir Lanche x0");
    // A snack is usually eaten fresh, so making it again is offered first.
    expect(
      within(panel).getByTestId("meal-sheet-repeat-mode-again"),
    ).toHaveAttribute("aria-pressed", "true");
    await user.click(within(panel).getByTestId("meal-sheet-repeat-count-more"));
    await user.click(within(panel).getByTestId("meal-sheet-repeat-count-more"));
    await user.click(
      within(panel).getByRole("button", { name: "Repetir mais 3 vezes" }),
    );
    expect(setCardRepeats).toHaveBeenCalledWith(
      "s1",
      "x0",
      { mode: "again", meals: 4 },
      expect.anything(),
    );

    const sheet = screen.getByTestId("meal-sheet");
    expect(await within(sheet).findByTestId("board-undo")).toHaveTextContent(
      "Lanche x0 repetido.",
    );
    expect(within(sheet).getByTestId("meal-sheet-repeat")).toHaveTextContent(
      "Já entra 4 vezes.",
    );
    await user.click(within(sheet).getByTestId("board-undo-action"));
    await waitFor(() =>
      expect(setCardRepeats).toHaveBeenLastCalledWith(
        "s1",
        "x0",
        { mode: null, meals: 1 },
        expect.anything(),
      ),
    );
    expect(undo).not.toHaveBeenCalled();
  });

  it("offers one bigger cooking first for lunch, up to four meals, and more by making it again", async () => {
    const lunch = plan({
      setup: { ...SETUP, mealTypes: ["lunch"], mealCounts: { lunch: 7 } },
      accepted: [chosen("x0", { mealType: "lunch", name: "Moqueca" })],
      groups: [{ mealType: "lunch", wanted: 7, filled: 1, options: [] }],
    });
    renderAt(
      // An empty meal type asks for options on its own; the answer keeps the same menu.
      repository({
        get: vi.fn(async () => lunch),
        addBatch: vi.fn(async () => lunch),
      }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-choose-lunch"));
    const panel = await within(
      await screen.findByTestId("meal-sheet"),
    ).findByTestId("meal-sheet-repeat");
    expect(
      within(panel).getByTestId("meal-sheet-repeat-mode-yield"),
    ).toHaveAttribute("aria-pressed", "true");
    const more = within(panel).getByTestId("meal-sheet-repeat-count-more");
    await user.click(more);
    await user.click(more);
    // One cooking covers four meals at most: one chosen plus three more.
    expect(
      within(panel).getByTestId("meal-sheet-repeat-count"),
    ).toHaveTextContent("3");
    expect(more).toBeDisabled();

    await user.click(within(panel).getByTestId("meal-sheet-repeat-mode-again"));
    for (let index = 0; index < 6; index += 1) await user.click(more);
    // Made again each time, it can fill all six that are missing.
    expect(
      within(panel).getByTestId("meal-sheet-repeat-count"),
    ).toHaveTextContent("6");
  });

  it("changes how many times a dish made again is cooked, from its sheet", async () => {
    const value = withChosen(3);
    value.accepted = [chosen("x0", { repeats: 3, mode: "again" })];
    const setCardRepeats = vi.fn(async () => value);
    renderAt(
      repository({ get: vi.fn(async () => value), setCardRepeats }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    const dish = await screen.findByTestId("board-dish");
    expect(dish).toHaveTextContent("3 vezes");
    await user.click(dish);
    const sheet = await screen.findByTestId("board-dish-sheet");
    // A dish made again each time has no "rende" choice; it has how many times.
    expect(within(sheet).queryByTestId("board-dish-yield")).toBeNull();
    await user.click(within(sheet).getByTestId("board-dish-times-less"));
    expect(setCardRepeats).toHaveBeenCalledWith(
      "s1",
      "x0",
      { mode: "again", meals: 2 },
      expect.anything(),
    );
  });

  it("closes the sheet when the meal type is complete and returns focus to the group", async () => {
    const decide = vi.fn(async () => withChosen(7));
    renderAt(
      repository({ get: vi.fn(async () => withChosen(6)), decide }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-choose-snack"));
    const sheet = await screen.findByTestId("meal-sheet");
    await user.click(within(sheet).getAllByTestId("meal-option-choose")[0]);
    await waitFor(() => expect(screen.queryByTestId("meal-sheet")).toBeNull());
    await waitFor(() =>
      expect(document.activeElement).toHaveAttribute(
        "id",
        "board-heading-snack",
      ),
    );
    expect(screen.getByTestId("board-undo")).toBeInTheDocument();
  });

  it("finds the person's own recipes while typing and asks the AI for exactly what was typed", async () => {
    const chooseRecipe = vi.fn(async () => withChosen(1));
    const addBatch = vi.fn(async () => plan());
    renderAt(
      repository({ get: vi.fn(async () => plan()), chooseRecipe, addBatch }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-choose-snack"));
    const sheet = await screen.findByTestId("meal-sheet");
    await user.type(
      within(sheet).getByTestId("meal-sheet-text"),
      "pao de queijo",
    );

    // Saved as a dinner, it still shows: it is the person's own recipe.
    const saved = await within(sheet).findByTestId("meal-sheet-saved");
    expect(saved).toHaveTextContent("Pão de queijo da vó");
    expect(within(sheet).getByTestId("meal-sheet-ask-ai")).toHaveTextContent(
      "Pedir à IA: pao de queijo",
    );
    await user.click(within(sheet).getByTestId("meal-sheet-ask-ai"));
    await waitFor(() =>
      expect(addBatch).toHaveBeenCalledWith(
        "s1",
        expect.objectContaining({
          mealType: "snack",
          request: "pao de queijo",
          replaceOffers: false,
        }),
        expect.objectContaining({ idempotencyKey: expect.any(String) }),
      ),
    );
    await user.click(within(sheet).getByTestId("meal-sheet-saved"));
    expect(chooseRecipe).toHaveBeenCalledWith(
      "s1",
      "r-pq",
      "snack",
      expect.anything(),
    );
  });

  it("names the restriction a request conflicts with, and offers no way around it", async () => {
    const addBatch = vi.fn(async () => {
      throw new RecipeApiError("request_blocked", "x", 422, {
        fieldErrors: { restriction: ["peanut_allergy"] },
      });
    });
    renderAt(
      repository({ get: vi.fn(async () => plan()), addBatch }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-choose-snack"));
    const sheet = await screen.findByTestId("meal-sheet");
    await user.type(
      within(sheet).getByTestId("meal-sheet-text"),
      "paçoca{Enter}",
    );
    expect(
      await within(sheet).findByTestId("meal-sheet-failure"),
    ).toHaveTextContent("Essa receita conflita com");
    // The options already there stay usable.
    expect(within(sheet).getAllByTestId("meal-option-choose")[0]).toBeEnabled();
  });

  it("sends only the last state of quick switch taps, then asks for new dishes", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const setPreferences = vi.fn(async () =>
      plan({
        preferences: { usePreferences: false, usePantry: true, goal: null },
      }),
    );
    const addBatch = vi.fn(async () => plan());
    renderAt(
      repository({ get: vi.fn(async () => plan()), setPreferences, addBatch }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    await user.click(await screen.findByTestId("board-choose-snack"));
    const sheet = await screen.findByTestId("meal-sheet");
    const pantry = within(sheet).getByTestId("meal-sheet-pantry");
    expect(pantry).toHaveAttribute("role", "switch");
    await user.click(within(sheet).getByTestId("meal-sheet-preferences"));
    await user.click(pantry);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    expect(setPreferences).toHaveBeenCalledTimes(1);
    expect(setPreferences).toHaveBeenCalledWith(
      "s1",
      { usePreferences: false, usePantry: true, goal: null },
      expect.anything(),
    );
    await waitFor(() =>
      expect(addBatch).toHaveBeenCalledWith(
        "s1",
        expect.objectContaining({ mealType: "snack", replaceOffers: true }),
        expect.anything(),
      ),
    );
  });

  it("asks for dishes on its own once when a meal type has few options", async () => {
    const few = plan({
      groups: [
        {
          mealType: "snack",
          wanted: 7,
          filled: 0,
          options: OPTIONS.slice(0, 4),
        },
      ],
    });
    const addBatch = vi.fn(async () => few);
    renderAt(
      repository({ get: vi.fn(async () => few), addBatch }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-choose-snack"));
    await waitFor(() => expect(addBatch).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole("button", { name: "Fechar" }));
    await user.click(screen.getByTestId("board-choose-snack"));
    await screen.findByTestId("meal-sheet");
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(addBatch).toHaveBeenCalledTimes(1);
  });

  it("says when the AI is off and keeps the options usable", async () => {
    const addBatch = vi.fn(async () => {
      throw new RecipeApiError("ai_capability_unavailable", "x", 503);
    });
    const few = plan({
      groups: [
        {
          mealType: "snack",
          wanted: 7,
          filled: 0,
          options: OPTIONS.slice(0, 2),
        },
      ],
    });
    renderAt(
      repository({ get: vi.fn(async () => few), addBatch }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-choose-snack"));
    const sheet = await screen.findByTestId("meal-sheet");
    expect(
      await within(sheet).findByTestId("meal-sheet-failure"),
    ).toHaveTextContent("A IA não está disponível agora");
    expect(within(sheet).getAllByTestId("meal-option-choose")).toHaveLength(2);
  });

  it("asks before using a menu with meals still open, then opens it", async () => {
    const accept = vi.fn(async () => withChosen(5));
    renderAt(
      repository({ get: vi.fn(async () => withChosen(5)), accept }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-use"));
    expect(screen.getByTestId("board-use-confirm")).toHaveTextContent(
      "Faltam 2 refeições. Usar assim?",
    );
    expect(accept).not.toHaveBeenCalled();
    await user.click(screen.getByTestId("board-use-anyway"));
    expect(await screen.findByText("cardápio aceito")).toBeInTheDocument();
  });

  it("waits while recipes are being prepared", async () => {
    const value = withChosen(7);
    value.accepted[0] = chosen("x0", {
      expansionState: "pending",
      recipeId: null,
    });
    renderAt(
      repository({
        get: vi.fn(async () => value),
        expand: vi.fn(() => new Promise<ShoppingPlan>(() => undefined)),
      }),
      "/app/compras/planejar/s1",
    );
    expect(await screen.findByTestId("board-use")).toBeDisabled();
    expect(screen.getByTestId("board-preparing")).toHaveTextContent(
      "Preparando receitas: 6 de 7",
    );
  });
});

describe("acting on chosen dishes (PLAN-0065 Slice 3)", () => {
  it("completes what is missing with one confirmation, after a swap", async () => {
    const chooseMany = vi.fn(async () => ({
      plan: withChosen(7),
      failed: [],
    }));
    renderAt(
      repository({ get: vi.fn(async () => withChosen(5)), chooseMany }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-complete"));
    const sheet = await screen.findByTestId("complete-sheet");
    const rows = within(sheet).getAllByTestId("complete-row");
    expect(rows.map((row) => row.textContent)).toEqual([
      expect.stringContaining("Lanche a"),
      expect.stringContaining("Lanche b"),
    ]);
    // Nothing is chosen until the person confirms.
    expect(chooseMany).not.toHaveBeenCalled();
    await user.click(
      within(sheet).getByRole("button", { name: "Trocar Lanche a" }),
    );
    expect(within(sheet).getAllByTestId("complete-row")[0]).toHaveTextContent(
      "Lanche c",
    );
    await user.click(within(sheet).getByTestId("complete-confirm"));
    expect(chooseMany).toHaveBeenCalledWith(
      "s1",
      [
        { cardId: "c", mealType: "snack" },
        { cardId: "b", mealType: "snack" },
      ],
      expect.anything(),
    );
    expect(await screen.findByTestId("board-notice")).toHaveTextContent(
      "2 pratos adicionados.",
    );
    await waitFor(() =>
      expect(screen.queryByTestId("complete-sheet")).toBeNull(),
    );
  });

  it("names what could not be added and what is still missing", async () => {
    const chooseMany = vi.fn(async () => ({
      plan: withChosen(6),
      failed: ["b"],
    }));
    renderAt(
      repository({ get: vi.fn(async () => withChosen(5)), chooseMany }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("board-complete"));
    const sheet = await screen.findByTestId("complete-sheet");
    await user.click(within(sheet).getByTestId("complete-confirm"));
    const failed = await within(sheet).findByTestId("complete-failed");
    expect(failed).toHaveTextContent("Não consegui adicionar: Lanche b.");
    expect(failed).toHaveTextContent("Ainda falta 1 refeição.");
  });

  it("gives a dish a day, and asks before it covers more than the menu wants", async () => {
    const value = withChosen(2, 3);
    const setCardDay = vi.fn(async () => value);
    const setMealCounts = vi.fn(async () => value);
    const setCardYield = vi.fn(async () => value);
    renderAt(
      repository({
        get: vi.fn(async () => value),
        setCardDay,
        setMealCounts,
        setCardYield,
      }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click((await screen.findAllByTestId("board-dish"))[0]);
    const sheet = await screen.findByTestId("board-dish-sheet");
    await user.click(within(sheet).getByTestId("board-dish-day-2026-11-06"));
    expect(setCardDay).toHaveBeenCalledWith(
      "s1",
      "x0",
      "2026-11-06",
      expect.anything(),
    );

    // One snack is left, so covering three would make four: the count rises only if confirmed.
    await user.click(within(sheet).getByTestId("board-dish-yield-3"));
    expect(within(sheet).getByTestId("board-dish-raise")).toHaveTextContent(
      "Você pediu 3. Com isso ficam 4.",
    );
    expect(setCardYield).not.toHaveBeenCalled();
    await user.click(within(sheet).getByTestId("board-dish-raise-confirm"));
    await waitFor(() =>
      expect(setCardYield).toHaveBeenCalledWith(
        "s1",
        "x0",
        3,
        expect.anything(),
      ),
    );
    expect(setMealCounts).toHaveBeenCalledWith(
      "s1",
      { breakfast: 0, lunch: 0, dinner: 0, snack: 4 },
      expect.anything(),
    );
  });

  it("swaps a dish by removing it and opening its meal's options", async () => {
    const removeCard = vi.fn(async () => withChosen(1));
    renderAt(
      repository({ get: vi.fn(async () => withChosen(2)), removeCard }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup();
    await user.click((await screen.findAllByTestId("board-dish"))[0]);
    await user.click(await screen.findByTestId("board-dish-swap"));
    expect(removeCard).toHaveBeenCalledWith("s1", "x0", expect.anything());
    expect(await screen.findByTestId("meal-sheet")).toBeInTheDocument();
    expect(screen.queryByTestId("board-dish-sheet")).toBeNull();
  });

  it("sends the last count after quick taps, and spreads dishes over the days on request", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const setMealCounts = vi.fn(async () => withChosen(2, 9));
    const distribute = vi.fn(async () => withChosen(2));
    renderAt(
      repository({
        get: vi.fn(async () => withChosen(2)),
        setMealCounts,
        distribute,
      }),
      "/app/compras/planejar/s1",
    );
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const more = await screen.findByTestId("board-count-snack-more");
    await user.click(more);
    await user.click(more);
    expect(screen.getByTestId("board-count-snack")).toHaveTextContent("9");
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    expect(setMealCounts).toHaveBeenCalledTimes(1);
    expect(setMealCounts).toHaveBeenCalledWith(
      "s1",
      { breakfast: 0, lunch: 0, dinner: 0, snack: 9 },
      expect.anything(),
    );
    // Never below what is chosen.
    expect(screen.getByTestId("board-count-snack-less")).toBeEnabled();

    await user.click(screen.getByTestId("board-spread-action"));
    expect(distribute).toHaveBeenCalledWith("s1", false, expect.anything());
  });
});

describe("expansion queue (PLAN-0062)", () => {
  function Queue({
    value,
    onPlan,
  }: {
    value: ShoppingPlan;
    onPlan: (next: ShoppingPlan) => void;
  }) {
    const stopped = useExpansionQueue(
      value,
      { csrfToken: "c", language: "pt-BR" },
      onPlan,
    );
    return <p data-testid="stopped">{stopped ?? "running"}</p>;
  }
  function Harness({
    value,
    repo,
    onPlan,
    screenKey = "a",
  }: {
    value: ShoppingPlan;
    repo: ShoppingPlanRepository;
    onPlan: (next: ShoppingPlan) => void;
    /** A new key is a different screen mounting, as when moving from the cards to the review. */
    screenKey?: string;
  }) {
    return (
      <ShoppingPlanProvider repository={repo}>
        <Queue key={screenKey} value={value} onPlan={onPlan} />
      </ShoppingPlanProvider>
    );
  }
  const accepted = (id: string, state = "pending") => ({
    cardId: id,
    name: id,
    mealType: "dinner",
    repeats: 1,
    mode: null,
    expansionState: state,
    failureCode: null,
    recipeId: null,
    guardCode: null,
    guardIngredient: null,
  });

  it("skips the undo window for cards already pending and waits for a new one", async () => {
    vi.useFakeTimers();
    const done = plan({ accepted: [accepted("a", "ready")] });
    const expand = vi.fn(async () => done);
    const onPlan = vi.fn();
    const repo = repository({ expand });
    const view = render(
      <Harness
        value={plan({ accepted: [accepted("a")] })}
        repo={repo}
        onPlan={onPlan}
      />,
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(expand).toHaveBeenCalledTimes(1);

    view.rerender(
      <Harness
        value={plan({ accepted: [accepted("a", "ready"), accepted("b")] })}
        repo={repo}
        onPlan={onPlan}
      />,
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(UNDO_WINDOW_MS - 100);
    });
    expect(expand).toHaveBeenCalledTimes(1);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(200);
    });
    expect(expand).toHaveBeenCalledTimes(2);
    expect((expand.mock.calls[1] as unknown[])[1]).toBe("b");
  });

  it("keeps one queue when the screen changes, so a card is never expanded twice at once", async () => {
    vi.useFakeTimers();
    let finish!: (value: ShoppingPlan) => void;
    const done = plan({ accepted: [accepted("a", "ready")] });
    const expand = vi.fn(
      () => new Promise<ShoppingPlan>((resolve) => (finish = resolve)),
    );
    const repo = repository({ expand });
    const pending = plan({ accepted: [accepted("a")] });
    const onPlan = vi.fn();
    const view = render(
      <Harness value={pending} repo={repo} onPlan={vi.fn()} />,
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(expand).toHaveBeenCalledTimes(1);

    view.rerender(
      <Harness value={pending} repo={repo} onPlan={onPlan} screenKey="b" />,
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(UNDO_WINDOW_MS * 2);
    });
    expect(expand).toHaveBeenCalledTimes(1);

    // The screen now open hears about the work the previous one started.
    await act(async () => {
      finish(done);
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(onPlan).toHaveBeenCalledWith(done);
  });

  it("retries a conflict and stops on an exhausted budget", async () => {
    vi.useFakeTimers();
    const expand = vi
      .fn()
      .mockRejectedValueOnce(
        new RecipeApiError("ai_operation_conflict", "x", 409),
      )
      .mockRejectedValueOnce(
        new RecipeApiError("ai_budget_exhausted", "x", 503),
      );
    const stillPending = plan({ accepted: [accepted("a")] });
    render(
      <Harness
        value={stillPending}
        repo={repository({ expand, get: vi.fn(async () => stillPending) })}
        onPlan={vi.fn()}
      />,
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(expand).toHaveBeenCalledTimes(1);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });
    expect(expand).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId("stopped")).toHaveTextContent(
      "ai_budget_exhausted",
    );
  });
});

describe("shopping list text and catalogs (PLAN-0062)", () => {
  const item = (extra: Partial<ShoppingItem>): ShoppingItem =>
    ({
      itemKey: "k",
      displayName: "Frango",
      unit: "Gram",
      category: "required",
      requiredQuantity: 500,
      availableQuantity: 0,
      toBuyQuantity: 500,
      checked: false,
      uses: [],
      ...extra,
    }) as ShoppingItem;

  it("holds only lines still to buy, grouped by section", () => {
    const text = buildShoppingListText(
      [
        item({ storeSection: "butcher_fish" }),
        item({
          displayName: "Tomate",
          storeSection: "produce",
          toBuyQuantity: 200,
        }),
        item({ displayName: "Comprado", checked: true }),
        item({
          displayName: "Em casa",
          category: "available",
          toBuyQuantity: 0,
        }),
      ],
      (key) => key,
      String,
    );

    expect(text.indexOf("Tomate")).toBeLessThan(text.indexOf("Frango"));
    expect(text).toContain("- Frango: 500 recipes.unit.g");
    expect(text).not.toContain("Comprado");
    expect(text).not.toContain("Em casa");
  });

  it("has every key in every language", () => {
    for (const locale of ["pt-BR", "en", "es"] as const) {
      for (const key of REQUIRED_SHOPPING_PLAN_I18N_KEYS) {
        expect(
          shoppingPlanCatalogs[locale][key],
          `${locale} ${key}`,
        ).toBeTruthy();
      }
    }
  });
});
