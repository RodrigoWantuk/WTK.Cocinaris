import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useSession } from "@/app/session/SessionProvider";
import { RecipeThumbnail } from "./RecipeThumbnail";
import { usePendingThumbnailRefresh } from "./usePendingThumbnailRefresh";
import { RecipeContent } from "./RecipeContent";
import { useRecipeRepository } from "./RecipeProvider";
import {
  RecipeApiError,
  type RecipeDetail,
  type RecipeHistory,
  type RecipeAvailabilityLine,
} from "@/contracts/recipes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import {
  ChefHat,
  ChevronDown,
  Clock,
  MoreHorizontal,
  Star,
  Users,
} from "lucide-react";
import { Button, buttonVariants } from "@/components/ui/button";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import { parseNormalizedRecipeJson } from "./parseNormalizedRecipe";
import { recipeLibraryErrorKey } from "./recipeErrors";
import { formatRecipeDate, mealTypeLabel } from "./recipeLabels";

/**
 * Authenticated detail of one owned recipe: the current revision with cooking, favorite, edit,
 * variation, and archive actions, plus its history (PLAN-0038). On
 * `/app/receitas/:recipeId/versoes/:revisionNumber` it shows an earlier revision read-only.
 */
export function RecipeDetailPage() {
  const { recipeId = "", revisionNumber: revisionParam } = useParams<{
    recipeId: string;
    revisionNumber?: string;
  }>();
  const revisionNumber = revisionParam ? Number(revisionParam) : null;
  const repo = useRecipeRepository();
  const { t, locale } = useProductionI18n();
  const [detail, setDetail] = useState<RecipeDetail | null>(null);
  const [availability, setAvailability] = useState<
    RecipeAvailabilityLine[] | null
  >(null);
  const [history, setHistory] = useState<RecipeHistory | null>(null);
  const [historyFailed, setHistoryFailed] = useState(false);
  const [status, setStatus] = useState<"loading" | "ready" | "error">(
    "loading",
  );
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const requestIdRef = useRef(0);
  const abortRef = useRef<AbortController | null>(null);
  const { session } = useSession();
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  // Starting is explicit; the backend returns the execution already in progress, if any.
  const startCooking = async () => {
    if (!session.csrfToken || !detail) return;
    setBusy(true);
    setActionError(null);
    try {
      const execution = await repo.startExecution(detail.recipeId, {
        csrfToken: session.csrfToken,
      });
      navigate(`/app/receitas/cozinhar/${execution.executionId}`);
    } catch (err) {
      setActionError(
        err instanceof RecipeApiError
          ? recipeLibraryErrorKey(err.code, "recipes.error.unexpected")
          : "recipes.error.unexpected",
      );
    } finally {
      setBusy(false);
    }
  };

  const organize = async (change: "favorite" | "archive") => {
    if (!session.csrfToken || !detail) return;
    if (
      change === "archive" &&
      !detail.archivedAt &&
      !window.confirm(t("recipes.detail.archiveConfirm"))
    )
      return;
    setBusy(true);
    setActionError(null);
    try {
      const options = { csrfToken: session.csrfToken };
      const updated =
        change === "favorite"
          ? await repo.setFavorite(detail.recipeId, !detail.isFavorite, options)
          : await repo.setArchived(
              detail.recipeId,
              !detail.archivedAt,
              options,
            );
      setDetail(updated);
    } catch {
      setActionError("library.error.update");
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    if (
      !recipeId ||
      (revisionNumber !== null && !Number.isInteger(revisionNumber))
    ) {
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
    setHistory(null);
    setHistoryFailed(false);
    void (async () => {
      try {
        const recipe =
          revisionNumber === null
            ? await repo.getRecipe(recipeId, controller.signal)
            : await repo.getRecipeRevision(
                recipeId,
                revisionNumber,
                controller.signal,
              );
        if (requestId !== requestIdRef.current) return;
        setDetail(recipe);
        setStatus("ready");
        // Pantry status is a separate, optional read; a failure only hides it.
        setAvailability(null);
        if (revisionNumber === null && repo.getRecipeAvailability) {
          repo
            .getRecipeAvailability(recipeId, controller.signal)
            .then((lines) => {
              if (requestId === requestIdRef.current) setAvailability(lines);
            })
            .catch(() => undefined);
        }
      } catch (err) {
        if (controller.signal.aborted) return;
        if (err instanceof RecipeApiError && err.code === "cancelled") return;
        if (requestId !== requestIdRef.current) return;
        setStatus("error");
        setErrorKey(
          err instanceof RecipeApiError
            ? recipeLibraryErrorKey(err.code, "recipes.detail.error")
            : "recipes.detail.error",
        );
        return;
      }
      // History is secondary: the recipe stays usable when it fails.
      try {
        const loaded = await repo.getRecipeHistory(recipeId, controller.signal);
        if (requestId === requestIdRef.current) setHistory(loaded);
      } catch {
        if (!controller.signal.aborted && requestId === requestIdRef.current)
          setHistoryFailed(true);
      }
    })();
    return () => controller.abort();
  }, [recipeId, revisionNumber, repo, reloadToken]);

  // Only the image changes while it is generated, so the refresh updates just that and never
  // shows the loading state or touches anything the cook is reading.
  const refreshThumbnail = useCallback(() => {
    const load =
      revisionNumber === null
        ? repo.getRecipe(recipeId)
        : repo.getRecipeRevision(recipeId, revisionNumber);
    void load
      .then((fresh) =>
        setDetail((current) =>
          current && current.recipeId === fresh.recipeId
            ? { ...current, thumbnail: fresh.thumbnail }
            : current,
        ),
      )
      .catch(() => undefined);
  }, [repo, recipeId, revisionNumber]);
  usePendingThumbnailRefresh(
    detail?.thumbnail?.state === "pending",
    detail,
    refreshThumbnail,
  );

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

  const isOldRevision = detail.revisionNumber !== detail.currentRevisionNumber;
  // Work plus waiting time of every stage, so a beginner knows how long it takes before starting.
  const totalMinutes = (parsed?.stages ?? []).reduce(
    (sum, stage) =>
      sum + (stage.activeMinutes ?? 0) + (stage.passiveMinutes ?? 0),
    0,
  );
  const date = (iso: string | null) =>
    iso ? formatRecipeDate(iso, locale) : "";

  return (
    <div data-testid="recipes-detail" className="space-y-6">
      {detail.thumbnail && detail.thumbnail.state !== "none" ? (
        <RecipeThumbnail
          thumbnail={detail.thumbnail}
          name={detail.name}
          disclose
          priority
        />
      ) : null}
      <div>
        {/* The version number lives in the history; the page leads with the dish (review M8). */}
        <h1 className="font-display text-3xl leading-tight">{detail.name}</h1>
        {/* Key facts as tiles (PLAN-0058): time, servings, and how often it was cooked. */}
        <dl className="mt-4 grid grid-cols-3 gap-2">
          {[
            {
              icon: Clock,
              label: t("recipes.detail.factTime"),
              value:
                totalMinutes > 0
                  ? t("recipes.detail.factMinutes", { minutes: totalMinutes })
                  : "—",
            },
            {
              icon: Users,
              label: t("recipes.detail.factServings"),
              value: String(detail.servings),
            },
            {
              icon: ChefHat,
              label: t("recipes.detail.factCooked"),
              value: String(
                history?.cooking.filter((entry) => entry.status === "Completed")
                  .length ?? "—",
              ),
            },
          ].map(({ icon: Icon, label, value }) => (
            <div key={label} className="surface flex flex-col gap-1 p-3">
              <Icon aria-hidden="true" className="h-5 w-5 text-primary" />
              <dt className="text-xs font-medium text-muted-foreground">
                {label}
              </dt>
              <dd className="tabular text-lg font-semibold leading-none">
                {value}
              </dd>
            </div>
          ))}
        </dl>
        {detail.mealTypes.length > 0 ? (
          <ul
            aria-label={t("recipes.detail.mealTypes")}
            className="mt-2 flex flex-wrap gap-1.5"
          >
            {detail.mealTypes.map((type) => (
              <li
                key={type}
                className="rounded-full bg-secondary px-2.5 py-0.5 text-[13px] font-medium text-secondary-foreground"
              >
                {mealTypeLabel(t, type)}
              </li>
            ))}
          </ul>
        ) : null}
        {parsed?.yield ? (
          <p className="mt-1 text-sm text-muted-foreground">
            {t("recipes.detail.yield", { value: parsed.yield })}
          </p>
        ) : null}
        {detail.changeReason ? (
          <p className="mt-1 text-sm text-muted-foreground">
            {t("recipes.detail.changeReason", { reason: detail.changeReason })}
          </p>
        ) : null}
        {detail.parentRecipeId ? (
          <p className="mt-2 text-sm" data-testid="recipes-detail-parent">
            {t("recipes.detail.derivedFrom", {
              reason: detail.derivationReason ?? "",
            })}{" "}
            <Link
              className="underline"
              to={`/app/receitas/${detail.parentRecipeId}`}
            >
              {t("recipes.detail.parentLink")}
            </Link>
          </p>
        ) : null}
      </div>

      {isOldRevision ? (
        <div
          role="status"
          data-testid="recipes-detail-old-revision"
          className="rounded-2xl border border-border bg-secondary/40 p-4 text-sm"
        >
          <p>
            {t("recipes.detail.oldRevision", {
              number: detail.revisionNumber,
              current: detail.currentRevisionNumber,
            })}
          </p>
          <Link className="underline" to={`/app/receitas/${detail.recipeId}`}>
            {t("recipes.detail.currentLink")}
          </Link>
        </div>
      ) : (
        <>
          {detail.archivedAt ? (
            <p role="status" className="text-sm text-muted-foreground">
              {t("recipes.detail.archivedNotice")}
            </p>
          ) : null}
          <div className="flex items-start gap-2">
            <Button
              type="button"
              size="lg"
              className="flex-1 sm:flex-none"
              data-testid="recipes-detail-cook"
              disabled={busy || !session.csrfToken}
              onClick={() => void startCooking()}
            >
              {busy ? t("recipes.detail.starting") : t("recipes.detail.cook")}
            </Button>
            <Button
              type="button"
              variant="secondary"
              size="lg"
              className="w-14 px-0"
              data-testid="recipes-detail-favorite"
              aria-pressed={detail.isFavorite}
              aria-label={t(
                detail.isFavorite
                  ? "recipes.detail.unfavorite"
                  : "recipes.detail.favorite",
              )}
              disabled={busy || !session.csrfToken}
              onClick={() => void organize("favorite")}
            >
              <Star
                aria-hidden="true"
                className={`!size-5 ${detail.isFavorite ? "fill-primary text-primary" : ""}`}
              />
            </Button>
            {/* Less frequent actions sit behind "Mais" (review M8). */}
            <details
              className="group relative"
              data-testid="recipes-detail-more"
            >
              <summary
                className={`${buttonVariants({ variant: "secondary", size: "lg" })} cursor-pointer list-none [&::-webkit-details-marker]:hidden`}
              >
                <MoreHorizontal aria-hidden="true" className="!size-5" />
                <span className="sr-only sm:not-sr-only">
                  {t("recipes.detail.more")}
                </span>
              </summary>
              <div className="absolute right-0 z-30 mt-2 flex w-56 flex-col gap-1 rounded-2xl border border-border bg-popover p-2 text-popover-foreground shadow-lg">
                <Button
                  asChild
                  variant="ghost"
                  className="justify-start"
                  data-testid="recipes-detail-edit"
                >
                  <Link to={`/app/receitas/${detail.recipeId}/editar`}>
                    {t("recipes.detail.edit")}
                  </Link>
                </Button>
                <Button
                  asChild
                  variant="ghost"
                  className="justify-start"
                  data-testid="recipes-detail-variation"
                >
                  <Link to={`/app/receitas/${detail.recipeId}/variacao`}>
                    {t("recipes.detail.variation")}
                  </Link>
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  className="justify-start"
                  data-testid="recipes-detail-archive"
                  disabled={busy || !session.csrfToken}
                  onClick={() => void organize("archive")}
                >
                  {t(
                    detail.archivedAt
                      ? "recipes.detail.unarchive"
                      : "recipes.detail.archive",
                  )}
                </Button>
              </div>
            </details>
          </div>
        </>
      )}
      {actionError ? (
        <p role="alert" className="text-sm text-destructive">
          {t(actionError)}
        </p>
      ) : null}

      {parsed ? (
        <RecipeContent recipe={parsed} availability={availability} />
      ) : null}

      <details
        data-testid="recipes-history"
        className="surface group p-4 sm:p-5"
      >
        <summary className="flex cursor-pointer list-none items-center justify-between gap-2 font-display text-xl [&::-webkit-details-marker]:hidden">
          {t("recipes.history.title")}
          <ChevronDown
            aria-hidden="true"
            className="h-5 w-5 text-muted-foreground transition-transform duration-150 group-open:rotate-180"
          />
        </summary>
        <div className="mt-4 space-y-4">
          {historyFailed ? (
            <p className="text-sm text-muted-foreground">
              {t("recipes.history.unavailable")}
            </p>
          ) : null}
          {history ? (
            <div className="grid gap-6 sm:grid-cols-2">
              <div>
                <h3 className="font-medium">{t("recipes.history.cooking")}</h3>
                {history.cooking.length === 0 ? (
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("recipes.history.cooking.empty")}
                  </p>
                ) : (
                  <ul className="mt-2 space-y-2 text-sm">
                    {history.cooking.map((entry) => (
                      <li
                        key={entry.executionId}
                        data-testid="recipes-history-cooking"
                      >
                        <p>
                          {entry.status === "Completed"
                            ? t("recipes.history.cooked", {
                                date: date(entry.finishedAt),
                                count: entry.servings,
                              })
                            : t("recipes.history.abandoned", {
                                date: date(entry.finishedAt),
                              })}
                          {entry.rating
                            ? ` · ${t("recipes.history.rating", { value: entry.rating })}`
                            : ""}
                        </p>
                        {entry.notes ? (
                          <p className="whitespace-pre-line text-muted-foreground">
                            {entry.notes}
                          </p>
                        ) : null}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <div>
                <h3 className="font-medium">
                  {t("recipes.history.revisions")}
                </h3>
                <ul className="mt-2 space-y-2 text-sm">
                  {history.revisions.map((entry) => {
                    const label = t("recipes.history.revision", {
                      number: entry.revisionNumber,
                    });
                    const current =
                      entry.revisionNumber === detail.currentRevisionNumber;
                    return (
                      <li
                        key={entry.revisionNumber}
                        data-testid="recipes-history-revision"
                      >
                        {entry.revisionNumber === detail.revisionNumber ? (
                          <span className="font-medium">{label}</span>
                        ) : (
                          <Link
                            className="underline"
                            to={
                              current
                                ? `/app/receitas/${detail.recipeId}`
                                : `/app/receitas/${detail.recipeId}/versoes/${entry.revisionNumber}`
                            }
                          >
                            {label}
                          </Link>
                        )}
                        {current ? ` (${t("recipes.history.current")})` : ""} ·{" "}
                        {t(`recipes.history.origin.${entry.origin}`)}
                        {entry.sourceExecutionId
                          ? ` ${t("recipes.history.fromCooking")}`
                          : ""}{" "}
                        · {date(entry.createdAt)}
                        {entry.changeReason ? (
                          <span className="block text-muted-foreground">
                            {entry.changeReason}
                          </span>
                        ) : null}
                      </li>
                    );
                  })}
                </ul>
              </div>
            </div>
          ) : null}
        </div>
      </details>
    </div>
  );
}
