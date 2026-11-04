import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Camera, ChevronLeft, ListPlus, Sparkles, Trash2 } from "lucide-react";
import type {
  MeasuredUnit,
  PantryListLine,
  StorageLocation,
} from "@/adapters/live/inventoryTypes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  formatLocaleDecimal,
  parseLocaleDecimal,
  type SupportedDecimalLocale,
} from "@/lib/localeDecimal";
import { useInventoryRepository } from "./InventoryProvider";

const PLACES: StorageLocation[] = ["Pantry", "Refrigerator", "Freezer"];
const UNITS: MeasuredUnit[] = ["Gram", "Milliliter", "Unit"];

/** One reviewed row: the proposal, editable, with its save state. */
interface Row {
  key: number;
  name: string;
  amount: string;
  unit: MeasuredUnit;
  place: StorageLocation;
  idempotencyKey: string;
  status: "pending" | "saved" | "failed";
  /** The AI was unsure of this reading, so the person should look at it. */
  uncertain: boolean;
  /** The name is not a catalog ingredient, so the AI may read the line better. */
  recognized: boolean;
}

/** Largest photo accepted by the API, in bytes. */
const MAX_PHOTO_BYTES = 12 * 1024 * 1024;

/**
 * "Adicionar uma lista" (PLAN-0046 Phase 1): paste or type several items, one per line ("2 kg de
 * arroz", "6 ovos", "leite 1 L"). The backend reads the list deterministically (no AI), and every
 * proposal is shown for review: name, amount, unit, and place can be changed and lines removed.
 * Only "Adicionar" saves, one pantry item per row through the normal create command, each with
 * its own Idempotency-Key so a retry never duplicates. Rows that fail stay on screen to retry;
 * saved ones are marked.
 *
 * Phases 2 and 3 feed the same review: "Organizar com IA" reads a messy list, and "Foto da nota"
 * reads a receipt (shown only when the deployment enables it). Both are explicit, because they use
 * AI credits and, for the photo, a third party. Doubtful readings are marked "Confira". A failure
 * keeps the typed text, and plain reading and manual entry keep working without the AI.
 */
export function PantryListPage() {
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const repo = useInventoryRepository();
  const navigate = useNavigate();
  const [text, setText] = useState("");
  const [rows, setRows] = useState<Row[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [receipts, setReceipts] = useState(false);
  const nextKey = useRef(0);
  const photoInput = useRef<HTMLInputElement>(null);
  const decimalLocale = locale as SupportedDecimalLocale;

  useEffect(() => {
    const controller = new AbortController();
    repo
      .isReceiptReadingAvailable?.(controller.signal)
      .then(setReceipts)
      .catch(() => undefined);
    return () => controller.abort();
  }, [repo]);

  if (!repo.parseList) {
    return (
      <p role="status" className="text-muted-foreground">
        {t("inventory.list.unavailable")}
      </p>
    );
  }

  const read = async () => {
    if (!session.csrfToken || !text.trim() || !repo.parseList) return;
    setBusy(true);
    setError(null);
    try {
      const lines = await repo.parseList(text, {
        csrfToken: session.csrfToken,
      });
      setRows(lines.map(toRow));
      if (lines.length === 0) setError("inventory.list.empty");
    } catch {
      setError("inventory.list.readError");
    } finally {
      setBusy(false);
    }
  };

  const readWithAi = async () => {
    if (!session.csrfToken || !text.trim() || !repo.parseListWithAi) return;
    setBusy(true);
    setError(null);
    try {
      const lines = await repo.parseListWithAi(text, {
        csrfToken: session.csrfToken,
        language: locale,
      });
      setRows(lines.map(toRow));
      if (lines.length === 0) setError("inventory.list.empty");
    } catch {
      setError("inventory.list.aiError");
    } finally {
      setBusy(false);
    }
  };

  const readPhoto = async (photo: File | undefined) => {
    // The same photo can be picked again after a failure.
    if (photoInput.current) photoInput.current.value = "";
    if (!photo || !session.csrfToken || !repo.parseReceipt) return;
    if (photo.size > MAX_PHOTO_BYTES) {
      setError("inventory.list.photoTooLarge");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const lines = await repo.parseReceipt(photo, {
        csrfToken: session.csrfToken,
        language: locale,
      });
      setRows(lines.map(toRow));
      if (lines.length === 0) setError("inventory.list.photoEmpty");
    } catch {
      setError("inventory.list.photoError");
    } finally {
      setBusy(false);
    }
  };

  const toRow = (line: PantryListLine): Row => ({
    key: nextKey.current++,
    name: line.name,
    amount:
      line.quantity === null
        ? ""
        : formatLocaleDecimal(line.quantity, decimalLocale),
    unit: line.unit ?? "Unit",
    place: line.storageLocation,
    idempotencyKey: crypto.randomUUID(),
    status: "pending",
    uncertain: line.uncertain === true,
    recognized: line.recognized,
  });

  const update = (key: number, change: Partial<Row>) =>
    setRows((current) =>
      (current ?? []).map((row) =>
        row.key === key ? { ...row, ...change } : row,
      ),
    );

  const save = async () => {
    if (!session.csrfToken || !rows) return;
    setBusy(true);
    setError(null);
    let failed = 0;
    let next = rows;
    for (const row of rows) {
      if (row.status === "saved" || !row.name.trim()) continue;
      const parsed = row.amount.trim()
        ? parseLocaleDecimal(row.amount, decimalLocale)
        : null;
      if (parsed && (!parsed.ok || parsed.value <= 0)) {
        failed++;
        next = next.map((item) =>
          item.key === row.key ? { ...item, status: "failed" } : item,
        );
        continue;
      }
      try {
        await repo.createLot(
          {
            productName: row.name.trim(),
            quantity:
              parsed && parsed.ok
                ? { kind: "measured", value: parsed.value, unit: row.unit }
                : { kind: "qualitative", availability: "Available" },
            storageLocation: row.place,
          },
          { csrfToken: session.csrfToken, idempotencyKey: row.idempotencyKey },
        );
        next = next.map((item) =>
          item.key === row.key ? { ...item, status: "saved" } : item,
        );
      } catch {
        failed++;
        next = next.map((item) =>
          item.key === row.key ? { ...item, status: "failed" } : item,
        );
      }
      setRows(next);
    }
    setBusy(false);
    if (failed === 0) {
      navigate("/app/despensa");
    } else {
      setError("inventory.list.saveError");
    }
  };

  const toSave =
    rows?.filter((row) => row.status !== "saved" && row.name.trim()).length ??
    0;

  return (
    <div data-testid="pantry-list-page" className="mx-auto max-w-xl space-y-6">
      <div className="space-y-2">
        <Link
          to="/app/despensa/novo"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline"
        >
          <ChevronLeft aria-hidden="true" className="h-4 w-4" />
          {t("inventory.list.back")}
        </Link>
        <h1 className="font-display text-3xl">{t("inventory.list.title")}</h1>
        <p className="text-muted-foreground">{t("inventory.list.hint")}</p>
      </div>

      {rows === null ? (
        <section className="surface space-y-3 p-4">
          <label htmlFor="pantry-list-text" className="font-semibold">
            {t("inventory.list.label")}
          </label>
          <textarea
            id="pantry-list-text"
            data-testid="pantry-list-text"
            rows={8}
            maxLength={8000}
            value={text}
            onChange={(event) => setText(event.target.value)}
            placeholder={t("inventory.list.placeholder")}
            className="block w-full rounded-xl border border-input bg-card px-4 py-3 text-base leading-relaxed placeholder:text-muted-foreground focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
          />
          <Button
            type="button"
            className="w-full"
            data-testid="pantry-list-read"
            disabled={busy || !text.trim() || !session.csrfToken}
            onClick={() => void read()}
          >
            <ListPlus aria-hidden="true" />
            {busy ? t("inventory.list.reading") : t("inventory.list.read")}
          </Button>
          {repo.parseListWithAi ? (
            <Button
              type="button"
              variant="secondary"
              className="w-full"
              data-testid="pantry-list-read-ai"
              disabled={busy || !text.trim() || !session.csrfToken}
              onClick={() => void readWithAi()}
            >
              <Sparkles aria-hidden="true" />
              {t("inventory.list.readAi")}
            </Button>
          ) : null}
          {receipts ? (
            <div className="space-y-2 border-t border-border pt-3">
              <input
                ref={photoInput}
                type="file"
                accept="image/jpeg,image/png,image/webp"
                capture="environment"
                className="sr-only"
                tabIndex={-1}
                aria-hidden="true"
                data-testid="pantry-list-photo-input"
                onChange={(event) => void readPhoto(event.target.files?.[0])}
              />
              <Button
                type="button"
                variant="secondary"
                className="w-full"
                data-testid="pantry-list-photo"
                disabled={busy || !session.csrfToken}
                onClick={() => photoInput.current?.click()}
              >
                <Camera aria-hidden="true" />
                {busy ? t("inventory.list.reading") : t("inventory.list.photo")}
              </Button>
              <p className="text-xs text-muted-foreground">
                {t("inventory.list.photoHelp")}
              </p>
            </div>
          ) : null}
        </section>
      ) : (
        <section aria-labelledby="pantry-list-review" className="space-y-3">
          <div className="flex items-baseline justify-between gap-2">
            <h2 id="pantry-list-review" className="font-display text-xl">
              {t("inventory.list.review", { count: rows.length })}
            </h2>
            <button
              type="button"
              className="text-sm font-medium text-primary underline-offset-4 hover:underline"
              onClick={() => setRows(null)}
            >
              {t("inventory.list.editText")}
            </button>
          </div>
          {repo.parseListWithAi &&
          text.trim() &&
          rows.some((row) => !row.recognized && row.status !== "saved") ? (
            <Button
              type="button"
              variant="secondary"
              className="w-full"
              data-testid="pantry-list-review-ai"
              disabled={busy || !session.csrfToken}
              onClick={() => void readWithAi()}
            >
              <Sparkles aria-hidden="true" />
              {t("inventory.list.readAi")}
            </Button>
          ) : null}
          <ul className="surface divide-y divide-border overflow-hidden">
            {rows.map((row) => (
              <li
                key={row.key}
                data-testid="pantry-list-row"
                className={`space-y-2 p-3 ${row.status === "saved" ? "opacity-60" : ""}`}
              >
                <div className="flex items-center gap-2">
                  <Input
                    aria-label={t("inventory.fields.productName")}
                    data-testid="pantry-list-name"
                    value={row.name}
                    maxLength={80}
                    disabled={busy || row.status === "saved"}
                    onChange={(event) =>
                      update(row.key, { name: event.target.value })
                    }
                    className="flex-1"
                  />
                  <button
                    type="button"
                    aria-label={t("inventory.list.remove", { name: row.name })}
                    disabled={busy || row.status === "saved"}
                    onClick={() =>
                      setRows((current) =>
                        (current ?? []).filter((item) => item.key !== row.key),
                      )
                    }
                    className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl text-muted-foreground transition-colors duration-150 hover:bg-secondary hover:text-destructive focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
                  >
                    <Trash2 aria-hidden="true" className="h-4 w-4" />
                  </button>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <Input
                    aria-label={t("inventory.fields.amount")}
                    data-testid="pantry-list-amount"
                    inputMode="decimal"
                    placeholder={t("inventory.list.noAmount")}
                    value={row.amount}
                    disabled={busy || row.status === "saved"}
                    onChange={(event) =>
                      update(row.key, { amount: event.target.value })
                    }
                    className="w-28"
                  />
                  <select
                    aria-label={t("inventory.fields.unit")}
                    value={row.unit}
                    disabled={busy || row.status === "saved"}
                    onChange={(event) =>
                      update(row.key, {
                        unit: event.target.value as MeasuredUnit,
                      })
                    }
                    className="h-11 rounded-xl border border-input bg-card px-3 text-[15px] focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
                  >
                    {UNITS.map((unit) => (
                      <option key={unit} value={unit}>
                        {t(`inventory.unit.${unit}`)}
                      </option>
                    ))}
                  </select>
                  <div
                    role="group"
                    aria-label={t("inventory.fields.location")}
                    className="flex gap-1"
                  >
                    {PLACES.map((place) => (
                      <button
                        key={place}
                        type="button"
                        aria-pressed={row.place === place}
                        disabled={busy || row.status === "saved"}
                        onClick={() => update(row.key, { place })}
                        className={`h-9 rounded-full border px-3 text-xs font-semibold transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
                          row.place === place
                            ? "border-primary bg-primary text-primary-foreground"
                            : "border-border bg-card text-foreground hover:bg-secondary"
                        }`}
                      >
                        {t(`inventory.location.${place}`)}
                      </button>
                    ))}
                  </div>
                </div>
                {row.uncertain && row.status !== "saved" ? (
                  <p className="status-pill status-warning w-fit">
                    {t("inventory.list.check")}
                  </p>
                ) : null}
                {row.status === "failed" ? (
                  <p role="alert" className="text-xs text-destructive">
                    {t("inventory.list.rowError")}
                  </p>
                ) : row.status === "saved" ? (
                  <p className="text-xs text-success">
                    {t("inventory.list.rowSaved")}
                  </p>
                ) : null}
              </li>
            ))}
          </ul>
          <Button
            type="button"
            size="lg"
            className="w-full"
            data-testid="pantry-list-save"
            disabled={busy || toSave === 0 || !session.csrfToken}
            onClick={() => void save()}
          >
            {busy
              ? t("inventory.list.saving")
              : t("inventory.list.save", { count: toSave })}
          </Button>
        </section>
      )}

      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(error)}
        </p>
      ) : null}
    </div>
  );
}
