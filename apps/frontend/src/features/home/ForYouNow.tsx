import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ChevronRight } from "lucide-react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import type {
  RecipeRecommendation,
  RecipeRecommendationReason,
  RecipeSummary,
} from "@/contracts/recipes";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import { RecipeCard } from "@/features/recipes/RecipeCard";
import { localIsoDate } from "@/features/planning/planningDates";
import { mealTypeLabel } from "@/features/recipes/recipeLabels";

const MEALS = ["breakfast", "lunch", "snack", "dinner"] as const;
/** Time answers: up to 20 minutes, up to 45 minutes, or no rush (no limit). */
const TIMES = [20, 45, null] as const;

/** The meal that fits the person's local hour, as the chooser's default. */
export function mealForHour(hour: number): (typeof MEALS)[number] {
  if (hour >= 5 && hour < 10) return "breakfast";
  if (hour >= 10 && hour < 15) return "lunch";
  if (hour >= 15 && hour < 18) return "snack";
  return "dinner";
}

/**
 * "Para você agora" (PLAN-0021: confirmed-profile tier and quick chooser). Two quick questions,
 * which meal (preset from the local hour) and how much time, narrow the person's saved recipes,
 * which the backend ranks deterministically: pantry coverage, items due soon, time, meal,
 * favorites, and variety. Each card says why it was picked. The answers live only in this view:
 * nothing is stored and the profile never changes. Hidden when the source is unavailable or
 * there are no saved recipes.
 *
 * @param recipes Saved recipe summaries (names and images), already loaded by the home.
 */
export function ForYouNow({ recipes }: { recipes: RecipeSummary[] }) {
  const { t } = useProductionI18n();
  const repo = useRecipeRepository();
  const [meal, setMeal] = useState<(typeof MEALS)[number]>(() =>
    mealForHour(new Date().getHours()),
  );
  const [time, setTime] = useState<(typeof TIMES)[number]>(null);
  const [items, setItems] = useState<RecipeRecommendation[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!repo.listRecommendations) return;
    const controller = new AbortController();
    repo
      .listRecommendations(
        { today: localIsoDate(), mealType: meal, maxMinutes: time, limit: 6 },
        controller.signal,
      )
      .then(
        (value) => {
          if (controller.signal.aborted) return;
          setItems(value);
          setFailed(false);
        },
        () => !controller.signal.aborted && setFailed(true),
      );
    return () => controller.abort();
  }, [repo, meal, time]);

  if (!repo.listRecommendations || failed) return null;
  const byId = new Map(recipes.map((recipe) => [recipe.recipeId, recipe]));
  const reason = (value: RecipeRecommendationReason) =>
    t(`home.forYou.reason.${value.code}`, { item: value.item ?? "" });
  const chip = (pressed: boolean) =>
    `h-9 shrink-0 rounded-full border px-3.5 text-sm font-semibold transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background ${
      pressed
        ? "border-primary bg-primary text-primary-foreground"
        : "border-border bg-card text-foreground hover:bg-secondary"
    }`;

  return (
    <section
      aria-labelledby="today-for-you"
      data-testid="today-for-you"
      className="space-y-3"
    >
      <div className="flex items-baseline justify-between gap-2">
        <h2 id="today-for-you" className="font-display text-xl">
          {t("home.forYou.title")}
        </h2>
        <Link
          to="/app/cozinhar"
          className="inline-flex items-center gap-0.5 text-sm font-medium text-primary underline-offset-4 hover:underline"
        >
          {t("home.forYou.all")}
          <ChevronRight aria-hidden="true" className="h-4 w-4" />
        </Link>
      </div>
      {/* The quick chooser: request-scoped answers, never saved. */}
      <div
        role="group"
        aria-label={t("home.forYou.meal")}
        className="-mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0"
      >
        {MEALS.map((value) => (
          <button
            key={value}
            type="button"
            aria-pressed={meal === value}
            data-testid={`for-you-meal-${value}`}
            onClick={() => setMeal(value)}
            className={chip(meal === value)}
          >
            {mealTypeLabel(t, value)}
          </button>
        ))}
      </div>
      <div
        role="group"
        aria-label={t("home.forYou.time")}
        className="-mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0"
      >
        {TIMES.map((value) => (
          <button
            key={String(value)}
            type="button"
            aria-pressed={time === value}
            data-testid={`for-you-time-${value ?? "any"}`}
            onClick={() => setTime(value)}
            className={chip(time === value)}
          >
            {value === null
              ? t("home.forYou.noRush")
              : t("home.forYou.upTo", { minutes: value })}
          </button>
        ))}
      </div>
      {items === null ? (
        <ul aria-hidden="true" className="flex gap-3 overflow-hidden">
          {[0, 1].map((index) => (
            <li key={index} className="surface w-44 shrink-0 space-y-2 p-2">
              <div className="aspect-video rounded-2xl bg-secondary motion-safe:animate-pulse" />
              <div className="h-4 w-3/4 rounded bg-secondary motion-safe:animate-pulse" />
            </li>
          ))}
        </ul>
      ) : items.length === 0 ? (
        <p
          role="status"
          data-testid="for-you-empty"
          className="surface p-4 text-sm text-muted-foreground"
        >
          {t("home.forYou.empty")}{" "}
          <Link
            to="/app/receitas/gerar"
            className="font-medium text-primary underline-offset-4 hover:underline"
          >
            {t("home.forYou.suggest")}
          </Link>
        </p>
      ) : (
        <ul
          aria-live="polite"
          className="-mx-4 flex snap-x gap-3 overflow-x-auto px-4 pb-1 sm:mx-0 sm:grid sm:grid-cols-3 sm:overflow-visible sm:px-0"
        >
          {items.map((item) => {
            const recipe = byId.get(item.recipeId);
            if (!recipe) return null;
            return (
              <li
                key={item.recipeId}
                data-testid="for-you-item"
                className="w-44 shrink-0 snap-start sm:w-auto"
              >
                <RecipeCard
                  recipeId={recipe.recipeId}
                  name={recipe.name}
                  thumbnail={recipe.thumbnail}
                  minutes={item.totalMinutes}
                  meta={item.reasons.slice(0, 1).map(reason)}
                  className="h-full"
                />
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
