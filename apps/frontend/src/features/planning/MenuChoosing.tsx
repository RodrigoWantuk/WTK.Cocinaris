import { useEffect, useRef, useState } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import type { MenuEntry } from "@/contracts/planning";
import type { ShoppingPlan } from "@/contracts/shoppingPlan";
import { useShoppingPlanRepository } from "@/features/shopping-plan/ShoppingPlanProvider";
import { useExpansionQueue } from "@/features/shopping-plan/useExpansionQueue";
import { useMealChooser } from "@/features/shopping-plan/useMealChooser";
import { localIsoDate } from "./planningDates";

/** What the menu page asks for: a meal type to fill, or one dish to swap. */
export interface MenuChoosingRequest {
  /** Changes on every request, so asking twice for the same meal opens the sheet again. */
  nonce: number;
  mealType: string;
  /** The dish being swapped; it leaves the menu when the request is sent. */
  replace?: MenuEntry;
}

/**
 * The options sheet on a menu in use (PLAN-0065 Slice 4). Each request opens the menu's plan for
 * changes on the server, which reads the menu as it is, removes the dish being swapped, and offers the
 * person's own recipes at once. Choosing adds one dish and moves nothing else. A swap gives the new dish
 * the old one's day. When the sheet closes and no recipe is still being prepared, the plan is closed
 * again, which drops the options it kept. It renders only the sheet.
 *
 * @param menuId The menu in use.
 * @param request The latest request from the page, or null.
 * @param onPlan Receives each plan the server returns, for what is still missing.
 * @param onMenuChanged Called whenever the menu's dishes changed, so the page reads it again.
 * @param onReplaced Called once a swap removed its dish, so the page can offer to undo it.
 * @param onError Receives a copy key when a request fails.
 */
export function MenuChoosing({
  menuId,
  request,
  onPlan,
  onMenuChanged,
  onReplaced,
  onError,
}: {
  menuId: string;
  request: MenuChoosingRequest | null;
  onPlan: (plan: ShoppingPlan) => void;
  onMenuChanged: () => void;
  onReplaced: (entry: MenuEntry) => void;
  onError: (key: string) => void;
}) {
  const repo = useShoppingPlanRepository();
  const { locale } = useProductionI18n();
  const { session } = useSession();
  const csrfToken = session.csrfToken;
  const [plan, setPlanState] = useState<ShoppingPlan | null>(null);
  const [plannedDate, setPlannedDate] = useState<string | null>(null);
  const handled = useRef<number | null>(null);
  const closing = useRef(false);
  const callbacks = useRef({ onPlan, onMenuChanged, onReplaced, onError });
  callbacks.current = { onPlan, onMenuChanged, onReplaced, onError };

  const ready = useRef<number | null>(null);

  // Keeps the plan, and says when a recipe finished being prepared: its dish is now on the menu.
  const setPlan = (next: ShoppingPlan) => {
    const now = next.accepted.filter(
      (card) => card.expansionState === "ready",
    ).length;
    if (ready.current !== null && now > ready.current)
      callbacks.current.onMenuChanged();
    ready.current = now;
    setPlanState(next);
    callbacks.current.onPlan(next);
  };

  const chooser = useMealChooser(plan?.sessionId ?? "", plan, setPlan, {
    csrfToken,
    plannedDate,
    onChosen: () => callbacks.current.onMenuChanged(),
    focusTarget: (meal) =>
      document.getElementById(`menu-choose-${meal}`) ??
      document.getElementById("menu-whenever"),
  });
  const { openSheet, sheetMeal } = chooser;

  useEffect(() => {
    if (!request || handled.current === request.nonce || !csrfToken) return;
    handled.current = request.nonce;
    const replace = request.replace;
    void repo
      .openAmendment(menuId, localIsoDate(), replace?.entryId ?? null, {
        csrfToken,
        language: locale,
      })
      .then(
        (opened) => {
          ready.current = null;
          setPlan(opened);
          setPlannedDate(replace?.plannedDate ?? null);
          if (replace) {
            callbacks.current.onReplaced(replace);
            callbacks.current.onMenuChanged();
          }
          openSheet(request.mealType);
        },
        () => callbacks.current.onError("planning.error.save"),
      );
  }, [request, repo, menuId, csrfToken, locale, openSheet]);

  useExpansionQueue(plan, { csrfToken, language: locale }, (next) => {
    if (next.sessionId === plan?.sessionId) setPlan(next);
  });

  const pending = (plan?.accepted ?? []).filter(
    (card) => card.expansionState === "pending",
  ).length;
  // Closing the plan drops the options it kept; a recipe still being prepared needs it open.
  useEffect(() => {
    if (
      !plan ||
      plan.status !== "amending" ||
      sheetMeal ||
      pending > 0 ||
      !csrfToken ||
      closing.current
    )
      return;
    closing.current = true;
    void repo
      .accept(plan.sessionId, { csrfToken })
      .then(
        (closed) => setPlanState(closed),
        () => undefined,
      )
      .finally(() => {
        closing.current = false;
      });
  }, [plan, sheetMeal, pending, csrfToken, repo]);

  return <>{chooser.sheet}</>;
}
