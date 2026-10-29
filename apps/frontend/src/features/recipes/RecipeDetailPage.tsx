import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useSession } from "@/app/session/SessionProvider";
import { RecipeContent } from "./RecipeContent";
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
  const { session } = useSession();
  const navigate = useNavigate();
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  // Starting is explicit; the backend returns the execution already in progress, if any.
  const startCooking = async () => {
    if (!session.csrfToken || !detail) return;
    setStarting(true);
    setStartError(null);
    try {
      const execution = await repo.startExecution(detail.recipeId, {
        csrfToken: session.csrfToken,
      });
      navigate(`/app/receitas/cozinhar/${execution.executionId}`);
    } catch (err) {
      setStartError(
        err instanceof RecipeApiError
          ? recipeErrorMessageKey(err.code)
          : "recipes.error.unexpected",
      );
    } finally {
      setStarting(false);
    }
  };

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

      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          data-testid="recipes-detail-cook"
          disabled={starting || !session.csrfToken}
          onClick={() => void startCooking()}
        >
          {starting ? t("recipes.detail.starting") : t("recipes.detail.cook")}
        </Button>
      </div>
      {startError ? (
        <p role="alert" className="text-sm text-destructive">
          {t(startError)}
        </p>
      ) : null}

      {parsed ? <RecipeContent recipe={parsed} /> : null}

      <Button asChild variant="secondary" data-testid="recipes-detail-back">
        <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
      </Button>
    </div>
  );
}
