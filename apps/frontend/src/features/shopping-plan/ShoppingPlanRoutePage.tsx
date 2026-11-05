import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { RecipeApiError } from "@/contracts/recipes";
import type {
  ShoppingPlanRoute,
  ShoppingPlanRouteProgress,
  ShoppingPlanRouteRecipe,
  ShoppingPlanRouteTask,
} from "@/contracts/shoppingPlan";
import {
  alertTimerDone,
  formatClock,
  StageTimer,
  useWakeLock,
} from "@/features/recipes/CookingGuide";
import { useRecipeQuantity } from "@/features/recipes/RecipeContent";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import { useShoppingPlanRepository } from "./ShoppingPlanProvider";

const finishLink = (executionId: string, sessionId: string) =>
  `/app/receitas/cozinhar/${executionId}?rota=${sessionId}&concluir=1`;

/** Removes a task from every part of the progress, as "previous" needs. */
function without(
  progress: ShoppingPlanRouteProgress,
  taskKey: string,
): ShoppingPlanRouteProgress {
  return {
    done: progress.done.filter((key) => key !== taskKey),
    skipped: progress.skipped.filter((key) => key !== taskKey),
    timers: progress.timers.filter((timer) => timer.taskKey !== taskKey),
  };
}

/**
 * The preparation session (PLAN-0062 Slice 6): the plan's dishes merged into one order of work, one
 * task per screen. Hands-on parts never overlap, and passive parts keep running as timers while the
 * next task is on screen. Timers count against a target time, so they stay right after a reload or
 * when a phone throttles the tab. The first task of a dish starts its cooking session, and every dish
 * is then finished on its normal cooking screen, bases first, so what a base makes is in the pantry
 * for the dishes that use it. Nothing here changes the pantry: only those finish screens do.
 */
export function ShoppingPlanRoutePage() {
  const { sessionId = "" } = useParams<{ sessionId: string }>();
  const repo = useShoppingPlanRepository();
  const recipes = useRecipeRepository();
  const { t } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const quantity = useRecipeQuantity();
  const [route, setRoute] = useState<ShoppingPlanRoute | null>(null);
  const [progress, setProgress] = useState<ShoppingPlanRouteProgress>({
    done: [],
    skipped: [],
    timers: [],
  });
  const [started, setStarted] = useState(false);
  const [failed, setFailed] = useState(false);
  const [saveFailed, setSaveFailed] = useState(false);
  const [startFailed, setStartFailed] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const [announcement, setAnnouncement] = useState("");
  const startedEntries = useRef(new Set<string>());
  const finishedTimers = useRef(new Set<string>());
  // Saves run in order, so an old state can never arrive after a newer one.
  const saves = useRef<Promise<void>>(Promise.resolve());
  const heading = useRef<HTMLHeadingElement>(null);
  const csrfToken = session.csrfToken;

  useEffect(() => {
    const controller = new AbortController();
    repo.getRoute(sessionId, controller.signal).then(
      (value) => {
        if (controller.signal.aborted) return;
        setRoute(value);
        setProgress(value.progress);
        // Someone who has begun comes back to where they stopped, not to the overview.
        setStarted(
          value.progress.done.length + value.progress.skipped.length > 0,
        );
        // A wait that ended while the page was closed needs no alarm now.
        finishedTimers.current = new Set(
          value.progress.timers
            .filter((timer) => Date.parse(timer.endsAt) <= Date.now())
            .map((timer) => timer.taskKey),
        );
      },
      (err: unknown) => {
        if (controller.signal.aborted) return;
        // A plan not yet accepted has no session: its place is the review.
        if (err instanceof RecipeApiError && err.status === 422) {
          navigate(`/app/compras/planejar/${sessionId}/revisar`, {
            replace: true,
          });
          return;
        }
        setFailed(true);
      },
    );
    return () => controller.abort();
  }, [repo, sessionId, navigate]);

  const tasks = route?.tasks;
  const current = tasks?.find(
    (task) =>
      !progress.done.includes(task.taskKey) &&
      !progress.skipped.includes(task.taskKey),
  );
  const cooking = started && route !== null;
  useWakeLock(cooking && (current !== undefined || progress.timers.length > 0));

  const hasTimers = progress.timers.length > 0;
  useEffect(() => {
    if (!hasTimers) return;
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [hasTimers]);

  const taskName = useCallback(
    (taskKey: string) =>
      tasks?.find((task) => task.taskKey === taskKey)?.name ?? "",
    [tasks],
  );

  useEffect(() => {
    for (const timer of progress.timers) {
      if (
        Date.parse(timer.endsAt) <= now &&
        !finishedTimers.current.has(timer.taskKey)
      ) {
        finishedTimers.current.add(timer.taskKey);
        alertTimerDone();
        setAnnouncement(
          t("shoppingPlan.route.timerDone", { task: taskName(timer.taskKey) }),
        );
      }
    }
  }, [now, progress.timers, t, taskName]);

  const refresh = useCallback(async () => {
    const value = await repo.getRoute(sessionId);
    // Only the dishes change here. Local progress stays, because it may be newer than the saved one.
    setRoute(value);
  }, [repo, sessionId]);

  // The first task of a dish starts its cooking session, so the menu shows it as cooking.
  const currentRecipe = route?.recipes.find(
    (recipe) => recipe.entryId === current?.entryId,
  );
  useEffect(() => {
    if (
      !cooking ||
      !csrfToken ||
      !currentRecipe ||
      currentRecipe.status !== "planned" ||
      startedEntries.current.has(currentRecipe.entryId)
    )
      return;
    startedEntries.current.add(currentRecipe.entryId);
    recipes
      .startExecution(currentRecipe.recipeId, {
        csrfToken,
        servings: currentRecipe.servings,
        menuEntryId: currentRecipe.entryId,
      })
      .then(refresh)
      .then(
        () => setStartFailed(null),
        // The route goes on; the finish step tries again.
        () => setStartFailed(currentRecipe.name),
      );
  }, [cooking, csrfToken, currentRecipe, recipes, refresh]);

  useEffect(() => {
    if (cooking) heading.current?.focus();
  }, [cooking, current?.taskKey]);

  const persist = (next: ShoppingPlanRouteProgress) => {
    setProgress(next);
    if (!csrfToken) return;
    saves.current = saves.current.then(() =>
      repo.saveRouteProgress(sessionId, next, { csrfToken }).then(
        () => setSaveFailed(false),
        () => setSaveFailed(true),
      ),
    );
    window.scrollTo?.({ top: 0 });
  };

  if (failed) {
    return (
      <p role="alert" className="text-muted-foreground">
        {t("shoppingPlan.error.load")}
      </p>
    );
  }
  if (!route || !csrfToken) return <p role="status">{t("planning.loading")}</p>;

  const recipeOf = (task: ShoppingPlanRouteTask) =>
    route.recipes.find((recipe) => recipe.entryId === task.entryId);
  const position = current ? route.tasks.indexOf(current) : route.tasks.length;

  const finishTask = (task: ShoppingPlanRouteTask) => {
    const startedAt = Date.now();
    setNow(startedAt);
    persist({
      ...progress,
      done: [...progress.done, task.taskKey],
      timers:
        task.passiveMinutes > 0
          ? [
              ...progress.timers,
              {
                taskKey: task.taskKey,
                endsAt: new Date(
                  startedAt + task.passiveMinutes * 60_000,
                ).toISOString(),
              },
            ]
          : progress.timers,
    });
  };
  const skipTask = (task: ShoppingPlanRouteTask) =>
    persist({ ...progress, skipped: [...progress.skipped, task.taskKey] });
  const dismissTimer = (taskKey: string) =>
    persist({
      ...progress,
      timers: progress.timers.filter((timer) => timer.taskKey !== taskKey),
    });

  // Tasks of a dish already cooked cannot be undone from here.
  const cookedEntries = new Set(
    route.recipes
      .filter((recipe) => recipe.status === "cooked")
      .map((recipe) => recipe.entryId),
  );
  const previous = route.tasks
    .slice(0, position)
    .reverse()
    .find((task) => !cookedEntries.has(task.entryId));

  const timers = (
    <RunningTimers
      timers={progress.timers}
      now={now}
      taskName={taskName}
      onDismiss={dismissTimer}
    />
  );
  const notices = (
    <>
      <p role="status" aria-live="polite" className="sr-only">
        {announcement}
      </p>
      {saveFailed ? (
        <p role="alert" className="text-sm text-destructive">
          {t("shoppingPlan.route.saveFailed")}
        </p>
      ) : null}
      {startFailed ? (
        <p role="alert" className="text-sm text-destructive">
          {t("shoppingPlan.route.startFailed", { dish: startFailed })}
        </p>
      ) : null}
    </>
  );

  if (route.recipes.length === 0) {
    return (
      <div data-testid="plan-route-empty" className="space-y-4">
        <h1 className="font-display text-3xl">
          {t("shoppingPlan.route.title")}
        </h1>
        <p className="text-muted-foreground">{t("shoppingPlan.route.empty")}</p>
        <Button asChild variant="secondary">
          <Link to={`/app/cardapio/${route.menuId}`}>
            {t("shoppingPlan.route.toMenu")}
          </Link>
        </Button>
      </div>
    );
  }

  if (!started) {
    return (
      <div data-testid="plan-route-overview" className="space-y-6">
        <header>
          <h1 className="font-display text-3xl">
            {t("shoppingPlan.route.title")}
          </h1>
          <p className="mt-1 text-muted-foreground">
            {t("shoppingPlan.route.summary", {
              dishes: route.recipes.length,
              minutes: route.totalMinutes,
            })}
          </p>
        </header>
        {route.overBudget ? (
          <p data-testid="plan-route-over-budget" className="surface p-4">
            {t("shoppingPlan.route.overBudget", {
              minutes: route.totalMinutes,
              budget: route.budgetMinutes ?? 0,
            })}
          </p>
        ) : null}
        <ol className="surface divide-y divide-border">
          {route.tasks.map((task) => (
            <li
              key={task.taskKey}
              data-testid="plan-route-overview-task"
              className="flex gap-3 p-4"
            >
              <span className="tabular w-16 shrink-0 text-sm font-semibold text-muted-foreground">
                {t("shoppingPlan.route.atMinute", {
                  minute: task.startMinute,
                })}
              </span>
              <span className="min-w-0">
                <span className="block font-semibold">{task.name}</span>
                <span className="block text-sm text-muted-foreground">
                  {recipeOf(task)?.name}
                </span>
              </span>
            </li>
          ))}
        </ol>
        <p className="text-sm text-muted-foreground">
          {t("shoppingPlan.route.equipment")}
        </p>
        <Button
          type="button"
          size="lg"
          className="w-full"
          data-testid="plan-route-start"
          onClick={() => setStarted(true)}
        >
          {t("shoppingPlan.route.start")}
        </Button>
      </div>
    );
  }

  if (!current) {
    return (
      <FinishStep
        route={route}
        busy={busy}
        heading={heading}
        timers={timers}
        notices={notices}
        onFinish={(recipe) => {
          if (busy) return;
          setBusy(true);
          setStartFailed(null);
          const open =
            recipe.status === "cooking" && recipe.executionId
              ? Promise.resolve(recipe.executionId)
              : recipes
                  .startExecution(recipe.recipeId, {
                    csrfToken,
                    servings: recipe.servings,
                    menuEntryId: recipe.entryId,
                  })
                  .then((execution) => execution.executionId);
          open.then(
            (executionId) => navigate(finishLink(executionId, sessionId)),
            () => {
              setStartFailed(recipe.name);
              setBusy(false);
            },
          );
        }}
      />
    );
  }

  const recipe = recipeOf(current);
  const next = route.tasks[position + 1];
  const waitingFor = current.dependsOn.filter((key) =>
    progress.timers.some(
      (timer) => timer.taskKey === key && Date.parse(timer.endsAt) > now,
    ),
  );
  return (
    <div data-testid="plan-route-task" className="space-y-6">
      <div className="space-y-2">
        <p className="text-sm font-semibold text-muted-foreground">
          {t("shoppingPlan.route.position", {
            n: position + 1,
            total: route.tasks.length,
          })}
        </p>
        <div
          className="h-1.5 overflow-hidden rounded-full bg-secondary"
          aria-hidden="true"
        >
          <div
            className="h-full rounded-full bg-primary"
            style={{
              width: `${((position + 1) / route.tasks.length) * 100}%`,
            }}
          />
        </div>
        <p className="font-semibold" data-testid="plan-route-dish">
          {recipe?.name}
        </p>
        <h1
          ref={heading}
          tabIndex={-1}
          className="font-display text-3xl outline-none"
          data-testid="plan-route-task-name"
        >
          {current.name}
        </h1>
      </div>
      {notices}
      {timers}
      {waitingFor.length > 0 ? (
        <p data-testid="plan-route-waiting" className="surface p-4">
          {t("shoppingPlan.route.waiting", {
            tasks: waitingFor.map(taskName).join(", "),
          })}
        </p>
      ) : null}
      <div className="space-y-3">
        {current.instructions
          .split(/\n+/)
          .map((line) => line.trim())
          .filter(Boolean)
          .map((line, index) => (
            <p key={index} className="text-[19px] leading-relaxed">
              {line}
            </p>
          ))}
      </div>
      {current.sensoryCues.length > 0 ? (
        <div className="rounded-2xl bg-accent p-4">
          <p className="font-semibold">{t("cooking.guide.look")}</p>
          <p className="mt-1">{current.sensoryCues.join("; ")}</p>
        </div>
      ) : null}
      {recipe && recipe.ingredients.length > 0 ? (
        <details data-testid="plan-route-ingredients">
          <summary className="min-h-11 cursor-pointer py-2 font-semibold">
            {t("shoppingPlan.route.ingredients", { dish: recipe.name })}
          </summary>
          <ul className="space-y-1 text-muted-foreground">
            {recipe.ingredients.map((ingredient, index) => (
              <li key={index}>
                {ingredient.name}: {quantity(ingredient)}
                {ingredient.optional
                  ? ` · ${t("recipes.detail.optional")}`
                  : ""}
              </li>
            ))}
          </ul>
        </details>
      ) : null}
      {current.activeMinutes > 0 ? (
        <StageTimer key={current.taskKey} minutes={current.activeMinutes} />
      ) : null}
      {next ? (
        <p data-testid="plan-route-next" className="text-muted-foreground">
          {t(
            next.startMinute < current.endMinute && current.passiveMinutes > 0
              ? "shoppingPlan.route.meanwhile"
              : "shoppingPlan.route.afterThis",
            { task: next.name, dish: recipeOf(next)?.name ?? "" },
          )}
        </p>
      ) : null}
      <div className="space-y-3">
        <Button
          type="button"
          size="lg"
          className="w-full"
          data-testid="plan-route-done"
          onClick={() => finishTask(current)}
        >
          {current.passiveMinutes > 0
            ? t("shoppingPlan.route.leaveRunning", {
                minutes: current.passiveMinutes,
              })
            : t("shoppingPlan.route.done")}
        </Button>
        <div className="flex gap-2">
          {previous ? (
            <Button
              type="button"
              variant="secondary"
              className="flex-1"
              data-testid="plan-route-previous"
              onClick={() => persist(without(progress, previous.taskKey))}
            >
              {t("cooking.guide.previous")}
            </Button>
          ) : null}
          <Button
            type="button"
            variant="ghost"
            className="flex-1"
            data-testid="plan-route-skip"
            onClick={() => skipTask(current)}
          >
            {t("shoppingPlan.route.skip")}
          </Button>
        </div>
      </div>
    </div>
  );
}

/** Waits running in the background, each with its countdown, and "pronto" once it has ended. */
function RunningTimers({
  timers,
  now,
  taskName,
  onDismiss,
}: {
  timers: ShoppingPlanRouteProgress["timers"];
  now: number;
  taskName: (taskKey: string) => string;
  onDismiss: (taskKey: string) => void;
}) {
  const { t } = useProductionI18n();
  if (timers.length === 0) return null;
  return (
    <ul className="space-y-2" aria-label={t("shoppingPlan.route.running")}>
      {timers.map((timer) => {
        const left = Math.max(
          0,
          Math.ceil((Date.parse(timer.endsAt) - now) / 1000),
        );
        return (
          <li
            key={timer.taskKey}
            data-testid="plan-route-timer"
            className="flex items-center justify-between gap-3 rounded-2xl bg-secondary p-3"
          >
            <span className="min-w-0 font-semibold">
              {taskName(timer.taskKey)}
            </span>
            {left > 0 ? (
              <span className="tabular shrink-0" aria-hidden="true">
                {formatClock(left)}
              </span>
            ) : (
              <Button
                type="button"
                size="sm"
                variant="secondary"
                data-testid="plan-route-timer-dismiss"
                onClick={() => onDismiss(timer.taskKey)}
              >
                {t("shoppingPlan.route.timerOk")}
              </Button>
            )}
          </li>
        );
      })}
    </ul>
  );
}

/** With the work done, each dish is finished in turn on its cooking screen, bases first. */
function FinishStep({
  route,
  busy,
  heading,
  timers,
  notices,
  onFinish,
}: {
  route: ShoppingPlanRoute;
  busy: boolean;
  heading: React.RefObject<HTMLHeadingElement | null>;
  timers: React.ReactNode;
  notices: React.ReactNode;
  onFinish: (recipe: ShoppingPlanRouteRecipe) => void;
}) {
  const { t } = useProductionI18n();
  const nextRecipe = route.recipes.find((recipe) => recipe.status !== "cooked");
  return (
    <div data-testid="plan-route-finish" className="space-y-6">
      <h1
        ref={heading}
        tabIndex={-1}
        className="font-display text-3xl outline-none"
      >
        {t(
          nextRecipe
            ? "shoppingPlan.route.finishTitle"
            : "shoppingPlan.route.allDone",
        )}
      </h1>
      {notices}
      {timers}
      <ul className="surface divide-y divide-border">
        {route.recipes.map((recipe) => (
          <li
            key={recipe.entryId}
            data-testid="plan-route-finish-dish"
            data-status={recipe.status}
            className="flex items-center justify-between gap-3 p-4"
          >
            <span className="min-w-0 font-semibold">{recipe.name}</span>
            <span className="shrink-0 text-sm text-muted-foreground">
              {t(
                recipe.status === "cooked"
                  ? "shoppingPlan.route.dishDone"
                  : "shoppingPlan.route.dishPending",
              )}
            </span>
          </li>
        ))}
      </ul>
      {nextRecipe ? (
        <Button
          type="button"
          size="lg"
          className="w-full"
          disabled={busy}
          data-testid="plan-route-finish-next"
          onClick={() => onFinish(nextRecipe)}
        >
          {t("shoppingPlan.route.finishDish", { dish: nextRecipe.name })}
        </Button>
      ) : (
        <div className="flex flex-wrap gap-2">
          <Button asChild data-testid="plan-route-complete">
            <Link to="/app/hoje">{t("cooking.done.home")}</Link>
          </Button>
          <Button asChild variant="secondary">
            <Link to="/app/despensa">{t("cooking.done.pantry")}</Link>
          </Button>
        </div>
      )}
    </div>
  );
}
