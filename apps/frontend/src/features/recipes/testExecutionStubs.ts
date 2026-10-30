import {
  RecipeApiError,
  type RecipeDetail,
  type RecipeRepository,
  type RecipeSummary,
} from "@/contracts/recipes";

const unused = async (): Promise<never> => {
  throw new RecipeApiError("unavailable", "Not used in this test.", 503);
};

/** Test-only cooking and library methods for recipe repository fakes that do not exercise them. */
export const executionStubs: Pick<
  RecipeRepository,
  | "startExecution"
  | "getExecution"
  | "listExecutionsInProgress"
  | "completeExecution"
  | "abandonExecution"
  | "getRecipeRevision"
  | "getRecipeHistory"
  | "createRecipe"
  | "reviseRecipe"
  | "deriveRecipe"
  | "setFavorite"
  | "setArchived"
  | "saveCandidate"
  | "listSavedCandidates"
  | "deleteSavedCandidate"
  | "cookSavedCandidate"
> = {
  startExecution: unused,
  getExecution: unused,
  listExecutionsInProgress: async () => [],
  completeExecution: unused,
  abandonExecution: unused,
  getRecipeRevision: unused,
  getRecipeHistory: async () => ({ revisions: [], cooking: [] }),
  createRecipe: unused,
  reviseRecipe: unused,
  deriveRecipe: unused,
  setFavorite: unused,
  setArchived: unused,
  saveCandidate: unused,
  listSavedCandidates: async () => [],
  deleteSavedCandidate: unused,
  cookSavedCandidate: unused,
};

/** Completes a test recipe detail with the library defaults of a generated, unorganized recipe. */
export function libraryDetail(
  base: Pick<
    RecipeDetail,
    | "recipeId"
    | "revisionNumber"
    | "name"
    | "mealTypes"
    | "servings"
    | "normalizedRecipeJson"
    | "thumbnailVisualJson"
    | "createdAt"
  > &
    Partial<RecipeDetail>,
): RecipeDetail {
  return {
    currentRevisionNumber: base.revisionNumber,
    updatedAt: base.createdAt,
    origin: "generated",
    isFavorite: false,
    archivedAt: null,
    parentRecipeId: null,
    parentRevisionNumber: null,
    derivationReason: null,
    revisionOrigin: "generated",
    changeReason: null,
    ...base,
  };
}

/** Completes a test library row with the defaults of a generated recipe never cooked. */
export function librarySummary(
  base: Pick<
    RecipeSummary,
    "recipeId" | "name" | "mealTypes" | "servings" | "createdAt"
  > &
    Partial<RecipeSummary>,
): RecipeSummary {
  return {
    revisionNumber: 1,
    origin: "generated",
    isFavorite: false,
    archivedAt: null,
    updatedAt: base.createdAt,
    timesCooked: 0,
    lastCookedAt: null,
    lastRating: null,
    ...base,
  };
}
