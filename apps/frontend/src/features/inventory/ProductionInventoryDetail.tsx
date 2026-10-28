import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useInventoryRepository } from "./InventoryProvider";
import { formatQuantityLabel } from "./quantityDisplay";
import {
  formatHistoryTimestamp,
  localizeInventoryKey,
} from "./inventoryLabels";
import {
  InventoryApiError,
  type AdjustmentType,
  type AvailabilityState,
  type InventoryHistoryEntry,
  type InventoryLotView,
} from "@/adapters/live/inventoryTypes";
import { useSession } from "@/app/session/SessionProvider";
import {
  useProductionI18n,
  type ProductionLocale,
} from "@/app/i18n/ProductionI18nProvider";
import { formatCalendarDateForDisplay } from "@/lib/calendarDate";
import { parseLocaleDecimal } from "@/lib/localeDecimal";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

type MeasuredAction = "Consume" | "Discard" | "Correct";

/**
 * Lot detail with adjust (consume/discard/correct/availability), history,
 * delete, and stale-version conflict handling.
 */
export function ProductionInventoryDetail() {
  const { lotId = "" } = useParams();
  const repo = useInventoryRepository();
  const { session } = useSession();
  const { t, locale } = useProductionI18n();
  const navigate = useNavigate();
  const [lot, setLot] = useState<InventoryLotView | null>(null);
  const [history, setHistory] = useState<InventoryHistoryEntry[]>([]);
  const [status, setStatus] = useState<"loading" | "ready" | "error">(
    "loading",
  );
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [missingPrecondition, setMissingPrecondition] = useState(false);
  const [adjustValue, setAdjustValue] = useState("");
  const [measuredAction, setMeasuredAction] =
    useState<MeasuredAction>("Consume");
  const [availability, setAvailability] =
    useState<AvailabilityState>("Available");
  const [adjustError, setAdjustError] = useState<string | null>(null);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const historyTimeZone =
    session.timeZone && session.timeZone.trim() ? session.timeZone : "UTC";
  const lotRef = useRef(lot);
  lotRef.current = lot;

  const reloadHistory = useCallback(
    async (targetLotId: string) => {
      try {
        const hist = await repo.getHistory(targetLotId);
        setHistory(hist);
        setHistoryError(null);
      } catch {
        // Keep any prior history entries visible but never clear the warning
        // until a successful refresh.
        setHistoryError(t("inventory.error.historyRefresh"));
      }
    },
    [repo, t],
  );

  const reload = useCallback(async () => {
    const hadLot = Boolean(lotRef.current);
    if (!hadLot) {
      setStatus("loading");
      setError(null);
    }
    setConflict(false);
    setMissingPrecondition(false);

    let lotLoaded = false;
    try {
      const next = await repo.getLot(lotId);
      setLot(next);
      lotRef.current = next;
      if (next.quantity.kind === "qualitative") {
        setAvailability(next.quantity.availability);
      }
      setError(null);
      lotLoaded = true;
    } catch (err) {
      const message =
        err instanceof InventoryApiError && err.code === "not_found"
          ? t("inventory.error.notFound")
          : t("inventory.error.loadDetail");
      if (!hadLot) {
        setStatus("error");
        setError(message);
        return;
      }
      // Keep the previous lot projection; surface a lot-reload warning.
      setError(message);
    }

    try {
      const hist = await repo.getHistory(lotId);
      setHistory(hist);
      setHistoryError(null);
    } catch {
      setHistoryError(t("inventory.error.historyRefresh"));
    }

    if (lotLoaded || hadLot) {
      setStatus("ready");
    }
  }, [lotId, repo, t]);

  useEffect(() => {
    void reload();
  }, [reload]);

  function requireCsrf(): string {
    if (!session.csrfToken) {
      throw new InventoryApiError(
        "authentication_required",
        "Missing CSRF token",
        401,
      );
    }
    return session.csrfToken;
  }

  function handleMutationError(err: unknown, fallbackKey: string) {
    if (err instanceof InventoryApiError) {
      if (err.code === "precondition_failed") {
        setConflict(true);
        setError(t("inventory.error.staleVersion"));
        return;
      }
      if (err.code === "precondition_required") {
        setMissingPrecondition(true);
        setError(t("inventory.error.missingPrecondition"));
        return;
      }
      if (err.code === "authentication_required") {
        setAdjustError(t("inventory.error.session"));
        return;
      }
      if (err.code === "validation_failed") {
        setAdjustError(err.message || t("inventory.error.validation"));
        return;
      }
    }
    setAdjustError(t(fallbackKey));
  }

  async function runMeasuredAdjustment() {
    if (!lot || lot.quantity.kind !== "measured") return;
    setAdjustError(null);
    setHistoryError(null);
    const parsed = parseLocaleDecimal(adjustValue, locale);
    if (!parsed.ok) {
      setAdjustError(t("inventory.error.invalidDecimal"));
      return;
    }
    setBusy(true);
    let nextLot: InventoryLotView | null = null;
    try {
      const csrfToken = requireCsrf();
      const type: AdjustmentType = measuredAction;
      nextLot = await repo.adjustLot(
        lot.lotId,
        {
          type,
          value: parsed.value,
          reasonCode: `ui_${type.toLowerCase()}`,
        },
        {
          csrfToken,
          etag: lot.etag,
          idempotencyKey: crypto.randomUUID(),
        },
      );
      // Authoritative mutation succeeded — commit lot/ETag before auxiliary history.
      setLot(nextLot);
      setAdjustValue("");
    } catch (err) {
      handleMutationError(err, "inventory.error.adjust");
      return;
    } finally {
      setBusy(false);
    }

    if (nextLot) {
      await reloadHistory(nextLot.lotId);
    }
  }

  async function runAvailabilityChange() {
    if (!lot || lot.quantity.kind !== "qualitative") return;
    setAdjustError(null);
    setHistoryError(null);
    setBusy(true);
    let nextLot: InventoryLotView | null = null;
    try {
      const csrfToken = requireCsrf();
      nextLot = await repo.adjustLot(
        lot.lotId,
        {
          type: "AvailabilityChanged",
          availabilityState: availability,
          reasonCode: "ui_availability",
        },
        {
          csrfToken,
          etag: lot.etag,
          idempotencyKey: crypto.randomUUID(),
        },
      );
      // Authoritative mutation succeeded — commit lot/ETag before auxiliary history.
      setLot(nextLot);
    } catch (err) {
      handleMutationError(err, "inventory.error.adjust");
      return;
    } finally {
      setBusy(false);
    }

    if (nextLot) {
      await reloadHistory(nextLot.lotId);
    }
  }

  async function onDelete() {
    if (!lot) return;
    if (!window.confirm(t("inventory.confirmDelete"))) return;
    setBusy(true);
    try {
      const csrfToken = requireCsrf();
      await repo.deleteLot(lot.lotId, { csrfToken, etag: lot.etag });
      navigate("/app/despensa");
    } catch (err) {
      handleMutationError(err, "inventory.error.delete");
    } finally {
      setBusy(false);
    }
  }

  if (status === "loading") {
    return (
      <p role="status" data-testid="inventory-detail-loading">
        {t("inventory.loading")}
      </p>
    );
  }

  if (status === "error" && !lot) {
    return (
      <div role="alert" className="space-y-3">
        <p>{error}</p>
        <Button type="button" onClick={() => void reload()}>
          {t("inventory.actions.retry")}
        </Button>
        <Button asChild variant="secondary">
          <Link to="/app/despensa">{t("inventory.actions.back")}</Link>
        </Button>
      </div>
    );
  }

  if (!lot) return null;

  return (
    <div data-testid="production-inventory-detail" className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-sm text-muted-foreground">
            <Link to="/app/despensa">{t("inventory.actions.back")}</Link>
          </p>
          <h1 className="mt-1 font-display text-3xl">{lot.productName}</h1>
          <p className="mt-2 text-muted-foreground">
            {formatQuantityLabel(lot.quantity, locale as ProductionLocale, t)}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button asChild variant="secondary">
            <Link
              to={`/app/despensa/${lot.lotId}/editar`}
              data-testid="inventory-edit"
            >
              {t("inventory.actions.edit")}
            </Link>
          </Button>
          <Button
            type="button"
            variant="destructive"
            data-testid="inventory-delete"
            disabled={busy || !session.csrfToken}
            onClick={() => void onDelete()}
          >
            {t("inventory.actions.delete")}
          </Button>
        </div>
      </div>

      {(conflict || missingPrecondition) && (
        <div
          role="alert"
          data-testid={
            conflict
              ? "inventory-stale-conflict"
              : "inventory-missing-precondition"
          }
          className="rounded-xl border border-warning/40 bg-warning/10 p-4"
        >
          <p>{error}</p>
          {conflict && (
            <p className="mt-1 text-sm text-muted-foreground">
              {t("inventory.error.staleHint")}
            </p>
          )}
          <Button
            type="button"
            className="mt-3"
            data-testid="inventory-reload-conflict"
            onClick={() => void reload()}
          >
            {t("inventory.actions.reloadReview")}
          </Button>
        </div>
      )}

      {error && !conflict && !missingPrecondition && (
        <div
          role="alert"
          data-testid="inventory-lot-reload-error"
          className="rounded-xl border border-warning/40 bg-warning/10 p-4"
        >
          <p>{error}</p>
          <Button
            type="button"
            className="mt-3"
            data-testid="inventory-reload-lot"
            onClick={() => void reload()}
          >
            {t("inventory.actions.retry")}
          </Button>
        </div>
      )}

      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-muted-foreground">
            {t("inventory.fields.location")}
          </dt>
          <dd>
            {localizeInventoryKey(t, "inventory.location", lot.storageLocation)}
            {lot.customLocation ? ` · ${lot.customLocation}` : ""}
          </dd>
        </div>
        <div>
          <dt className="text-muted-foreground">
            {t("inventory.fields.packageState")}
          </dt>
          <dd>
            {localizeInventoryKey(t, "inventory.package", lot.packageState)}
          </dd>
        </div>
        <div>
          <dt className="text-muted-foreground">
            {t("inventory.fields.printedDate")}
          </dt>
          <dd data-testid="inventory-printed-date">
            {lot.printedExpirationDate
              ? formatCalendarDateForDisplay(lot.printedExpirationDate, locale)
              : t("inventory.fields.none")}
          </dd>
        </div>
        <div className="sm:col-span-2">
          <dt className="text-muted-foreground">
            {t("inventory.fields.notes")}
          </dt>
          <dd>{lot.notes || t("inventory.fields.none")}</dd>
        </div>
      </dl>
      {/* Outside the <dl>: a definition list may only contain term/description groups. */}
      <p className="text-xs text-muted-foreground">
        {t("inventory.printedDateDisclaimer")}
      </p>

      {!session.csrfToken && (
        <p role="alert" data-testid="inventory-missing-csrf">
          {t("inventory.error.session")}
        </p>
      )}

      {lot.quantity.kind === "measured" && (
        <section className="space-y-3 rounded-xl border p-4">
          <h2 className="font-display text-xl">
            {t("inventory.actions.adjust")}
          </h2>
          <label className="block max-w-xs space-y-1">
            <span className="text-sm">
              {t("inventory.fields.adjustmentType")}
            </span>
            <select
              data-testid="inventory-adjust-type"
              className="flex h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm"
              value={measuredAction}
              disabled={busy}
              onChange={(event) =>
                setMeasuredAction(event.target.value as MeasuredAction)
              }
            >
              <option value="Consume">
                {t("inventory.adjustment.Consume")}
              </option>
              <option value="Discard">
                {t("inventory.adjustment.Discard")}
              </option>
              <option value="Correct">
                {t("inventory.adjustment.Correct")}
              </option>
            </select>
          </label>
          <label className="block max-w-xs">
            <span className="text-sm">{t("inventory.fields.amount")}</span>
            <Input
              data-testid="inventory-adjust-value"
              value={adjustValue}
              disabled={busy || !session.csrfToken}
              onChange={(event) => setAdjustValue(event.target.value)}
              inputMode="decimal"
              aria-invalid={Boolean(adjustError)}
              aria-describedby={adjustError ? "adjust-error" : undefined}
            />
          </label>
          {adjustError && (
            <p
              id="adjust-error"
              role="alert"
              className="text-sm text-destructive"
            >
              {adjustError}
            </p>
          )}
          <Button
            type="button"
            data-testid="inventory-adjust-submit"
            disabled={busy || !session.csrfToken}
            onClick={() => void runMeasuredAdjustment()}
          >
            {t(`inventory.adjustment.${measuredAction}`)}
          </Button>
        </section>
      )}

      {lot.quantity.kind === "qualitative" && (
        <section className="space-y-3 rounded-xl border p-4">
          <h2 className="font-display text-xl">
            {t("inventory.actions.changeAvailability")}
          </h2>
          <label className="block max-w-xs space-y-1">
            <span className="text-sm">
              {t("inventory.fields.availability")}
            </span>
            <select
              data-testid="inventory-availability"
              className="flex h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm"
              value={availability}
              disabled={busy || !session.csrfToken}
              onChange={(event) =>
                setAvailability(event.target.value as AvailabilityState)
              }
            >
              {(["Available", "Low", "Unavailable"] as const).map((state) => (
                <option key={state} value={state}>
                  {t(`inventory.availability.${state}`)}
                </option>
              ))}
            </select>
          </label>
          {adjustError && (
            <p role="alert" className="text-sm text-destructive">
              {adjustError}
            </p>
          )}
          <Button
            type="button"
            data-testid="inventory-availability-submit"
            disabled={busy || !session.csrfToken}
            onClick={() => void runAvailabilityChange()}
          >
            {t("inventory.actions.changeAvailability")}
          </Button>
        </section>
      )}

      <section className="space-y-3">
        <h2 className="font-display text-xl">{t("inventory.history.title")}</h2>
        {historyError && (
          <div
            role="status"
            data-testid="inventory-history-refresh-error"
            className="rounded-xl border border-warning/40 bg-warning/10 p-4"
          >
            <p>{historyError}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button
                type="button"
                data-testid="inventory-reload-history"
                onClick={() => void reloadHistory(lot.lotId)}
              >
                {t("inventory.actions.reloadHistory")}
              </Button>
              <Button
                type="button"
                variant="secondary"
                data-testid="inventory-reload-after-history"
                onClick={() => void reload()}
              >
                {t("inventory.actions.reloadReview")}
              </Button>
            </div>
          </div>
        )}
        <ol
          data-testid="inventory-history"
          data-history-stale={historyError ? "true" : "false"}
          className="space-y-2"
        >
          {history.map((entry) => (
            <li
              key={entry.entryId}
              className="rounded-lg border px-3 py-2 text-sm"
            >
              <span className="font-medium">
                {localizeInventoryKey(t, "inventory.history.kind", entry.kind)}
              </span>
              {entry.type
                ? ` · ${localizeInventoryKey(t, "inventory.adjustment", entry.type)}`
                : ""}
              {entry.reasonCode
                ? ` · ${localizeInventoryKey(t, "inventory.reason", entry.reasonCode)}`
                : ""}
              <span className="block text-xs text-muted-foreground">
                {formatHistoryTimestamp(
                  entry.occurredAt,
                  locale,
                  historyTimeZone,
                )}
              </span>
              {entry.changedFields?.length ? (
                <span className="block text-xs text-muted-foreground">
                  {t("inventory.history.changedFields")}:{" "}
                  {entry.changedFields
                    .map((field) =>
                      localizeInventoryKey(t, "inventory.fields", field),
                    )
                    .join(", ")}
                </span>
              ) : null}
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
