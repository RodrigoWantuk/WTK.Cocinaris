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
    requestCandidates: async () => fail(),
    getGenerationSession: async () => fail(),
    selectCandidate: async () => fail(),
  };
}
