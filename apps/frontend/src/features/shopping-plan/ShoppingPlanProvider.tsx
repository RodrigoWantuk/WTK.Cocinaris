import { createContext, useContext, useMemo, type ReactNode } from "react";
import { createLiveShoppingPlanRepository } from "@/adapters/live/liveShoppingPlanRepository";
import type { ShoppingPlanRepository } from "@/contracts/shoppingPlan";

const ShoppingPlanContext = createContext<ShoppingPlanRepository | null>(null);

/**
 * Provides the shopping plan repository (PLAN-0062).
 *
 * @param repository Injected in tests; the live repository is created when omitted.
 */
export function ShoppingPlanProvider({
  repository,
  children,
}: {
  repository?: ShoppingPlanRepository;
  children: ReactNode;
}) {
  const value = useMemo(
    () => repository ?? createLiveShoppingPlanRepository(),
    [repository],
  );
  return (
    <ShoppingPlanContext.Provider value={value}>
      {children}
    </ShoppingPlanContext.Provider>
  );
}

/** Returns the injected repository; throws outside {@link ShoppingPlanProvider}. */
export function useShoppingPlanRepository(): ShoppingPlanRepository {
  const repo = useContext(ShoppingPlanContext);
  if (!repo) {
    throw new Error("useShoppingPlanRepository requires ShoppingPlanProvider");
  }
  return repo;
}
