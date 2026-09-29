import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useRecipeRepository } from "./RecipeProvider";
import { RecipeApiError, type RecipeDetail } from "@/contracts/recipes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import { parseNormalizedRecipeJson } from "./parseNormalizedRecipe";
import { recipeErrorMessageKey } from "./recipeErrors";

/**
 * Authenticated production detail for one owned saved recipe revision.
 */
export function RecipeDetailPage() {
  const { recipeId = "" } = useParams<{ recipeId: string }>();
  const repo = useRecipeRepository();
  const { t } = useProductionI18n();
  const [detail, setDetail] = useState<RecipeDetail | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "error">(
    "loading",
  );
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const requestIdRef = useRef(0);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (!recipeId) {
      setStatus("error");
      setErrorKey("recipes.detail.notFound");
      return;
    }
    const requestId = ++requestIdRef.current;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setStatus("loading");
    setErrorKey(null);
    void (async () => {
      try {
        const recipe = await repo.getRecipe(recipeId, controller.signal);
        if (requestId !== requestIdRef.current) return;
        setDetail(recipe);
        setStatus("ready");
      } catch (err) {
        if (controller.signal.aborted) return;
        if (err instanceof RecipeApiError && err.code === "cancelled") return;
        if (requestId !== requestIdRef.current) return;
        setStatus("error");
        setErrorKey(
          err instanceof RecipeApiError
            ? recipeErrorMessageKey(err.code)
            : "recipes.detail.error",
        );
      }
    })();
    return () => controller.abort();
  }, [recipeId, repo, reloadToken]);

  const parsed = useMemo(
    () =>
      detail ? parseNormalizedRecipeJson(detail.normalizedRecipeJson) : null,
    [detail],
  );

  if (status === "loading") {
    return (
      <div data-testid="recipes-detail-loading" role="status">
        <p>{t("recipes.detail.loading")}</p>
      </div>
    );
  }

  if (status === "error" || !detail) {
    return (
      <div data-testid="recipes-detail-error" className="space-y-4">
        <FeatureUnavailable
          feature="recipes-detail"
          title={t("feature.unavailable")}
          detail={t(errorKey ?? "recipes.detail.error")}
        />
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            data-testid="recipes-detail-retry"
            onClick={() => setReloadToken((value) => value + 1)}
          >
            {t("recipes.actions.retry")}
          </Button>
          <Button asChild variant="secondary">
            <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div data-testid="recipes-detail" className="space-y-6">
      <div>
        <p className="text-sm text-muted-foreground">
          {t("recipes.detail.revision", { number: detail.revisionNumber })}
        </p>
        <h1 className="font-display text-3xl">{detail.name}</h1>
        <p className="mt-2 text-muted-foreground">
          {t("recipes.detail.servings", { count: detail.servings })}
        </p>
        {detail.mealTypes.length > 0 ? (
          <p className="mt-1 text-sm text-muted-foreground">
            {t("recipes.detail.mealTypes")}: {detail.mealTypes.join(", ")}
          </p>
        ) : null}
        {parsed?.yield ? (
          <p className="mt-1 text-sm text-muted-foreground">
            {t("recipes.detail.yield", { value: parsed.yield })}
          </p>
        ) : null}
      </div>

      {parsed && parsed.ingredients.length > 0 ? (
        <section aria-labelledby="recipes-detail-ingredients">
          <h2 id="recipes-detail-ingredients" className="font-display text-xl">
            {t("recipes.detail.ingredients")}
          </h2>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm">
            {parsed.ingredients.map((ingredient, index) => (
              <li key={`${ingredient.displayName}-${index}`}>
                {ingredient.displayName}
                {ingredient.quantityLabel
                  ? ` — ${ingredient.quantityLabel}`
                  : ""}
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {parsed && parsed.stages.length > 0 ? (
        <section aria-labelledby="recipes-detail-stages">
          <h2 id="recipes-detail-stages" className="font-display text-xl">
            {t("recipes.detail.stages")}
          </h2>
          <ol className="mt-3 space-y-4">
            {parsed.stages.map((stage, index) => (
              <li key={`${stage.title}-${index}`}>
                <h3 className="font-medium">{stage.title}</h3>
                <ol className="mt-1 list-decimal space-y-1 pl-5 text-sm text-muted-foreground">
                  {stage.instructions.map((instruction, stepIndex) => (
                    <li key={stepIndex}>{instruction}</li>
                  ))}
                </ol>
              </li>
            ))}
          </ol>
        </section>
      ) : null}

      {parsed && parsed.assumptions.length > 0 ? (
        <section aria-labelledby="recipes-detail-assumptions">
          <h2 id="recipes-detail-assumptions" className="font-display text-xl">
            {t("recipes.detail.assumptions")}
          </h2>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted-foreground">
            {parsed.assumptions.map((assumption) => (
              <li key={assumption}>{assumption}</li>
            ))}
          </ul>
        </section>
      ) : null}

      <Button asChild variant="secondary" data-testid="recipes-detail-back">
        <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
      </Button>
    </div>
  );
}
