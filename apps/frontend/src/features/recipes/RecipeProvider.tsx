import {
  createContext,
  createElement,
  useContext,
  type ReactNode,
} from "react";
import type { RecipeRepository } from "@/contracts/recipes";

const RecipeContext = createContext<RecipeRepository | null>(null);

/**
 * Injects the live recipe repository into production recipe screens.
 */
export function RecipeProvider({
  repository,
  children,
}: {
  repository: RecipeRepository;
  children: ReactNode;
}) {
  return createElement(RecipeContext.Provider, { value: repository }, children);
}

export function useRecipeRepository(): RecipeRepository {
  const repo = useContext(RecipeContext);
  if (!repo) {
    throw new Error("useRecipeRepository requires RecipeProvider");
  }
  return repo;
}
