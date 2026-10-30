import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { InventoryLotView } from "@/adapters/live/inventoryTypes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import {
  CalendarDays,
  ChefHat,
  ChevronRight,
  Clock,
  Plus,
  Refrigerator,
  ShoppingBasket,
  ShoppingCart,
  Snowflake,
  Sparkles,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import type {
  RecipeExecutionSummary,
  RecipeSummary,
} from "@/contracts/recipes";
import { AttentionBadge } from "@/features/inventory/AttentionBadge";
import { useInventoryRepository } from "@/features/inventory/InventoryProvider";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import type { PreparationReminder, TodayMenuEntry } from "@/contracts/planning";
import { usePlanningRepository } from "@/features/planning/PlanningProvider";
import {
  addDays,
  formatMenuDay,
  localIsoDate,
} from "@/features/planning/planningDates";
import { formatLocaleDecimal } from "@/lib/localeDecimal";
import { mealTypeLabel } from "@/features/recipes/recipeLabels";
import { RecipeCard } from "@/features/recipes/RecipeCard";
import { RecipeThumbnail } from "@/features/recipes/RecipeThumbnail";
import { buildHomeGreeting, readBrowserTimeZone } from "./dayPart";

const UNIT_KEYS: Record<string, string> = {
  Gram: "recipes.unit.g",
  Milliliter: "recipes.unit.ml",
  Unit: "recipes.unit.unit",
};

type Source<T> =
  | { status: "loading" }
  | { status: "ready"; items: T[] }
  | { status: "failed" };

/** Loads one home source; a failure affects only its own section. */
function useSource<T>(load: (signal: AbortSignal) => Promise<T[]>): Source<T> {
  const [state, setState] = useState<Source<T>>({ status: "loading" });
  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal).then(
      (items) =>
        !controller.signal.aborted && setState({ status: "ready", items }),
      () => !controller.signal.aborted && setState({ status: "failed" }),
    );
    return () => controller.abort();
  }, [load]);
  return state;
}

/** A section of the home page: a heading, an optional "see all" link, and its content. */
function HomeSection({
  id,
  title,
  link,
  children,
  testId,
}: {
  id: string;
  title: string;
  link?: { to: string; label: string };
  children: ReactNode;
  testId?: string;
}) {
  return (
    <section aria-labelledby={id} data-testid={testId} className="space-y-3">
      <div className="flex items-baseline justify-between gap-2">
        <h2 id={id} className="font-display text-xl">
          {title}
        </h2>
        {link ? (
          <Link
            to={link.to}
            className="inline-flex items-center gap-0.5 text-sm font-medium text-primary underline-offset-4 hover:underline"
          >
            {link.label}
            <ChevronRight aria-hidden="true" className="h-4 w-4" />
          </Link>
        ) : null}
      </div>
      {children}
    </section>
  );
}

/**
 * First-run welcome (PLAN-0058): shown while the person has no recipes yet, it turns the empty
 * home into three steps, each a link to where it happens. It disappears by itself once a recipe
 * exists; nothing is stored to hide it.
 */
function WelcomeCard() {
  const { t } = useProductionI18n();
  const steps = [
    {
      to: "/app/despensa/novo",
      icon: Refrigerator,
      title: t("today.welcome.step1"),
      detail: t("today.welcome.step1Detail"),
    },
    {
      to: "/app/receitas/gerar",
      icon: Sparkles,
      title: t("today.welcome.step2"),
      detail: t("today.welcome.step2Detail"),
    },
    {
      to: "/app/cardapio",
      icon: CalendarDays,
      title: t("today.welcome.step3"),
      detail: t("today.welcome.step3Detail"),
    },
  ];
  return (
    <section
      aria-labelledby="today-welcome"
      data-testid="today-welcome"
      className="overflow-hidden rounded-2xl border border-primary/20 bg-accent text-accent-foreground"
    >
      <div className="p-5">
        <ChefHat aria-hidden="true" className="h-8 w-8" />
        <h2 id="today-welcome" className="mt-3 font-display text-2xl">
          {t("today.welcome.title")}
        </h2>
        <p className="mt-1 text-sm opacity-90">{t("today.welcome.detail")}</p>
      </div>
      <ol className="divide-y divide-primary/10 border-t border-primary/10 bg-card text-foreground">
        {steps.map(({ to, icon: Icon, title, detail }, index) => (
          <li key={to}>
            <Link
              to={to}
              className="flex items-center gap-3 p-4 transition-colors duration-150 hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
            >
              <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground">
                <Icon aria-hidden="true" className="h-5 w-5" />
              </span>
              <span className="min-w-0 flex-1">
                <span className="block font-semibold">
                  {index + 1}. {title}
                </span>
                <span className="block text-sm text-muted-foreground">
                  {detail}
                </span>
              </span>
              <ChevronRight
                aria-hidden="true"
                className="h-5 w-5 shrink-0 text-muted-foreground"
              />
            </Link>
          </li>
        ))}
      </ol>
    </section>
  );
}

/**
 * The four everyday starting points as large tiles (PLAN-0058): suggest recipes (the main
 * action, on the accent), add to the pantry, the week's menu, and the shopping list.
 */
function QuickActions() {
  const { t } = useProductionI18n();
  const tiles = [
    {
      to: "/app/despensa/novo",
      icon: Plus,
      title: t("today.cta.addItem"),
      detail: t("today.quick.addDetail"),
      testId: "today-add-item",
    },
    {
      to: "/app/cardapio",
      icon: CalendarDays,
      title: t("today.quick.menu"),
      detail: t("today.quick.menuDetail"),
      testId: "today-quick-menu",
    },
    {
      to: "/app/compras",
      icon: ShoppingBasket,
      title: t("today.prep.shopping"),
      detail: t("today.quick.shoppingDetail"),
      testId: "today-quick-shopping",
    },
  ];
  const tile =
    "flex min-h-[5.5rem] flex-col justify-between gap-2 rounded-2xl p-4 transition-[background-color,border-color,transform] duration-150 ease-out focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background motion-safe:active:scale-[0.98]";
  return (
    <nav
      aria-label={t("today.quick.label")}
      className="grid grid-cols-3 gap-3 sm:grid-cols-4"
    >
      <Link
        to="/app/receitas/gerar"
        data-testid="today-generate"
        className={`${tile} col-span-3 bg-primary text-primary-foreground hover:bg-primary/90 sm:col-span-1`}
      >
        <Sparkles aria-hidden="true" className="h-6 w-6" />
        <span>
          <span className="block text-base font-semibold">
            {t("today.cta.generate")}
          </span>
          <span className="block text-sm opacity-90">
            {t("today.quick.generateDetail")}
          </span>
        </span>
      </Link>
      {tiles.map(({ to, icon: Icon, title, detail, testId }) => (
        <Link
          key={to}
          to={to}
          data-testid={testId}
          className={`${tile} surface items-start hover:border-input/60 hover:bg-secondary/40`}
        >
          <Icon aria-hidden="true" className="h-6 w-6 text-primary" />
          <span>
            <span className="block text-sm font-semibold leading-tight">
              {title}
            </span>
            <span className="hidden text-sm text-muted-foreground sm:block">
              {detail}
            </span>
          </span>
        </Link>
      ))}
    </nav>
  );
}

const PREP_ICON = {
  buy: ShoppingCart,
  thaw: Snowflake,
  use_by: Clock,
} as const;

/**
 * "Prepare-se" (PLAN-0029 Phase 4): what to buy, thaw, or use first for the menu meals of today
 * and tomorrow, one row each with an icon for the kind of task. The reminders are derived on
 * every read, so each disappears once its cause does; nothing here changes the pantry or the
 * menu. Renders nothing when there is nothing to do.
 */
function PreparationSection({ items }: { items: PreparationReminder[] }) {
  const { t, locale } = useProductionI18n();
  if (items.length === 0) return null;
  const today = localIsoDate();
  const day = (iso: string) =>
    iso <= today
      ? t("today.prep.dueToday")
      : iso === addDays(today, 1)
        ? t("today.prep.dueTomorrow")
        : formatMenuDay(iso, locale);
  const amount = (item: PreparationReminder) =>
    item.quantity === null
      ? ""
      : `${formatLocaleDecimal(item.quantity, locale)} ${t(UNIT_KEYS[item.unit ?? ""] ?? "recipes.unit.unit")}`;
  const action = (item: PreparationReminder) =>
    item.kind === "buy"
      ? t("today.prep.buy", { item: item.itemName, amount: amount(item) })
      : item.kind === "thaw"
        ? t("today.prep.thaw", { item: item.itemName })
        : t("today.prep.useBy", {
            item: item.itemName,
            date: formatMenuDay(item.useByDate ?? item.dueDate, locale),
          });

  return (
    <HomeSection
      id="today-preparation"
      testId="today-preparation"
      title={t("today.prep.title")}
    >
      <ul className="surface divide-y divide-border overflow-hidden">
        {items.map((item) => {
          const Icon = PREP_ICON[item.kind as keyof typeof PREP_ICON] ?? Clock;
          const to =
            item.kind === "buy"
              ? `/app/cardapio/${item.menuId}/compras`
              : item.lotId
                ? `/app/despensa/${item.lotId}`
                : `/app/cardapio/${item.menuId}`;
          return (
            <li
              key={`${item.entryId}-${item.kind}-${item.lotId ?? item.itemName}`}
              data-testid="today-preparation-item"
            >
              <Link
                to={to}
                aria-label={`${action(item)} · ${t(item.kind === "buy" ? "today.prep.shopping" : "today.prep.pantry")}`}
                className="flex items-center gap-3 p-4 transition-colors duration-150 hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
              >
                <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-accent text-accent-foreground">
                  <Icon aria-hidden="true" className="h-5 w-5" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block font-medium">{action(item)}</span>
                  <span className="block text-sm text-muted-foreground">
                    {day(item.dueDate) !== day(item.plannedDate)
                      ? `${day(item.dueDate)} · `
                      : ""}
                    {t("today.prep.for", {
                      recipe: item.recipeName,
                      day: day(item.plannedDate),
                    })}
                  </span>
                </span>
                <ChevronRight
                  aria-hidden="true"
                  className="h-5 w-5 shrink-0 text-muted-foreground"
                />
              </Link>
            </li>
          );
        })}
      </ul>
    </HomeSection>
  );
}

/**
 * Production "Hoje" (PLAN-0021 tiers, PLAN-0042 resume, PLAN-0029 menu; layout PLAN-0058). The
 * page answers "what do we cook today?" in task order: what is on the stove, today's planned
 * meals as image cards, the four starting points, what to prepare, what to use soon, and the
 * person's recipes as a carousel of image cards. Every section reads an authoritative source
 * independently; a failed source hides only its own section, and reading mutates nothing.
 */
export function TodayPage() {
  const { t } = useProductionI18n();
  const { session } = useSession();
  const recipes = useRecipeRepository();
  const inventory = useInventoryRepository();
  const planning = usePlanningRepository();
  const navigate = useNavigate();
  const [startingEntry, setStartingEntry] = useState<string | null>(null);
  const [menuError, setMenuError] = useState(false);

  const menu = useSource<TodayMenuEntry>(
    useMemo(
      () => (signal: AbortSignal) => planning.getToday(localIsoDate(), signal),
      [planning],
    ),
  );

  // Optional source: a repository without reminders simply shows none.
  const preparation = useSource<PreparationReminder>(
    useMemo(
      () => (signal: AbortSignal) =>
        planning.getPreparation?.(localIsoDate(), 2, signal) ??
        Promise.resolve([]),
      [planning],
    ),
  );

  const cooking = useSource<RecipeExecutionSummary>(
    useMemo(
      () => (signal: AbortSignal) => recipes.listExecutionsInProgress(signal),
      [recipes],
    ),
  );
  const attention = useSource<InventoryLotView>(
    useMemo(
      () => (signal: AbortSignal) => inventory.listAttention(signal),
      [inventory],
    ),
  );
  const saved = useSource<RecipeSummary>(
    useMemo(
      () => (signal: AbortSignal) => recipes.listRecipes(signal),
      [recipes],
    ),
  );

  // Cooking a menu meal links the session to it, so the menu shows it as cooked afterwards.
  const cookMenuEntry = async (item: TodayMenuEntry) => {
    if (item.entry.status === "cooking" && item.entry.executionId) {
      navigate(`/app/receitas/cozinhar/${item.entry.executionId}`);
      return;
    }
    if (!session.csrfToken) return;
    setStartingEntry(item.entry.entryId);
    setMenuError(false);
    try {
      const execution = await recipes.startExecution(item.entry.recipeId, {
        csrfToken: session.csrfToken,
        servings: item.entry.servings,
        menuEntryId: item.entry.entryId,
      });
      navigate(`/app/receitas/cozinhar/${execution.executionId}`);
    } catch {
      setMenuError(true);
      setStartingEntry(null);
    }
  };

  const greeting = buildHomeGreeting({
    displayName:
      session.status === "authenticated" ? session.displayName : null,
    profileTimeZone:
      session.status === "authenticated" ? session.timeZone : null,
    browserTimeZone: readBrowserTimeZone(),
  });
  const greetingText = greeting.displayName
    ? t(`home.greeting.named.${greeting.dayPart}`, {
        name: greeting.displayName,
      })
    : t(`home.greeting.anonymous.${greeting.dayPart}`);

  const thumbnails = new Map(
    saved.status === "ready"
      ? saved.items.map((recipe) => [recipe.recipeId, recipe.thumbnail])
      : [],
  );
  const recipeMeta = (recipe: RecipeSummary) =>
    [
      recipe.mealTypes[0] ? mealTypeLabel(t, recipe.mealTypes[0]) : null,
      t("recipes.detail.servings", { count: recipe.servings }),
    ].filter((value): value is string => Boolean(value));

  const firstRun =
    saved.status === "ready" &&
    saved.items.length === 0 &&
    cooking.status === "ready" &&
    cooking.items.length === 0;

  return (
    <div data-testid="today-page" className="space-y-8">
      <header>
        <p data-testid="home-greeting" className="text-muted-foreground">
          {greetingText}
        </p>
        <h1 className="font-display text-3xl">{t("home.primaryQuestion")}</h1>
      </header>

      {firstRun ? <WelcomeCard /> : null}

      {/* Resuming what is on the stove comes before anything new. */}
      {cooking.status === "ready" && cooking.items.length > 0 ? (
        <section
          aria-labelledby="today-cooking"
          data-testid="today-cooking"
          className="space-y-3"
        >
          <h2 id="today-cooking" className="sr-only">
            {t("today.cooking.title")}
          </h2>
          {cooking.items.map((item) => (
            <div
              key={item.executionId}
              className="flex items-center gap-3 rounded-2xl border border-primary/30 bg-accent p-3 text-accent-foreground"
            >
              <span className="w-20 shrink-0 sm:w-28">
                <RecipeThumbnail
                  thumbnail={
                    thumbnails.get(item.recipeId) ?? {
                      state: "none",
                      url: null,
                    }
                  }
                  name={item.recipeName}
                  size="small"
                />
              </span>
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide">
                  <ChefHat aria-hidden="true" className="h-3.5 w-3.5" />
                  {t("today.cooking.title")}
                </span>
                <span className="mt-0.5 block truncate font-semibold">
                  {item.recipeName}
                </span>
              </span>
              <Button asChild size="sm">
                <Link to={`/app/receitas/cozinhar/${item.executionId}`}>
                  {t("today.cooking.resume")}
                </Link>
              </Button>
            </div>
          ))}
        </section>
      ) : null}

      {menu.status === "ready" && menu.items.length > 0 ? (
        <HomeSection
          id="today-menu"
          testId="today-menu"
          title={t("today.menu.title")}
          link={{
            to: `/app/cardapio/${menu.items[0]!.menuId}`,
            label: t("today.menu.open"),
          }}
        >
          <ul className="grid gap-3 sm:grid-cols-2">
            {menu.items.map((item) => {
              const readiness = item.entry.readiness;
              return (
                <li
                  key={item.entry.entryId}
                  data-testid="today-menu-entry"
                  className="surface flex flex-col gap-3 p-3"
                >
                  <div className="flex items-center gap-3">
                    <span className="w-28 shrink-0 sm:w-32">
                      <RecipeThumbnail
                        thumbnail={
                          thumbnails.get(item.entry.recipeId) ?? {
                            state: "none",
                            url: null,
                          }
                        }
                        name={item.entry.recipeName}
                        size="small"
                      />
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="text-xs font-semibold uppercase tracking-wide text-primary">
                        {item.entry.plannedDate === null
                          ? t("today.menu.flexible")
                          : item.entry.mealType
                            ? mealTypeLabel(t, item.entry.mealType)
                            : t("today.menu.today")}
                      </p>
                      <p className="mt-0.5 text-lg font-semibold leading-snug">
                        {item.entry.recipeName}
                      </p>
                      {readiness ? (
                        <p
                          className={`mt-2 inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                            readiness.readiness === "ready"
                              ? "bg-success-soft text-success"
                              : "bg-warning-soft text-warning"
                          }`}
                        >
                          {readiness.readiness === "ready"
                            ? t("today.menu.ready")
                            : t("today.menu.missing", {
                                count: readiness.missing.length,
                              })}
                        </p>
                      ) : null}
                    </div>
                  </div>
                  <Button
                    className="mt-auto w-full"
                    data-testid="today-menu-cook"
                    disabled={startingEntry !== null || !session.csrfToken}
                    onClick={() => void cookMenuEntry(item)}
                  >
                    {t(
                      item.entry.status === "cooking"
                        ? "today.cooking.resume"
                        : "recipes.detail.cook",
                    )}
                  </Button>
                </li>
              );
            })}
          </ul>
          {menuError ? (
            <p role="alert" className="text-sm text-destructive">
              {t("planning.error.cook")}
            </p>
          ) : null}
        </HomeSection>
      ) : null}

      <QuickActions />

      {preparation.status === "ready" ? (
        <PreparationSection items={preparation.items} />
      ) : null}

      <HomeSection
        id="today-attention"
        testId="today-attention"
        title={t("today.attention.title")}
        link={{ to: "/app/despensa", label: t("today.attention.all") }}
      >
        {attention.status === "loading" ? (
          <p role="status" className="text-sm text-muted-foreground">
            {t("app.loading")}
          </p>
        ) : attention.status === "failed" ? (
          <p className="text-sm text-muted-foreground">
            {t("today.attention.unavailable")}
          </p>
        ) : attention.items.length === 0 ? (
          <p className="surface p-4 text-sm text-muted-foreground">
            {t("today.attention.empty")}
          </p>
        ) : (
          <ul className="-mx-4 flex snap-x gap-3 overflow-x-auto px-4 pb-1 sm:mx-0 sm:grid sm:grid-cols-3 sm:overflow-visible sm:px-0">
            {attention.items.slice(0, 6).map((lot) => (
              <li
                key={lot.lotId}
                className="surface w-56 shrink-0 snap-start space-y-2 p-4 sm:w-auto"
              >
                <Link
                  to={`/app/despensa/${lot.lotId}`}
                  className="block font-semibold underline-offset-4 hover:underline"
                >
                  {lot.productName}
                </Link>
                <AttentionBadge attention={lot.attention} />
              </li>
            ))}
          </ul>
        )}
      </HomeSection>

      {firstRun ? null : (
        <HomeSection
          id="today-recipes"
          testId="today-recipes"
          title={t("today.recipes.title")}
          link={{ to: "/app/receitas", label: t("today.recipes.all") }}
        >
          {saved.status === "loading" ? (
            <p role="status" className="text-sm text-muted-foreground">
              {t("app.loading")}
            </p>
          ) : saved.status === "failed" ? (
            <p className="text-sm text-muted-foreground">
              {t("today.recipes.unavailable")}
            </p>
          ) : saved.items.length === 0 ? (
            <p className="surface p-4 text-sm text-muted-foreground">
              {t("today.recipes.empty")}
            </p>
          ) : (
            <ul className="-mx-4 flex snap-x gap-3 overflow-x-auto px-4 pb-1 sm:mx-0 sm:grid sm:grid-cols-3 sm:overflow-visible sm:px-0">
              {saved.items.slice(0, 6).map((recipe) => (
                <li
                  key={recipe.recipeId}
                  className="w-44 shrink-0 snap-start sm:w-auto"
                >
                  <RecipeCard
                    recipeId={recipe.recipeId}
                    name={recipe.name}
                    thumbnail={recipe.thumbnail}
                    meta={recipeMeta(recipe)}
                    className="h-full"
                  />
                </li>
              ))}
            </ul>
          )}
        </HomeSection>
      )}
    </div>
  );
}
