import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { RecipeGeneratePage } from "./RecipeGeneratePage";
import { RecipesListPage } from "./RecipesListPage";
import { RecipeDetailPage } from "./RecipeDetailPage";
import { RecipeProvider } from "./RecipeProvider";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import {
  REQUIRED_RECIPES_I18N_KEYS,
  recipesCatalogs,
} from "@/app/i18n/recipesCatalog";
import type { SessionAdapter, SessionState } from "@/app/session/types";
import type { InventoryRepository } from "@/adapters/live/inventoryTypes";
import type {
  RecipeCandidate,
  RecipeDetail,
  RecipeRepository,
  RecipeSummary,
} from "@/contracts/recipes";
import { RecipeApiError } from "@/contracts/recipes";
import { PRODUCTION_LOCALES } from "@/app/i18n/productionCatalog";

const CANDIDATES: RecipeCandidate[] = [
  {
    candidateId: "cand-a",
    candidateStrategy: "use_on_hand",
    name: "Tomato Skillet",
    targetMealType: "dinner",
    dishFormat: "skillet",
    primaryTechnique: "saute",
    primaryIngredientRefs: ["lot-1"],
    summary: "Quick tomato skillet",
    servings: 2,
    activeMinutes: 15,
    passiveMinutes: 5,
    totalMinutes: 20,
    difficulty: "easy",
    requiredEquipmentIds: [],
    requiredCapabilities: [],
  },
  {
    candidateId: "cand-b",
    candidateStrategy: "expiry_priority",
    name: "Herb Pasta",
    targetMealType: "dinner",
    dishFormat: "pasta",
    primaryTechnique: "boil",
    primaryIngredientRefs: ["lot-2"],
    summary: "Pasta with herbs",
    servings: 2,
    activeMinutes: 20,
    passiveMinutes: 0,
    totalMinutes: 20,
    difficulty: "easy",
    requiredEquipmentIds: [],
    requiredCapabilities: [],
  },
  {
    candidateId: "cand-c",
    candidateStrategy: "minimal_additional",
    name: "Bean Bowl",
    targetMealType: "lunch",
    dishFormat: "bowl",
    primaryTechnique: "assemble",
    primaryIngredientRefs: ["lot-3"],
    summary: "Bean bowl",
    servings: 1,
    activeMinutes: 10,
    passiveMinutes: 0,
    totalMinutes: 10,
    difficulty: "easy",
    requiredEquipmentIds: [],
    requiredCapabilities: [],
  },
];

function authenticatedSession(): SessionState {
  return {
    status: "authenticated",
    displayName: "Ana",
    csrfToken: "csrf-test",
    timeZone: "America/Sao_Paulo",
  };
}

function sessionAdapter(
  state: SessionState = authenticatedSession(),
): SessionAdapter {
  return {
    getSession: async () => state,
    beginLogin: () => undefined,
    logout: async () => undefined,
  };
}

function inventoryWithLots(count: number): InventoryRepository {
  return {
    listLots: async () => ({
      items: Array.from({ length: count }, (_, i) => ({
        lotId: `lot-${i}`,
        productId: `p-${i}`,
        productName: `Product ${i}`,
        quantity: {
          kind: "measured" as const,
          value: 1,
          unit: "Unit" as const,
        },
        storageLocation: "Pantry" as const,
        customLocation: null,
        packageState: null,
        printedExpirationDate: null,
        notes: null,
        version: "v1",
        etag: "v1",
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      })),
      nextCursor: null,
    }),
    getLot: async () => {
      throw new Error("unused");
    },
    createLot: async () => {
      throw new Error("unused");
    },
    updateLot: async () => {
      throw new Error("unused");
    },
    adjustLot: async () => {
      throw new Error("unused");
    },
    deleteLot: async () => undefined,
    getHistory: async () => [],
  };
}

function createRecipeRepo(
  overrides: Partial<RecipeRepository> = {},
): RecipeRepository {
  const detail: RecipeDetail = {
    recipeId: "recipe-1",
    revisionNumber: 1,
    name: "Tomato Skillet",
    mealTypes: ["dinner"],
    servings: 2,
    normalizedRecipeJson: JSON.stringify({
      name: "Tomato Skillet",
      mealTypes: ["dinner"],
      servings: 2,
      ingredients: [{ name: "Tomato", quantity: { value: 200, unit: "g" } }],
      stages: [{ title: "Cook", steps: ["Saute"] }],
    }),
    thumbnailVisualJson: "{}",
    createdAt: new Date().toISOString(),
  };
  const summaries: RecipeSummary[] = [
    {
      recipeId: "recipe-1",
      name: "Tomato Skillet",
      mealTypes: ["dinner"],
      servings: 2,
      createdAt: new Date().toISOString(),
    },
  ];

  return {
    listRecipes: async () => summaries,
    getRecipe: async () => detail,
    requestCandidates: async () => ({
      sessionId: "session-1",
      status: "CandidatesReady",
      candidates: CANDIDATES,
      failureReason: null,
    }),
    selectCandidate: async () => detail,
    getGenerationSession: async () => ({
      sessionId: "session-1",
      status: "CandidatesReady",
      candidates: CANDIDATES,
      failureReason: null,
    }),
    ...overrides,
  };
}

function renderGenerate(recipeRepo: RecipeRepository, lotCount = 1) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={sessionAdapter()}>
        <InventoryProvider repository={inventoryWithLots(lotCount)}>
          <RecipeProvider repository={recipeRepo}>
            <MemoryRouter initialEntries={["/app/receitas/gerar"]}>
              <Routes>
                <Route
                  path="/app/receitas/gerar"
                  element={<RecipeGeneratePage />}
                />
                <Route
                  path="/app/receitas/:recipeId"
                  element={
                    <div data-testid="saved-recipe-destination">saved</div>
                  }
                />
              </Routes>
            </MemoryRouter>
          </RecipeProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("production recipes vertical slice", () => {
  it("lists recipes and links generate/detail", async () => {
    const user = userEvent.setup();
    render(
      <ProductionI18nProvider initialLocale="en">
        <SessionProvider adapter={sessionAdapter()}>
          <InventoryProvider repository={inventoryWithLots(1)}>
            <RecipeProvider repository={createRecipeRepo()}>
              <MemoryRouter initialEntries={["/app/receitas"]}>
                <Routes>
                  <Route path="/app/receitas" element={<RecipesListPage />} />
                  <Route
                    path="/app/receitas/gerar"
                    element={
                      <div data-testid="generate-destination">generate</div>
                    }
                  />
                  <Route
                    path="/app/receitas/:recipeId"
                    element={<div data-testid="detail-destination">detail</div>}
                  />
                </Routes>
              </MemoryRouter>
            </RecipeProvider>
          </InventoryProvider>
        </SessionProvider>
      </ProductionI18nProvider>,
    );

    expect(await screen.findByTestId("recipes-list")).toBeInTheDocument();
    expect(screen.getByText("Tomato Skillet")).toBeInTheDocument();
    await user.click(screen.getByTestId("recipes-generate-link"));
    expect(
      await screen.findByTestId("generate-destination"),
    ).toBeInTheDocument();
  });

  it("renders exactly three candidates and navigates after selection", async () => {
    const user = userEvent.setup();
    const selectCandidate = jest.fn(async () => ({
      recipeId: "recipe-1",
      revisionNumber: 1,
      name: "Tomato Skillet",
      mealTypes: ["dinner"],
      servings: 2,
      normalizedRecipeJson: "{}",
      thumbnailVisualJson: "{}",
      createdAt: new Date().toISOString(),
    }));
    renderGenerate(
      createRecipeRepo({
        selectCandidate,
      }),
    );

    await user.click(await screen.findByTestId("recipes-confirm-generate"));
    expect(
      await screen.findByTestId("recipes-candidate-list"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("recipes-candidate-cand-a")).toBeInTheDocument();
    expect(screen.getByTestId("recipes-candidate-cand-b")).toBeInTheDocument();
    expect(screen.getByTestId("recipes-candidate-cand-c")).toBeInTheDocument();

    await user.click(screen.getByTestId("recipes-select-cand-a"));
    expect(selectCandidate).toHaveBeenCalledWith(
      "session-1",
      "cand-a",
      expect.objectContaining({ csrfToken: "csrf-test" }),
    );
    expect(
      await screen.findByTestId("saved-recipe-destination"),
    ).toBeInTheDocument();
  });

  it("shows unavailable state for provider failures", async () => {
    const user = userEvent.setup();
    renderGenerate(
      createRecipeRepo({
        requestCandidates: async () => {
          throw new RecipeApiError(
            "ai_provider_unavailable",
            "Provider down",
            503,
          );
        },
      }),
    );

    await user.click(await screen.findByTestId("recipes-confirm-generate"));
    expect(
      await screen.findByTestId("feature-unavailable-recipes-generate"),
    ).toHaveTextContent(/Recipe AI is unavailable/i);
  });

  it("shows invalid-output state without silent mock substitution", async () => {
    const user = userEvent.setup();
    renderGenerate(
      createRecipeRepo({
        requestCandidates: async () => {
          throw new RecipeApiError("ai_output_invalid", "Bad output", 422);
        },
      }),
    );

    await user.click(await screen.findByTestId("recipes-confirm-generate"));
    expect(
      await screen.findByTestId("feature-unavailable-recipes-generate"),
    ).toHaveTextContent(/could not be validated/i);
    expect(screen.queryByTestId(/recipes-candidate-/)).not.toBeInTheDocument();
  });

  it("opens a saved recipe detail", async () => {
    render(
      <ProductionI18nProvider initialLocale="en">
        <SessionProvider adapter={sessionAdapter()}>
          <InventoryProvider repository={inventoryWithLots(1)}>
            <RecipeProvider repository={createRecipeRepo()}>
              <MemoryRouter initialEntries={["/app/receitas/recipe-1"]}>
                <Routes>
                  <Route
                    path="/app/receitas/:recipeId"
                    element={<RecipeDetailPage />}
                  />
                </Routes>
              </MemoryRouter>
            </RecipeProvider>
          </InventoryProvider>
        </SessionProvider>
      </ProductionI18nProvider>,
    );

    expect(await screen.findByTestId("recipes-detail")).toBeInTheDocument();
    expect(screen.getByText("Tomato Skillet")).toBeInTheDocument();
  });

  it("has complete en/pt-BR/es recipe catalogs", () => {
    for (const locale of PRODUCTION_LOCALES) {
      const catalog = recipesCatalogs[locale];
      for (const key of REQUIRED_RECIPES_I18N_KEYS) {
        expect(catalog[key]?.length).toBeGreaterThan(0);
      }
    }
  });
});
