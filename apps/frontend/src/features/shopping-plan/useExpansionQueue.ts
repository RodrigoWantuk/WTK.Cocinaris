import { useEffect, useRef } from "react";
import type { ShoppingPlan } from "@/contracts/shoppingPlan";
import { usePlanExpansionRunner } from "./ShoppingPlanProvider";

export { UNDO_WINDOW_MS } from "./planExpansionRunner";

/**
 * Connects a screen to the app's single follower of dishes being prepared (see
 * {@link PlanExpansionRunner}): while the plan has a pending dish, the newest plan arrives through
 * `onPlan`. The server prepares the recipes (PLAN-0039 Slice 2); nothing here can stop them.
 *
 * @param plan The current plan.
 * @param onPlan Receives each plan the server returns.
 */
export function useExpansionQueue(
  plan: ShoppingPlan | null,
  onPlan: (plan: ShoppingPlan) => void,
): void {
  const runner = usePlanExpansionRunner();
  const onPlanRef = useRef(onPlan);
  onPlanRef.current = onPlan;

  useEffect(
    () => runner.subscribe((next) => onPlanRef.current(next)),
    [runner],
  );

  const pendingIds = (plan?.accepted ?? [])
    .filter((card) => card.expansionState === "pending")
    .map((card) => card.cardId)
    .join(",");

  useEffect(() => {
    if (plan) runner.track(plan);
    // Runs again when the set of pending dishes changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingIds, plan?.sessionId, runner]);
}
