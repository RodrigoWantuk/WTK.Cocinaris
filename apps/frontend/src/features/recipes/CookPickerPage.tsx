import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { BookOpen, ChevronRight, PenLine, Sparkles } from "lucide-react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { EmptyState } from "@/components/feedback/EmptyState";
import { Button } from "@/components/ui/button";
import type { RecipeReadiness, RecipeSummary } from "@/contracts/recipes";
import { useRecipeRepository } from "./RecipeProvider";
import { RecipeThumbnail } from "./RecipeThumbnail";
import { mealTypeLabel } from "./recipeLabels";

type Loaded =
  | { status: "loading" }
  | { status: "failed" }
  | {
      status: "ready";
      recipes: RecipeSummary[];
      readiness: Map<string, RecipeReadiness>;
    };

/**
 * "O que vamos cozinhar?" (PLAN-0059, owner idea 2026-10-31), reached from the main button's
 * "Cozinhar". It offers a new recipe first (suggest from the pantry, or write one) and then the
 * person's saved recipes ordered by how many of their ingredients the pantry covers: everything
 * first, then the fewest missing. Each row opens the recipe; "Cozinhar" starts cooking at once.
 *
 * The pantry counts come from `listRecipeReadiness` (deterministic, read-only). Without it, or if
 * it fails, the recipes still list in library order without counts.
 */
export function CookPickerPage() {
  const { t } = useProductionI18n();
  const { session } = useSession();
  const repo = useRecipeRepository();
  const navigate = useNavigate();
  const [state, setState] = useState<Loaded>({ status: "loading" });
  const [starting, setStarting] = useState<string | null>(null);
  const [startError, setStartError] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const [recipes, readiness] = await Promise.all([
          repo.listRecipes(controller.signal),
          repo.listRecipeReadiness?.(controller.signal).catch(() => []) ?? [],
        ]);
        if (controller.signal.aborted) return;
        setState({
          status: "ready",
          recipes,
          readiness: new Map(readiness.map((item) => [item.recipeId, item])),
        });
      } catch {
        if (!controller.signal.aborted) setState({ status: "failed" });
      }
    })();
    return () => controller.abort();
  }, [repo]);

  const cook = async (recipe: RecipeSummary) => {
    if (!session.csrfToken) return;
    setStarting(recipe.recipeId);
    setStartError(false);
    try {
      const execution = await repo.startExecution(recipe.recipeId, {
        csrfToken: session.csrfToken,
        servings: recipe.servings,
      });
      navigate(`/app/receitas/cozinhar/${execution.executionId}`);
    } catch {
      setStartError(true);
      setStarting(null);
    }
  };

  const newRecipe = (
    <section aria-labelledby="cook-new" className="space-y-3">
      <h2 id="cook-new" className="font-display text-xl">
        {t("cook.new")}
      </h2>
      <div className="grid grid-cols-2 gap-3">
        <Link
          to="/app/receitas/gerar"
          data-testid="cook-suggest"
          className="flex min-h-[6rem] flex-col justify-between gap-2 rounded-2xl bg-primary p-4 text-primary-foreground transition-[background-color,transform] duration-150 hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 motion-safe:active:scale-[0.98]"
        >
          <Sparkles aria-hidden="true" className="h-6 w-6" />
          <span>
            <span className="block font-semibold leading-tight">
              {t("cook.suggest")}
            </span>
            <span className="block text-sm opacity-90">
              {t("cook.suggestDetail")}
            </span>
          </span>
        </Link>
        <Link
          to="/app/receitas/nova"
          data-testid="cook-write"
          className="surface flex min-h-[6rem] flex-col justify-between gap-2 p-4 transition-[background-color,border-color,transform] duration-150 hover:border-input/60 hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 motion-safe:active:scale-[0.98]"
        >
          <PenLine aria-hidden="true" className="h-6 w-6 text-primary" />
          <span>
            <span className="block font-semibold leading-tight">
              {t("cook.write")}
            </span>
            <span className="block text-sm text-muted-foreground">
              {t("cook.writeDetail")}
            </span>
          </span>
        </Link>
      </div>
    </section>
  );

  // Saved recipes in pantry order; recipes without counts keep their library order at the end.
  const ordered =
    state.status === "ready"
      ? [...state.recipes].sort((a, b) => {
          const order = [...state.readiness.keys()];
          const ia = order.indexOf(a.recipeId);
          const ib = order.indexOf(b.recipeId);
          return (ia < 0 ? order.length : ia) - (ib < 0 ? order.length : ib);
        })
      : [];

  return (
    <div data-testid="cook-picker" className="space-y-8">
      <header>
        <h1 className="font-display text-3xl">{t("cook.title")}</h1>
        <p className="mt-1 text-muted-foreground">{t("cook.subtitle")}</p>
      </header>

      {newRecipe}

      <section aria-labelledby="cook-saved" className="space-y-3">
        <h2 id="cook-saved" className="font-display text-xl">
          {t("cook.saved")}
        </h2>
        {startError ? (
          <p role="alert" className="text-sm text-destructive">
            {t("cook.error")}
          </p>
        ) : null}
        {state.status === "loading" ? (
          <ul aria-hidden="true" className="surface divide-y divide-border">
            {[0, 1, 2].map((index) => (
              <li key={index} className="flex items-center gap-3 p-3">
                <div className="aspect-video w-24 rounded-xl bg-secondary motion-safe:animate-pulse" />
                <div className="h-4 w-1/2 rounded bg-secondary motion-safe:animate-pulse" />
              </li>
            ))}
          </ul>
        ) : state.status === "failed" ? (
          <p className="text-sm text-muted-foreground">
            {t("cook.unavailable")}
          </p>
        ) : ordered.length === 0 ? (
          <EmptyState
            testId="cook-empty"
            icon={BookOpen}
            title={t("cook.empty")}
            detail={t("cook.emptyDetail")}
          />
        ) : (
          <ul className="surface divide-y divide-border overflow-hidden">
            {ordered.map((recipe) => {
              const readiness = state.readiness.get(recipe.recipeId);
              const total = readiness
                ? readiness.available + readiness.partial + readiness.missing
                : 0;
              const all =
                readiness !== undefined &&
                total > 0 &&
                readiness.available === total;
              return (
                <li
                  key={recipe.recipeId}
                  data-testid="cook-recipe"
                  className="flex items-center gap-3 p-3"
                >
                  <Link
                    to={`/app/receitas/${recipe.recipeId}`}
                    className="flex min-w-0 flex-1 items-center gap-3 rounded-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <span className="w-24 shrink-0">
                      <RecipeThumbnail
                        thumbnail={
                          recipe.thumbnail ?? { state: "none", url: null }
                        }
                        name={recipe.name}
                        size="small"
                      />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="line-clamp-2 font-semibold leading-snug">
                        {recipe.name}
                      </span>
                      <span className="block text-xs text-muted-foreground">
                        {[
                          recipe.mealTypes[0]
                            ? mealTypeLabel(t, recipe.mealTypes[0])
                            : null,
                          t("recipes.detail.servings", {
                            count: recipe.servings,
                          }),
                        ]
                          .filter(Boolean)
                          .join(" · ")}
                      </span>
                      {readiness && total > 0 ? (
                        <span
                          data-testid="cook-recipe-readiness"
                          className={`mt-1 inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                            all
                              ? "bg-success-soft text-success"
                              : "bg-warning-soft text-warning"
                          }`}
                        >
                          {all
                            ? t("cook.haveAll")
                            : t("cook.haveSome", {
                                have: readiness.available,
                                total,
                              })}
                        </span>
                      ) : null}
                    </span>
                    <ChevronRight
                      aria-hidden="true"
                      className="hidden h-5 w-5 shrink-0 text-muted-foreground sm:block"
                    />
                  </Link>
                  <Button
                    size="sm"
                    variant={all ? "default" : "secondary"}
                    aria-label={`${t("cook.start")}: ${recipe.name}`}
                    disabled={starting !== null || !session.csrfToken}
                    onClick={() => void cook(recipe)}
                  >
                    {t("cook.start")}
                  </Button>
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </div>
  );
}
