import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useRecipeRepository } from "./RecipeProvider";
import { RecipeApiError, type RecipeSummary } from "@/contracts/recipes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import { recipeErrorMessageKey } from "./recipeErrors";

/**
 * Authenticated production recipe list for saved cook-now revisions.
 */
export function RecipesListPage() {
  const repo = useRecipeRepository();
  const { t } = useProductionI18n();
  const [items, setItems] = useState<RecipeSummary[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "error">(
    "loading",
  );
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const requestIdRef = useRef(0);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const requestId = ++requestIdRef.current;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setStatus("loading");
    setErrorKey(null);
    void (async () => {
      try {
        const recipes = await repo.listRecipes(controller.signal);
        if (requestId !== requestIdRef.current) return;
        setItems(recipes);
        setStatus("ready");
      } catch (err) {
        if (controller.signal.aborted) return;
        if (err instanceof RecipeApiError && err.code === "cancelled") return;
        if (requestId !== requestIdRef.current) return;
        setStatus("error");
        setErrorKey(
          err instanceof RecipeApiError
            ? recipeErrorMessageKey(err.code)
            : "recipes.list.error",
        );
      }
    })();
    return () => controller.abort();
  }, [repo, reloadToken]);

  if (status === "loading") {
    return (
      <div data-testid="recipes-list-loading" role="status">
        <p>{t("recipes.loading")}</p>
      </div>
    );
  }

  if (status === "error") {
    return (
      <div data-testid="recipes-list-error" className="space-y-4">
        <FeatureUnavailable
          feature="recipes-list"
          title={t("feature.serviceUnavailable")}
          detail={t(errorKey ?? "recipes.list.error")}
        />
        <Button
          type="button"
          data-testid="recipes-list-retry"
          onClick={() => setReloadToken((value) => value + 1)}
        >
          {t("recipes.actions.retry")}
        </Button>
      </div>
    );
  }

  return (
    <div data-testid="recipes-list" className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="font-display text-3xl">{t("recipes.title")}</h1>
          <p className="mt-1 text-muted-foreground">{t("recipes.subtitle")}</p>
        </div>
        <Button asChild data-testid="recipes-generate-link">
          <Link to="/app/receitas/gerar">{t("recipes.actions.generate")}</Link>
        </Button>
      </div>

      {items.length === 0 ? (
        <div
          data-testid="recipes-empty"
          role="status"
          className="rounded-2xl border border-border bg-secondary/40 p-6"
        >
          <p className="font-display text-xl">{t("recipes.empty")}</p>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("recipes.empty.guidance")}
          </p>
        </div>
      ) : (
        <ul className="space-y-3" aria-label={t("recipes.title")}>
          {items.map((recipe) => (
            <li key={recipe.recipeId}>
              <Link
                to={`/app/receitas/${recipe.recipeId}`}
                data-testid={`recipes-item-${recipe.recipeId}`}
                className="block rounded-2xl border border-border bg-card p-4 hover:bg-accent/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <h2 className="font-display text-xl">{recipe.name}</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("recipes.detail.servings", { count: recipe.servings })}
                  {recipe.mealTypes.length > 0
                    ? ` · ${recipe.mealTypes.join(", ")}`
                    : ""}
                </p>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
