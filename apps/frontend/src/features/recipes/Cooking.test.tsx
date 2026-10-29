import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import {
  RecipeApiError,
  type RecipeExecution,
  type RecipeRepository,
} from "@/contracts/recipes";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import {
  createMockInventoryRepo,
  createSessionAdapter,
  sampleLot,
} from "@/features/inventory/testUtils";
import { TodayPage } from "@/features/home/TodayPage";
import { CookingPage } from "./CookingPage";
import { parseNormalizedRecipeJson } from "./parseNormalizedRecipe";
import { RecipeProvider } from "./RecipeProvider";
import { executionStubs } from "./testExecutionStubs";

const RECIPE_JSON = JSON.stringify({
  recipe: {
    yield: "2 pratos",
    ingredients: [
      {
        ingredientId: "ing-rice",
        sourceType: "inventory",
        displayName: "Arroz",
        inventoryItemId: "lot-rice",
        requiredQuantity: 180,
        unit: "g",
        optional: false,
      },
      {
        ingredientId: "ing-salt",
        sourceType: "assumption",
        displayName: "Sal",
        requiredQuantity: 5,
        unit: "g",
        optional: false,
      },
    ],
    equipment: [{ equipmentId: "eq-1", name: "Panela", capabilities: [] }],
    stages: [
      {
        stageId: "stage-1",
        name: "Refogar",
        instructions:
          "Aqueça a panela em fogo médio.\nJunte o arroz e mexa por 2 minutos.",
        activeMinutes: 5,
        passiveMinutes: 0,
        dependsOn: [],
        sensoryCues: ["grãos translúcidos"],
        usesIngredientIds: ["ing-rice"],
      },
    ],
    assumptions: [],
  },
});

function execution(overrides: Partial<RecipeExecution> = {}): RecipeExecution {
  return {
    executionId: "exec-1",
    recipeId: "recipe-1",
    revisionNumber: 1,
    recipeName: "Arroz soltinho",
    servings: 2,
    recipeServings: 2,
    status: "InProgress",
    startedAt: "2026-10-29T10:00:00Z",
    finishedAt: null,
    inventoryBatchId: null,
    rating: null,
    notes: null,
    normalizedRecipeJson: RECIPE_JSON,
    proposal: {
      lines: [
        {
          ingredientId: "ing-rice",
          displayName: "Arroz",
          optional: false,
          requiredQuantity: 180,
          unit: "Gram",
          status: "ready",
          lotId: "lot-rice",
          proposedQuantity: 180,
        },
        {
          ingredientId: "ing-salt",
          displayName: "Sal",
          optional: false,
          requiredQuantity: 5,
          unit: "Gram",
          status: "not_tracked",
          lotId: null,
          proposedQuantity: null,
        },
      ],
      pantryLots: [
        {
          lotId: "lot-rice",
          productName: "Arroz",
          quantity: 500,
          unit: "Gram",
          storageLocation: "Pantry",
          version: "v-rice",
        },
      ],
    },
    ...overrides,
  };
}

function renderCooking(repo: RecipeRepository) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={createSessionAdapter()}>
        <RecipeProvider repository={repo}>
          <MemoryRouter initialEntries={["/app/receitas/cozinhar/exec-1"]}>
            <Routes>
              <Route
                path="/app/receitas/cozinhar/:executionId"
                element={<CookingPage />}
              />
            </Routes>
          </MemoryRouter>
        </RecipeProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

function repoWith(overrides: Partial<RecipeRepository>): RecipeRepository {
  return {
    ...executionStubs,
    listRecipes: async () => [],
    getRecipe: vi.fn(),
    requestCandidates: vi.fn(),
    getGenerationSession: vi.fn(),
    selectCandidate: vi.fn(),
    ...overrides,
  };
}

describe("protocol 0.3 recipe content", () => {
  it("reads stage instructions, quantities, cues, and equipment", () => {
    const parsed = parseNormalizedRecipeJson(RECIPE_JSON)!;
    expect(parsed.ingredients[0]).toMatchObject({
      displayName: "Arroz",
      quantity: 180,
      unit: "g",
      sourceType: "inventory",
    });
    expect(parsed.stages[0]).toMatchObject({
      title: "Refogar",
      instructions: [
        "Aqueça a panela em fogo médio.",
        "Junte o arroz e mexa por 2 minutos.",
      ],
      sensoryCues: ["grãos translúcidos"],
    });
    expect(parsed.equipment).toEqual(["Panela"]);
  });
});

describe("cooking a recipe", () => {
  beforeEach(() => localStorage.clear());

  it("shows the steps, then confirms only the reviewed consumption and the leftovers", async () => {
    const user = userEvent.setup();
    const completeExecution = vi.fn(async () =>
      execution({
        status: "Completed",
        proposal: null,
        inventoryBatchId: "batch-1",
      }),
    );
    renderCooking(
      repoWith({ getExecution: async () => execution(), completeExecution }),
    );

    expect(
      await screen.findByText("Junte o arroz e mexa por 2 minutos."),
    ).toBeInTheDocument();
    expect(screen.getByText(/grãos translúcidos/)).toBeInTheDocument();
    await user.click(screen.getByRole("checkbox", { name: "Refogar" }));
    expect(localStorage.getItem("cocinaris.cooking.exec-1")).toContain("s:0");

    await user.click(screen.getByTestId("cooking-finish"));
    const lines = screen.getAllByTestId("cooking-review-line");
    expect(lines).toHaveLength(2);
    expect(within(lines[1]!).getByRole("checkbox")).not.toBeChecked();
    const quantity = within(lines[0]!).getByRole("textbox");
    await user.clear(quantity);
    await user.type(quantity, "150");
    await user.click(screen.getByTestId("cooking-leftover-toggle"));
    await user.click(screen.getByTestId("cooking-confirm"));

    await waitFor(() => expect(completeExecution).toHaveBeenCalledTimes(1));
    expect(completeExecution).toHaveBeenCalledWith(
      "exec-1",
      {
        consumptions: [
          { lotId: "lot-rice", quantity: 150, unit: "Gram", version: "v-rice" },
        ],
        leftover: { portions: 1, storageLocation: "Refrigerator", useBy: null },
        rating: null,
        notes: null,
      },
      expect.objectContaining({ csrfToken: "csrf-test" }),
    );
    expect(await screen.findByTestId("cooking-finished")).toBeInTheDocument();
    expect(screen.getByText("1 pantry item(s) deducted.")).toBeInTheDocument();
    expect(localStorage.getItem("cocinaris.cooking.exec-1")).toBeNull();
  });

  it("refuses more than the lot holds and reloads the proposal when the pantry changed", async () => {
    const user = userEvent.setup();
    const getExecution = vi.fn(async () => execution());
    const completeExecution = vi.fn(async () => {
      throw new RecipeApiError("precondition_failed", "changed", 412);
    });
    renderCooking(repoWith({ getExecution, completeExecution }));

    await user.click(await screen.findByTestId("cooking-finish"));
    const quantity = within(
      screen.getAllByTestId("cooking-review-line")[0]!,
    ).getByRole("textbox");
    await user.clear(quantity);
    await user.type(quantity, "900");
    await user.click(screen.getByTestId("cooking-confirm"));
    expect(screen.getByRole("alert")).toHaveTextContent(
      "at most what the pantry has",
    );
    expect(completeExecution).not.toHaveBeenCalled();

    await user.clear(quantity);
    await user.type(quantity, "100");
    await user.click(screen.getByTestId("cooking-confirm"));
    expect(
      await screen.findByText(/The pantry changed while you were cooking/),
    ).toBeInTheDocument();
    expect(getExecution).toHaveBeenCalledTimes(2);
  });
});

describe("today page", () => {
  it("shows resume, attention, and recipes, and a failed source hides only its section", async () => {
    const inventory = createMockInventoryRepo({
      listAttention: vi.fn(async () => [
        {
          ...sampleLot,
          attention: {
            state: "UsePriority",
            estimatedUseBy: "2026-10-30",
            evidence: "PrintedDate",
            reasons: ["printed_date"],
          },
        },
      ]),
    });
    const recipes = repoWith({
      listExecutionsInProgress: async () => [
        {
          executionId: "exec-1",
          recipeId: "recipe-1",
          recipeName: "Arroz soltinho",
          servings: 2,
          startedAt: "2026-10-29T10:00:00Z",
        },
      ],
      listRecipes: async () => {
        throw new RecipeApiError("unavailable", "down", 503);
      },
    });
    render(
      <ProductionI18nProvider initialLocale="en">
        <SessionProvider adapter={createSessionAdapter()}>
          <InventoryProvider repository={inventory}>
            <RecipeProvider repository={recipes}>
              <MemoryRouter>
                <TodayPage />
              </MemoryRouter>
            </RecipeProvider>
          </InventoryProvider>
        </SessionProvider>
      </ProductionI18nProvider>,
    );

    expect(
      await screen.findByRole("link", { name: "Continue" }),
    ).toHaveAttribute("href", "/app/receitas/cozinhar/exec-1");
    expect(await screen.findByRole("link", { name: "Rice" })).toHaveAttribute(
      "href",
      `/app/despensa/${sampleLot.lotId}`,
    );
    expect(
      await screen.findByText("Your recipes could not be loaded right now."),
    ).toBeInTheDocument();
    expect(screen.getByTestId("today-generate")).toHaveAttribute(
      "href",
      "/app/receitas/gerar",
    );
  });
});
