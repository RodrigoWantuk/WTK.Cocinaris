import { sharedMockPreparationRouteRepository } from "@/adapters/mock/preparationRouteRepository";
import { MOCK_SHOPPING_REQUIREMENTS } from "@/adapters/mock/shoppingRequirementFixtures";
import { createMockContextualHomeAdapter } from "@/adapters/mock/contextual-home/mockContextualHomeAdapter";
import { createLiveInventoryRepository } from "@/adapters/live/liveInventoryRepository";
import { createLiveProfileRepository } from "@/adapters/live/profile/liveProfileRepository";
import { createUnavailableRecipeRepository } from "@/adapters/live/unavailableRecipeRepository";
import { createSyntheticAdultDeclarationPolicy } from "@/features/profile/adultDeclarationPolicy";
import { createMockSessionAdapter } from "@/app/session/mockSessionAdapter";
import type { FrontendRuntime } from "./types";

/**
 * Prototype composition root: fixtures, ScenarioBar, and mock session are explicit.
 * Inventory and profile repositories are the live adapter shape; prototype UX still
 * uses StoreProvider pantry. Recipes stay unavailable (no mock recipe fixtures).
 * Contextual home and adult declaration use synthetic mocks — never wired into production.
 */
export function createPrototypeRuntime(): FrontendRuntime {
  return {
    mode: "prototype",
    sessionAdapter: createMockSessionAdapter({
      initiallyAuthenticated: false,
      displayName: "Ana",
      timeZone: "America/Sao_Paulo",
    }),
    inventoryRepository: createLiveInventoryRepository(),
    recipeRepository: createUnavailableRecipeRepository(),
    preparationRouteRepository: sharedMockPreparationRouteRepository,
    contextualHomeAdapter: createMockContextualHomeAdapter({
      scenario: "default",
    }),
    profileRepository: createLiveProfileRepository(),
    adultDeclarationPolicy: createSyntheticAdultDeclarationPolicy(),
    enableScenarioBar: true,
    enablePrototypeFixtures: true,
    persistPrototypeAuth: true,
    shoppingRequirementProjections: MOCK_SHOPPING_REQUIREMENTS,
    prototypeBanner: true,
  };
}
