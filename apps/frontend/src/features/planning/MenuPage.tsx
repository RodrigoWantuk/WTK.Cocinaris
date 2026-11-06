import { ChevronLeft } from "lucide-react";
import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import type { Menu, MenuEntry, ShoppingList } from "@/contracts/planning";
import type { RecipeSummary } from "@/contracts/recipes";
import { MEAL_ORDER, type ShoppingPlan } from "@/contracts/shoppingPlan";
import { PushOptIn } from "@/features/notifications/PushOptIn";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import { RecipeThumbnail } from "@/features/recipes/RecipeThumbnail";
import { mealTypeLabel } from "@/features/recipes/recipeLabels";
import { useOptionalShoppingPlanRepository } from "@/features/shopping-plan/ShoppingPlanProvider";
import { formatLocaleDecimal } from "@/lib/localeDecimal";
import { CookAheadSheet } from "./CookAheadSheet";
import { MenuChoosing, type MenuChoosingRequest } from "./MenuChoosing";
import { MenuEntrySheet } from "./MenuEntrySheet";
import { nextPlanningStep, type PlanningStep } from "./planningOverview";
import {
  daysBetween,
  formatMenuDay,
  formatMenuPeriod,
  localIsoDate,
} from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";

/** Portions wait this long, so quick taps send only the last number. */
const SERVINGS_DELAY_MS = 600;
/** The line about what is left appears this many days before the period ends. */
const END_LINE_DAYS = 3;

const isOpenDish = (entry: MenuEntry) =>
  entry.status === "planned" || entry.status === "cooking";

const byOrder = (a: MenuEntry, b: MenuEntry) =>
  (a.suggestedOrder ?? Number.MAX_SAFE_INTEGER) -
  (b.suggestedOrder ?? Number.MAX_SAFE_INTEGER);

/**
 * Meals still missing per meal type, from what the menu's plan wants and the dishes on the menu.
 * Lunch and dinner share what passes one of them, as the server counts them. Pure.
 *
 * @param counts How many of each meal the plan wants.
 * @param entries The menu's dishes.
 */
export function missingByMeal(
  counts: Record<string, number>,
  entries: MenuEntry[],
): Record<string, number> {
  const filled: Record<string, number> = {};
  for (const entry of entries) {
    if (entry.kind === "component" || entry.status === "skipped") continue;
    if (!entry.mealType) continue;
    filled[entry.mealType] = (filled[entry.mealType] ?? 0) + entry.mealsCovered;
  }
  for (const [meal, other] of [
    ["lunch", "dinner"],
    ["dinner", "lunch"],
  ] as const) {
    const extra = (filled[meal] ?? 0) - (counts[meal] ?? 0);
    if (extra > 0 && (counts[other] ?? 0) > 0) {
      filled[meal] -= extra;
      filled[other] = (filled[other] ?? 0) + extra;
    }
  }
  return Object.fromEntries(
    Object.entries(counts).map(([meal, wanted]) => [
      meal,
      Math.max(0, wanted - (filled[meal] ?? 0)),
    ]),
  );
}

/**
 * One menu (PLAN-0065 Slice 4, after PLAN-0029). It opens with the next step, as on "Planejar", then
 * today's dishes, later dated dishes, and "Quando quiser": undated dishes by meal type, with what is
 * still missing. Empty days do not appear. "Cozinhar" is the main action only on today's or the next
 * dish; every other action is in the dish's sheet, one explicit command on that dish. On a menu in use
 * a dish can be swapped and a missing meal filled through the options sheet, and several dishes can be
 * cooked ahead on one day. An archived menu is read-only.
 */
export function MenuPage() {
  const { menuId = "" } = useParams<{ menuId: string }>();
  const repo = usePlanningRepository();
  const plans = useOptionalShoppingPlanRepository();
  const recipes = useRecipeRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const csrfToken = session.csrfToken;
  const [menu, setMenu] = useState<Menu | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [version, setVersion] = useState(0);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [link, setLink] = useState<{
    sessionId: string;
    status: string;
    counts: Record<string, number>;
  } | null>(null);
  const [list, setList] = useState<ShoppingList | null>(null);
  const [routeSessionId, setRouteSessionId] = useState<string | null>(null);
  const [routeVersion, setRouteVersion] = useState(0);
  const [openEntryId, setOpenEntryId] = useState<string | null>(null);
  const [servingsDraft, setServingsDraft] = useState<{
    entryId: string;
    servings: number;
  } | null>(null);
  const [prepareOpen, setPrepareOpen] = useState(false);
  const [prepareError, setPrepareError] = useState<string | null>(null);
  const [request, setRequest] = useState<MenuChoosingRequest | null>(null);
  const [swapped, setSwapped] = useState<MenuEntry | null>(null);
  const servingsTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Dish images (PLAN-0058); a failed read only leaves the placeholders.
  const [thumbnails, setThumbnails] = useState<
    Map<string, RecipeSummary["thumbnail"]>
  >(new Map());
  const today = localIsoDate();

  useEffect(() => {
    const controller = new AbortController();
    recipes
      .listRecipes(controller.signal)
      .then(
        (items) =>
          !controller.signal.aborted &&
          setThumbnails(
            new Map(items.map((recipe) => [recipe.recipeId, recipe.thumbnail])),
          ),
      )
      .catch(() => undefined);
    return () => controller.abort();
  }, [recipes]);

  useEffect(() => {
    const controller = new AbortController();
    repo.getMenu(menuId, controller.signal).then(
      (value) => !controller.signal.aborted && setMenu(value),
      () => !controller.signal.aborted && setLoadError(true),
    );
    return () => controller.abort();
  }, [repo, menuId, version]);

  // The plan behind the menu says how many of each meal it wants; a menu built by hand has none.
  useEffect(() => {
    if (!plans) return;
    const controller = new AbortController();
    plans.getMenuLink(menuId, controller.signal).then(
      (value) =>
        !controller.signal.aborted &&
        setLink(
          value
            ? {
                sessionId: value.sessionId,
                status: value.status,
                counts: Object.fromEntries(
                  Object.entries(value.mealCounts).filter(
                    (item): item is [string, number] => item[1] !== undefined,
                  ),
                ),
              }
            : null,
        ),
      () => undefined,
    );
    return () => controller.abort();
  }, [plans, menuId]);

  const accepted = menu?.status === "accepted";
  // The next step reads the list and the preparation session; each failure only hides its step.
  useEffect(() => {
    if (!accepted) return;
    const controller = new AbortController();
    repo.getShopping(menuId, today, controller.signal).then(
      (value) => !controller.signal.aborted && setList(value),
      () => undefined,
    );
    return () => controller.abort();
  }, [repo, menuId, today, accepted, version]);

  useEffect(() => {
    if (!accepted || !plans) return;
    const controller = new AbortController();
    plans.findRouteLink({ menuId }, controller.signal).then(
      (value) => !controller.signal.aborted && setRouteSessionId(value),
      () => undefined,
    );
    return () => controller.abort();
  }, [plans, menuId, accepted, routeVersion]);

  useEffect(
    () => () => {
      if (servingsTimer.current) clearTimeout(servingsTimer.current);
    },
    [],
  );

  const reload = useCallback(() => setVersion((value) => value + 1), []);

  const mutate = useCallback(
    async (change: (token: string) => Promise<Menu>) => {
      if (!csrfToken) return false;
      setBusy(true);
      setActionError(null);
      try {
        setMenu(await change(csrfToken));
        return true;
      } catch {
        setActionError("planning.error.save");
        return false;
      } finally {
        setBusy(false);
      }
    },
    [csrfToken],
  );

  const cook = async (entry: MenuEntry) => {
    if (entry.status === "cooking" && entry.executionId) {
      navigate(`/app/receitas/cozinhar/${entry.executionId}`);
      return;
    }
    if (!csrfToken) return;
    setBusy(true);
    setActionError(null);
    try {
      const execution = await recipes.startExecution(entry.recipeId, {
        csrfToken,
        servings: entry.servings,
        menuEntryId: entry.entryId,
      });
      navigate(`/app/receitas/cozinhar/${execution.executionId}`);
    } catch {
      setActionError("planning.error.cook");
      setBusy(false);
    }
  };

  if (loadError) {
    return (
      <div data-testid="menu-error" className="space-y-4">
        <FeatureUnavailable
          feature="planning-menu"
          title={t("feature.unavailable")}
          detail={t("planning.error.load")}
        />
        <Button asChild variant="secondary">
          <Link to="/app/cardapio">{t("planning.back")}</Link>
        </Button>
      </div>
    );
  }

  if (!menu) {
    return (
      <div role="status" data-testid="menu-loading">
        <p>{t("planning.loading")}</p>
      </div>
    );
  }

  const editable = menu.status !== "archived";
  const canChoose = accepted && plans !== null;
  const dishes = menu.entries.filter((entry) => entry.kind !== "component");
  const bases = menu.entries.filter((entry) => entry.kind === "component");
  const todays = dishes.filter((entry) => entry.plannedDate === today);
  const later = dishes.filter(
    (entry) => entry.plannedDate !== null && entry.plannedDate > today,
  );
  const past = dishes.filter(
    (entry) => entry.plannedDate !== null && entry.plannedDate < today,
  );
  const undated = dishes.filter((entry) => entry.plannedDate === null);
  const missing =
    canChoose && link ? missingByMeal(link.counts, menu.entries) : {};
  const wheneverMeals = [
    ...MEAL_ORDER.filter(
      (meal) =>
        undated.some((entry) => entry.mealType === meal) ||
        (missing[meal] ?? 0) > 0,
    ),
    ...(undated.some((entry) => !entry.mealType) ? ["none"] : []),
  ];
  // Today's first dish to cook, else the next one in the menu's order.
  const next =
    todays.find(isOpenDish) ??
    [...undated].filter(isOpenDish).sort(byOrder)[0] ??
    later.find(isOpenDish) ??
    null;

  const step: PlanningStep | null = accepted
    ? nextPlanningStep({
        planInProgress: false,
        menu: {
          menuId: menu.menuId,
          name: menu.name,
          startDate: menu.startDate,
          endDate: menu.endDate,
          status: menu.status,
          entryCount: menu.entries.length,
          updatedAt: menu.updatedAt,
        },
        list,
        prepareSessionId: routeSessionId,
      })
    : null;
  const stepLink = stepAction(step, menu.menuId, routeSessionId);

  const openEntry =
    menu.entries.find((entry) => entry.entryId === openEntryId) ?? null;
  const entryInput = (entry: MenuEntry) => ({
    plannedDate: entry.plannedDate,
    mealType: entry.mealType,
    servings: entry.servings,
    status: entry.status === "skipped" ? "skipped" : "planned",
  });
  const change = (
    entry: MenuEntry,
    patch: Partial<ReturnType<typeof entryInput>> & {
      reserveIngredients?: boolean;
    },
  ) =>
    void mutate((token) =>
      repo.changeEntry(
        menu.menuId,
        entry.entryId,
        { ...entryInput(entry), ...patch },
        { csrfToken: token },
      ),
    );

  const actions = {
    cook: (entry: MenuEntry) => void cook(entry),
    swap: (entry: MenuEntry) => {
      if (!entry.mealType) return;
      setOpenEntryId(null);
      setSwapped(null);
      setRequest({
        nonce: Date.now(),
        mealType: entry.mealType,
        replace: entry,
      });
    },
    setDay: (entry: MenuEntry, day: string | null) =>
      change(entry, { plannedDate: day }),
    setServings: (entry: MenuEntry, servings: number) => {
      setServingsDraft({ entryId: entry.entryId, servings });
      if (servingsTimer.current) clearTimeout(servingsTimer.current);
      servingsTimer.current = setTimeout(() => {
        void mutate((token) =>
          repo.changeEntry(
            menu.menuId,
            entry.entryId,
            { ...entryInput(entry), servings },
            { csrfToken: token },
          ),
        ).finally(() => setServingsDraft(null));
      }, SERVINGS_DELAY_MS);
    },
    setReserve: (entry: MenuEntry, reserve: boolean) =>
      change(entry, { reserveIngredients: reserve }),
    toggleSkip: (entry: MenuEntry) =>
      change(entry, {
        status: entry.status === "skipped" ? "planned" : "skipped",
      }),
    remove: (entry: MenuEntry) => {
      setOpenEntryId(null);
      void mutate((token) =>
        repo.removeEntry(menu.menuId, entry.entryId, { csrfToken: token }),
      );
    },
  };

  // A swapped dish can come back as it was, as long as nothing took its place.
  const undoSwap = (entry: MenuEntry) => {
    setSwapped(null);
    void mutate((token) =>
      repo.addEntry(
        menu.menuId,
        {
          recipeId: entry.recipeId,
          plannedDate: entry.plannedDate,
          mealType: entry.mealType,
          servings: entry.servings,
        },
        { csrfToken: token, idempotencyKey: crypto.randomUUID() },
      ),
    );
  };

  const onPlan = (plan: ShoppingPlan) => {
    setLink({
      sessionId: plan.sessionId,
      status: plan.status,
      counts: Object.fromEntries(
        plan.groups.map((group) => [group.mealType, group.wanted]),
      ),
    });
    const group = swapped
      ? plan.groups.find((item) => item.mealType === swapped.mealType)
      : undefined;
    if (group && group.filled >= group.wanted) setSwapped(null);
  };

  const row = (
    entry: MenuEntry,
    context: { meal?: boolean; day?: boolean },
  ) => (
    <EntryRow
      key={entry.entryId}
      entry={entry}
      today={today}
      thumbnail={thumbnails.get(entry.recipeId)}
      showMeal={context.meal ?? true}
      showDay={context.day ?? false}
      cookNow={editable && next?.entryId === entry.entryId}
      // One main action per screen: the next step, when there is one, outranks cooking.
      cookVariant={stepLink ? "secondary" : "default"}
      busy={busy}
      onOpen={() => setOpenEntryId(entry.entryId)}
      onCook={() => void cook(entry)}
    />
  );

  const laterDays = [
    ...new Set(later.map((entry) => entry.plannedDate!)),
  ].sort();
  const pastDays = [...new Set(past.map((entry) => entry.plannedDate!))].sort();
  const endLine = accepted ? endOfPeriodLine(menu, dishes, today, t) : null;

  return (
    <div data-testid="menu-page" className="space-y-6 pb-8">
      <header className="space-y-2">
        <Link
          to="/app/cardapio"
          className="inline-flex min-h-11 items-center gap-1 text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline"
        >
          <ChevronLeft aria-hidden="true" className="h-4 w-4" />
          {t("planning.back")}
        </Link>
        <h1 className="font-display text-3xl">
          {menu.name || t("planning.title")}
        </h1>
        <p className="text-muted-foreground">
          {formatMenuPeriod(menu.startDate, menu.endDate, locale)} ·{" "}
          <span data-testid="menu-status">
            {t(`planning.status.${menu.status}`)}
          </span>
        </p>
        {endLine ? (
          <p className="text-sm font-medium" data-testid="menu-end-line">
            {endLine}
          </p>
        ) : null}
      </header>

      <section aria-label={t("menuUse.next")} className="space-y-2">
        {menu.status === "draft" ? (
          link &&
          (link.status === "building" || link.status === "reviewing") ? (
            <Button asChild size="lg" className="w-full sm:w-auto">
              <Link
                to={`/app/compras/planejar/${link.sessionId}`}
                data-testid="menu-continue"
              >
                {t("menuUse.draft.continue")}
              </Link>
            </Button>
          ) : (
            <Button
              type="button"
              size="lg"
              className="w-full sm:w-auto"
              data-testid="menu-accept"
              disabled={busy || menu.entries.length === 0}
              onClick={() =>
                void mutate((token) =>
                  repo.acceptMenu(menu.menuId, { csrfToken: token }),
                )
              }
            >
              {t("planning.accept")}
            </Button>
          )
        ) : null}
        {stepLink ? (
          <Button asChild size="lg" className="w-full sm:w-auto">
            <Link to={stepLink.to} data-testid={stepLink.testId}>
              {t(stepLink.labelKey, { count: stepLink.count ?? 0 })}
            </Link>
          </Button>
        ) : null}
        {accepted ? (
          <div className="flex flex-wrap gap-x-4">
            {stepLink?.testId !== "menu-step-buy" ? (
              <Link
                to={`/app/cardapio/${menu.menuId}/compras`}
                data-testid="menu-shopping-link"
                className="inline-flex min-h-11 items-center font-medium text-primary underline-offset-4 hover:underline"
              >
                {t("planning.shoppingLink")}
              </Link>
            ) : null}
            {routeSessionId && stepLink?.testId !== "session-route-link" ? (
              <Link
                to={`/app/compras/planejar/${routeSessionId}/preparo`}
                data-testid="session-route-link"
                className="inline-flex min-h-11 items-center font-medium text-primary underline-offset-4 hover:underline"
              >
                {t("shoppingPlan.route.open")}
              </Link>
            ) : null}
          </div>
        ) : menu.status === "draft" ? (
          <Link
            to={`/app/cardapio/${menu.menuId}/compras`}
            data-testid="menu-shopping-link"
            className="inline-flex min-h-11 items-center font-medium text-primary underline-offset-4 hover:underline"
          >
            {t("planning.shoppingLink")}
          </Link>
        ) : null}
      </section>

      {accepted ? <PushOptIn variant="prompt" /> : null}
      {actionError ? (
        <p role="alert" className="text-sm text-destructive">
          {t(actionError)}
        </p>
      ) : null}
      {swapped ? (
        <div
          role="status"
          data-testid="menu-swap-undo"
          className="flex items-center justify-between gap-3 rounded-xl bg-secondary px-3 py-2 text-sm"
        >
          <span className="min-w-0">
            {t("menuUse.swapped", { dish: swapped.recipeName })}
          </span>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            data-testid="menu-swap-undo-action"
            disabled={busy}
            onClick={() => undoSwap(swapped)}
          >
            {t("shoppingPlan.undo")}
          </Button>
        </div>
      ) : null}

      {dishes.length === 0 && wheneverMeals.length === 0 ? (
        <p
          className="surface p-4 text-muted-foreground"
          data-testid="menu-empty"
        >
          {t("menuUse.empty")}
        </p>
      ) : null}

      {todays.length > 0 ? (
        <MenuSection id="menu-today" title={t("menuUse.section.today")}>
          <ul data-testid={`menu-day-${today}`}>
            {todays.map((entry) => row(entry, {}))}
          </ul>
        </MenuSection>
      ) : null}

      {laterDays.length > 0 ? (
        <MenuSection id="menu-later" title={t("menuUse.section.later")}>
          {laterDays.map((day) => (
            <div key={day} data-testid={`menu-day-${day}`}>
              <h3 className="px-2 pt-2 text-sm font-semibold text-muted-foreground">
                {formatMenuDay(day, locale)}
              </h3>
              <ul>
                {later
                  .filter((entry) => entry.plannedDate === day)
                  .map((entry) => row(entry, {}))}
              </ul>
            </div>
          ))}
        </MenuSection>
      ) : null}

      {wheneverMeals.length > 0 ? (
        <MenuSection id="menu-whenever" title={t("menuUse.section.whenever")}>
          {wheneverMeals.map((meal) => {
            const left = missing[meal] ?? 0;
            const items = undated
              .filter((entry) =>
                meal === "none" ? !entry.mealType : entry.mealType === meal,
              )
              .sort(byOrder);
            return (
              <div key={meal} data-testid={`menu-whenever-${meal}`}>
                <h3 className="px-2 pt-2 text-sm font-semibold text-muted-foreground">
                  {meal === "none"
                    ? t("menuUse.meal.none")
                    : t(`menuBuild.meal.plural.${meal}`)}
                </h3>
                <ul>
                  {items.map((entry) => row(entry, { meal: false }))}
                  {left > 0 && canChoose && editable ? (
                    <li>
                      <button
                        type="button"
                        id={`menu-choose-${meal}`}
                        data-testid={`menu-choose-${meal}`}
                        onClick={() =>
                          setRequest({ nonce: Date.now(), mealType: meal })
                        }
                        className="flex min-h-14 w-full items-center gap-2 rounded-lg px-2 py-2 text-left font-semibold text-primary transition-colors hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                      >
                        <span
                          aria-hidden="true"
                          className="text-xl leading-none"
                        >
                          +
                        </span>
                        {t(`menuBuild.choose.${meal}`)}{" "}
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
              </div>
            );
          })}
        </MenuSection>
      ) : null}

      {bases.length > 0 ? (
        <MenuSection id="menu-bases" title={t("menuBuild.board.bases")}>
          <ul>
            {bases.map((entry) => row(entry, { meal: false, day: true }))}
          </ul>
        </MenuSection>
      ) : null}

      {pastDays.length > 0 ? (
        <details className="surface p-2" data-testid="menu-past">
          <summary className="flex min-h-11 cursor-pointer items-center rounded-lg px-2 font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
            {t("menuUse.section.past")}
          </summary>
          {pastDays.map((day) => (
            <ul key={day} data-testid={`menu-day-${day}`}>
              {past
                .filter((entry) => entry.plannedDate === day)
                .map((entry) => row(entry, { day: true }))}
            </ul>
          ))}
        </details>
      ) : null}

      {editable ? (
        // Rare changes to the whole menu stay folded, so the page keeps to its dishes.
        <details className="surface p-2" data-testid="menu-more">
          <summary className="flex min-h-11 cursor-pointer items-center rounded-lg px-2 font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
            {t("menuBuild.board.more")}
          </summary>
          <div className="flex flex-col items-start gap-1 px-2 pb-2 pt-1">
            {accepted && dishes.some(isOpenDish) ? (
              <Button
                type="button"
                variant="ghost"
                data-testid="menu-prepare"
                onClick={() => {
                  setPrepareError(null);
                  setPrepareOpen(true);
                }}
              >
                {t("menuUse.prepare.title")}
              </Button>
            ) : null}
            <Button
              type="button"
              variant="ghost"
              className="text-destructive"
              data-testid="menu-archive"
              disabled={busy}
              onClick={() => {
                if (window.confirm(t("planning.archiveConfirm")))
                  void mutate((token) =>
                    repo.archiveMenu(menu.menuId, { csrfToken: token }),
                  );
              }}
            >
              {t("menuUse.archive")}
            </Button>
          </div>
        </details>
      ) : null}

      <MenuEntrySheet
        menu={menu}
        entry={openEntry}
        open={openEntry !== null}
        onOpenChange={(open) => {
          if (!open) setOpenEntryId(null);
        }}
        servings={
          openEntry && servingsDraft?.entryId === openEntry.entryId
            ? servingsDraft.servings
            : (openEntry?.servings ?? 1)
        }
        canSwap={
          canChoose &&
          openEntry?.status === "planned" &&
          openEntry.kind !== "component" &&
          openEntry.mealType !== null
        }
        busy={busy}
        notice={
          actionError ? (
            <p role="alert" className="text-sm text-destructive">
              {t(actionError)}
            </p>
          ) : null
        }
        actions={actions}
      />

      {accepted ? (
        <CookAheadSheet
          menu={menu}
          today={today}
          open={prepareOpen}
          onOpenChange={setPrepareOpen}
          busy={busy}
          notice={
            prepareError ? (
              <p role="alert" className="text-sm text-destructive">
                {t(prepareError)}
              </p>
            ) : null
          }
          onConfirm={(day, entryIds) => {
            if (!csrfToken) return;
            setBusy(true);
            setPrepareError(null);
            repo
              .setCookDay(menu.menuId, day, entryIds, { csrfToken })
              .then(
                (updated) => {
                  setMenu(updated);
                  setPrepareOpen(false);
                  setRouteVersion((value) => value + 1);
                },
                () => setPrepareError("planning.error.save"),
              )
              .finally(() => setBusy(false));
          }}
        />
      ) : null}

      {canChoose ? (
        <MenuChoosing
          menuId={menu.menuId}
          request={request}
          onPlan={onPlan}
          onMenuChanged={reload}
          onReplaced={(entry) => setSwapped(entry)}
          onError={setActionError}
        />
      ) : null}
    </div>
  );
}

/** The next step's link, as on "Planejar"; null when the next thing is to cook. */
function stepAction(
  step: PlanningStep | null,
  menuId: string,
  routeSessionId: string | null,
): { to: string; labelKey: string; testId: string; count?: number } | null {
  switch (step?.kind) {
    case "buy":
      return {
        to: `/app/cardapio/${menuId}/compras`,
        labelKey: "planningHub.buy",
        testId: "menu-step-buy",
        count: step.count,
      };
    case "arrival":
      return {
        to: `/app/cardapio/${menuId}/chegada`,
        labelKey: "shopping.arrival",
        testId: "menu-step-arrival",
      };
    case "prepare":
      return routeSessionId
        ? {
            to: `/app/compras/planejar/${routeSessionId}/preparo`,
            labelKey: "shoppingPlan.route.open",
            testId: "session-route-link",
          }
        : null;
    default:
      return null;
  }
}

/**
 * "Faltam 2 dias · 3 lanches por fazer", near the end of the period while dishes are left. Null
 * otherwise.
 */
function endOfPeriodLine(
  menu: Menu,
  dishes: MenuEntry[],
  today: string,
  t: (key: string, values?: Record<string, string | number>) => string,
): string | null {
  if (today < menu.startDate || today > menu.endDate) return null;
  const daysLeft = daysBetween(today, menu.endDate).length;
  if (daysLeft > END_LINE_DAYS) return null;
  const left: Record<string, number> = {};
  for (const entry of dishes) {
    if (entry.status !== "planned" || !entry.mealType) continue;
    left[entry.mealType] = (left[entry.mealType] ?? 0) + entry.mealsCovered;
  }
  const parts = MEAL_ORDER.filter((meal) => left[meal]).map((meal) =>
    t(
      left[meal] === 1
        ? `menuUse.left.${meal}.one`
        : `menuUse.left.${meal}.other`,
      {
        count: left[meal]!,
      },
    ),
  );
  if (parts.length === 0) return null;
  const days =
    daysLeft === 1
      ? t("menuUse.days.last")
      : t("menuUse.days.other", { count: daysLeft });
  return t("menuUse.endLine", { days, meals: parts.join(", ") });
}

function MenuSection({
  id,
  title,
  children,
}: {
  id: string;
  title: string;
  children: ReactNode;
}) {
  return (
    <section
      aria-labelledby={`${id}-title`}
      data-testid={id}
      className="surface p-2"
    >
      <h2
        id={`${id}-title`}
        tabIndex={-1}
        className="px-2 pt-1 text-lg font-semibold outline-none"
      >
        {title}
      </h2>
      {children}
    </section>
  );
}

/** One dish: tapping opens its sheet; only the next dish carries "Cozinhar" in the row. */
function EntryRow({
  entry,
  today,
  thumbnail,
  showMeal,
  showDay,
  cookNow,
  cookVariant,
  busy,
  onOpen,
  onCook,
}: {
  entry: MenuEntry;
  today: string;
  thumbnail?: RecipeSummary["thumbnail"];
  showMeal: boolean;
  showDay: boolean;
  cookNow: boolean;
  cookVariant: "default" | "secondary";
  busy: boolean;
  onOpen: () => void;
  onCook: () => void;
}) {
  const { t, locale } = useProductionI18n();
  // A dish cooked ahead for a later day is ready, not done.
  const readyAhead =
    entry.status === "cooked" &&
    entry.cookOnDate &&
    (entry.plannedDate === null || entry.plannedDate >= today);
  const meta = [
    showMeal && entry.mealType ? mealTypeLabel(t, entry.mealType) : null,
    showDay && entry.plannedDate
      ? formatMenuDay(entry.plannedDate, locale)
      : null,
    entry.kind === "component" && entry.producesName
      ? entry.producesName
      : t("recipes.detail.servings", { count: entry.servings }),
    entry.mealsCovered > 1
      ? t("shoppingPlan.review.covers", { count: entry.mealsCovered })
      : null,
    readyAhead
      ? t("menuUse.cookedAhead")
      : entry.status !== "planned"
        ? t(`planning.entryStatus.${entry.status}`)
        : null,
  ].filter(Boolean);
  const unitKey: Record<string, string> = {
    Gram: "recipes.unit.g",
    Milliliter: "recipes.unit.ml",
    Unit: "recipes.unit.unit",
  };
  // Whether the pantry covers it (PLAN-0029 Phase 3): reserved on a menu in use, simulated on a draft.
  const open = entry.status === "planned" || entry.status === "cooking";
  const readiness = !open
    ? null
    : !entry.reserveIngredients
      ? {
          tone: "text-muted-foreground",
          text: t("planning.readiness.released"),
        }
      : entry.readiness?.readiness === "ready"
        ? { tone: "text-primary", text: t("planning.readiness.ready") }
        : entry.readiness?.readiness === "missing"
          ? {
              tone: "font-semibold text-warning",
              text: t("planning.readiness.missing", {
                items: entry.readiness.missing
                  .map(
                    (item) =>
                      `${item.name} ${formatLocaleDecimal(item.quantity, locale)} ${t(unitKey[item.unit] ?? "recipes.unit.unit")}`,
                  )
                  .join(", "),
              }),
            }
          : null;
  return (
    // "Cozinhar" goes under the dish, aligned with its name, so a long name keeps the row's width.
    <li data-testid="menu-entry" className="flex flex-col">
      <button
        type="button"
        data-testid="menu-entry-open"
        onClick={onOpen}
        className="flex min-h-14 min-w-0 flex-1 items-center gap-3 rounded-lg px-2 py-2 text-left transition-colors hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <span className="w-14 shrink-0">
          <RecipeThumbnail
            thumbnail={thumbnail ?? { state: "none", url: null }}
            name={entry.recipeName}
            size="small"
          />
        </span>
        <span className="min-w-0">
          <span
            className={`line-clamp-2 font-medium ${entry.status === "skipped" ? "line-through" : ""}`}
          >
            {entry.recipeName}
          </span>
          <span className="block text-sm text-muted-foreground">
            {meta.join(" · ")}
          </span>
          {entry.cookOnDate && entry.status === "planned" ? (
            <span
              data-testid="menu-entry-cook-on"
              className="mt-1 inline-block rounded-full bg-secondary px-2 py-0.5 text-xs text-foreground"
            >
              {t("menuUse.cookOn", {
                day:
                  entry.cookOnDate === today
                    ? t("today.menu.today")
                    : formatMenuDay(entry.cookOnDate, locale),
              })}
            </span>
          ) : null}
          {readiness ? (
            <span
              data-testid="menu-entry-readiness"
              className={`mt-1 block text-xs ${readiness.tone}`}
            >
              {readiness.text}
            </span>
          ) : null}
        </span>
      </button>
      {cookNow ? (
        <Button
          type="button"
          size="sm"
          variant={cookVariant}
          // Lines up with the dish name: the image is 3.5rem wide, plus the row's padding and gap.
          className="mb-2 ml-[4.75rem] self-start"
          data-testid="menu-entry-cook-next"
          disabled={busy}
          onClick={onCook}
        >
          {t(
            entry.status === "cooking"
              ? "today.cooking.resume"
              : "recipes.detail.cook",
          )}
        </Button>
      ) : null}
    </li>
  );
}
