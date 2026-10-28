import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { RuntimeProvider } from "@/app/runtime/RuntimeProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { createMockSessionAdapter } from "@/app/session/mockSessionAdapter";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import { RecipeProvider } from "./RecipeProvider";
import { RecipeGeneratePage } from "./RecipeGeneratePage";
import { RecipeDetailPage } from "./RecipeDetailPage";
import type { FrontendRuntime } from "@/app/runtime/types";
import type { RecipeRepository } from "@/contracts/recipes";
import type { InventoryRepository } from "@/adapters/live/inventoryTypes";
import { UnavailablePreparationRouteRepository } from "@/adapters/live/unavailablePreparationRouteRepository";
import { createUnavailableContextualHomeAdapter } from "@/adapters/live/unavailableContextualHomeAdapter";
import { createUnavailableAdultDeclarationPolicy } from "@/features/profile/adultDeclarationPolicy";
import { RecipeApiError } from "@/contracts/recipes";
import ProductionApp from "@/app/ProductionApp";
import { PRODUCTION_LOCALE_STORAGE_KEY } from "@/app/i18n/productionCatalog";

const candidate = (id: string, name: string) => ({
  candidateId: id,
  candidateStrategy: "prefer_inventory",
  name,
  targetMealType: "dinner",
  dishFormat: "plate",
  primaryTechnique: "saute",
  primaryIngredientRefs: ["egg"],
  summary: `${name} summary`,
  servings: 2,
  activeMinutes: 10,
  passiveMinutes: 5,
  totalMinutes: 15,
  difficulty: "easy",
  requiredEquipmentIds: [] as string[],
  requiredCapabilities: [] as string[],
});

function createInventoryStub(
  overrides?: Partial<InventoryRepository>,
): InventoryRepository {
  return {
    listLots: async () => ({
      items: [
        {
          lotId: "lot-1",
          productId: "p-1",
          productName: "Eggs",
          quantity: { kind: "measured", value: 6, unit: "Unit" },
          storageLocation: "Refrigerator",
          customLocation: null,
          packageState: null,
          printedExpirationDate: null,
          notes: null,
          version: "v1",
          etag: "v1",
          createdAt: "2026-08-05T00:00:00Z",
          updatedAt: "2026-08-05T00:00:00Z",
        },
      ],
      nextCursor: null,
    }),
    getLot: async () => {
      throw new Error("not used");
    },
    createLot: async () => {
      throw new Error("not used");
    },
    updateLot: async () => {
      throw new Error("not used");
    },
    adjustLot: async () => {
      throw new Error("not used");
    },
    deleteLot: async () => {
      throw new Error("not used");
    },
    getHistory: async () => [],
    ...overrides,
  };
}

function createAuthedSessionAdapter() {
  const sessionAdapter = createMockSessionAdapter({
    initiallyAuthenticated: true,
    displayName: "Ada",
    timeZone: "UTC",
  });
  const originalGet = sessionAdapter.getSession.bind(sessionAdapter);
  sessionAdapter.getSession = async () => {
    const session = await originalGet();
    return { ...session, csrfToken: "csrf-test", status: "authenticated" };
  };
  sessionAdapter.beginLogin();
  return sessionAdapter;
}

function renderGenerateTree(
  recipeRepo: RecipeRepository,
  inventoryRepo: InventoryRepository,
  routes: ReactNode,
) {
  const sessionAdapter = createAuthedSessionAdapter();
  const runtime: FrontendRuntime = {
    mode: "test",
    sessionAdapter,
    inventoryRepository: inventoryRepo,
    recipeRepository: recipeRepo,
    preparationRouteRepository: new UnavailablePreparationRouteRepository(),
    contextualHomeAdapter: createUnavailableContextualHomeAdapter(),
    profileRepository: {} as FrontendRuntime["profileRepository"],
    adultDeclarationPolicy: createUnavailableAdultDeclarationPolicy(),
    enableScenarioBar: false,
    enablePrototypeFixtures: false,
    persistPrototypeAuth: false,
    shoppingRequirementProjections: [],
  };

  return render(
    <RuntimeProvider runtime={runtime}>
      <SessionProvider adapter={sessionAdapter}>
        <ProductionI18nProvider initialLocale="en">
          <InventoryProvider repository={inventoryRepo}>
            <RecipeProvider repository={recipeRepo}>
              <MemoryRouter initialEntries={["/app/receitas/gerar"]}>
                {routes}
              </MemoryRouter>
            </RecipeProvider>
          </InventoryProvider>
        </ProductionI18nProvider>
      </SessionProvider>
    </RuntimeProvider>,
  );
}

describe("Recipes generate flow", () => {
  it("renders three candidates and navigates after selection", async () => {
    const user = userEvent.setup();
    const selectCandidate = jest.fn(async () => ({
      recipeId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
      revisionNumber: 1,
      name: "Tomato Pasta",
      mealTypes: ["dinner"],
      servings: 2,
      normalizedRecipeJson: "{}",
      thumbnailVisualJson: "{}",
      createdAt: "2026-08-05T12:00:00Z",
    }));

    const recipeRepo: RecipeRepository = {
      listRecipes: async () => [],
      getRecipe: async (recipeId) => ({
        recipeId,
        revisionNumber: 1,
        name: "Tomato Pasta",
        mealTypes: ["dinner"],
        servings: 2,
        normalizedRecipeJson: "{}",
        thumbnailVisualJson: "{}",
        createdAt: "2026-08-05T12:00:00Z",
      }),
      requestCandidates: async () => ({
        sessionId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        status: "CandidatesReady",
        candidates: [
          candidate("c1", "Tomato Pasta"),
          candidate("c2", "Egg Fried Rice"),
          candidate("c3", "Bean Stew"),
        ],
        failureReason: null,
      }),
      getGenerationSession: async () => {
        throw new Error("not used");
      },
      selectCandidate,
    };

    renderGenerateTree(
      recipeRepo,
      createInventoryStub(),
      <Routes>
        <Route path="/app/receitas/gerar" element={<RecipeGeneratePage />} />
        <Route path="/app/receitas/:recipeId" element={<RecipeDetailPage />} />
      </Routes>,
    );

    expect(
      await screen.findByTestId("recipes-generate-confirm"),
    ).toBeInTheDocument();
    await user.click(screen.getByTestId("recipes-confirm-generate"));

    const list = await screen.findByTestId("recipes-candidate-list");
    expect(within(list).getAllByRole("article")).toHaveLength(3);
    expect(screen.getByTestId("recipes-candidate-c1")).toHaveTextContent(
      "Tomato Pasta",
    );
    expect(screen.getByTestId("recipes-candidate-c2")).toHaveTextContent(
      "Egg Fried Rice",
    );
    expect(screen.getByTestId("recipes-candidate-c3")).toHaveTextContent(
      "Bean Stew",
    );

    await user.click(screen.getByTestId("recipes-select-c1"));
    await waitFor(() =>
      expect(selectCandidate).toHaveBeenCalledWith(
        "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        "c1",
        expect.objectContaining({ csrfToken: "csrf-test" }),
      ),
    );
    expect(await screen.findByTestId("recipes-detail")).toHaveTextContent(
      "Tomato Pasta",
    );
  });

  it("shows provider unavailable and allows retry", async () => {
    const user = userEvent.setup();
    let failOnce = true;
    const requestCandidates = jest.fn(async () => {
      if (failOnce) {
        failOnce = false;
        throw new RecipeApiError("ai_provider_unavailable", "down", 503);
      }
      return {
        sessionId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        status: "CandidatesReady",
        candidates: [
          candidate("c1", "A"),
          candidate("c2", "B"),
          candidate("c3", "C"),
        ],
        failureReason: null,
      };
    });

    const recipeRepo: RecipeRepository = {
      listRecipes: async () => [],
      getRecipe: async () => {
        throw new Error("not used");
      },
      requestCandidates,
      getGenerationSession: async () => {
        throw new Error("not used");
      },
      selectCandidate: async () => {
        throw new Error("not used");
      },
    };

    renderGenerateTree(
      recipeRepo,
      createInventoryStub(),
      <Routes>
        <Route path="/app/receitas/gerar" element={<RecipeGeneratePage />} />
      </Routes>,
    );

    await user.click(await screen.findByTestId("recipes-confirm-generate"));
    expect(
      await screen.findByTestId("recipes-generate-error"),
    ).toHaveTextContent(/unavailable|indispon/i);
    await user.click(screen.getByTestId("recipes-generate-retry"));
    await user.click(await screen.findByTestId("recipes-confirm-generate"));
    expect(
      await screen.findByTestId("recipes-candidate-list"),
    ).toBeInTheDocument();
  });

  it("shows invalid-output state", async () => {
    const user = userEvent.setup();
    const recipeRepo: RecipeRepository = {
      listRecipes: async () => [],
      getRecipe: async () => {
        throw new Error("not used");
      },
      requestCandidates: async () => {
        throw new RecipeApiError("ai_output_invalid", "bad", 422);
      },
      getGenerationSession: async () => {
        throw new Error("not used");
      },
      selectCandidate: async () => {
        throw new Error("not used");
      },
    };

    renderGenerateTree(
      recipeRepo,
      createInventoryStub(),
      <Routes>
        <Route path="/app/receitas/gerar" element={<RecipeGeneratePage />} />
      </Routes>,
    );

    await user.click(await screen.findByTestId("recipes-confirm-generate"));
    expect(
      await screen.findByTestId("recipes-generate-error"),
    ).toHaveTextContent(/validated|validar|validarse/i);
  });

  it("guides when inventory is empty", async () => {
    const recipeRepo: RecipeRepository = {
      listRecipes: async () => [],
      getRecipe: async () => {
        throw new Error("not used");
      },
      requestCandidates: async () => {
        throw new Error("should not request");
      },
      getGenerationSession: async () => {
        throw new Error("not used");
      },
      selectCandidate: async () => {
        throw new Error("not used");
      },
    };

    renderGenerateTree(
      recipeRepo,
      createInventoryStub({
        listLots: async () => ({ items: [], nextCursor: null }),
      }),
      <Routes>
        <Route path="/app/receitas/gerar" element={<RecipeGeneratePage />} />
      </Routes>,
    );

    expect(
      await screen.findByTestId("recipes-generate-empty-inventory"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("recipes-open-inventory")).toBeInTheDocument();
  });
});

describe("Production recipes routes and nav", () => {
  beforeEach(() => {
    localStorage.removeItem(PRODUCTION_LOCALE_STORAGE_KEY);
    window.history.pushState({}, "", "/app/receitas");
    jest.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof Request
            ? input.url
            : String(input);
      if (url.includes("/api/v1/session")) {
        return new Response(
          JSON.stringify({
            userId: "11111111-1111-1111-1111-111111111111",
            csrfToken: "csrf-route",
            supportedLocales: ["en", "pt-BR", "es"],
            displayName: "Ada",
            language: "en",
            timeZone: "UTC",
            measurementSystem: "Metric",
            profileExists: true,
            profilePercentComplete: 50,
            adultDeclarationState: "Declared",
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        );
      }
      if (url.includes("/api/v1/recipes") && !url.includes("generation")) {
        return new Response(JSON.stringify([]), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      }
      if (url.includes("/api/v1/inventory/lots")) {
        return new Response(JSON.stringify({ items: [], nextCursor: null }), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      }
      return new Response(null, { status: 404 });
    });
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it("renders recipes list and primary nav recipes link", async () => {
    render(<ProductionApp />);
    expect(await screen.findByTestId("recipes-list")).toBeInTheDocument();
    expect(screen.getByTestId("production-nav-receitas")).toBeInTheDocument();
    expect(
      screen.queryByTestId("feature-unavailable-app"),
    ).not.toBeInTheDocument();
  });

  it("does not treat gerar as a recipe id", async () => {
    window.history.pushState({}, "", "/app/receitas/gerar");
    render(<ProductionApp />);
    expect(
      await screen.findByTestId("recipes-generate-empty-inventory"),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("recipes-detail")).not.toBeInTheDocument();
  });
});
