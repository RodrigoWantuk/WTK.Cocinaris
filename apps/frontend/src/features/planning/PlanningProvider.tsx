import {
  createContext,
  createElement,
  useContext,
  type ReactNode,
} from "react";
import type { PlanningRepository } from "@/contracts/planning";

const PlanningContext = createContext<PlanningRepository | null>(null);

/** Injects the planning repository (PLAN-0029) into menu, shopping, and home screens. */
export function PlanningProvider({
  repository,
  children,
}: {
  repository: PlanningRepository;
  children: ReactNode;
}) {
  return createElement(
    PlanningContext.Provider,
    { value: repository },
    children,
  );
}

/** Returns the injected planning repository; throws outside {@link PlanningProvider}. */
export function usePlanningRepository(): PlanningRepository {
  const repo = useContext(PlanningContext);
  if (!repo) {
    throw new Error("usePlanningRepository requires PlanningProvider");
  }
  return repo;
}
