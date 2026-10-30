import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import {
  RecipeApiError,
  type ExecutionPantryLot,
  type RecipeExecution,
} from "@/contracts/recipes";
import { formatLocaleDecimal, parseLocaleDecimal } from "@/lib/localeDecimal";
import { parseNormalizedRecipeJson } from "./parseNormalizedRecipe";
import { RecipeContent } from "./RecipeContent";
import { CookingAdjustments } from "./CookingAdjustments";
import {
  FinishScreen,
  PrepScreen,
  StepScreen,
  useWakeLock,
} from "./CookingGuide";
import { useRecipeRepository } from "./RecipeProvider";

const STORAGE_PREFIX = "cocinaris.cooking.";

/** Reads the per-execution checklist; browser storage may be unavailable (private mode). */
function readChecked(executionId: string): Set<string> {
  try {
    const raw = localStorage.getItem(STORAGE_PREFIX + executionId);
    const parsed = raw ? (JSON.parse(raw) as unknown) : [];
    return new Set(
      Array.isArray(parsed)
        ? parsed.filter((item) => typeof item === "string")
        : [],
    );
  } catch {
    return new Set();
  }
}

/** The guided step the cook was on (-1: preparation), so a reload resumes there. */
function readStep(executionId: string): number {
  try {
    const raw = localStorage.getItem(`${STORAGE_PREFIX}${executionId}.step`);
    // Number(null) is 0, so a missing entry must be checked before parsing.
    const value = raw === null ? NaN : Number(raw);
    return Number.isInteger(value) && value >= 0 ? value : -1;
  } catch {
    return -1;
  }
}

function writeStep(executionId: string, step: number | null) {
  try {
    if (step === null)
      localStorage.removeItem(`${STORAGE_PREFIX}${executionId}.step`);
    else
      localStorage.setItem(
        `${STORAGE_PREFIX}${executionId}.step`,
        String(step),
      );
  } catch {
    // Convenience only, like the checklist.
  }
}

function writeChecked(executionId: string, keys: Set<string> | null) {
  try {
    if (keys)
      localStorage.setItem(
        STORAGE_PREFIX + executionId,
        JSON.stringify([...keys]),
      );
    else localStorage.removeItem(STORAGE_PREFIX + executionId);
  } catch {
    // The checklist is a convenience; losing it never affects authoritative state.
  }
}

interface ReviewLine {
  key: string;
  name: string;
  /** The recipe's name when the cook used something else (PLAN-0042 Phase 2). */
  originalName: string | null;
  status: string;
  include: boolean;
  lotId: string;
  quantity: string;
}

/**
 * Cooking view for one recipe execution (PLAN-0042, guided since PLAN-0054 Phase 4).
 *
 * The cook goes through the preparation ("Antes de começar"), then one stage per screen with its
 * look and a timer, and finishes with one tap ("Concluir") that uses the backend's proposal of
 * what leaves the pantry. "Ajustar" opens the detailed review, and "Ver receita inteira" shows
 * the full checklist view. The proposal is only a starting point: nothing is deducted until the
 * cook confirms, and the backend commits the consumption, the leftovers, and the completion
 * together. The screen is kept awake during preparation and the steps.
 */
export function CookingPage() {
  const { executionId = "" } = useParams<{ executionId: string }>();
  const repo = useRecipeRepository();
  const { session } = useSession();
  const { t, locale } = useProductionI18n();
  const [execution, setExecution] = useState<RecipeExecution | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "error">(
    "loading",
  );
  const [mode, setMode] = useState<
    "prep" | "step" | "overview" | "finish" | "review"
  >(() => (readStep(executionId) >= 0 ? "step" : "prep"));
  const [step, setStep] = useState(() => Math.max(0, readStep(executionId)));
  const [checked, setChecked] = useState<Set<string>>(() =>
    readChecked(executionId),
  );
  const [lines, setLines] = useState<ReviewLine[]>([]);
  const [leftover, setLeftover] = useState({
    enabled: false,
    portions: "1",
    storage: "Refrigerator",
    useBy: "",
  });
  const [rating, setRating] = useState("");
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [done, setDone] = useState<{
    consumed: number;
    leftover: number;
  } | null>(null);
  // One key per confirmation attempt: a network retry replays instead of deducting twice.
  const keyRef = useRef<string>(crypto.randomUUID());

  const load = useCallback(
    async (signal?: AbortSignal) => {
      try {
        const value = await repo.getExecution(executionId, signal);
        setExecution(value);
        setStatus("ready");
        if (value.proposal) {
          setLines(
            value.proposal.lines.map((line, index) => ({
              key: `${line.ingredientId}-${index}`,
              name: line.displayName,
              originalName: line.originalName ?? null,
              status: line.status,
              include: line.lotId !== null && (line.proposedQuantity ?? 0) > 0,
              lotId: line.lotId ?? "",
              quantity:
                line.proposedQuantity !== null
                  ? formatLocaleDecimal(line.proposedQuantity, locale)
                  : "",
            })),
          );
        }
      } catch (err) {
        if (
          signal?.aborted ||
          (err instanceof RecipeApiError && err.code === "cancelled")
        )
          return;
        setStatus("error");
      }
    },
    [executionId, repo, locale],
  );

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const parsed = useMemo(
    () =>
      execution
        ? parseNormalizedRecipeJson(execution.normalizedRecipeJson)
        : null,
    [execution],
  );
  const lots = useMemo(
    () =>
      new Map(
        (execution?.proposal?.pantryLots ?? []).map((lot) => [lot.lotId, lot]),
      ),
    [execution],
  );

  useWakeLock(
    status === "ready" &&
      execution?.status === "InProgress" &&
      (mode === "prep" || mode === "step"),
  );

  const goToStep = (next: number) => {
    setStep(next);
    setMode("step");
    writeStep(executionId, next);
    window.scrollTo?.({ top: 0 });
  };

  const toggle = (key: string) =>
    setChecked((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      writeChecked(executionId, next);
      return next;
    });

  const lotLabel = (lot: ExecutionPantryLot) =>
    `${lot.productName} — ${formatLocaleDecimal(lot.quantity, locale)} ${t(`inventory.unit.${lot.unit}`)}`;

  const updateLine = (key: string, patch: Partial<ReviewLine>) =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  const confirm = async () => {
    if (!execution || !session.csrfToken) return;
    const consumptions = [];
    for (const line of lines.filter((item) => item.include)) {
      const lot = lots.get(line.lotId);
      const value = parseLocaleDecimal(line.quantity, locale);
      if (!lot || !value.ok || value.value <= 0 || value.value > lot.quantity) {
        setMessage("cooking.error.quantity");
        return;
      }
      consumptions.push({
        lotId: lot.lotId,
        quantity: value.value,
        unit: lot.unit,
        version: lot.version,
      });
    }
    setBusy(true);
    setMessage(null);
    try {
      const result = await repo.completeExecution(
        execution.executionId,
        {
          consumptions,
          leftover: leftover.enabled
            ? {
                portions: Number(leftover.portions),
                storageLocation: leftover.storage,
                useBy: leftover.useBy || null,
              }
            : null,
          rating: rating ? Number(rating) : null,
          notes: notes.trim() || null,
        },
        { csrfToken: session.csrfToken, idempotencyKey: keyRef.current },
      );
      writeChecked(execution.executionId, null);
      writeStep(execution.executionId, null);
      setExecution({ ...result, proposal: null });
      setDone({
        consumed: new Set(consumptions.map((item) => item.lotId)).size,
        leftover: leftover.enabled ? Number(leftover.portions) : 0,
      });
    } catch (err) {
      keyRef.current = crypto.randomUUID();
      if (err instanceof RecipeApiError && err.code === "precondition_failed") {
        setMessage("cooking.error.stale");
        await load();
      } else {
        setMessage("cooking.error.generic");
      }
    } finally {
      setBusy(false);
    }
  };

  const abandon = async () => {
    if (
      !execution ||
      !session.csrfToken ||
      !window.confirm(t("cooking.abandonConfirm"))
    )
      return;
    setBusy(true);
    try {
      const result = await repo.abandonExecution(execution.executionId, {
        csrfToken: session.csrfToken,
      });
      writeChecked(execution.executionId, null);
      writeStep(execution.executionId, null);
      setExecution(result);
    } catch {
      setMessage("cooking.error.generic");
    } finally {
      setBusy(false);
    }
  };

  if (status === "loading") {
    return (
      <p role="status" data-testid="cooking-loading">
        {t("cooking.loading")}
      </p>
    );
  }
  if (status === "error" || !execution) {
    return (
      <div data-testid="cooking-error" className="space-y-4">
        <FeatureUnavailable
          feature="cooking"
          title={t("feature.unavailable")}
          detail={t("cooking.error")}
        />
        <Button asChild variant="secondary">
          <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
        </Button>
      </div>
    );
  }

  const header = (
    <div>
      <p className="text-sm text-muted-foreground">{t("page.title.cooking")}</p>
      <h1 className="font-display text-3xl">{execution.recipeName}</h1>
      <p className="mt-1 text-muted-foreground">
        {t("cooking.servings", { count: execution.servings })}
      </p>
    </div>
  );

  if (execution.status !== "InProgress") {
    return (
      <div data-testid="cooking-finished" className="space-y-4">
        {header}
        {execution.status === "Completed" ? (
          <div role="status" className="space-y-1">
            <h2 className="font-display text-2xl">{t("cooking.done.title")}</h2>
            {done ? (
              <>
                <p>
                  {done.consumed > 0
                    ? t("cooking.done.consumed", { count: done.consumed })
                    : t("cooking.done.nothing")}
                </p>
                {done.leftover > 0 ? (
                  <p>{t("cooking.done.leftover", { count: done.leftover })}</p>
                ) : null}
              </>
            ) : null}
          </div>
        ) : (
          <p role="status">{t("cooking.abandoned")}</p>
        )}
        {execution.status === "Completed" ? (
          <div className="space-y-2 rounded-2xl border border-border p-4">
            <p className="text-sm">{t("cooking.done.changesHint")}</p>
            <div className="flex flex-wrap gap-2">
              <Button asChild variant="secondary">
                <Link
                  to={`/app/receitas/${execution.recipeId}/editar?execucao=${execution.executionId}`}
                  data-testid="cooking-done-save-changes"
                >
                  {t("cooking.done.saveChanges")}
                </Link>
              </Button>
              <Button asChild variant="secondary">
                <Link
                  to={`/app/receitas/${execution.recipeId}/variacao?execucao=${execution.executionId}`}
                  data-testid="cooking-done-variation"
                >
                  {t("cooking.done.variation")}
                </Link>
              </Button>
            </div>
          </div>
        ) : null}
        <div className="flex flex-wrap gap-2">
          <Button asChild>
            <Link to="/app/despensa" data-testid="cooking-done-pantry">
              {t("cooking.done.pantry")}
            </Link>
          </Button>
          <Button asChild variant="secondary">
            <Link to="/app/hoje">{t("cooking.done.home")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  const guideLinks = (
    <div className="flex flex-wrap gap-x-4 gap-y-2 text-sm">
      <button
        type="button"
        data-testid="cooking-overview"
        className="font-semibold text-muted-foreground underline-offset-4 hover:underline"
        onClick={() => setMode("overview")}
      >
        {t("cooking.guide.overview")}
      </button>
      <button
        type="button"
        data-testid="cooking-abandon"
        className="font-semibold text-muted-foreground underline-offset-4 hover:underline"
        disabled={busy}
        onClick={() => void abandon()}
      >
        {t("cooking.abandon")}
      </button>
    </div>
  );

  if (parsed && mode === "prep") {
    return (
      <div data-testid="cooking-guided" className="space-y-6">
        {header}
        <PrepScreen
          recipe={parsed}
          checked={checked}
          onToggle={toggle}
          onStart={() =>
            parsed.stages.length > 0 ? goToStep(0) : setMode("finish")
          }
        />
        {execution.status === "InProgress" ? (
          <CookingAdjustments execution={execution} onChanged={setExecution} />
        ) : null}
        {guideLinks}
      </div>
    );
  }

  if (parsed && mode === "step" && parsed.stages[step]) {
    return (
      <div data-testid="cooking-guided" className="space-y-6">
        <p className="text-sm text-muted-foreground">{execution.recipeName}</p>
        <StepScreen
          recipe={parsed}
          index={step}
          onPrevious={() => {
            if (step === 0) {
              setMode("prep");
              writeStep(executionId, null);
            } else goToStep(step - 1);
          }}
          onNext={() =>
            step + 1 < parsed.stages.length
              ? goToStep(step + 1)
              : setMode("finish")
          }
        />
        {guideLinks}
      </div>
    );
  }

  if (mode === "finish") {
    return (
      <div data-testid="cooking-guided" className="space-y-6">
        <p className="text-sm text-muted-foreground">{execution.recipeName}</p>
        <FinishScreen
          usedNames={lines
            .filter((line) => line.include)
            .map((line) => line.name)}
          servings={execution.servings}
          leftoverPortions={leftover.enabled ? Number(leftover.portions) : 0}
          onLeftover={(portions) =>
            setLeftover({
              ...leftover,
              enabled: portions > 0,
              portions: String(Math.max(1, portions)),
            })
          }
          rating={rating}
          onRating={setRating}
          busy={busy}
          canSubmit={Boolean(session.csrfToken)}
          onDone={() => void confirm()}
          onAdjust={() => {
            setMessage(null);
            setMode("review");
          }}
          message={message}
        />
      </div>
    );
  }

  if (mode !== "review") {
    return (
      <div data-testid="cooking-page" className="space-y-6">
        {header}
        <p className="text-sm text-muted-foreground">
          {t("cooking.ingredientsHint")} {t("cooking.stepsHint")}
        </p>
        {parsed ? (
          <RecipeContent
            recipe={parsed}
            checkable
            checked={checked}
            onToggle={toggle}
          />
        ) : null}
        {execution.status === "InProgress" ? (
          <CookingAdjustments execution={execution} onChanged={setExecution} />
        ) : null}
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            data-testid="cooking-finish"
            onClick={() => setMode("finish")}
          >
            {t("cooking.finish")}
          </Button>
          {parsed ? (
            <Button
              type="button"
              variant="secondary"
              data-testid="cooking-guided-mode"
              onClick={() => (step > 0 ? goToStep(step) : setMode("prep"))}
            >
              {t("cooking.guide.guided")}
            </Button>
          ) : null}
          <Button
            type="button"
            variant="secondary"
            data-testid="cooking-abandon"
            disabled={busy}
            onClick={() => void abandon()}
          >
            {t("cooking.abandon")}
          </Button>
        </div>
        {message ? (
          <p role="alert" className="text-sm text-destructive">
            {t(message)}
          </p>
        ) : null}
      </div>
    );
  }

  const pantryLots = execution.proposal?.pantryLots ?? [];
  return (
    <form
      data-testid="cooking-review"
      className="space-y-6"
      onSubmit={(event) => {
        event.preventDefault();
        void confirm();
      }}
    >
      {header}
      <section aria-labelledby="cooking-review-title" className="space-y-3">
        <h2 id="cooking-review-title" className="font-display text-2xl">
          {t("cooking.review.title")}
        </h2>
        <p className="text-sm text-muted-foreground">
          {t("cooking.review.hint")}
        </p>
        <ul className="space-y-3">
          {lines.map((line) => {
            const lot = lots.get(line.lotId);
            return (
              <li
                key={line.key}
                data-testid="cooking-review-line"
                data-status={line.status}
                className="rounded-md border p-3"
              >
                <label className="flex items-center gap-2 font-medium">
                  <input
                    type="checkbox"
                    className="h-4 w-4"
                    checked={line.include}
                    onChange={(event) =>
                      updateLine(line.key, { include: event.target.checked })
                    }
                  />
                  {t("cooking.review.use")}: {line.name}
                  {line.originalName ? (
                    <span className="font-normal text-muted-foreground">
                      {" "}
                      {t("cooking.adjust.instead", { name: line.originalName })}
                    </span>
                  ) : null}
                </label>
                <p className="mt-1 text-xs text-muted-foreground">
                  {t(`cooking.review.status.${line.status}`)}
                </p>
                {line.include ? (
                  <div className="mt-2 grid gap-2 sm:grid-cols-2">
                    <label className="text-sm">
                      {t("cooking.review.from")}
                      <select
                        className="mt-1 block w-full rounded-md border bg-background p-2"
                        value={line.lotId}
                        onChange={(event) =>
                          updateLine(line.key, { lotId: event.target.value })
                        }
                      >
                        <option value="">{t("cooking.review.noLot")}</option>
                        {pantryLots.map((option) => (
                          <option key={option.lotId} value={option.lotId}>
                            {lotLabel(option)}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label className="text-sm">
                      {t("cooking.review.quantity")}
                      {lot ? ` (${t(`inventory.unit.${lot.unit}`)})` : ""}
                      <input
                        inputMode="decimal"
                        className="mt-1 block w-full rounded-md border bg-background p-2"
                        value={line.quantity}
                        onChange={(event) =>
                          updateLine(line.key, { quantity: event.target.value })
                        }
                      />
                      {lot ? (
                        <span className="text-xs text-muted-foreground">
                          {t("cooking.review.available", {
                            value: lotLabel(lot),
                          })}
                        </span>
                      ) : null}
                    </label>
                    {lot ? (
                      <CommitmentNotice lot={lot} quantity={line.quantity} />
                    ) : null}
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>
      </section>

      <section aria-labelledby="cooking-leftover-title" className="space-y-2">
        <h2 id="cooking-leftover-title" className="font-display text-xl">
          {t("cooking.leftover.title")}
        </h2>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="h-4 w-4"
            data-testid="cooking-leftover-toggle"
            checked={leftover.enabled}
            onChange={(event) =>
              setLeftover({ ...leftover, enabled: event.target.checked })
            }
          />
          {t("cooking.leftover.toggle")}
        </label>
        {leftover.enabled ? (
          <div className="grid gap-2 sm:grid-cols-3">
            <label className="text-sm">
              {t("cooking.leftover.portions")}
              <select
                className="mt-1 block w-full rounded-md border bg-background p-2"
                value={leftover.portions}
                onChange={(event) =>
                  setLeftover({ ...leftover, portions: event.target.value })
                }
              >
                {Array.from({ length: execution.servings }, (_, index) =>
                  String(index + 1),
                ).map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </select>
            </label>
            <label className="text-sm">
              {t("cooking.leftover.storage")}
              <select
                className="mt-1 block w-full rounded-md border bg-background p-2"
                value={leftover.storage}
                onChange={(event) =>
                  setLeftover({ ...leftover, storage: event.target.value })
                }
              >
                {["Refrigerator", "Freezer", "Pantry"].map((value) => (
                  <option key={value} value={value}>
                    {t(`inventory.location.${value}`)}
                  </option>
                ))}
              </select>
            </label>
            <label className="text-sm">
              {t("cooking.leftover.useBy")}
              <input
                type="date"
                className="mt-1 block w-full rounded-md border bg-background p-2"
                value={leftover.useBy}
                onChange={(event) =>
                  setLeftover({ ...leftover, useBy: event.target.value })
                }
              />
            </label>
            <p className="text-xs text-muted-foreground sm:col-span-3">
              {t("cooking.leftover.useByHint")}
            </p>
          </div>
        ) : null}
      </section>

      <section className="grid gap-2 sm:grid-cols-2">
        <label className="text-sm">
          {t("cooking.rating")}
          <select
            className="mt-1 block w-full rounded-md border bg-background p-2"
            value={rating}
            onChange={(event) => setRating(event.target.value)}
          >
            <option value="">{t("cooking.rating.none")}</option>
            {["5", "4", "3", "2", "1"].map((value) => (
              <option key={value} value={value}>
                {"★".repeat(Number(value))}
              </option>
            ))}
          </select>
        </label>
        <label className="text-sm sm:col-span-2">
          {t("cooking.notes")}
          <textarea
            className="mt-1 block w-full rounded-md border bg-background p-2"
            maxLength={1000}
            rows={3}
            value={notes}
            onChange={(event) => setNotes(event.target.value)}
          />
        </label>
      </section>

      {message ? (
        <p role="alert" className="text-sm text-destructive">
          {t(message)}
        </p>
      ) : null}
      <div className="flex flex-wrap gap-2">
        <Button
          type="submit"
          data-testid="cooking-confirm"
          disabled={busy || !session.csrfToken}
        >
          {busy ? t("cooking.confirming") : t("cooking.confirm")}
        </Button>
        <Button
          type="button"
          variant="secondary"
          onClick={() => setMode("finish")}
        >
          {t("cooking.review.back")}
        </Button>
      </div>
    </form>
  );
}

/**
 * Explains what is promised from the chosen lot: the part the owner protected and what other
 * menu meals reserve. Advisory only (PLAN-0029 Phase 3): the cook may still use it, and the notice
 * says which meal would be short.
 */
function CommitmentNotice({
  lot,
  quantity,
}: {
  lot: ExecutionPantryLot;
  quantity: string;
}) {
  const { t, locale } = useProductionI18n();
  if (lot.protectedQuantity <= 0 && lot.reservedQuantity <= 0) return null;
  const unit = t(`inventory.unit.${lot.unit}`);
  const amount = (value: number) =>
    `${formatLocaleDecimal(value, locale)} ${unit}`;
  const parsed = parseLocaleDecimal(quantity, locale);
  const used = parsed.ok ? parsed.value : 0;
  const free = lot.quantity - lot.protectedQuantity - lot.reservedQuantity;
  const intoReserved = used > Math.max(0, free);
  const intoProtected = used > lot.quantity - lot.protectedQuantity;
  const names = lot.reservedFor.map((meal) => meal.recipeName).join(", ");
  return (
    <div
      data-testid="cooking-review-commitment"
      className="space-y-1 text-xs sm:col-span-2"
    >
      {lot.protectedQuantity > 0 ? (
        <p className="text-muted-foreground">
          {t("cooking.review.protected", {
            value: amount(lot.protectedQuantity),
          })}
        </p>
      ) : null}
      {lot.reservedQuantity > 0 ? (
        <p className="text-muted-foreground">
          {names
            ? t("cooking.review.reservedFor", {
                value: amount(lot.reservedQuantity),
                meals: names,
              })
            : t("cooking.review.reserved", {
                value: amount(lot.reservedQuantity),
              })}
        </p>
      ) : null}
      {intoProtected || intoReserved ? (
        <p role="status" className="font-medium text-destructive">
          {t(
            intoProtected
              ? "cooking.review.usesProtected"
              : "cooking.review.usesReserved",
          )}
        </p>
      ) : null}
    </div>
  );
}
