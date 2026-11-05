import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { RecipeApiError } from "@/contracts/recipes";
import type {
  ShoppingPlan,
  ShoppingPlanBatchOptions,
  ShoppingPlanCard,
} from "@/contracts/shoppingPlan";
import { formatLocaleDecimal } from "@/lib/localeDecimal";
import { restrictionLabel } from "./restrictionLabel";
import { useShoppingPlanRepository } from "./ShoppingPlanProvider";
import { useExpansionQueue } from "./useExpansionQueue";

const MORE_FAMILIES = [
  "pasta",
  "soup_broth",
  "fish_seafood",
  "vegetarian",
  "salad",
  "sandwich_wrap",
];
const MORE_CUISINES = ["brazilian", "italian", "mexican", "japanese"];
const MAX_REPEATS = 4;
const MAX_ENTRY_SERVINGS = 24;
const UNIT_KEYS: Record<string, string> = {
  Gram: "recipes.unit.g",
  Milliliter: "recipes.unit.ml",
  Unit: "recipes.unit.unit",
};

/**
 * The dish cards of a shopping plan (PLAN-0062). One card at a time, with buttons only. Decisions feel
 * instant: the next card from the queue appears at once and the server's answer then corrects the
 * "+N" count. Focus moves to the new card's title and a polite live region announces progress.
 */
export function ShoppingPlanDeckPage() {
  const { sessionId = "" } = useParams<{ sessionId: string }>();
  const repo = useShoppingPlanRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const [plan, setPlan] = useState<ShoppingPlan | null>(null);
  const [failed, setFailed] = useState(false);
  const [hidden, setHidden] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [batchFailure, setBatchFailure] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [repeatOpen, setRepeatOpen] = useState(false);
  const [repeats, setRepeats] = useState(2);
  const [mode, setMode] = useState<"yield" | "again">("yield");
  const [lastRejected, setLastRejected] = useState<ShoppingPlanCard | null>(
    null,
  );
  const [moreOpen, setMoreOpen] = useState(false);
  const titleRef = useRef<HTMLHeadingElement>(null);
  const batching = useRef(false);
  const csrfToken = session.csrfToken;

  useEffect(() => {
    const controller = new AbortController();
    repo.get(sessionId, controller.signal).then(
      (value) => {
        if (controller.signal.aborted) return;
        if (value.status === "reviewing") {
          navigate(`/app/compras/planejar/${sessionId}/revisar`, {
            replace: true,
          });
          return;
        }
        if (value.status === "finished") {
          navigate(`/app/cardapio/${value.menuId}`, { replace: true });
          return;
        }
        if (value.status === "abandoned") {
          navigate("/app/compras/planejar", { replace: true });
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

  const fetchBatch = useCallback(
    async (options: ShoppingPlanBatchOptions) => {
      if (!csrfToken || batching.current) return;
      batching.current = true;
      setFetching(true);
      setBatchFailure(null);
      try {
        setPlan(
          await repo.addBatch(sessionId, options, {
            csrfToken,
            idempotencyKey: crypto.randomUUID(),
            language: locale,
          }),
        );
      } catch (err) {
        // The plan keeps working on the cards it has; the person is told once.
        setBatchFailure(
          err instanceof RecipeApiError ? err.code : "unexpected",
        );
      } finally {
        batching.current = false;
        setFetching(false);
      }
    },
    [csrfToken, locale, repo, sessionId],
  );

  const needsBatch = plan?.needsBatch === true;
  useEffect(() => {
    if (needsBatch && !batchFailure) void fetchBatch({});
  }, [needsBatch, batchFailure, fetchBatch]);

  const queue = (plan?.queue ?? []).filter((card) => !hidden.has(card.cardId));
  const card = queue[0] ?? null;
  const cardId = card?.cardId;
  const progress = plan?.progress;
  const filled = progress ? progress.mealsFilled >= progress.mealsTotal : false;

  useEffect(() => {
    if (cardId) titleRef.current?.focus();
  }, [cardId]);

  useEffect(() => {
    // A base offered for the dish just chosen still gets its answer before review.
    if (
      plan &&
      filled &&
      plan.status === "building" &&
      !plan.queue.some((item) => item.component)
    ) {
      navigate(`/app/compras/planejar/${sessionId}/revisar`);
    }
  }, [filled, navigate, plan, sessionId]);

  useEffect(() => {
    setRepeatOpen(false);
    setRepeats(2);
    setMode(plan?.setup.cookingStyle === "daily" ? "again" : "yield");
  }, [cardId, plan?.setup.cookingStyle]);

  const decide = async (
    target: ShoppingPlanCard,
    decision: "accept" | "reject",
    choice?: { mode: "yield" | "again"; repeats: number },
  ) => {
    if (!csrfToken) return;
    setError(null);
    setHidden((current) => new Set(current).add(target.cardId));
    setLastRejected(decision === "reject" ? target : null);
    try {
      const updated = await repo.decide(
        sessionId,
        target.cardId,
        { decision, mode: choice?.mode ?? null, repeats: choice?.repeats ?? 1 },
        { csrfToken },
      );
      setPlan(updated);
      setHidden((current) => {
        const next = new Set(current);
        next.delete(target.cardId);
        return next;
      });
    } catch {
      setHidden((current) => {
        const next = new Set(current);
        next.delete(target.cardId);
        return next;
      });
      setLastRejected(null);
      setError("shoppingPlan.error.save");
    }
  };

  const giveReason = async (
    reason: "dislike" | "too_much_work" | "tired_of",
  ) => {
    if (!csrfToken || !lastRejected) return;
    const target = lastRejected;
    setLastRejected(null);
    try {
      setPlan(
        await repo.setReason(sessionId, target.cardId, reason, { csrfToken }),
      );
    } catch {
      setError("shoppingPlan.error.save");
    }
  };

  const undo = async () => {
    if (!csrfToken) return;
    setError(null);
    setLastRejected(null);
    try {
      setPlan(await repo.undo(sessionId, { csrfToken }));
    } catch {
      setError("shoppingPlan.error.save");
    }
  };

  if (failed) {
    return (
      <p role="alert" className="text-muted-foreground">
        {t("shoppingPlan.error.load")}
      </p>
    );
  }
  if (!plan || !progress) return <p role="status">{t("planning.loading")}</p>;

  const remaining = progress.mealsTotal - progress.mealsFilled;
  const repeatLimit = Math.min(
    MAX_REPEATS,
    remaining,
    mode === "yield"
      ? Math.floor(MAX_ENTRY_SERVINGS / plan.setup.people)
      : MAX_REPEATS,
  );
  const amount = (quantity: number, unit: string) =>
    `${formatLocaleDecimal(quantity, locale)} ${t(UNIT_KEYS[unit] ?? "recipes.unit.unit")}`;
  const budget = progress.sessionMinutesBudget;
  const canReview = plan.accepted.length > 0;

  return (
    <div data-testid="plan-deck" className="space-y-4">
      <div role="status" data-testid="plan-progress" className="space-y-0.5">
        <p className="font-semibold">
          {t("shoppingPlan.progress", {
            current: Math.min(progress.mealsFilled + 1, progress.mealsTotal),
            total: progress.mealsTotal,
            items: progress.listItemCount,
          })}
        </p>
        {budget ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="plan-session-time"
          >
            {t("shoppingPlan.progress.session", {
              used: progress.sessionMinutesUsed,
              budget,
            })}
          </p>
        ) : null}
      </div>

      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(error)}
        </p>
      ) : null}
      {batchFailure && card ? (
        <p
          role="status"
          data-testid="plan-ai-notice"
          className="text-sm text-muted-foreground"
        >
          {t("shoppingPlan.ai.notice")}
        </p>
      ) : null}
      {stopped ? (
        <p
          role="alert"
          data-testid="plan-expansion-stopped"
          className="text-sm text-destructive"
        >
          {t("shoppingPlan.expansion.stopped")}
        </p>
      ) : null}

      {lastRejected ? (
        <div className="space-y-2" data-testid="plan-reasons">
          <p className="text-sm text-muted-foreground">
            {t("shoppingPlan.reason.why")}
          </p>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              size="sm"
              variant="secondary"
              data-testid="plan-reason-dislike"
              onClick={() => void giveReason("dislike")}
            >
              {t("shoppingPlan.reason.dislike")}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="secondary"
              data-testid="plan-reason-work"
              onClick={() => void giveReason("too_much_work")}
            >
              {t("shoppingPlan.reason.work")}
            </Button>
            {lastRejected.leadIngredient ? (
              <Button
                type="button"
                size="sm"
                variant="secondary"
                data-testid="plan-reason-tired"
                onClick={() => void giveReason("tired_of")}
              >
                {t("shoppingPlan.reason.tired", {
                  ingredient: lastRejected.leadIngredient,
                })}
              </Button>
            ) : null}
          </div>
        </div>
      ) : null}

      {card ? (
        <article className="surface space-y-3 p-4" data-testid="plan-card">
          <h1
            ref={titleRef}
            tabIndex={-1}
            data-testid="plan-card-name"
            className="font-display text-2xl outline-none"
          >
            {card.name}
          </h1>
          {card.component ? (
            <div data-testid="plan-card-base" className="space-y-1">
              <p className="font-semibold">
                {t("shoppingPlan.card.base", {
                  name: card.component.outputName,
                  amount: `${formatLocaleDecimal(card.component.outputQuantity, locale)} ${t(UNIT_KEYS[card.component.outputUnit] ?? "recipes.unit.unit")}`,
                })}
              </p>
              <p className="text-sm">
                {t("shoppingPlan.card.baseFor", {
                  dishes: card.component.usedBy.join(", "),
                })}
              </p>
              <p className="text-sm text-muted-foreground">
                {t("shoppingPlan.card.baseNo")}
              </p>
            </div>
          ) : null}
          {card.summary ? (
            <p className="text-muted-foreground">{card.summary}</p>
          ) : null}
          <p className="text-sm text-muted-foreground">
            {[
              t("recipes.generate.minutes", { count: card.totalMinutes }),
              t("recipes.detail.servings", { count: plan.setup.people }),
            ].join(" · ")}
          </p>
          <p data-testid="plan-card-new-items" className="font-semibold">
            {card.newItemCount === 0
              ? t("shoppingPlan.card.noNewItems")
              : t("shoppingPlan.card.newItems", { count: card.newItemCount })}
          </p>
          {card.reusedNames.length > 0 ? (
            <p className="text-sm">
              {t("shoppingPlan.card.uses", {
                names: card.reusedNames.join(", "),
              })}
            </p>
          ) : null}
          {card.reasonCode === "uses_attention_item" &&
          card.reasonIngredient ? (
            <p
              className="status-pill status-warning rounded-xl py-1.5 leading-snug"
              data-testid="plan-card-reason"
            >
              {t("shoppingPlan.card.usesAttention", {
                ingredient: card.reasonIngredient,
              })}
            </p>
          ) : null}
          {card.overBudget ? (
            <p
              className="status-pill status-warning rounded-xl py-1.5 leading-snug"
              data-testid="plan-card-over-budget"
            >
              {t("shoppingPlan.card.overBudget")}
            </p>
          ) : null}
          {card.guardCode ? (
            <p
              className="text-sm text-muted-foreground"
              data-testid="plan-card-guard"
            >
              {t("shoppingPlan.card.guard", {
                ingredient:
                  card.guardIngredient ?? t("shoppingPlan.card.guardAny"),
                restriction: restrictionLabel(locale, card.guardCode),
              })}
            </p>
          ) : null}
          {card.ingredients.length > 0 ? (
            <details>
              <summary className="min-h-11 cursor-pointer py-2 text-sm font-medium">
                {t("shoppingPlan.card.ingredients")}
              </summary>
              <ul className="space-y-1 text-sm text-muted-foreground">
                {card.ingredients.map((item) => (
                  <li key={`${item.name}-${item.unit}`}>
                    {item.name}: {amount(item.quantity, item.unit)}
                  </li>
                ))}
              </ul>
            </details>
          ) : null}

          <Button
            type="button"
            size="lg"
            className="w-full"
            data-testid="plan-yes"
            onClick={() => void decide(card, "accept")}
          >
            {t("shoppingPlan.yes")}
          </Button>
          <div className="grid grid-cols-2 gap-2">
            <Button
              type="button"
              variant="secondary"
              data-testid="plan-yes-twice"
              aria-expanded={repeatOpen}
              // A base is prepared once.
              disabled={remaining < 2 || Boolean(card.component)}
              onClick={() => setRepeatOpen((value) => !value)}
            >
              {t("shoppingPlan.yesTwice")}
            </Button>
            <Button
              type="button"
              variant="secondary"
              data-testid="plan-no"
              onClick={() => void decide(card, "reject")}
            >
              {t("shoppingPlan.no")}
            </Button>
          </div>

          {repeatOpen ? (
            <div
              className="space-y-3 rounded-xl bg-secondary/50 p-3"
              data-testid="plan-repeat"
            >
              <div className="flex items-center gap-3">
                <span className="flex-1 text-sm font-medium">
                  {t("shoppingPlan.repeat.meals")}
                </span>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label={t("shoppingPlan.stepper.less", {
                    label: t("shoppingPlan.repeat.meals"),
                  })}
                  disabled={repeats <= 2}
                  onClick={() => setRepeats(repeats - 1)}
                >
                  −
                </Button>
                <span
                  role="status"
                  data-testid="plan-repeat-count"
                  className="tabular w-8 text-center font-semibold"
                >
                  {Math.min(repeats, repeatLimit)}
                </span>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label={t("shoppingPlan.stepper.more", {
                    label: t("shoppingPlan.repeat.meals"),
                  })}
                  data-testid="plan-repeat-more"
                  disabled={repeats >= repeatLimit}
                  onClick={() => setRepeats(repeats + 1)}
                >
                  +
                </Button>
              </div>
              <fieldset className="space-y-1">
                <legend className="sr-only">
                  {t("shoppingPlan.repeat.how")}
                </legend>
                {(["yield", "again"] as const).map((value) => (
                  <label
                    key={value}
                    className="flex min-h-11 cursor-pointer items-center gap-3 text-sm"
                  >
                    <input
                      type="radio"
                      name="plan-repeat-mode"
                      className="h-5 w-5 accent-primary"
                      data-testid={`plan-repeat-${value}`}
                      checked={mode === value}
                      onChange={() => setMode(value)}
                    />
                    {t(`shoppingPlan.repeat.${value}`, {
                      count: Math.min(repeats, repeatLimit),
                    })}
                  </label>
                ))}
              </fieldset>
              <Button
                type="button"
                className="w-full"
                variant="secondary"
                data-testid="plan-repeat-confirm"
                disabled={repeatLimit < 2}
                onClick={() =>
                  void decide(card, "accept", {
                    mode,
                    repeats: Math.min(repeats, repeatLimit),
                  })
                }
              >
                {t("shoppingPlan.repeat.confirm")}
              </Button>
            </div>
          ) : null}
        </article>
      ) : fetching ? (
        <div
          className="surface space-y-3 p-4"
          data-testid="plan-searching"
          role="status"
        >
          <div
            aria-hidden="true"
            className="h-6 w-2/3 rounded bg-secondary motion-safe:animate-pulse"
          />
          <div
            aria-hidden="true"
            className="h-4 w-full rounded bg-secondary motion-safe:animate-pulse"
          />
          <p className="text-sm text-muted-foreground">
            {t("shoppingPlan.searching")}
          </p>
        </div>
      ) : (
        <div className="surface space-y-3 p-4" data-testid="plan-no-cards">
          <p role="status">
            {batchFailure
              ? t("shoppingPlan.empty.noAi")
              : t("shoppingPlan.empty.exhausted")}
          </p>
          {plan.accepted.length === 0 ? (
            <Button asChild variant="secondary">
              <Link to="/app/receitas">{t("shoppingPlan.empty.recipes")}</Link>
            </Button>
          ) : null}
        </div>
      )}

      <div className="flex flex-wrap gap-2">
        {plan.canUndo ? (
          <Button
            type="button"
            variant="ghost"
            data-testid="plan-undo"
            onClick={() => void undo()}
          >
            {t("shoppingPlan.undo")}
          </Button>
        ) : null}
        <Button
          type="button"
          variant={plan.offerMoreOptions ? "secondary" : "ghost"}
          data-testid="plan-more"
          aria-expanded={moreOpen}
          disabled={plan.exhausted}
          onClick={() => setMoreOpen((value) => !value)}
        >
          {t("shoppingPlan.more")}
        </Button>
        {canReview ? (
          <Button asChild variant="ghost">
            <Link
              to={`/app/compras/planejar/${sessionId}/revisar`}
              data-testid="plan-enough"
            >
              {t("shoppingPlan.enough")}
            </Link>
          </Button>
        ) : null}
      </div>

      {moreOpen ? (
        <div className="surface space-y-3 p-4" data-testid="plan-more-options">
          <div className="flex flex-wrap gap-2">
            {MORE_FAMILIES.map((code) => (
              <Button
                key={code}
                type="button"
                size="sm"
                variant="secondary"
                disabled={fetching}
                data-testid={`plan-more-${code}`}
                onClick={() => {
                  setMoreOpen(false);
                  setBatchFailure(null);
                  void fetchBatch({ dishFamilies: [code] });
                }}
              >
                {t(`recipes.guidance.family.${code}`)}
              </Button>
            ))}
            {MORE_CUISINES.map((code) => (
              <Button
                key={code}
                type="button"
                size="sm"
                variant="secondary"
                disabled={fetching}
                onClick={() => {
                  setMoreOpen(false);
                  setBatchFailure(null);
                  void fetchBatch({ cuisine: code });
                }}
              >
                {t(`recipes.guidance.cuisine.${code}`)}
              </Button>
            ))}
            <Button
              type="button"
              size="sm"
              variant="secondary"
              disabled={fetching}
              onClick={() => {
                setMoreOpen(false);
                setBatchFailure(null);
                void fetchBatch({ time: "quick" });
              }}
            >
              {t("recipes.guidance.time.quick")}
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
