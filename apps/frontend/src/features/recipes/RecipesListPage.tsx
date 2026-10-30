import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { PenLine, Sparkles } from "lucide-react";
import { RecipeCard } from "./RecipeCard";
import { SavedCandidatesSection } from "./SavedCandidatesSection";
import { usePendingThumbnailRefresh } from "./usePendingThumbnailRefresh";
import { useRecipeRepository } from "./RecipeProvider";
import { RecipeApiError, type RecipeSummary } from "@/contracts/recipes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import { recipeLibraryErrorKey } from "./recipeErrors";
import { mealTypeLabel } from "./recipeLabels";

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
      <div className="space-y-4">
        <div>
          <h1 className="font-display text-3xl">{t("recipes.title")}</h1>
          <p className="mt-1 text-muted-foreground">{t("recipes.subtitle")}</p>
        </div>
        <div className="flex flex-col gap-2 sm:flex-row">
          <Button asChild data-testid="recipes-generate-link">
            <Link to="/app/receitas/gerar">
              <Sparkles aria-hidden="true" />
              {t("recipes.actions.generate")}
            </Link>
          </Button>
          <Button asChild variant="secondary" data-testid="recipes-new-link">
            <Link to="/app/receitas/nova">
              <PenLine aria-hidden="true" />
              {t("library.new")}
            </Link>
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

      <SavedCandidatesSection />

      {updateError ? (
        <p role="alert" className="text-sm text-destructive">
          {t("library.error.update")}
        </p>
      ) : null}

      {items.length === 0 ? (
        <div
          data-testid="recipes-empty"
          role="status"
          className="surface flex flex-col items-center gap-2 p-8 text-center"
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
        // A grid of image cards (PLAN-0058): two per row on phones, three from `sm`, four on
        // wide screens, so the library reads as dishes rather than a list of names.
        <ul
          className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4"
          aria-label={t("recipes.title")}
        >
          {items.map((recipe) => {
            const meta = [
              recipe.mealTypes[0]
                ? mealTypeLabel(t, recipe.mealTypes[0])
                : null,
              recipe.timesCooked > 0
                ? t("library.cooked", { count: recipe.timesCooked }) +
                  (recipe.lastRating
                    ? ` · ${t("library.rating", { value: recipe.lastRating })}`
                    : "")
                : t("library.cooked.never"),
              recipe.origin === "derived" ? t("library.origin.derived") : null,
              recipe.archivedAt ? t("library.archived") : null,
            ].filter((value): value is string => Boolean(value));
            return (
              <li key={recipe.recipeId}>
                <RecipeCard
                  recipeId={recipe.recipeId}
                  name={recipe.name}
                  thumbnail={recipe.thumbnail}
                  meta={meta}
                  testId={`recipes-item-${recipe.recipeId}`}
                  className="h-full"
                  favorite={
                    recipe.archivedAt
                      ? undefined
                      : {
                          active: recipe.isFavorite,
                          label: t(
                            recipe.isFavorite
                              ? "library.favorite.remove"
                              : "library.favorite.add",
                            { name: recipe.name },
                          ),
                          disabled: !session.csrfToken,
                          onToggle: () => void toggleFavorite(recipe),
                          testId: `recipes-favorite-${recipe.recipeId}`,
                        }
                  }
                />
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
