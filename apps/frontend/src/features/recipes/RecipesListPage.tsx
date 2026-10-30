import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { RecipeThumbnail } from "./RecipeThumbnail";
import { usePendingThumbnailRefresh } from "./usePendingThumbnailRefresh";
import { useRecipeRepository } from "./RecipeProvider";
import { RecipeApiError, type RecipeSummary } from "@/contracts/recipes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import { recipeLibraryErrorKey } from "./recipeErrors";
import { mealTypesLabel } from "./recipeLabels";

type LibraryFilter = "all" | "favorites" | "archived";

const SEARCH_DELAY_MS = 300;

/**
 * Authenticated recipe library (PLAN-0038): search, favorites and archived filters, and a
 * favorite toggle per recipe. Filtering happens on the server so archived recipes never reach the
 * default list.
 */
export function RecipesListPage() {
  const repo = useRecipeRepository();
  const { t } = useProductionI18n();
  const { session } = useSession();
  const [items, setItems] = useState<RecipeSummary[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "error">(
    "loading",
  );
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [filter, setFilter] = useState<LibraryFilter>("all");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [updateError, setUpdateError] = useState(false);
  const requestIdRef = useRef(0);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const handle = window.setTimeout(
      () => setSearch(searchInput.trim()),
      SEARCH_DELAY_MS,
    );
    return () => window.clearTimeout(handle);
  }, [searchInput]);

  useEffect(() => {
    const requestId = ++requestIdRef.current;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setStatus((current) => (current === "ready" ? current : "loading"));
    setErrorKey(null);
    void (async () => {
      try {
        const recipes = await repo.listRecipes(controller.signal, {
          archived: filter === "archived",
          favorites: filter === "favorites",
          search: search || undefined,
        });
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
            ? recipeLibraryErrorKey(err.code, "recipes.list.error")
            : "recipes.list.error",
        );
      }
    })();
    return () => controller.abort();
  }, [repo, reloadToken, filter, search]);

  // A new recipe's image is generated after the first read; reload quietly until it is ready.
  usePendingThumbnailRefresh(
    items.some((recipe) => recipe.thumbnail?.state === "pending"),
    items,
    () => setReloadToken((token) => token + 1),
  );

  const toggleFavorite = async (recipe: RecipeSummary) => {
    if (!session.csrfToken) return;
    setUpdateError(false);
    try {
      const updated = await repo.setFavorite(
        recipe.recipeId,
        !recipe.isFavorite,
        { csrfToken: session.csrfToken },
      );
      setItems((current) =>
        filter === "favorites" && !updated.isFavorite
          ? current.filter((item) => item.recipeId !== recipe.recipeId)
          : current.map((item) =>
              item.recipeId === recipe.recipeId
                ? { ...item, isFavorite: updated.isFavorite }
                : item,
            ),
      );
    } catch {
      setUpdateError(true);
    }
  };

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

  const emptyKey = search
    ? "library.empty.search"
    : filter === "favorites"
      ? "library.empty.favorites"
      : filter === "archived"
        ? "library.empty.archived"
        : null;

  return (
    <div data-testid="recipes-list" className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="font-display text-3xl">{t("recipes.title")}</h1>
          <p className="mt-1 text-muted-foreground">{t("recipes.subtitle")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button asChild data-testid="recipes-generate-link">
            <Link to="/app/receitas/gerar">
              {t("recipes.actions.generate")}
            </Link>
          </Button>
          <Button asChild variant="secondary" data-testid="recipes-new-link">
            <Link to="/app/receitas/nova">{t("library.new")}</Link>
          </Button>
        </div>
      </div>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="flex-1">
          <label htmlFor="recipes-search" className="sr-only">
            {t("library.search")}
          </label>
          <Input
            id="recipes-search"
            type="search"
            data-testid="recipes-search"
            value={searchInput}
            maxLength={80}
            placeholder={t("library.search.placeholder")}
            onChange={(event) => setSearchInput(event.target.value)}
          />
        </div>
        <div
          role="group"
          aria-label={t("library.filter.label")}
          className="flex gap-1"
        >
          {(["all", "favorites", "archived"] as const).map((value) => (
            <Button
              key={value}
              type="button"
              size="sm"
              variant={filter === value ? "default" : "secondary"}
              aria-pressed={filter === value}
              data-testid={`recipes-filter-${value}`}
              onClick={() => setFilter(value)}
            >
              {t(`library.filter.${value}`)}
            </Button>
          ))}
        </div>
      </div>

      {updateError ? (
        <p role="alert" className="text-sm text-destructive">
          {t("library.error.update")}
        </p>
      ) : null}

      {items.length === 0 ? (
        <div
          data-testid="recipes-empty"
          role="status"
          className="rounded-2xl border border-border bg-secondary/40 p-6"
        >
          {emptyKey ? (
            <p>{t(emptyKey)}</p>
          ) : (
            <>
              <p className="font-display text-xl">{t("recipes.empty")}</p>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("recipes.empty.guidance")}
              </p>
            </>
          )}
        </div>
      ) : (
        <ul className="space-y-3" aria-label={t("recipes.title")}>
          {items.map((recipe) => {
            const meta = [
              t("recipes.detail.servings", { count: recipe.servings }),
              recipe.mealTypes.length > 0
                ? mealTypesLabel(t, recipe.mealTypes)
                : null,
              recipe.timesCooked > 0
                ? t("library.cooked", { count: recipe.timesCooked }) +
                  (recipe.lastRating
                    ? ` · ${t("library.rating", { value: recipe.lastRating })}`
                    : "")
                : t("library.cooked.never"),
            ].filter(Boolean);
            const badge =
              recipe.origin === "manual"
                ? t("library.origin.manual")
                : recipe.origin === "derived"
                  ? t("library.origin.derived")
                  : null;
            return (
              <li
                key={recipe.recipeId}
                className="flex items-stretch gap-2 rounded-2xl border border-border bg-card"
              >
                <Link
                  to={`/app/receitas/${recipe.recipeId}`}
                  data-testid={`recipes-item-${recipe.recipeId}`}
                  className="flex flex-1 items-center gap-4 rounded-2xl p-3 hover:bg-accent/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <RecipeThumbnail
                    thumbnail={recipe.thumbnail ?? { state: "none", url: null }}
                    name={recipe.name}
                    size="small"
                    className="w-28 shrink-0 sm:w-36"
                  />
                  <span className="min-w-0 flex-1">
                    <h2 className="font-display text-xl">{recipe.name}</h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {meta.join(" · ")}
                    </p>
                    {badge || recipe.archivedAt ? (
                      <p className="mt-1 text-xs text-muted-foreground">
                        {[
                          badge,
                          recipe.archivedAt ? t("library.archived") : null,
                        ]
                          .filter(Boolean)
                          .join(" · ")}
                      </p>
                    ) : null}
                  </span>
                </Link>
                {recipe.archivedAt ? null : (
                  <button
                    type="button"
                    data-testid={`recipes-favorite-${recipe.recipeId}`}
                    aria-pressed={recipe.isFavorite}
                    aria-label={t(
                      recipe.isFavorite
                        ? "library.favorite.remove"
                        : "library.favorite.add",
                      { name: recipe.name },
                    )}
                    disabled={!session.csrfToken}
                    onClick={() => void toggleFavorite(recipe)}
                    className="shrink-0 rounded-2xl px-4 text-2xl text-primary hover:bg-accent/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <span aria-hidden="true">
                      {recipe.isFavorite ? "★" : "☆"}
                    </span>
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
