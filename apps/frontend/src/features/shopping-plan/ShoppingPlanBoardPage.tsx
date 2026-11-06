import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { RecipeApiError, type RecipeSummary } from "@/contracts/recipes";
import {
  MEAL_ORDER,
  type MealCounts,
  type ShoppingPlan,
  type ShoppingPlanAccepted,
  type ShoppingPlanCard,
  type ShoppingPlanPreferences,
} from "@/contracts/shoppingPlan";
import { formatMenuDay } from "@/features/planning/planningDates";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import { CompleteSheet } from "./CompleteSheet";
import { Counter } from "./Counter";
import { DishActionsSheet, type MealRaise } from "./DishActionsSheet";
import { MealOptionsSheet } from "./MealOptionsSheet";
import { restrictionLabel } from "./restrictionLabel";
import { useShoppingPlanRepository } from "./ShoppingPlanProvider";
import { useExpansionQueue, UNDO_WINDOW_MS } from "./useExpansionQueue";
import { useMealBatches, type MealBatchJob } from "./useMealBatches";

/** How many options a meal type should have before more are asked for on their own. */
const FULL_OPTIONS = 6;
const LOW_OPTIONS = 3;
/** Automatic requests per meal type and visit; the person can always ask for more. */
const MAX_AUTO = 2;
/** Switches and counters wait this long, so quick taps send only the last state. */
const SWITCH_DELAY_MS = 600;
const MAX_MEALS = 40;
/** How long a short confirmation stays on screen. */
const NOTICE_MS = 5000;

/** The menu's counts as the board shows them; a meal type without a group wants none. */
function countsOf(plan: ShoppingPlan): MealCounts {
  const counts: MealCounts = { breakfast: 0, lunch: 0, dinner: 0, snack: 0 };
  for (const group of plan.groups) {
    if (group.mealType in counts)
      counts[group.mealType as keyof MealCounts] = group.wanted;
  }
  return counts;
}

/**
 * The board of a menu being built (PLAN-0065): one surface per meal type with its chosen dishes and
 * one row for what is missing, and a fixed bar with the progress and the main action. Options open
 * in a sheet per meal type. Dates are optional and appear only when given. Menus started before
 * PLAN-0065 appear here too, read as their days for each meal type.
 */
export function ShoppingPlanBoardPage() {
  const { sessionId = "" } = useParams<{ sessionId: string }>();
  const repo = useShoppingPlanRepository();
  const recipes = useRecipeRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const csrfToken = session.csrfToken;
  const [plan, setPlan] = useState<ShoppingPlan | null>(null);
  const [failed, setFailed] = useState(false);
  const [sheetMeal, setSheetMeal] = useState<string | null>(null);
  // The last meal type shown, so the sheet can close with its content instead of vanishing.
  const [shownMeal, setShownMeal] = useState<string | null>(null);
  const [saved, setSaved] = useState<RecipeSummary[] | null>(null);
  const [busyCardId, setBusyCardId] = useState<string | null>(null);
  const [chosen, setChosen] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sheetError, setSheetError] = useState<string | null>(null);
  const [localPreferences, setLocalPreferences] =
    useState<ShoppingPlanPreferences | null>(null);
  const [dish, setDish] = useState<ShoppingPlanAccepted | null>(null);
  const [confirmUse, setConfirmUse] = useState(false);
  const [saving, setSaving] = useState(false);
  const [countDraft, setCountDraft] = useState<MealCounts | null>(null);
  const [peopleDraft, setPeopleDraft] = useState<{
    cardId: string;
    people: number;
  } | null>(null);
  const [dishError, setDishError] = useState<string | null>(null);
  const [completeOpen, setCompleteOpen] = useState(false);
  const [completeFailed, setCompleteFailed] = useState<string[] | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const autoCount = useRef<Record<string, number>>({});
  const switchTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const undoTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const countTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const peopleTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const noticeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    repo.get(sessionId, controller.signal).then(
      (value) => {
        if (controller.signal.aborted) return;
        if (value.status === "finished") {
          navigate(`/app/cardapio/${value.menuId}`, { replace: true });
          return;
        }
        if (value.status === "abandoned") {
          navigate("/app/compras/planejar", { replace: true });
          return;
        }
        setPlan(value);
      },
      () => !controller.signal.aborted && setFailed(true),
    );
    return () => controller.abort();
  }, [repo, sessionId, navigate]);

  useEffect(
    () => () => {
      for (const timer of [
        switchTimer,
        undoTimer,
        countTimer,
        peopleTimer,
        noticeTimer,
      ]) {
        if (timer.current) clearTimeout(timer.current);
      }
    },
    [],
  );

  const stopped = useExpansionQueue(
    plan,
    { csrfToken, language: locale },
    setPlan,
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

  const showUndo = useCallback((name: string) => {
    setChosen(name);
    if (undoTimer.current) clearTimeout(undoTimer.current);
    undoTimer.current = setTimeout(() => setChosen(null), UNDO_WINDOW_MS);
  }, []);

  const afterChoice = useCallback(
    (updated: ShoppingPlan, name: string, mealType: string) => {
      setPlan(updated);
      showUndo(name);
      const after = updated.groups.find((item) => item.mealType === mealType);
      // The sheet stays for the same meal type until nothing is missing in it.
      if (after && after.filled >= after.wanted) closeSheet();
    },
    // closeSheet only uses state setters.
    [showUndo],
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
          { decision: "accept", mealType },
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
      async () => setPlan(await repo.undo(sessionId, { csrfToken })),
      () => setError("shoppingPlan.error.save"),
    );
  };

  const ask = (job: MealBatchJob) => {
    batches.clearFailure(job.mealType);
    enqueue(job);
  };

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

  function openSheet(mealType: string) {
    setSheetError(null);
    setSheetMeal(mealType);
    setShownMeal(mealType);
  }

  // Focus goes back to the row that opened the sheet, or to its group's heading once the row is
  // gone because nothing is missing there any more.
  function closeSheet() {
    setSheetMeal(null);
  }

  // Focus goes back to the row that opened the sheet, or to its group's heading once the row is
  // gone because nothing is missing there any more.
  const returnFocus = (event: Event) => {
    if (!shownMeal) return;
    event.preventDefault();
    (
      document.getElementById(`board-choose-${shownMeal}`) ??
      document.getElementById(`board-heading-${shownMeal}`)
    )?.focus();
  };

  const view = useMemo(
    () =>
      plan && localPreferences
        ? { ...plan, preferences: localPreferences }
        : plan,
    [plan, localPreferences],
  );

  if (failed) {
    return (
      <p role="alert" className="text-muted-foreground">
        {t("menuBuild.error.load")}
      </p>
    );
  }
  if (!plan || !view) return <p role="status">{t("planning.loading")}</p>;

  const pending = plan.accepted.filter(
    (card) => card.expansionState === "pending",
  ).length;
  const ready = plan.accepted.filter(
    (card) => card.expansionState === "ready",
  ).length;
  const missing = Math.max(
    0,
    plan.progress.mealsTotal - plan.progress.mealsFilled,
  );
  const items = plan.progress.listItemCount;
  const bases = plan.accepted.filter((card) => card.isComponent);
  // A base appears once a dish that uses it is chosen; it is a quiet offer, never a step.
  const suggestedBases = plan.queue.filter((card) => card.component);
  const dishesOnly = plan.accepted.filter(
    (card) => !card.isComponent && card.expansionState !== "failed",
  );
  const undated = dishesOnly.filter((card) => !card.plannedDate).length;
  const dated = dishesOnly.filter((card) => card.plannedDate).length;
  const listOf = (names: string[]) =>
    new Intl.ListFormat(locale, { style: "long", type: "conjunction" }).format(
      names,
    );
  const start = plan.setup.startDate;
  const end = plan.setup.endDate;

  const decideBase = (
    card: ShoppingPlanCard,
    decision: "accept" | "reject",
  ) => {
    if (!csrfToken || busyCardId) return;
    setBusyCardId(card.cardId);
    void run(
      async () =>
        setPlan(
          await repo.decide(
            sessionId,
            card.cardId,
            { decision },
            { csrfToken },
          ),
        ),
      () => setError("shoppingPlan.error.save"),
    ).finally(() => setBusyCardId(null));
  };

  const use = () => {
    if (!csrfToken || saving) return;
    setSaving(true);
    setError(null);
    void run(
      async () => {
        await repo.accept(sessionId, { csrfToken });
        navigate(`/app/cardapio/${plan.menuId}`);
      },
      (code) =>
        setError(
          code === "expansions_pending"
            ? "menuBuild.bar.wait"
            : "shoppingPlan.error.save",
        ),
    ).finally(() => setSaving(false));
  };

  // Changes to one dish keep its sheet open, showing the new state; removing or swapping closes it.
  const dishAction = (
    work: () => Promise<ShoppingPlan>,
    after?: (updated: ShoppingPlan) => void,
  ) => {
    if (saving) return;
    setSaving(true);
    setDishError(null);
    void run(
      async () => {
        const updated = await work();
        setPlan(updated);
        if (after) after(updated);
        else
          setDish(
            updated.accepted.find((card) => card.cardId === dish?.cardId) ??
              null,
          );
      },
      () => setDishError("shoppingPlan.error.save"),
    ).finally(() => setSaving(false));
  };

  const showNotice = (text: string) => {
    setNotice(text);
    if (noticeTimer.current) clearTimeout(noticeTimer.current);
    noticeTimer.current = setTimeout(() => setNotice(null), NOTICE_MS);
  };

  const counts = countDraft ?? countsOf(plan);
  const totalWanted = MEAL_ORDER.reduce((sum, meal) => sum + counts[meal], 0);
  const canAddMeal =
    totalWanted < MAX_MEALS &&
    MEAL_ORDER.some(
      (meal) => !plan.groups.some((item) => item.mealType === meal),
    );

  // Quick taps on a counter send only the last count; the server keeps it at or above what is chosen.
  const changeCount = (mealType: string, value: number, wait = true) => {
    if (!csrfToken) return;
    const next = { ...counts, [mealType]: value };
    setCountDraft(next);
    setError(null);
    if (countTimer.current) clearTimeout(countTimer.current);
    const send = () =>
      void run(
        async () => {
          setPlan(await repo.setMealCounts(sessionId, next, { csrfToken }));
          setCountDraft(null);
        },
        (code) => {
          setCountDraft(null);
          setError(
            code === "meal_count_below_chosen"
              ? "menuBuild.group.belowChosen"
              : "shoppingPlan.error.save",
          );
        },
      );
    if (wait) countTimer.current = setTimeout(send, SWITCH_DELAY_MS);
    else send();
  };

  const dishPeople = (card: ShoppingPlanAccepted) =>
    peopleDraft?.cardId === card.cardId
      ? peopleDraft.people
      : (card.people ?? plan.setup.people);

  const dishActions = {
    swap: (card: ShoppingPlanAccepted) =>
      dishAction(
        () =>
          repo.removeCard(sessionId, card.cardId, { csrfToken: csrfToken! }),
        () => {
          setDish(null);
          openSheet(card.mealType);
        },
      ),
    setDay: (card: ShoppingPlanAccepted, day: string | null) =>
      dishAction(() =>
        repo.setCardDay(sessionId, card.cardId, day, { csrfToken: csrfToken! }),
      ),
    setYield: (card: ShoppingPlanAccepted, meals: number, raise?: MealRaise) =>
      dishAction(async () => {
        if (raise)
          await repo.setMealCounts(
            sessionId,
            { ...countsOf(plan), [raise.mealType]: raise.count },
            { csrfToken: csrfToken! },
          );
        return repo.setCardYield(sessionId, card.cardId, meals, {
          csrfToken: csrfToken!,
        });
      }),
    setPeople: (card: ShoppingPlanAccepted, people: number) => {
      setPeopleDraft({ cardId: card.cardId, people });
      if (peopleTimer.current) clearTimeout(peopleTimer.current);
      peopleTimer.current = setTimeout(
        () =>
          void run(
            async () => {
              const updated = await repo.setCardPeople(
                sessionId,
                card.cardId,
                people,
                { csrfToken: csrfToken! },
              );
              setPlan(updated);
              setDish(
                (current) =>
                  updated.accepted.find(
                    (item) => item.cardId === current?.cardId,
                  ) ?? null,
              );
              setPeopleDraft(null);
            },
            () => {
              setPeopleDraft(null);
              setDishError("shoppingPlan.error.save");
            },
          ),
        SWITCH_DELAY_MS,
      );
    },
    retry: (card: ShoppingPlanAccepted) =>
      dishAction(() =>
        repo.expand(sessionId, card.cardId, {
          csrfToken: csrfToken!,
          language: locale,
        }),
      ),
    remove: (card: ShoppingPlanAccepted) =>
      dishAction(
        () =>
          repo.removeCard(sessionId, card.cardId, { csrfToken: csrfToken! }),
        () => setDish(null),
      ),
  };

  const complete = (
    choices: Array<{ cardId: string; mealType: string; name: string }>,
  ) => {
    if (!csrfToken || saving) return;
    setSaving(true);
    setCompleteFailed(null);
    void run(
      async () => {
        const result = await repo.chooseMany(
          sessionId,
          choices.map(({ cardId, mealType }) => ({ cardId, mealType })),
          { csrfToken },
        );
        setPlan(result.plan);
        const added = choices.length - result.failed.length;
        if (result.failed.length > 0) {
          // The sheet stays open and says what is still missing; the rest is already chosen.
          setCompleteFailed(
            choices
              .filter((choice) => result.failed.includes(choice.cardId))
              .map((choice) => choice.name),
          );
          return;
        }
        setCompleteOpen(false);
        showNotice(
          t(
            added === 1
              ? "menuBuild.complete.done.one"
              : "menuBuild.complete.done",
            { count: added },
          ),
        );
      },
      () => setCompleteFailed([]),
    ).finally(() => setSaving(false));
  };

  const distribute = (clear: boolean) => {
    if (!csrfToken || saving) return;
    setSaving(true);
    setError(null);
    void run(
      async () =>
        setPlan(await repo.distribute(sessionId, clear, { csrfToken })),
      (code) =>
        setError(
          code === "expansions_pending"
            ? "menuBuild.bar.wait"
            : "shoppingPlan.error.save",
        ),
    ).finally(() => setSaving(false));
  };

  const undoNotice = chosen ? (
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

  return (
    <div data-testid="plan-board" className="space-y-6 pb-48">
      <header className="space-y-1">
        <div className="flex items-start justify-between gap-3">
          <h1 className="font-display text-3xl">
            {t("menuBuild.board.title")}
          </h1>
          <Button asChild variant="ghost" size="sm" className="-mr-2 shrink-0">
            <Link to="/app/compras" data-testid="board-leave">
              {t("menuBuild.board.leave")}
            </Link>
          </Button>
        </div>
        {start && end ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="board-period"
          >
            {t("menuBuild.board.period", {
              start: formatMenuDay(start, locale),
              end: formatMenuDay(end, locale),
            })}
          </p>
        ) : null}
      </header>

      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(error)}
        </p>
      ) : null}
      {stopped ? (
        <p
          role="alert"
          data-testid="board-expansion-stopped"
          className="text-sm text-destructive"
        >
          {t("shoppingPlan.expansion.stopped")}
        </p>
      ) : null}
      {sheetMeal ? null : undoNotice}
      {notice ? (
        <p
          role="status"
          data-testid="board-notice"
          className="rounded-xl bg-secondary px-3 py-2 text-sm"
        >
          {notice}
        </p>
      ) : null}

      {plan.groups.map((item) => {
        const dishes = plan.accepted.filter(
          (card) => !card.isComponent && card.mealType === item.mealType,
        );
        const left = Math.max(0, item.wanted - item.filled);
        return (
          <section
            key={item.mealType}
            data-testid={`board-group-${item.mealType}`}
            aria-labelledby={`board-heading-${item.mealType}`}
            className="surface p-2"
          >
            <div className="flex items-center justify-between gap-2 px-2 pt-1">
              <h2
                id={`board-heading-${item.mealType}`}
                tabIndex={-1}
                className="min-w-0 text-lg font-semibold outline-none"
              >
                {t(`menuBuild.meal.plural.${item.mealType}`)}{" "}
                <span className="text-base font-normal text-muted-foreground">
                  ·{" "}
                  {t("menuBuild.group.count", {
                    filled: item.filled,
                    wanted:
                      counts[item.mealType as keyof MealCounts] ?? item.wanted,
                  })}
                </span>
              </h2>
              <Counter
                compact
                hideLabel
                label={t(`menuBuild.meal.plural.${item.mealType}`)}
                value={counts[item.mealType as keyof MealCounts] ?? item.wanted}
                // A group never goes below what is chosen, and the menu keeps at least one meal.
                min={Math.max(item.filled, totalWanted <= 1 ? 1 : 0)}
                max={
                  MAX_MEALS -
                  totalWanted +
                  (counts[item.mealType as keyof MealCounts] ?? item.wanted)
                }
                onChange={(next) => changeCount(item.mealType, next)}
                testId={`board-count-${item.mealType}`}
              />
            </div>
            <ul>
              {dishes.map((card) => (
                <li key={card.cardId}>
                  <button
                    type="button"
                    data-testid="board-dish"
                    aria-label={t("menuBuild.dish.open", {
                      dish: card.name,
                      meal: t(`shoppingPlan.meal.${item.mealType}`),
                    })}
                    onClick={() => setDish(card)}
                    className="flex min-h-14 w-full flex-col items-start gap-0.5 rounded-lg px-2 py-2 text-left transition-colors hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <span className="line-clamp-2 font-medium">
                      {card.name}
                    </span>
                    <span className="flex flex-wrap items-center gap-x-2 text-sm text-muted-foreground">
                      {card.totalMinutes ? (
                        <span>
                          {t("recipes.generate.minutes", {
                            count: card.totalMinutes,
                          })}
                        </span>
                      ) : null}
                      {card.repeats > 1 ? (
                        <span>
                          {t("menuBuild.board.covers", { count: card.repeats })}
                        </span>
                      ) : null}
                      {card.plannedDate ? (
                        <span className="rounded-full bg-secondary px-2 py-0.5 text-xs text-foreground">
                          {formatMenuDay(card.plannedDate, locale)}
                        </span>
                      ) : null}
                      {card.expansionState === "pending" ? (
                        <span data-testid="board-dish-preparing">
                          {t("menuBuild.board.preparing")}
                        </span>
                      ) : null}
                      {card.expansionState === "failed" ? (
                        <span
                          className="text-destructive"
                          data-testid="board-dish-failed"
                        >
                          {t(
                            card.failureCode === "restriction_blocked"
                              ? "menuBuild.board.blocked"
                              : "menuBuild.board.failed",
                          )}
                        </span>
                      ) : null}
                    </span>
                  </button>
                </li>
              ))}
              {left > 0 ? (
                <li>
                  <button
                    type="button"
                    id={`board-choose-${item.mealType}`}
                    data-testid={`board-choose-${item.mealType}`}
                    onClick={() => openSheet(item.mealType)}
                    className="flex min-h-14 w-full items-center gap-2 rounded-lg px-2 py-2 text-left font-semibold text-primary transition-colors hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <span aria-hidden="true" className="text-xl leading-none">
                      +
                    </span>
                    {t(`menuBuild.choose.${item.mealType}`)}{" "}
                    <span className="font-normal text-muted-foreground">
                      ·{" "}
                      {t(
                        left === 1
                          ? "menuBuild.missing.one"
                          : "menuBuild.missing",
                        { count: left },
                      )}
                    </span>
                  </button>
                </li>
              ) : null}
            </ul>
          </section>
        );
      })}

      {suggestedBases.map((card) => (
        <section
          key={card.cardId}
          data-testid="board-base-suggestion"
          className="space-y-2 rounded-xl border border-border p-3 text-sm"
        >
          <p>
            {t("menuBuild.bases.suggested", {
              base: card.name,
              dishes: listOf(card.component?.usedBy ?? []),
            })}
          </p>
          <div className="flex gap-2">
            <Button
              type="button"
              size="sm"
              variant="secondary"
              data-testid="board-base-add"
              disabled={busyCardId !== null}
              onClick={() => decideBase(card, "accept")}
            >
              {t("menuBuild.bases.add")}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="ghost"
              data-testid="board-base-skip"
              disabled={busyCardId !== null}
              onClick={() => decideBase(card, "reject")}
            >
              {t("menuBuild.bases.skip")}
            </Button>
          </div>
        </section>
      ))}

      {bases.length > 0 ? (
        <section className="space-y-1" data-testid="board-bases">
          <h2 className="text-sm font-semibold text-muted-foreground">
            {t("menuBuild.board.bases")}
          </h2>
          <ul className="text-sm">
            {bases.map((card) => (
              <li key={card.cardId}>{card.name}</li>
            ))}
          </ul>
        </section>
      ) : null}
      {canAddMeal || undated > 0 || dated > 0 ? (
        // Rare, optional changes to the whole menu stay folded, so the board keeps to its dishes.
        <details data-testid="board-more" className="surface p-2">
          <summary className="flex min-h-11 cursor-pointer items-center rounded-lg px-2 font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
            {t("menuBuild.board.more")}
          </summary>
          <div className="space-y-4 px-2 pb-2 pt-2">
            {canAddMeal ? (
              <section className="space-y-2" data-testid="board-add-meal">
                <h2 className="text-sm font-semibold text-muted-foreground">
                  {t("menuBuild.addMeal")}
                </h2>
                <div className="flex flex-wrap gap-2">
                  {MEAL_ORDER.filter(
                    (meal) =>
                      !plan.groups.some((item) => item.mealType === meal),
                  ).map((meal) => (
                    <Button
                      key={meal}
                      type="button"
                      variant="secondary"
                      size="sm"
                      data-testid={`board-add-${meal}`}
                      onClick={() => changeCount(meal, 1, false)}
                    >
                      + {t(`shoppingPlan.meal.${meal}`)}
                    </Button>
                  ))}
                </div>
              </section>
            ) : null}

            {undated > 0 || dated > 0 ? (
              <section className="space-y-1" data-testid="board-spread">
                <div className="flex flex-wrap gap-2">
                  {undated > 0 ? (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      data-testid="board-spread-action"
                      disabled={saving || pending > 0}
                      onClick={() => distribute(false)}
                    >
                      {t("menuBuild.spread")}
                    </Button>
                  ) : null}
                  {dated > 0 ? (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      data-testid="board-spread-clear"
                      disabled={saving}
                      onClick={() => distribute(true)}
                    >
                      {t("menuBuild.spread.clear")}
                    </Button>
                  ) : null}
                </div>
                {undated > 0 ? (
                  <p className="px-3 text-sm text-muted-foreground">
                    {t("menuBuild.spread.hint")}
                  </p>
                ) : null}
              </section>
            ) : null}
          </div>
        </details>
      ) : null}

      <div
        data-testid="board-bar"
        className="safe-b fixed inset-x-0 bottom-0 z-30 border-t border-border bg-background/95 px-4 pt-3"
      >
        <div className="mx-auto max-w-3xl space-y-2 pb-3 sm:px-4">
          <div className="flex items-center justify-between gap-3 text-sm">
            <p
              role="status"
              data-testid="board-progress"
              className="font-medium"
            >
              {t(
                items === 1
                  ? "menuBuild.bar.progress.one"
                  : "menuBuild.bar.progress",
                {
                  filled: plan.progress.mealsFilled,
                  total: plan.progress.mealsTotal,
                  items,
                },
              )}
            </p>
            <Link
              to={`/app/cardapio/${plan.menuId}/compras`}
              state={{ backToPlan: sessionId }}
              data-testid="board-list"
              className="min-h-11 content-center font-medium text-primary underline-offset-4 hover:underline"
            >
              {t("menuBuild.bar.list")}
            </Link>
          </div>
          {pending > 0 ? (
            <p
              className="text-sm text-muted-foreground"
              data-testid="board-preparing"
            >
              {t("menuBuild.bar.preparing", {
                done: ready,
                total: ready + pending,
              })}
            </p>
          ) : null}
          {confirmUse ? (
            <div className="space-y-2" data-testid="board-use-confirm">
              <p className="font-medium">
                {t(
                  missing === 1
                    ? "menuBuild.confirm.missing.one"
                    : "menuBuild.confirm.missing",
                  {
                    count: missing,
                  },
                )}
              </p>
              <div className="grid grid-cols-2 gap-2">
                <Button
                  type="button"
                  variant="secondary"
                  data-testid="board-keep-choosing"
                  onClick={() => setConfirmUse(false)}
                >
                  {t("menuBuild.confirm.keep")}
                </Button>
                <Button
                  type="button"
                  data-testid="board-use-anyway"
                  disabled={saving || pending > 0}
                  onClick={use}
                >
                  {t("menuBuild.confirm.use")}
                </Button>
              </div>
            </div>
          ) : missing > 0 ? (
            // Something is missing: completing is the main way on, using it as it is stays possible.
            <div className="grid grid-cols-2 gap-2">
              <Button
                type="button"
                variant="secondary"
                size="lg"
                data-testid="board-use"
                disabled={saving || pending > 0 || plan.accepted.length === 0}
                onClick={() => setConfirmUse(true)}
              >
                {t("menuBuild.bar.use")}
              </Button>
              <Button
                type="button"
                size="lg"
                data-testid="board-complete"
                disabled={saving}
                onClick={() => {
                  setCompleteFailed(null);
                  setCompleteOpen(true);
                }}
              >
                {t("menuBuild.bar.complete", { count: missing })}
              </Button>
            </div>
          ) : (
            <Button
              type="button"
              size="lg"
              className="w-full"
              data-testid="board-use"
              disabled={saving || pending > 0 || plan.accepted.length === 0}
              onClick={use}
            >
              {t("menuBuild.bar.use")}
            </Button>
          )}
        </div>
      </div>

      {shownMeal ? (
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
      ) : null}

      <DishActionsSheet
        plan={plan}
        dish={dish}
        open={dish !== null}
        onOpenChange={(open) => {
          if (!open) {
            setDish(null);
            setDishError(null);
          }
        }}
        people={dish ? dishPeople(dish) : plan.setup.people}
        busy={saving}
        canRetry={
          dish?.expansionState === "failed" &&
          dish.failureCode !== "restriction_blocked"
        }
        notice={
          dishError ? (
            <p role="alert" className="text-sm text-destructive">
              {t(dishError)}
            </p>
          ) : null
        }
        actions={dishActions}
      />

      <CompleteSheet
        plan={plan}
        open={completeOpen}
        onOpenChange={setCompleteOpen}
        running={running !== null}
        busy={saving}
        onAsk={(mealType, kind) => ask({ mealType, kind })}
        onConfirm={complete}
        notice={
          completeFailed ? (
            <div
              role="alert"
              data-testid="complete-failed"
              className="space-y-1 rounded-xl bg-secondary p-3 text-sm"
            >
              {completeFailed.length > 0 ? (
                <p>
                  {t("menuBuild.complete.partial", {
                    dishes: listOf(completeFailed),
                  })}
                </p>
              ) : (
                <p>{t("shoppingPlan.error.save")}</p>
              )}
              {missing > 0 ? (
                <p>
                  {t(
                    missing === 1
                      ? "menuBuild.complete.left.one"
                      : "menuBuild.complete.left",
                    { count: missing },
                  )}
                </p>
              ) : null}
            </div>
          ) : null
        }
      />
    </div>
  );
}
