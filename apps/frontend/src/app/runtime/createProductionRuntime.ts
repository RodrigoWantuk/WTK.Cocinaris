import { UnavailablePreparationRouteRepository } from "@/adapters/live/unavailablePreparationRouteRepository";
import { createUnavailableContextualHomeAdapter } from "@/adapters/live/unavailableContextualHomeAdapter";
import { createLiveInventoryRepository } from "@/adapters/live/liveInventoryRepository";
import { createLiveRecipeRepository } from "@/adapters/live/liveRecipeRepository";
import { createLiveProfileRepository } from "@/adapters/live/profile/liveProfileRepository";
import { createUnavailableAdultDeclarationPolicy } from "@/features/profile/adultDeclarationPolicy";
import { createBffSessionAdapter } from "@/app/session/bffSessionAdapter";
import type { FrontendRuntime } from "./types";

/**
 * Production composition root: scenario tooling off, no synthetic seeds, no mock prep repo.
 * Session, inventory, recipes, and profile use live BFF adapters; missing prep/home routes remain
 * unavailable; adult declaration stays unavailable until PLAN-0011 supplies legal copy.
 */
export function createProductionRuntime(): FrontendRuntime {
  return {
    mode: "production",
    sessionAdapter: createBffSessionAdapter(),
    inventoryRepository: createLiveInventoryRepository(),
    recipeRepository: createLiveRecipeRepository(),
    preparationRouteRepository: new UnavailablePreparationRouteRepository(),
    contextualHomeAdapter: createUnavailableContextualHomeAdapter(),
    profileRepository: createLiveProfileRepository(),
    adultDeclarationPolicy: createUnavailableAdultDeclarationPolicy(),
    enableScenarioBar: false,
    enablePrototypeFixtures: false,
    persistPrototypeAuth: false,
    shoppingRequirementProjections: [],
    prototypeBanner: false,
  };
}
