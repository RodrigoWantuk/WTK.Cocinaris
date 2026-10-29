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
  status: string;
  include: boolean;
  lotId: string;
  quantity: string;
}

/**
 * Cooking view for one recipe execution (PLAN-0042): ingredient and stage checklists, then a
 * completion review in which the cook confirms what leaves the pantry. The proposal comes from the
 * backend and is only a starting point; nothing is deducted until the cook confirms, and the
 * backend commits the consumption, the leftovers, and the completion together.
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
  const [mode, setMode] = useState<"cooking" | "review">("cooking");
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

  if (mode === "cooking") {
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
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            data-testid="cooking-finish"
            onClick={() => setMode("review")}
          >
            {t("cooking.finish")}
          </Button>
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
          onClick={() => setMode("cooking")}
        >
          {t("cooking.review.back")}
        </Button>
      </div>
    </form>
  );
}
