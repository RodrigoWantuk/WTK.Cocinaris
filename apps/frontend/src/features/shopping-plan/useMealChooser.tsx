import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { RecipeApiError, type RecipeSummary } from "@/contracts/recipes";
import type {
  ShoppingPlan,
  ShoppingPlanCard,
  ShoppingPlanPreferences,
} from "@/contracts/shoppingPlan";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import { MealOptionsSheet } from "./MealOptionsSheet";
import { restrictionLabel } from "./restrictionLabel";
import { useShoppingPlanRepository } from "./ShoppingPlanProvider";
import { UNDO_WINDOW_MS } from "./useExpansionQueue";
import { useMealBatches, type MealBatchJob } from "./useMealBatches";

/** How many options a meal type should have before more are asked for on their own. */
const FULL_OPTIONS = 6;
const LOW_OPTIONS = 3;
/** Automatic requests per meal type and visit; the person can always ask for more. */
const MAX_AUTO = 2;
/** Switches wait this long, so quick taps send only the last state. */
export const SWITCH_DELAY_MS = 600;

/**
 * Choosing dishes for one meal type in the options sheet (PLAN-0065), shared by the board of a menu
 * being built and by a menu in use. It owns the sheet, its requests for dishes, the undo offer, and the
 * switches; the page owns the plan and decides what a finished choice means for it.
 *
 * @param sessionId The plan whose cards are chosen.
 * @param plan The plan as last returned by the server, or null before it is loaded.
 * @param setPlan Receives every plan the server returns.
 * @param options.plannedDate A day given to every dish chosen, such as the day of a dish being swapped.
 * @param options.onChosen Called after a dish is chosen, with the updated plan.
 * @param options.focusTarget Where focus goes when the sheet closes; Radix returns it to the opener otherwise.
 * @returns The sheet element, the undo notice, and the controls the page needs.
 */
export function useMealChooser(
  sessionId: string,
  plan: ShoppingPlan | null,
  setPlan: (plan: ShoppingPlan) => void,
  options: {
    csrfToken: string | null | undefined;
    plannedDate?: string | null;
    onChosen?: (plan: ShoppingPlan) => void;
    focusTarget?: (mealType: string) => HTMLElement | null;
  },
) {
  const repo = useShoppingPlanRepository();
  const recipes = useRecipeRepository();
  const { t, locale } = useProductionI18n();
  const { csrfToken } = options;
  const [sheetMeal, setSheetMeal] = useState<string | null>(null);
  // The last meal type shown, so the sheet can close with its content instead of vanishing.
  const [shownMeal, setShownMeal] = useState<string | null>(null);
  const [saved, setSaved] = useState<RecipeSummary[] | null>(null);
  const [busyCardId, setBusyCardId] = useState<string | null>(null);
  const [chosen, setChosen] = useState<string | null>(null);
  const [sheetError, setSheetError] = useState<string | null>(null);
  const [localPreferences, setLocalPreferences] =
    useState<ShoppingPlanPreferences | null>(null);
  const autoCount = useRef<Record<string, number>>({});
  const switchTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const undoTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const optionsRef = useRef(options);
  optionsRef.current = options;

  useEffect(
    () => () => {
      for (const timer of [switchTimer, undoTimer]) {
        if (timer.current) clearTimeout(timer.current);
      }
    },
    [],
  );

  const batches = useMealBatches(
    sessionId,
    { csrfToken, language: locale },
    setPlan,
  );

  // The person's own recipes load once, the first time a sheet opens, so typing filters instantly.
  useEffect(() => {
    if (!sheetMeal || saved !== null) return;
    const controller = new AbortController();
    recipes.listRecipes(controller.signal).then(
      (items) => !controller.signal.aborted && setSaved(items),
      () => !controller.signal.aborted && setSaved([]),
    );
    return () => controller.abort();
  }, [sheetMeal, saved, recipes]);

  const group = plan?.groups.find((item) => item.mealType === sheetMeal);
  const optionCount = group?.options.length ?? 0;
  const groupMissing = group ? group.wanted - group.filled : 0;
  const { enqueue, running, failures, exhausted, hasAsked } = batches;
  // One automatic request when a meal type has few options, never one per opening of the sheet.
  useEffect(() => {
    if (!sheetMeal || !group || groupMissing <= 0 || running) return;
    if (failures[sheetMeal] || exhausted.has(sheetMeal)) return;
    const done = autoCount.current[sheetMeal] ?? 0;
    const wanted =
      (!hasAsked(sheetMeal) && optionCount < FULL_OPTIONS) ||
      optionCount < LOW_OPTIONS;
    if (!wanted || done >= MAX_AUTO) return;
    autoCount.current[sheetMeal] = done + 1;
    enqueue({ mealType: sheetMeal, kind: "auto" });
  }, [
    sheetMeal,
    group,
    groupMissing,
    optionCount,
    running,
    failures,
    exhausted,
    hasAsked,
    enqueue,
  ]);

  const openSheet = useCallback((mealType: string) => {
    setSheetError(null);
    setSheetMeal(mealType);
    setShownMeal(mealType);
  }, []);

  const closeSheet = useCallback(() => setSheetMeal(null), []);

  const showUndo = useCallback((name: string) => {
    setChosen(name);
    if (undoTimer.current) clearTimeout(undoTimer.current);
    undoTimer.current = setTimeout(() => setChosen(null), UNDO_WINDOW_MS);
  }, []);

  const afterChoice = useCallback(
    (updated: ShoppingPlan, name: string, mealType: string) => {
      setPlan(updated);
      showUndo(name);
      optionsRef.current.onChosen?.(updated);
      const after = updated.groups.find((item) => item.mealType === mealType);
      // The sheet stays for the same meal type until nothing is missing in it.
      if (after && after.filled >= after.wanted) closeSheet();
    },
    [setPlan, showUndo, closeSheet],
  );

  const run = async (
    work: () => Promise<void>,
    onError: (code: string, err: unknown) => void,
  ) => {
    try {
      await work();
    } catch (err) {
      onError(err instanceof RecipeApiError ? err.code : "unexpected", err);
    }
  };

  const choose = (card: ShoppingPlanCard) => {
    if (!csrfToken || !sheetMeal || busyCardId) return;
    const mealType = sheetMeal;
    setBusyCardId(card.cardId);
    setSheetError(null);
    void run(
      async () => {
        const updated = await repo.decide(
          sessionId,
          card.cardId,
          {
            decision: "accept",
            mealType,
            ...(options.plannedDate
              ? { plannedDate: options.plannedDate }
              : {}),
          },
          { csrfToken },
        );
        afterChoice(updated, card.name, mealType);
      },
      () => setSheetError("shoppingPlan.error.save"),
    ).finally(() => setBusyCardId(null));
  };

  const chooseRecipe = (recipeId: string, name: string) => {
    if (!csrfToken || !sheetMeal || busyCardId) return;
    const mealType = sheetMeal;
    setBusyCardId(recipeId);
    setSheetError(null);
    void run(
      async () => {
        const updated = await repo.chooseRecipe(sessionId, recipeId, mealType, {
          csrfToken,
          plannedDate: options.plannedDate ?? null,
        });
        afterChoice(updated, name, mealType);
      },
      (code, err) =>
        setSheetError(
          code === "request_blocked"
            ? `blocked:${err instanceof RecipeApiError ? (err.fieldErrors.restriction?.[0] ?? "") : ""}`
            : "shoppingPlan.error.save",
        ),
    ).finally(() => setBusyCardId(null));
  };

  const refuse = (card: ShoppingPlanCard) => {
    if (!csrfToken || busyCardId) return;
    setBusyCardId(card.cardId);
    void run(
      async () =>
        setPlan(
          await repo.decide(
            sessionId,
            card.cardId,
            { decision: "reject" },
            { csrfToken },
          ),
        ),
      () => setSheetError("shoppingPlan.error.save"),
    ).finally(() => setBusyCardId(null));
  };

  const undo = () => {
    if (!csrfToken) return;
    setChosen(null);
    void run(
      async () => {
        const updated = await repo.undo(sessionId, { csrfToken });
        setPlan(updated);
        optionsRef.current.onChosen?.(updated);
      },
      () => setSheetError("shoppingPlan.error.save"),
    );
  };

  const ask = useCallback(
    (job: MealBatchJob) => {
      batches.clearFailure(job.mealType);
      enqueue(job);
    },
    [batches, enqueue],
  );

  // A switch changes what the existing options were made for, so new ones are asked for at once;
  // the goal only re-ranks what is there.
  const setPreferences = (next: ShoppingPlanPreferences) => {
    if (!csrfToken || !plan) return;
    setLocalPreferences(next);
    if (switchTimer.current) clearTimeout(switchTimer.current);
    const mealType = sheetMeal;
    switchTimer.current = setTimeout(() => {
      const before = plan.preferences;
      void run(
        async () => {
          const updated = await repo.setPreferences(sessionId, next, {
            csrfToken,
          });
          setPlan(updated);
          setLocalPreferences(null);
          const switched =
            before.usePreferences !== next.usePreferences ||
            before.usePantry !== next.usePantry;
          if (switched && mealType) ask({ mealType, kind: "switch" });
        },
        () => {
          setLocalPreferences(null);
          setSheetError("shoppingPlan.error.save");
        },
      );
    }, SWITCH_DELAY_MS);
  };

  const view = useMemo(
    () =>
      plan && localPreferences
        ? { ...plan, preferences: localPreferences }
        : plan,
    [plan, localPreferences],
  );

  const returnFocus = (event: Event) => {
    const target = shownMeal ? options.focusTarget?.(shownMeal) : null;
    if (!target) return;
    event.preventDefault();
    target.focus();
  };

  const undoNotice: ReactNode = chosen ? (
    <div
      role="status"
      data-testid="board-undo"
      className="flex items-center justify-between gap-3 rounded-xl bg-secondary px-3 py-2 text-sm"
    >
      <span className="min-w-0">
        {t("menuBuild.undo.chosen", { dish: chosen })}
      </span>
      <Button
        type="button"
        size="sm"
        variant="ghost"
        data-testid="board-undo-action"
        onClick={undo}
      >
        {t("shoppingPlan.undo")}
      </Button>
    </div>
  ) : null;

  const sheetFailure = sheetError
    ? sheetError.startsWith("blocked:")
      ? t("menuBuild.sheet.blocked", {
          restriction:
            sheetError.length > 8
              ? restrictionLabel(locale, sheetError.slice(8))
              : t("menuBuild.sheet.blocked.any"),
        })
      : t(sheetError)
    : null;

  const sheet: ReactNode =
    shownMeal && view ? (
      <MealOptionsSheet
        plan={view}
        mealType={shownMeal}
        open={sheetMeal !== null}
        onOpenChange={(open) => {
          if (!open) closeSheet();
        }}
        onCloseAutoFocus={returnFocus}
        saved={saved ?? []}
        running={running}
        failure={failures[shownMeal]}
        exhausted={exhausted.has(shownMeal)}
        busyCardId={busyCardId}
        actions={{ choose, chooseRecipe, refuse, ask, setPreferences }}
        notice={
          <>
            {undoNotice}
            {sheetFailure ? (
              <p role="alert" className="text-sm text-destructive">
                {sheetFailure}
              </p>
            ) : null}
          </>
        }
      />
    ) : null;

  return {
    /** The meal type whose sheet is open, or null. */
    sheetMeal,
    openSheet,
    closeSheet,
    /** The options sheet, to render once on the page. */
    sheet,
    /** "Escolhido · Desfazer" for a few seconds after a choice; also shown inside the sheet. */
    undoNotice,
    /** A choice being saved, shared with the page's own card actions. */
    busyCardId,
    setBusyCardId,
    /** The request for dishes being answered, if any. */
    running,
    ask,
  };
}
