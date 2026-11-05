import { useEffect, useRef, useState } from "react";
import type { ShoppingPlan } from "@/contracts/shoppingPlan";
import { usePlanExpansionRunner } from "./ShoppingPlanProvider";

export { UNDO_WINDOW_MS } from "./planExpansionRunner";

/**
 * Connects a screen to the app's single expansion runner (see {@link PlanExpansionRunner}).
 *
 * @param plan The current plan; expansion runs while it has pending cards.
 * @param options CSRF token and interface language for requests.
 * @param onPlan Receives each plan the server returns.
 * @returns A stable error code when expansion stopped because AI is unavailable or out of budget.
 */
export function useExpansionQueue(
  plan: ShoppingPlan | null,
  options: { csrfToken: string | null | undefined; language: string },
  onPlan: (plan: ShoppingPlan) => void,
): string | null {
  const runner = usePlanExpansionRunner();
  const [stopped, setStopped] = useState<string | null>(null);
  const onPlanRef = useRef(onPlan);
  onPlanRef.current = onPlan;

  useEffect(() => {
    runner.resume();
    return runner.subscribe((next) => onPlanRef.current(next), setStopped);
  }, [runner]);

  const pendingIds = (plan?.accepted ?? [])
    .filter((card) => card.expansionState === "pending")
    .map((card) => card.cardId)
    .join(",");

  useEffect(() => {
    if (!plan || !options.csrfToken || stopped) return;
    runner.track(plan, {
      csrfToken: options.csrfToken,
      language: options.language,
    });
    // Runs again when the set of pending cards changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingIds, plan?.sessionId, options.csrfToken, stopped, runner]);

  return stopped;
}
