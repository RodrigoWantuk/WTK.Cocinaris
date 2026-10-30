import { RecipeApiError, type RecipeRepository } from "@/contracts/recipes";

/**
 * Prototype stand-in that never serves mock recipe fixtures.
 * Production must wire {@link createLiveRecipeRepository} instead.
 */
export function createUnavailableRecipeRepository(): RecipeRepository {
  const fail = (): never => {
    throw new RecipeApiError(
      "ai_capability_unavailable",
      "Recipe cook-now is not available in this runtime.",
      503,
    );
  };

  return {
    listRecipes: async () => fail(),
    getRecipe: async () => fail(),
    getRecipeRevision: async () => fail(),
    getRecipeHistory: async () => fail(),
    createRecipe: async () => fail(),
    reviseRecipe: async () => fail(),
    deriveRecipe: async () => fail(),
    setFavorite: async () => fail(),
    setArchived: async () => fail(),
    requestCandidates: async () => fail(),
    saveCandidate: async () => fail(),
    listSavedCandidates: async () => [],
    deleteSavedCandidate: async () => fail(),
    cookSavedCandidate: async () => fail(),
    setExecutionAdjustments: async () => fail(),
    getGenerationSession: async () => fail(),
    selectCandidate: async () => fail(),
    startExecution: async () => fail(),
    getExecution: async () => fail(),
    listExecutionsInProgress: async () => fail(),
    completeExecution: async () => fail(),
    abandonExecution: async () => fail(),
  };
}
