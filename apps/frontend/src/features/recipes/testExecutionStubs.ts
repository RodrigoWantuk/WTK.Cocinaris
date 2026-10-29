import { RecipeApiError, type RecipeRepository } from "@/contracts/recipes";

/** Test-only execution methods for recipe repository fakes that do not exercise cooking. */
export const executionStubs: Pick<
  RecipeRepository,
  | "startExecution"
  | "getExecution"
  | "listExecutionsInProgress"
  | "completeExecution"
  | "abandonExecution"
> = {
  startExecution: async () => {
    throw new RecipeApiError("unavailable", "Not used in this test.", 503);
  },
  getExecution: async () => {
    throw new RecipeApiError("unavailable", "Not used in this test.", 503);
  },
  listExecutionsInProgress: async () => [],
  completeExecution: async () => {
    throw new RecipeApiError("unavailable", "Not used in this test.", 503);
  },
  abandonExecution: async () => {
    throw new RecipeApiError("unavailable", "Not used in this test.", 503);
  },
};
