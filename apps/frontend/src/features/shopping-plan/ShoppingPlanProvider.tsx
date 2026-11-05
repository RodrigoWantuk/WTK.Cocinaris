import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  type ReactNode,
} from "react";
import { createLiveShoppingPlanRepository } from "@/adapters/live/liveShoppingPlanRepository";
import type { ShoppingPlanRepository } from "@/contracts/shoppingPlan";
import { PlanExpansionRunner } from "./planExpansionRunner";

const ShoppingPlanContext = createContext<ShoppingPlanRepository | null>(null);
const ExpansionContext = createContext<PlanExpansionRunner | null>(null);

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
  const runner = useMemo(() => new PlanExpansionRunner(value), [value]);
  useEffect(() => () => runner.dispose(), [runner]);
  return (
    <ShoppingPlanContext.Provider value={value}>
      <ExpansionContext.Provider value={runner}>
        {children}
      </ExpansionContext.Provider>
    </ShoppingPlanContext.Provider>
  );
}

/** Returns the repository, or null outside the provider, for links on pages that also work without plans. */
export function useOptionalShoppingPlanRepository(): ShoppingPlanRepository | null {
  return useContext(ShoppingPlanContext);
}

/** Returns the app's single expansion runner; throws outside {@link ShoppingPlanProvider}. */
export function usePlanExpansionRunner(): PlanExpansionRunner {
  const runner = useContext(ExpansionContext);
  if (!runner) {
    throw new Error("usePlanExpansionRunner requires ShoppingPlanProvider");
  }
  return runner;
}

/** Returns the injected repository; throws outside {@link ShoppingPlanProvider}. */
export function useShoppingPlanRepository(): ShoppingPlanRepository {
  const repo = useContext(ShoppingPlanContext);
  if (!repo) {
    throw new Error("useShoppingPlanRepository requires ShoppingPlanProvider");
  }
  return repo;
}
