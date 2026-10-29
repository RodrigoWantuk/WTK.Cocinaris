import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import {
  libraryCatalogs,
  REQUIRED_LIBRARY_I18N_KEYS,
} from "@/app/i18n/libraryCatalog";
import { SessionProvider } from "@/app/session/SessionProvider";
import { RecipeApiError, type RecipeRepository } from "@/contracts/recipes";
import { createSessionAdapter } from "@/features/inventory/testUtils";
import { RecipeDetailPage } from "./RecipeDetailPage";
import { RecipeEditorPage } from "./RecipeEditorPage";
import { RecipeProvider } from "./RecipeProvider";
import { RecipesListPage } from "./RecipesListPage";
import {
  executionStubs,
  libraryDetail,
  librarySummary,
} from "./testExecutionStubs";

const CONTENT = JSON.stringify({
  name: "Arroz de forno",
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
  ],
  equipment: [{ equipmentId: "eq-1", name: "Travessa", capabilities: [] }],
  stages: [
    {
      stageId: "stage-1",
      name: "Assar",
      instructions: "Leve ao forno.\nSirva quente.",
      activeMinutes: 5,
      passiveMinutes: 30,
      dependsOn: [],
      sensoryCues: ["dourado"],
      usesIngredientIds: ["ing-rice"],
    },
  ],
});

const DETAIL = libraryDetail({
  recipeId: "recipe-1",
  revisionNumber: 2,
  currentRevisionNumber: 2,
  name: "Arroz de forno",
  mealTypes: ["lunch"],
  servings: 2,
  normalizedRecipeJson: CONTENT,
  thumbnailVisualJson: "{}",
  createdAt: "2026-10-01T12:00:00Z",
  revisionOrigin: "edited",
});

function repoWith(overrides: Partial<RecipeRepository>): RecipeRepository {
  return {
    ...executionStubs,
    listRecipes: async () => [],
    getRecipe: async () => DETAIL,
    requestCandidates: vi.fn(),
    getGenerationSession: vi.fn(),
    selectCandidate: vi.fn(),
    ...overrides,
  } as RecipeRepository;
}

function renderAt(repo: RecipeRepository, path: string) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={createSessionAdapter()}>
        <RecipeProvider repository={repo}>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/app/receitas" element={<RecipesListPage />} />
              <Route
                path="/app/receitas/:recipeId"
                element={<RecipeDetailPage />}
              />
              <Route
                path="/app/receitas/:recipeId/versoes/:revisionNumber"
                element={<RecipeDetailPage />}
              />
              <Route
                path="/app/receitas/:recipeId/editar"
                element={<RecipeEditorPage mode="edit" />}
              />
              <Route
                path="/app/receitas/:recipeId/variacao"
                element={<RecipeEditorPage mode="variation" />}
              />
            </Routes>
          </MemoryRouter>
        </RecipeProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("recipe library (PLAN-0038)", () => {
  it("has every library key in every locale", () => {
    for (const locale of ["pt-BR", "en", "es"] as const) {
      const catalog = libraryCatalogs[locale];
      for (const key of REQUIRED_LIBRARY_I18N_KEYS) {
        expect(catalog[key], `${locale} ${key}`).toBeTruthy();
      }
    }
  });

  it("filters on the server and toggles favorites", async () => {
    const user = userEvent.setup();
    const row = librarySummary({
      recipeId: "recipe-1",
      name: "Arroz de forno",
      mealTypes: ["lunch"],
      servings: 2,
      createdAt: "2026-10-01T12:00:00Z",
      timesCooked: 3,
      lastRating: 4,
    });
    const listRecipes = vi.fn(async () => [row]);
    const setFavorite = vi.fn(async () => ({ ...DETAIL, isFavorite: true }));
    renderAt(repoWith({ listRecipes, setFavorite }), "/app/receitas");

    const item = await screen.findByTestId("recipes-item-recipe-1");
    expect(item).toHaveTextContent("Lunch");
    expect(item).toHaveTextContent("Cooked 3× · rated 4");

    await user.click(screen.getByTestId("recipes-favorite-recipe-1"));
    expect(setFavorite).toHaveBeenCalledWith("recipe-1", true, {
      csrfToken: expect.any(String),
    });
    await waitFor(() =>
      expect(screen.getByTestId("recipes-favorite-recipe-1")).toHaveAttribute(
        "aria-pressed",
        "true",
      ),
    );

    await user.click(screen.getByTestId("recipes-filter-archived"));
    await waitFor(() =>
      expect(listRecipes).toHaveBeenLastCalledWith(expect.anything(), {
        archived: true,
        favorites: false,
        search: undefined,
      }),
    );
  });

  it("saves an edit as the next revision, keeping row identifiers", async () => {
    const user = userEvent.setup();
    const reviseRecipe = vi.fn(async () => ({
      ...DETAIL,
      revisionNumber: 3,
      currentRevisionNumber: 3,
    }));
    renderAt(repoWith({ reviseRecipe }), "/app/receitas/recipe-1/editar");

    const ingredient = await screen.findByTestId("recipe-editor-ingredient");
    expect(within(ingredient).getByText(/From the pantry/)).toBeInTheDocument();
    const quantity = within(ingredient).getByLabelText("Quantity");
    await user.clear(quantity);
    await user.type(quantity, "250");
    await user.type(screen.getByTestId("recipe-editor-reason"), "More rice");
    await user.click(screen.getByTestId("recipe-editor-save"));

    await waitFor(() => expect(reviseRecipe).toHaveBeenCalledTimes(1));
    const [recipeId, input] = reviseRecipe.mock.calls[0] as unknown as [
      string,
      Parameters<RecipeRepository["reviseRecipe"]>[1],
    ];
    expect(recipeId).toBe("recipe-1");
    expect(input.expectedRevisionNumber).toBe(2);
    expect(input.changeReason).toBe("More rice");
    expect(input.content.ingredients[0]).toMatchObject({
      ingredientId: "ing-rice",
      quantity: 250,
      unit: "g",
    });
    expect(input.content.stages[0]).toMatchObject({
      stageId: "stage-1",
      instructions: "Leve ao forno.\nSirva quente.",
      sensoryCues: ["dourado"],
    });
    expect(input.content.equipment).toEqual(["Travessa"]);
    expect(await screen.findByTestId("recipes-detail")).toBeInTheDocument();
  });

  it("accepts a kitchen measure and saves it in grams, showing both", async () => {
    const user = userEvent.setup();
    const reviseRecipe = vi.fn(async () => ({
      ...DETAIL,
      revisionNumber: 3,
      currentRevisionNumber: 3,
    }));
    renderAt(repoWith({ reviseRecipe }), "/app/receitas/recipe-1/editar");

    const ingredient = await screen.findByTestId("recipe-editor-ingredient");
    const quantity = within(ingredient).getByLabelText("Quantity");
    await user.clear(quantity);
    await user.type(quantity, "250");
    expect(screen.getByTestId("editor-measure-hint")).toHaveTextContent(
      "≈ 1 ⅓ cups",
    );
    await user.selectOptions(within(ingredient).getByLabelText("Unit"), "cup");
    await user.clear(quantity);
    await user.type(quantity, "1");
    expect(screen.getByTestId("editor-measure-hint")).toHaveTextContent(
      "Saved as ≈ 185 g",
    );
    await user.type(screen.getByTestId("recipe-editor-reason"), "Cups");
    await user.click(screen.getByTestId("recipe-editor-save"));

    await waitFor(() => expect(reviseRecipe).toHaveBeenCalledTimes(1));
    const [, input] = reviseRecipe.mock.calls[0] as unknown as [
      string,
      Parameters<RecipeRepository["reviseRecipe"]>[1],
    ];
    expect(input.content.ingredients[0]).toMatchObject({
      quantity: 185,
      unit: "g",
    });
  });

  it("reports a stale edit instead of overwriting the newer revision", async () => {
    const user = userEvent.setup();
    const reviseRecipe = vi.fn(async () => {
      throw new RecipeApiError("precondition_failed", "stale", 412);
    });
    renderAt(repoWith({ reviseRecipe }), "/app/receitas/recipe-1/editar");

    await user.click(await screen.findByTestId("recipe-editor-save"));

    expect(
      await screen.findByTestId("recipe-editor-stale"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("recipe-editor-save")).toBeDisabled();
  });

  it("requires a reason before creating a variation", async () => {
    const user = userEvent.setup();
    const deriveRecipe = vi.fn();
    renderAt(repoWith({ deriveRecipe }), "/app/receitas/recipe-1/variacao");

    await user.click(await screen.findByTestId("recipe-editor-save"));

    expect(deriveRecipe).not.toHaveBeenCalled();
    expect(
      screen.getAllByTestId("recipe-editor-field-error")[0],
    ).toHaveTextContent("Fill in this field.");
  });

  it("shows an earlier revision read-only with the history", async () => {
    const getRecipeRevision = vi.fn(async () => ({
      ...DETAIL,
      revisionNumber: 1,
      name: "Arroz simples",
      revisionOrigin: "generated",
    }));
    const getRecipeHistory = vi.fn(async () => ({
      revisions: [
        {
          revisionNumber: 2,
          name: "Arroz de forno",
          origin: "edited",
          changeReason: "More rice",
          sourceExecutionId: "exec-1",
          createdAt: "2026-10-02T12:00:00Z",
        },
        {
          revisionNumber: 1,
          name: "Arroz simples",
          origin: "generated",
          changeReason: null,
          sourceExecutionId: null,
          createdAt: "2026-10-01T12:00:00Z",
        },
      ],
      cooking: [
        {
          executionId: "exec-1",
          revisionNumber: 1,
          servings: 2,
          status: "Completed",
          startedAt: "2026-10-01T18:00:00Z",
          finishedAt: "2026-10-01T19:00:00Z",
          rating: 4,
          notes: "Pouco sal",
        },
      ],
    }));
    renderAt(
      repoWith({ getRecipeRevision, getRecipeHistory }),
      "/app/receitas/recipe-1/versoes/1",
    );

    expect(
      await screen.findByTestId("recipes-detail-old-revision"),
    ).toHaveTextContent("You are viewing version 1. The current version is 2.");
    expect(screen.queryByTestId("recipes-detail-cook")).not.toBeInTheDocument();
    expect(getRecipeRevision).toHaveBeenCalledWith(
      "recipe-1",
      1,
      expect.anything(),
    );
    const revisions = await screen.findAllByTestId("recipes-history-revision");
    expect(revisions[0]).toHaveTextContent("edited after cooking");
    expect(revisions[0]).toHaveTextContent("More rice");
    expect(screen.getByTestId("recipes-history-cooking")).toHaveTextContent(
      "rated 4 of 5",
    );
  });
});
