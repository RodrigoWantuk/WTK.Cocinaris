import { useEffect, useRef, useState } from "react";
import type { MenuSummary, ShoppingList } from "@/contracts/planning";
import { useOptionalShoppingPlanRepository } from "@/features/shopping-plan/ShoppingPlanProvider";
import { useOfflineList } from "./offline/OfflineListProvider";
import { localIsoDate } from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";

/**
 * The menu "Planejar" talks about: the one in use today, else the next accepted one, else the latest
 * draft that has not ended.
 *
 * @param menus The owner's menus.
 * @param today The owner's local day (`YYYY-MM-DD`).
 */
export function pickCurrentMenu(
  menus: MenuSummary[],
  today: string,
): MenuSummary | null {
  const accepted = menus.filter((menu) => menu.status === "accepted");
  return (
    accepted.find((menu) => menu.startDate <= today && menu.endDate >= today) ??
    accepted
      .filter((menu) => menu.startDate > today)
      .sort((a, b) => a.startDate.localeCompare(b.startDate))[0] ??
    menus.find((menu) => menu.status === "draft" && menu.endDate >= today) ??
    null
  );
}

/** What the person should do next in planning. */
export type PlanningStep =
  | { kind: "start" }
  | { kind: "resume" }
  | { kind: "draft" }
  | { kind: "buy"; count: number }
  | { kind: "arrival" }
  | { kind: "prepare" }
  | { kind: "view" };

/** Lines still to buy, and lines already marked as bought but not yet put away. */
export function countShoppingLines(list: ShoppingList | null): {
  toBuy: number;
  bought: number;
} {
  const lines = (list?.items ?? []).filter(
    (item) => item.toBuyQuantity > 0 && !item.later,
  );
  const bought = lines.filter((item) => item.checked).length;
  return { toBuy: lines.length - bought, bought };
}

/**
 * Picks the next step. Pure. A plan being built comes first, because it is unfinished work. Then the
 * order follows the real sequence: buy, put away, prepare, and cook from the menu. While lines remain,
 * buying stays first even if some are marked: the person may still be at the market.
 *
 * @param input What is known about the plan. `list` is null when it could not be read, and the step
 * then falls back to the menu, never to a claim that nothing is missing.
 */
export function nextPlanningStep(input: {
  planInProgress: boolean;
  menu: MenuSummary | null;
  list: ShoppingList | null;
  prepareSessionId: string | null;
}): PlanningStep {
  if (input.planInProgress) return { kind: "resume" };
  if (!input.menu) return { kind: "start" };
  if (input.menu.status !== "accepted") return { kind: "draft" };
  if (!input.list) return { kind: "view" };
  const { toBuy, bought } = countShoppingLines(input.list);
  if (toBuy > 0) return { kind: "buy", count: toBuy };
  if (bought > 0) return { kind: "arrival" };
  return input.prepareSessionId ? { kind: "prepare" } : { kind: "view" };
}

/** What the planning page and the home tile show. */
export interface PlanningOverview {
  menu: MenuSummary | null;
  /** The plan being built, with where to continue it. */
  resumePath: string | null;
  prepareSessionId: string | null;
  boughtCount: number;
  step: PlanningStep;
}

/** The result of {@link usePlanningOverview}. */
export type PlanningOverviewState =
  | { status: "loading" }
  | { status: "failed"; error: unknown }
  | { status: "ready"; overview: PlanningOverview };

/**
 * Reads the planning overview. Only the menu list is required. The plan in progress, the shopping list,
 * and the preparation session each degrade on their own, so one failure never blanks the page.
 * The read is cancelled on unmount and changes nothing on the server. It keeps the current list on
 * the device for use without a connection.
 */
export function usePlanningOverview(): PlanningOverviewState {
  const repo = usePlanningRepository();
  const plans = useOptionalShoppingPlanRepository();
  // A ref, because the signed-in owner may arrive after this read starts, and reading again when the
  // connection state changes would be wasteful.
  const offlineList = useOfflineList();
  const offlineRef = useRef(offlineList);
  offlineRef.current = offlineList;
  const [state, setState] = useState<PlanningOverviewState>({
    status: "loading",
  });

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    void (async () => {
      let menus: MenuSummary[];
      try {
        menus = await repo.listMenus(signal);
      } catch (error) {
        if (!signal.aborted) setState({ status: "failed", error });
        return;
      }
      const today = localIsoDate();
      const menu = pickCurrentMenu(menus, today);
      const [start, list, prepareSessionId] = await Promise.all([
        plans?.getStart(signal).catch(() => null) ?? null,
        menu?.status === "accepted"
          ? repo.getShopping(menu.menuId, today, signal).catch(() => null)
          : null,
        menu?.status === "accepted" && plans
          ? plans
              .findRouteLink({ menuId: menu.menuId }, signal)
              .catch(() => null)
          : null,
      ]);
      if (signal.aborted) return;
      // The list must be on the device before the market, which has no signal, and this page no
      // longer opens it. Best effort: a storage failure must not affect the page (ADR-0012).
      if (list) {
        void offlineRef.current
          ?.saveList(list, new Date().toISOString())
          .catch(() => undefined);
      }
      const current = start?.current ?? null;
      setState({
        status: "ready",
        overview: {
          menu,
          resumePath: current
            ? `/app/compras/planejar/${current.sessionId}${current.status === "reviewing" ? "/revisar" : ""}`
            : null,
          prepareSessionId,
          boughtCount: countShoppingLines(list).bought,
          step: nextPlanningStep({
            planInProgress: current !== null,
            menu,
            list,
            prepareSessionId,
          }),
        },
      });
    })();
    return () => controller.abort();
  }, [repo, plans]);

  return state;
}
