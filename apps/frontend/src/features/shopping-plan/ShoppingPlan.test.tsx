import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import {
  shoppingPlanCatalogs,
  REQUIRED_SHOPPING_PLAN_I18N_KEYS,
} from "@/app/i18n/shoppingPlanCatalog";
import { SessionProvider } from "@/app/session/SessionProvider";
import { RecipeApiError } from "@/contracts/recipes";
import type { ShoppingItem } from "@/contracts/planning";
import type {
  ShoppingPlan,
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
import { ShoppingPlanDeckPage } from "./ShoppingPlanDeckPage";
import { ShoppingPlanProvider } from "./ShoppingPlanProvider";
import { ShoppingPlanSetupPage } from "./ShoppingPlanSetupPage";
import { UNDO_WINDOW_MS, useExpansionQueue } from "./useExpansionQueue";

const SETUP = {
  today: "2026-11-04",
  days: 5,
  mealTypes: ["dinner"],
  mealsOut: 0,
  cookingStyle: "mix",
  priority: "waste",
  prepDate: "2026-11-04",
  prepMinutes: 120,
  anchorIngredient: null,
  people: 2,
};

const card = (
  id: string,
  extra: Partial<ShoppingPlanCard> = {},
): ShoppingPlanCard => ({
  cardId: id,
  source: "ai",
  name: `Prato ${id}`,
  summary: "Um prato simples.",
  mealType: "dinner",
  servings: 2,
  totalMinutes: 30,
  activeMinutes: 20,
  newItemCount: 2,
  reusedNames: ["Tomate"],
  reasonCode: null,
  reasonIngredient: null,
  overBudget: false,
  guardCode: null,
  guardIngredient: null,
  leadIngredient: "Frango",
  ingredients: [
    { name: "Frango", quantity: 300, unit: "Gram", optional: false },
  ],
  ...extra,
});

const plan = (extra: Partial<ShoppingPlan> = {}): ShoppingPlan => ({
  sessionId: "s1",
  menuId: "m1",
  status: "building",
  setup: SETUP,
  progress: {
    mealsTotal: 5,
    mealsFilled: 0,
    listItemCount: 0,
    sessionMinutesUsed: 0,
    sessionMinutesBudget: 120,
  },
  queue: [card("a"), card("b")],
  accepted: [],
  needsBatch: false,
  offerMoreOptions: false,
  exhausted: false,
  canUndo: false,
  ...extra,
});

const START: ShoppingPlanStart = {
  lastSetup: null,
  people: 3,
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
    setReason: unused,
    undo: unused,
    expand: unused,
    removeCard: unused,
    review: unused,
    replace: unused,
    accept: unused,
    abandon: unused,
    getRoute: vi.fn(async () => {
      throw new Error("unused");
    }),
    saveRouteProgress: vi.fn(async () => undefined),
    findRouteLink: vi.fn(async () => null),
    ...overrides,
  };
}

function renderAt(
  repo: ShoppingPlanRepository,
  path: string,
  planning = createUnavailablePlanningRepository(),
) {
  return render(
    <ProductionI18nProvider initialLocale="pt-BR">
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider repository={createMockInventoryRepo()}>
          <PlanningProvider repository={planning}>
            <ShoppingPlanProvider repository={repo}>
              <MemoryRouter initialEntries={[path]}>
                <Routes>
                  <Route
                    path="/app/compras/planejar"
                    element={<ShoppingPlanSetupPage />}
                  />
                  <Route
                    path="/app/compras/planejar/:sessionId"
                    element={<ShoppingPlanDeckPage />}
                  />
                  <Route
                    path="/app/compras/planejar/:sessionId/revisar"
                    element={<p>revisão</p>}
                  />
                </Routes>
              </MemoryRouter>
            </ShoppingPlanProvider>
          </PlanningProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

afterEach(() => vi.useRealTimers());

describe("shopping plan setup (PLAN-0062)", () => {
  it("uses defaults, updates the total, and sends the answers", async () => {
    const user = userEvent.setup();
    const create = vi.fn(async () => plan());
    renderAt(repository({ create }), "/app/compras/planejar");

    expect(await screen.findByTestId("plan-meal-total")).toHaveTextContent(
      "10 refeições",
    );
    expect(screen.getByTestId("plan-people")).toHaveTextContent("3");
    expect(screen.getByTestId("plan-restrictions")).toHaveTextContent(
      "Alergia a amendoim",
    );
    expect(screen.getByTestId("plan-unverifiable")).toBeInTheDocument();

    await user.click(screen.getByTestId("plan-meals-out-more"));
    expect(screen.getByTestId("plan-meal-total")).toHaveTextContent(
      "9 refeições",
    );
    await waitFor(() => expect(screen.getByTestId("plan-begin")).toBeEnabled());
    await user.click(screen.getByTestId("plan-begin"));

    await waitFor(() => expect(create).toHaveBeenCalledTimes(1));
    expect((create.mock.calls[0] as unknown[])[0]).toMatchObject({
      days: 5,
      mealsOut: 1,
      mealTypes: ["lunch", "dinner"],
      cookingStyle: "mix",
      people: 3,
    });
  });

  it("hides the session questions for a dish each day and refuses too many meals", async () => {
    const user = userEvent.setup();
    renderAt(repository(), "/app/compras/planejar");

    expect(await screen.findByTestId("plan-prep-day")).toBeInTheDocument();
    await user.click(screen.getByTestId("plan-style-daily"));
    expect(screen.queryByTestId("plan-prep-day")).toBeNull();

    await user.click(screen.getByTestId("plan-days-other"));
    for (let index = 0; index < 9; index += 1)
      await user.click(screen.getByTestId("plan-days-count-more"));
    await user.click(screen.getByTestId("plan-meal-snack"));
    expect(screen.getByTestId("plan-meal-total")).toHaveTextContent("até 40");
    expect(screen.getByTestId("plan-begin")).toBeDisabled();
  });

  it("offers to continue a plan in progress", async () => {
    renderAt(
      repository({
        getStart: vi.fn(async () => ({
          ...START,
          current: { sessionId: "s9", menuId: "m9", status: "reviewing" },
        })),
      }),
      "/app/compras/planejar",
    );

    expect(await screen.findByTestId("plan-continue")).toHaveAttribute(
      "href",
      "/app/compras/planejar/s9/revisar",
    );
    expect(screen.queryByTestId("plan-begin")).toBeNull();
  });

  const RECENT = {
    menuId: "old",
    name: "",
    startDate: "2026-10-01",
    endDate: "2026-10-05",
    mealsTotal: 5,
    dishes: ["Sopa", "Arroz"],
  };

  it("repeats a finished plan from its menu and opens the deck", async () => {
    const user = userEvent.setup();
    const create = vi.fn(async () => plan());
    renderAt(
      repository({
        getStart: vi.fn(async () => ({ ...START, recent: [RECENT] })),
        create,
      }),
      "/app/compras/planejar",
    );

    expect(await screen.findByTestId("plan-recent-name")).toHaveTextContent(
      "Sopa, Arroz",
    );
    await user.click(screen.getByTestId("plan-repeat"));
    await screen.findByTestId("plan-card");
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ fromMenuId: "old" }),
      expect.objectContaining({ idempotencyKey: expect.any(String) }),
    );
  });

  it("starts from the answers, not from a plan, with the main button", async () => {
    const user = userEvent.setup();
    const create = vi.fn(async () => plan());
    renderAt(
      repository({
        getStart: vi.fn(async () => ({ ...START, recent: [RECENT] })),
        create,
      }),
      "/app/compras/planejar",
    );

    await user.click(await screen.findByTestId("plan-begin"));
    await screen.findByTestId("plan-card");
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ fromMenuId: null }),
      expect.anything(),
    );
  });

  it("names a finished plan through its menu, and says when that fails", async () => {
    const user = userEvent.setup();
    const create = vi.fn(async () => plan());
    const changeMenu = vi
      .fn()
      .mockRejectedValueOnce(new Error("down"))
      .mockResolvedValue({ name: "Semana boa" });
    renderAt(
      repository({
        getStart: vi.fn(async () => ({ ...START, recent: [RECENT] })),
        create,
      }),
      "/app/compras/planejar",
      { ...createUnavailablePlanningRepository(), changeMenu },
    );

    await user.click(await screen.findByTestId("plan-recent-name-edit"));
    await user.type(screen.getByTestId("plan-recent-name-input"), "Semana boa");
    await user.click(screen.getByTestId("plan-recent-name-save"));
    await screen.findByRole("alert");
    expect(create).not.toHaveBeenCalled();

    await user.click(screen.getByTestId("plan-recent-name-save"));
    await waitFor(() =>
      expect(screen.getByTestId("plan-recent-name")).toHaveTextContent(
        "Semana boa",
      ),
    );
    expect(changeMenu).toHaveBeenLastCalledWith(
      "old",
      { name: "Semana boa", startDate: "2026-10-01", endDate: "2026-10-05" },
      expect.anything(),
    );
  });
});

describe("shopping plan cards (PLAN-0062)", () => {
  const BASE = card("base", {
    name: "Frango desfiado para a semana",
    component: {
      outputName: "Frango desfiado",
      outputQuantity: 700,
      outputUnit: "Gram",
      usedBy: ["Wrap", "Escondidinho"],
    },
  });

  it("offers a base as a single decision and says what it is for", async () => {
    renderAt(
      repository({ get: vi.fn(async () => plan({ queue: [BASE] })) }),
      "/app/compras/planejar/s1",
    );

    const base = await screen.findByTestId("plan-card-base");
    expect(base).toHaveTextContent("Base sugerida: Frango desfiado, 700 g.");
    expect(base).toHaveTextContent("Serve para: Wrap, Escondidinho.");
    expect(screen.getByTestId("plan-yes-twice")).toBeDisabled();
  });

  it("waits for the answer about a base even when every meal is filled", async () => {
    const filled = {
      mealsTotal: 5,
      mealsFilled: 5,
      listItemCount: 1,
      sessionMinutesUsed: 0,
      sessionMinutesBudget: 120,
    };
    const user = userEvent.setup();
    const decide = vi.fn(async () => plan({ queue: [], progress: filled }));
    renderAt(
      repository({
        get: vi.fn(async () => plan({ queue: [BASE], progress: filled })),
        decide,
      }),
      "/app/compras/planejar/s1",
    );

    await screen.findByTestId("plan-card-base");
    expect(screen.queryByText("revisão")).toBeNull();
    await user.click(screen.getByTestId("plan-yes"));
    await screen.findByText("revisão");
  });

  it("moves on at once, takes a reason, and undoes", async () => {
    const user = userEvent.setup();
    const rejected = plan({ queue: [card("b")], canUndo: true });
    const decide = vi.fn(async () => rejected);
    const setReason = vi.fn(async () => rejected);
    const undo = vi.fn(async () => plan());
    renderAt(
      repository({ get: vi.fn(async () => plan()), decide, setReason, undo }),
      "/app/compras/planejar/s1",
    );

    expect(await screen.findByTestId("plan-card-name")).toHaveTextContent(
      "Prato a",
    );
    expect(screen.getByTestId("plan-card-new-items")).toHaveTextContent(
      "+2 itens novos",
    );
    expect(screen.getByTestId("plan-progress")).toHaveTextContent(
      "Refeição 1 de 5",
    );
    expect(screen.getByTestId("plan-card-name")).toHaveFocus();

    await user.click(screen.getByTestId("plan-no"));
    expect(await screen.findByTestId("plan-card-name")).toHaveTextContent(
      "Prato b",
    );
    expect(decide).toHaveBeenCalledWith(
      "s1",
      "a",
      expect.objectContaining({ decision: "reject" }),
      expect.anything(),
    );

    await user.click(screen.getByTestId("plan-reason-tired"));
    expect(setReason).toHaveBeenCalledWith(
      "s1",
      "a",
      "tired_of",
      expect.anything(),
    );

    await user.click(screen.getByTestId("plan-undo"));
    await waitFor(() => expect(undo).toHaveBeenCalled());
    expect(await screen.findByTestId("plan-card-name")).toHaveTextContent(
      "Prato a",
    );
  });

  it("asks how a repeat is cooked, with the style's choice preselected", async () => {
    const user = userEvent.setup();
    const decide = vi.fn(async () => plan({ queue: [card("b")] }));
    renderAt(
      repository({ get: vi.fn(async () => plan()), decide }),
      "/app/compras/planejar/s1",
    );

    await user.click(await screen.findByTestId("plan-yes-twice"));
    expect(screen.getByTestId("plan-repeat-yield")).toBeChecked();
    await user.click(screen.getByTestId("plan-repeat-more"));
    await user.click(screen.getByTestId("plan-repeat-again"));
    await user.click(screen.getByTestId("plan-repeat-confirm"));

    expect(decide).toHaveBeenCalledWith(
      "s1",
      "a",
      { decision: "accept", mode: "again", repeats: 3 },
      expect.anything(),
    );
  });

  it("puts the card back and says so when saving a decision fails", async () => {
    const user = userEvent.setup();
    renderAt(
      repository({
        get: vi.fn(async () => plan()),
        decide: vi.fn(async () => {
          throw new RecipeApiError("unavailable", "x", 503);
        }),
      }),
      "/app/compras/planejar/s1",
    );

    await user.click(await screen.findByTestId("plan-yes"));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Não consegui salvar",
    );
    expect(screen.getByTestId("plan-card-name")).toHaveTextContent("Prato a");
  });

  it("asks for cards when low, and keeps working when AI is unavailable", async () => {
    const addBatch = vi.fn(async () => {
      throw new RecipeApiError("ai_budget_exhausted", "x", 503);
    });
    renderAt(
      repository({
        get: vi.fn(async () => plan({ needsBatch: true, queue: [card("a")] })),
        addBatch,
      }),
      "/app/compras/planejar/s1",
    );

    expect(await screen.findByTestId("plan-ai-notice")).toBeInTheDocument();
    expect(addBatch).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("plan-card-name")).toHaveTextContent("Prato a");
  });

  describe("Quero outras opções (PLAN-0064)", () => {
    const sentOptions = (addBatch: ReturnType<typeof vi.fn>) =>
      (addBatch.mock.calls[0] as unknown[])[1];

    async function openPanel(addBatch: ReturnType<typeof vi.fn>) {
      const user = userEvent.setup();
      renderAt(
        repository({
          get: vi.fn(async () => plan({ queue: [card("a")] })),
          addBatch,
        } as never),
        "/app/compras/planejar/s1",
      );
      await user.click(await screen.findByTestId("plan-more"));
      return user;
    }

    it("asks first, and sends nothing until the person confirms", async () => {
      const addBatch = vi.fn(async () => plan({ queue: [card("b")] }));
      const user = await openPanel(addBatch);

      expect(screen.getByTestId("plan-more-pantry")).toBeInTheDocument();
      expect(screen.getByTestId("plan-more-similarity")).toBeInTheDocument();
      expect(screen.queryByTestId("plan-more-hint")).toBeNull();
      expect(addBatch).not.toHaveBeenCalled();

      await user.click(screen.getByTestId("plan-more-submit"));

      await waitFor(() =>
        expect(screen.getByTestId("plan-card-name")).toHaveTextContent(
          "Prato b",
        ),
      );
      expect(sentOptions(addBatch)).toEqual({
        pantry: "free",
        similarity: "different",
        referenceCardId: card("a").cardId,
        hint: null,
      });
    });

    it("sends the answers and a hint limited to 40 characters", async () => {
      const addBatch = vi.fn(async () => plan({ queue: [card("b")] }));
      const user = await openPanel(addBatch);

      await user.click(screen.getByTestId("plan-more-pantry-use"));
      await user.click(screen.getByTestId("plan-more-similarity-similar"));
      await user.click(screen.getByTestId("plan-more-hint-open"));
      await user.type(
        screen.getByTestId("plan-more-hint"),
        "  a".padEnd(50, "x"),
      );
      expect(screen.getByTestId("plan-more-hint")).toHaveValue(
        "  a".padEnd(40, "x"),
      );
      await user.click(screen.getByTestId("plan-more-submit"));

      await waitFor(() => expect(addBatch).toHaveBeenCalledTimes(1));
      expect(sentOptions(addBatch)).toEqual({
        pantry: "use",
        similarity: "similar",
        referenceCardId: card("a").cardId,
        hint: "a".padEnd(38, "x"),
      });
    });

    it("does not compare when the person does not care", async () => {
      const addBatch = vi.fn(async () => plan({ queue: [card("b")] }));
      const user = await openPanel(addBatch);
      await user.click(screen.getByTestId("plan-more-similarity-any"));
      await user.click(screen.getByTestId("plan-more-submit"));
      await waitFor(() => expect(addBatch).toHaveBeenCalledTimes(1));
      expect(sentOptions(addBatch)).toMatchObject({
        similarity: "any",
        referenceCardId: null,
      });
    });

    it("replaces the dish with a wait, so the click visibly did something", async () => {
      let finish!: (value: unknown) => void;
      const addBatch = vi.fn(
        () => new Promise((resolve) => (finish = resolve)),
      );
      const user = await openPanel(addBatch);
      await user.click(screen.getByTestId("plan-more-submit"));

      expect(screen.queryByTestId("plan-card-name")).toBeNull();
      expect(screen.getByText(/Procurando pratos/)).toBeInTheDocument();

      finish(plan({ queue: [card("b")] }));
      await waitFor(() =>
        expect(screen.getByTestId("plan-card-name")).toHaveTextContent(
          "Prato b",
        ),
      );
    });

    it("does not lose a request made during an automatic batch", async () => {
      const calls: unknown[] = [];
      let finishAutomatic!: (value: unknown) => void;
      const addBatch = vi.fn((_id: string, options: unknown) => {
        calls.push(options);
        return calls.length === 1
          ? new Promise((resolve) => (finishAutomatic = resolve))
          : Promise.resolve(plan({ queue: [card("c")] }));
      });
      const user = userEvent.setup();
      renderAt(
        repository({
          get: vi.fn(async () =>
            plan({ needsBatch: true, queue: [card("a")] }),
          ),
          addBatch,
        } as never),
        "/app/compras/planejar/s1",
      );
      await user.click(await screen.findByTestId("plan-more"));
      await user.click(screen.getByTestId("plan-more-submit"));
      expect(addBatch).toHaveBeenCalledTimes(1);

      finishAutomatic(plan({ needsBatch: false, queue: [card("b")] }));

      await waitFor(() => expect(addBatch).toHaveBeenCalledTimes(2));
      expect(calls[1]).toMatchObject({ pantry: "free" });
      await waitFor(() =>
        expect(screen.getByTestId("plan-card-name")).toHaveTextContent(
          "Prato c",
        ),
      );
    });

    it("says so when there is nothing new, and brings the dish back", async () => {
      const addBatch = vi.fn(async () => {
        throw new RecipeApiError("no_more_options", "x", 422);
      });
      const user = await openPanel(addBatch);
      await user.click(screen.getByTestId("plan-more-submit"));

      expect(await screen.findByTestId("plan-ai-notice")).toHaveTextContent(
        /outras opções novas/,
      );
      expect(screen.getByTestId("plan-card-name")).toHaveTextContent("Prato a");
    });

    it("keeps the filters, using the same answers", async () => {
      const addBatch = vi.fn(async () => plan({ queue: [card("b")] }));
      const user = await openPanel(addBatch);
      await user.click(screen.getByTestId("plan-more-pantry-use"));
      await user.click(screen.getByTestId("plan-more-pasta"));
      await waitFor(() => expect(addBatch).toHaveBeenCalledTimes(1));
      expect(sentOptions(addBatch)).toMatchObject({
        dishFamilies: ["pasta"],
        pantry: "use",
      });
    });
  });

  it("shows why a card is flagged", async () => {
    const flagged = card("a", {
      overBudget: true,
      reasonCode: "uses_attention_item",
      reasonIngredient: "Iogurte",
      guardCode: "peanut_allergy",
      guardIngredient: "Tempero",
    });
    renderAt(
      repository({ get: vi.fn(async () => plan({ queue: [flagged] })) }),
      "/app/compras/planejar/s1",
    );

    expect(
      await screen.findByTestId("plan-card-over-budget"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("plan-card-reason")).toHaveTextContent("Iogurte");
    expect(screen.getByTestId("plan-card-guard")).toHaveTextContent("Tempero");
    expect(screen.getByTestId("plan-card-guard")).toHaveTextContent(
      "Alergia a amendoim",
    );
  });

  it("goes to review when every meal is filled", async () => {
    renderAt(
      repository({
        get: vi.fn(async () =>
          plan({
            progress: {
              mealsTotal: 2,
              mealsFilled: 2,
              listItemCount: 3,
              sessionMinutesUsed: 0,
              sessionMinutesBudget: null,
            },
            queue: [],
          }),
        ),
      }),
      "/app/compras/planejar/s1",
    );

    expect(await screen.findByText("revisão")).toBeInTheDocument();
  });
});

describe("expansion queue (PLAN-0062)", () => {
  function Harness({
    value,
    repo,
    onPlan,
  }: {
    value: ShoppingPlan;
    repo: ShoppingPlanRepository;
    onPlan: (next: ShoppingPlan) => void;
  }) {
    const stopped = useExpansionQueue(
      value,
      repo,
      { csrfToken: "c", language: "pt-BR" },
      onPlan,
    );
    return <p data-testid="stopped">{stopped ?? "running"}</p>;
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
