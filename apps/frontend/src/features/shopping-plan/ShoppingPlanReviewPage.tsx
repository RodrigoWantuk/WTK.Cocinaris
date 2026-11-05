import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { Menu, MenuEntry } from "@/contracts/planning";
import type { ShoppingPlan } from "@/contracts/shoppingPlan";
import { formatMenuDay } from "@/features/planning/planningDates";
import { usePlanningRepository } from "@/features/planning/PlanningProvider";
import { restrictionLabel } from "./restrictionLabel";
import { useShoppingPlanRepository } from "./ShoppingPlanProvider";
import { useExpansionQueue } from "./useExpansionQueue";

const byOrder = (a: MenuEntry, b: MenuEntry) =>
  (a.suggestedOrder ?? Number.MAX_SAFE_INTEGER) -
  (b.suggestedOrder ?? Number.MAX_SAFE_INTEGER);

/**
 * Review of a shopping plan (PLAN-0062): the dishes in the suggested order, what spoils first coming
 * first. The order is a suggestion, and any dish can be given a date. The list already works here as
 * a simulation; only "Usar este plano" puts meals on the home page and reserves the pantry.
 */
export function ShoppingPlanReviewPage() {
  const { sessionId = "" } = useParams<{ sessionId: string }>();
  const repo = useShoppingPlanRepository();
  const planning = usePlanningRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const [plan, setPlan] = useState<ShoppingPlan | null>(null);
  const [menu, setMenu] = useState<Menu | null>(null);
  const [failed, setFailed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const csrfToken = session.csrfToken;
  const reviewedFor = useRef<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    repo.get(sessionId, controller.signal).then(
      (value) => {
        if (controller.signal.aborted) return;
        if (value.status === "finished") {
          navigate(`/app/cardapio/${value.menuId}`, { replace: true });
          return;
        }
        setPlan(value);
      },
      () => !controller.signal.aborted && setFailed(true),
    );
    return () => controller.abort();
  }, [repo, sessionId, navigate]);

  const stopped = useExpansionQueue(
    plan,
    repo,
    { csrfToken, language: locale },
    setPlan,
  );
  const pending =
    plan?.accepted.filter((card) => card.expansionState === "pending").length ??
    0;
  const ready =
    plan?.accepted.filter((card) => card.expansionState === "ready").length ??
    0;
  const menuId = plan?.menuId;

  const refreshMenu = useCallback(async () => {
    if (menuId) setMenu(await planning.getMenu(menuId));
  }, [menuId, planning]);

  // Reviewing computes the order, so it runs each time the set of ready dishes changes.
  const signature = `${pending}:${ready}`;
  useEffect(() => {
    if (!plan || !csrfToken || pending > 0 || reviewedFor.current === signature)
      return;
    reviewedFor.current = signature;
    repo.review(sessionId, { csrfToken }).then(
      async (value) => {
        setPlan(value);
        await refreshMenu();
      },
      () => setFailed(true),
    );
  }, [plan, csrfToken, pending, signature, repo, sessionId, refreshMenu]);

  const act = async (work: () => Promise<void>) => {
    if (saving) return;
    setSaving(true);
    setError(null);
    try {
      await work();
    } catch {
      setError("shoppingPlan.error.save");
    } finally {
      setSaving(false);
    }
  };

  if (failed) {
    return (
      <p role="alert" className="text-muted-foreground">
        {t("shoppingPlan.error.load")}
      </p>
    );
  }
  if (!plan || !csrfToken) return <p role="status">{t("planning.loading")}</p>;

  const replaceEntry = (entry: MenuEntry) =>
    act(async () => {
      await repo.replace(sessionId, entry.entryId, { csrfToken });
      navigate(`/app/compras/planejar/${sessionId}`);
    });
  const removeEntry = (entry: MenuEntry) =>
    act(async () => {
      const updated = await repo.replace(sessionId, entry.entryId, {
        csrfToken,
      });
      setPlan(updated);
      reviewedFor.current = null;
      await refreshMenu();
    });
  const changeEntry = (
    entry: MenuEntry,
    change: { plannedDate?: string | null; servings?: number },
  ) =>
    act(async () => {
      const updated = await planning.changeEntry(
        plan.menuId,
        entry.entryId,
        {
          plannedDate:
            change.plannedDate === undefined
              ? entry.plannedDate
              : change.plannedDate,
          mealType: entry.mealType,
          servings: change.servings ?? entry.servings,
          status: entry.status === "skipped" ? "skipped" : "planned",
        },
        { csrfToken },
      );
      setMenu(updated);
    });
  const failedCards = plan.accepted.filter(
    (card) => card.expansionState === "failed",
  );
  const notes = plan.accepted.filter((card) => card.guardCode);
  const entries = [...(menu?.entries ?? [])].sort(byOrder);

  return (
    <div data-testid="plan-review" className="space-y-6">
      <header>
        <h1 className="font-display text-3xl">
          {t("shoppingPlan.review.title")}
        </h1>
        <p className="mt-1 text-muted-foreground">
          {t("shoppingPlan.review.subtitle")}
        </p>
      </header>

      {pending > 0 ? (
        <p
          role="status"
          data-testid="plan-review-preparing"
          className="surface p-4"
        >
          {t("shoppingPlan.review.preparing", {
            done: ready,
            total: ready + pending,
          })}
        </p>
      ) : null}
      {stopped ? (
        <p role="alert" className="text-sm text-destructive">
          {t("shoppingPlan.expansion.stopped")}
        </p>
      ) : null}
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(error)}
        </p>
      ) : null}

      {failedCards.length > 0 ? (
        <ul className="space-y-2" data-testid="plan-review-failed">
          {failedCards.map((card) => (
            <li key={card.cardId} className="surface space-y-2 p-4">
              <p className="font-semibold">
                {t(
                  card.failureCode === "restriction_blocked"
                    ? "shoppingPlan.review.blocked"
                    : "shoppingPlan.review.failed",
                  { dish: card.name },
                )}
              </p>
              <div className="flex flex-wrap gap-2">
                {card.failureCode !== "restriction_blocked" ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    disabled={saving}
                    data-testid="plan-review-retry"
                    onClick={() =>
                      void act(async () => {
                        setPlan(
                          await repo.expand(sessionId, card.cardId, {
                            csrfToken,
                            language: locale,
                          }),
                        );
                      })
                    }
                  >
                    {t("shoppingPlan.review.retry")}
                  </Button>
                ) : null}
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  disabled={saving}
                  onClick={() =>
                    void act(async () => {
                      await repo.removeCard(sessionId, card.cardId, {
                        csrfToken,
                      });
                      navigate(`/app/compras/planejar/${sessionId}`);
                    })
                  }
                >
                  {t("shoppingPlan.review.swap")}
                </Button>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  disabled={saving}
                  data-testid="plan-review-remove-failed"
                  onClick={() =>
                    void act(async () => {
                      setPlan(
                        await repo.removeCard(sessionId, card.cardId, {
                          csrfToken,
                        }),
                      );
                    })
                  }
                >
                  {t("shoppingPlan.review.remove")}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      ) : null}

      {notes.length > 0 ? (
        <ul
          className="space-y-1 text-sm text-muted-foreground"
          data-testid="plan-review-notes"
        >
          {notes.map((card) => (
            <li key={card.cardId}>
              {card.name}:{" "}
              {t("shoppingPlan.card.guard", {
                ingredient:
                  card.guardIngredient ?? t("shoppingPlan.card.guardAny"),
                restriction: restrictionLabel(locale, card.guardCode ?? ""),
              })}
            </li>
          ))}
        </ul>
      ) : null}

      <ol
        className="surface divide-y divide-border"
        data-testid="plan-review-list"
      >
        {entries.map((entry, index) => (
          <li
            key={entry.entryId}
            className="space-y-3 p-4"
            data-testid="plan-review-entry"
          >
            <div>
              <p className="text-xs font-semibold text-muted-foreground">
                {entry.plannedDate
                  ? t("shoppingPlan.review.onDay", {
                      day: formatMenuDay(entry.plannedDate, locale),
                    })
                  : t("shoppingPlan.review.position", { position: index + 1 })}
              </p>
              <p className="text-[17px] font-semibold">{entry.recipeName}</p>
              {entry.kind === "component" ? (
                <p
                  className="text-sm font-medium"
                  data-testid="plan-review-base"
                >
                  {t("shoppingPlan.review.base", {
                    name: entry.producesName ?? "",
                  })}
                </p>
              ) : null}
              <p className="text-sm text-muted-foreground">
                {[
                  t("recipes.detail.servings", { count: entry.servings }),
                  entry.mealsCovered > 1
                    ? t("shoppingPlan.review.covers", {
                        count: entry.mealsCovered,
                      })
                    : null,
                ]
                  .filter(Boolean)
                  .join(" · ")}
              </p>
            </div>
            <div className="grid gap-2 sm:grid-cols-2">
              <label className="space-y-1 text-xs text-muted-foreground">
                {t("shoppingPlan.review.servings")}
                <Input
                  type="number"
                  min={1}
                  max={24}
                  defaultValue={entry.servings}
                  data-testid="plan-review-servings"
                  onBlur={(event) => {
                    const value = Number(event.target.value);
                    if (
                      Number.isInteger(value) &&
                      value >= 1 &&
                      value <= 24 &&
                      value !== entry.servings
                    ) {
                      void changeEntry(entry, { servings: value });
                    }
                  }}
                />
              </label>
              <label className="space-y-1 text-xs text-muted-foreground">
                {t("shoppingPlan.review.date")}
                <Input
                  type="date"
                  min={menu?.startDate}
                  max={menu?.endDate}
                  value={entry.plannedDate ?? ""}
                  data-testid="plan-review-date"
                  onChange={(event) =>
                    void changeEntry(entry, {
                      plannedDate: event.target.value || null,
                    })
                  }
                />
              </label>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                variant="secondary"
                disabled={saving}
                data-testid="plan-review-swap"
                onClick={() => void replaceEntry(entry)}
              >
                {t("shoppingPlan.review.swap")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                disabled={saving}
                data-testid="plan-review-remove"
                onClick={() => void removeEntry(entry)}
              >
                {t("shoppingPlan.review.remove")}
              </Button>
            </div>
          </li>
        ))}
      </ol>
      {entries.length === 0 && pending === 0 ? (
        <p data-testid="plan-review-empty" className="text-muted-foreground">
          {t("shoppingPlan.review.empty")}
        </p>
      ) : null}

      <div className="space-y-2">
        <Button
          type="button"
          size="lg"
          className="w-full"
          data-testid="plan-use"
          disabled={saving || pending > 0 || entries.length === 0}
          onClick={() =>
            void act(async () => {
              await repo.accept(sessionId, { csrfToken });
              navigate(`/app/cardapio/${plan.menuId}`);
            })
          }
        >
          {t("shoppingPlan.review.use")}
        </Button>
        <Button asChild variant="secondary" className="w-full">
          <Link
            to={`/app/cardapio/${plan.menuId}/compras`}
            data-testid="plan-see-list"
          >
            {t("shoppingPlan.review.list")}
          </Link>
        </Button>
        <Button asChild variant="ghost" className="w-full">
          <Link
            to={`/app/compras/planejar/${sessionId}`}
            data-testid="plan-more-dishes"
          >
            {t("shoppingPlan.review.more")}
          </Link>
        </Button>
      </div>
    </div>
  );
}
