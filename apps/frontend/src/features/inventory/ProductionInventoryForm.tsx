import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useInventoryRepository } from "./InventoryProvider";
import { ProductNameCombobox } from "./ProductNameCombobox";
import {
  InventoryApiError,
  type CreateLotInput,
  type InventoryLotView,
  type InventoryQuantity,
  type PackageState,
  type StorageLocation,
} from "@/adapters/live/inventoryTypes";
import { useSession } from "@/app/session/SessionProvider";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { isCalendarDateString } from "@/lib/calendarDate";
import {
  parseLocaleDecimal,
  type SupportedDecimalLocale,
} from "@/lib/localeDecimal";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  estimateFromPackage,
  formatQuantityLabel,
  type PackageFraction,
} from "./quantityDisplay";
import { staplesFor } from "./pantryStaples";

/**
 * How the owner states the amount: exactly, as a share of a package (an estimate), or with no
 * amount at all (PLAN-0054 Phase 2: "se não, pode colocar um pouco ou 'meia embalagem'").
 */
type Mode = "measured" | PackageFraction | "qualitative";
const MODES: readonly Mode[] = [
  "measured",
  "full",
  "half",
  "little",
  "qualitative",
];
const CUSTOM_LOCATION_MAX = 80;

type EditLoadStatus =
  | "idle"
  | "loading"
  | "ready"
  | "not_found"
  | "session"
  | "error";

type CreateAttempt = {
  fingerprint: string;
  idempotencyKey: string;
};

type NormalizedCreateResult =
  | { ok: true; command: CreateLotInput }
  | { ok: false; errorKey: string; fieldErrors?: Record<string, string> };

/**
 * Builds the exact create payload sent to the API. The same object is used for
 * idempotency fingerprinting so textual formatting cannot change the key.
 */
function buildNormalizedCreateCommand(
  input: {
    productName: string;
    quantityMode: Mode;
    amount: string;
    unit: "Gram" | "Milliliter" | "Unit";
    availability: "Available" | "Low" | "Unavailable";
    storageLocation: StorageLocation;
    customLocation: string;
    packageState: PackageState | "";
    printedDate: string;
    autoRemoveOn: string;
    notes: string;
  },
  locale: SupportedDecimalLocale,
  messages: {
    customLocationRequired: string;
    customLocationLength: string;
  },
): NormalizedCreateResult {
  const productName = input.productName.trim();
  if (!productName) {
    return { ok: false, errorKey: "inventory.error.productName" };
  }

  let quantity: InventoryQuantity;
  if (input.quantityMode === "measured") {
    const parsed = parseLocaleDecimal(input.amount, locale);
    if (!parsed.ok) {
      return { ok: false, errorKey: "inventory.error.invalidDecimal" };
    }
    quantity = {
      kind: "measured",
      value: parsed.value,
      unit: input.unit,
    };
  } else if (input.quantityMode !== "qualitative") {
    // Without a package size there is nothing to take a share of, so the lot only records that
    // there is some (or, for "a little", that it is running low).
    if (!input.amount.trim()) {
      quantity = {
        kind: "qualitative",
        availability: input.quantityMode === "little" ? "Low" : "Available",
      };
    } else {
      const parsed = parseLocaleDecimal(input.amount, locale);
      if (!parsed.ok) {
        return { ok: false, errorKey: "inventory.error.invalidDecimal" };
      }
      quantity = {
        kind: "measured",
        value: estimateFromPackage(
          parsed.value,
          input.unit,
          input.quantityMode,
        ),
        unit: input.unit,
        estimated: true,
      };
    }
  } else {
    quantity = {
      kind: "qualitative",
      availability: input.availability,
    };
  }

  let customLocation: string | null = null;
  if (input.storageLocation === "Other") {
    const trimmed = input.customLocation.trim();
    if (!trimmed) {
      return {
        ok: false,
        errorKey: "inventory.error.customLocationRequired",
        fieldErrors: {
          customLocation: messages.customLocationRequired,
        },
      };
    }
    if ([...trimmed].length > CUSTOM_LOCATION_MAX) {
      return {
        ok: false,
        errorKey: "inventory.error.customLocationLength",
        fieldErrors: {
          customLocation: messages.customLocationLength,
        },
      };
    }
    customLocation = trimmed;
  }

  const printedExpirationDate = input.printedDate.trim() || null;
  if (printedExpirationDate && !isCalendarDateString(printedExpirationDate)) {
    return { ok: false, errorKey: "inventory.error.printedDate" };
  }
  const autoRemoveOn = input.autoRemoveOn.trim() || null;
  if (autoRemoveOn && !isCalendarDateString(autoRemoveOn)) {
    return { ok: false, errorKey: "inventory.error.autoRemoveOn" };
  }

  return {
    ok: true,
    command: {
      productName,
      quantity,
      storageLocation: input.storageLocation,
      customLocation,
      packageState: input.packageState || null,
      printedExpirationDate,
      autoRemoveOn,
      notes: input.notes.trim() || null,
    },
  };
}

/** Deterministic fingerprint of the normalized create command only. */
function fingerprintCreateCommand(command: CreateLotInput): string {
  return JSON.stringify(command);
}

/**
 * Create or edit inventory lot metadata / initial quantity.
 */
export function ProductionInventoryForm({ mode }: { mode: "create" | "edit" }) {
  const { lotId = "" } = useParams();
  const repo = useInventoryRepository();
  const { session } = useSession();
  const { t, locale } = useProductionI18n();
  const navigate = useNavigate();
  const [existing, setExisting] = useState<InventoryLotView | null>(null);
  const [editLoadStatus, setEditLoadStatus] = useState<EditLoadStatus>(
    mode === "edit" ? "loading" : "idle",
  );
  const [productName, setProductName] = useState("");
  const [quantityMode, setQuantityMode] = useState<Mode>("measured");
  const [amount, setAmount] = useState("");
  const [unit, setUnit] = useState<"Gram" | "Milliliter" | "Unit">("Gram");
  const [availability, setAvailability] = useState<
    "Available" | "Low" | "Unavailable"
  >("Available");
  const [storageLocation, setStorageLocation] =
    useState<StorageLocation>("Pantry");
  const [customLocation, setCustomLocation] = useState("");
  const [packageState, setPackageState] = useState<PackageState | "">("");
  const [printedDate, setPrintedDate] = useState("");
  const [autoRemoveOn, setAutoRemoveOn] = useState("");
  const [notes, setNotes] = useState("");
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [conflict, setConflict] = useState(false);
  const [busy, setBusy] = useState(false);
  const [lastSaved, setLastSaved] = useState<string | null>(null);
  /** Set by "save and add another": stay on the form for the next item instead of opening the lot. */
  const addAnotherRef = useRef(false);
  /** Logical create attempt — form lifetime only; never persisted to storage. */
  const createAttemptRef = useRef<CreateAttempt | null>(null);

  useEffect(() => {
    return () => {
      createAttemptRef.current = null;
    };
  }, []);

  const applyLotToForm = useCallback(
    (lot: InventoryLotView) => {
      setExisting(lot);
      setProductName(lot.productName);
      setStorageLocation(lot.storageLocation as StorageLocation);
      setCustomLocation(lot.customLocation ?? "");
      setPackageState((lot.packageState as PackageState) || "");
      setPrintedDate(lot.printedExpirationDate ?? "");
      setAutoRemoveOn(lot.autoRemoveOn ?? "");
      setNotes(lot.notes ?? "");
      if (lot.quantity.kind === "measured") {
        setQuantityMode("measured");
        setAmount(
          String(lot.quantity.value).replace(".", locale === "en" ? "." : ","),
        );
        setUnit(lot.quantity.unit);
      } else {
        setQuantityMode("qualitative");
        setAvailability(lot.quantity.availability);
      }
    },
    [locale],
  );

  const loadExisting = useCallback(async () => {
    if (mode !== "edit") return;
    setEditLoadStatus("loading");
    setFieldError(null);
    setExisting(null);
    try {
      const lot = await repo.getLot(lotId);
      applyLotToForm(lot);
      setEditLoadStatus("ready");
    } catch (err) {
      setExisting(null);
      if (err instanceof InventoryApiError) {
        if (err.code === "not_found") {
          setEditLoadStatus("not_found");
          return;
        }
        if (err.code === "authentication_required") {
          setEditLoadStatus("session");
          return;
        }
      }
      setEditLoadStatus("error");
    }
  }, [applyLotToForm, lotId, mode, repo]);

  useEffect(() => {
    void loadExisting();
  }, [loadExisting]);

  function setStorage(next: StorageLocation) {
    setStorageLocation(next);
    if (next !== "Other") {
      setCustomLocation("");
    }
  }

  function resolveCreateIdempotencyKey(command: CreateLotInput): string {
    const fingerprint = fingerprintCreateCommand(command);
    const current = createAttemptRef.current;
    if (current && current.fingerprint === fingerprint) {
      return current.idempotencyKey;
    }
    const idempotencyKey = crypto.randomUUID();
    createAttemptRef.current = { fingerprint, idempotencyKey };
    return idempotencyKey;
  }

  function mapBackendFieldErrors(errors: Record<string, string[]>) {
    const mapped: Record<string, string> = {};
    for (const [key, values] of Object.entries(errors)) {
      if (values?.[0]) mapped[key] = values[0];
    }
    setFieldErrors(mapped);
    if (mapped.customLocation) {
      setFieldError(mapped.customLocation);
    } else if (mapped.productName) {
      setFieldError(mapped.productName);
    } else if (Object.keys(mapped).length) {
      setFieldError(Object.values(mapped)[0]);
    }
  }

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setFieldError(null);
    setFieldErrors({});
    setConflict(false);
    if (mode === "edit") {
      if (editLoadStatus !== "ready" || !existing) {
        setFieldError(t("inventory.error.loadEdit"));
        return;
      }
    }
    if (!session.csrfToken) {
      setFieldError(t("inventory.error.session"));
      return;
    }

    if (mode === "create") {
      const built = buildNormalizedCreateCommand(
        {
          productName,
          quantityMode,
          amount,
          unit,
          availability,
          storageLocation,
          customLocation,
          packageState,
          printedDate,
          autoRemoveOn,
          notes,
        },
        locale as SupportedDecimalLocale,
        {
          customLocationRequired: t("inventory.error.customLocationRequired"),
          customLocationLength: t("inventory.error.customLocationLength"),
        },
      );
      if (!built.ok) {
        if (built.fieldErrors) {
          setFieldErrors(built.fieldErrors);
          setFieldError(
            built.fieldErrors.customLocation || t(built.errorKey as never),
          );
        } else {
          setFieldError(t(built.errorKey as never));
        }
        return;
      }

      setBusy(true);
      try {
        const idempotencyKey = resolveCreateIdempotencyKey(built.command);
        const created = await repo.createLot(built.command, {
          csrfToken: session.csrfToken,
          idempotencyKey,
        });
        createAttemptRef.current = null;
        if (addAnotherRef.current) {
          setLastSaved(created.productName);
          setProductName("");
          setAmount("");
          setNotes("");
          setPrintedDate("");
          setAutoRemoveOn("");
          setPackageState("");
          return;
        }
        navigate(`/app/despensa/${created.lotId}`);
      } catch (err) {
        if (
          err instanceof InventoryApiError &&
          err.code === "validation_failed"
        ) {
          mapBackendFieldErrors(err.fieldErrors);
          if (!Object.keys(err.fieldErrors).length) {
            setFieldError(err.message || t("inventory.error.validation"));
          }
        } else {
          // Transport / ambiguous failure — keep createAttemptRef so retry reuses key.
          setFieldError(t("inventory.error.save"));
        }
      } finally {
        setBusy(false);
      }
      return;
    }

    if (!productName.trim()) {
      setFieldError(t("inventory.error.productName"));
      return;
    }
    if (storageLocation === "Other") {
      const trimmed = customLocation.trim();
      if (!trimmed) {
        setFieldError(t("inventory.error.customLocationRequired"));
        setFieldErrors({
          customLocation: t("inventory.error.customLocationRequired"),
        });
        return;
      }
      if ([...trimmed].length > CUSTOM_LOCATION_MAX) {
        setFieldError(t("inventory.error.customLocationLength"));
        setFieldErrors({
          customLocation: t("inventory.error.customLocationLength"),
        });
        return;
      }
    }
    if (printedDate && !isCalendarDateString(printedDate)) {
      setFieldError(t("inventory.error.printedDate"));
      return;
    }

    setBusy(true);
    try {
      const resolvedCustom =
        storageLocation === "Other" ? customLocation.trim() : null;

      if (!existing) {
        setFieldError(t("inventory.error.loadEdit"));
        return;
      }
      const updated = await repo.updateLot(
        existing.lotId,
        {
          productName: productName.trim(),
          storageLocation,
          customLocation: resolvedCustom,
          packageState: packageState || null,
          printedExpirationDate: printedDate.trim() || null,
          autoRemoveOn: autoRemoveOn.trim() || null,
          notes: notes.trim() || null,
        },
        { csrfToken: session.csrfToken, etag: existing.etag },
      );
      navigate(`/app/despensa/${updated.lotId}`);
    } catch (err) {
      if (
        err instanceof InventoryApiError &&
        err.code === "precondition_failed"
      ) {
        setConflict(true);
        setFieldError(t("inventory.error.staleVersion"));
      } else if (
        err instanceof InventoryApiError &&
        err.code === "precondition_required"
      ) {
        setFieldError(t("inventory.error.missingPrecondition"));
      } else if (
        err instanceof InventoryApiError &&
        err.code === "validation_failed"
      ) {
        mapBackendFieldErrors(err.fieldErrors);
        if (!Object.keys(err.fieldErrors).length) {
          setFieldError(err.message || t("inventory.error.validation"));
        }
      } else {
        setFieldError(t("inventory.error.save"));
      }
    } finally {
      setBusy(false);
    }
  }

  if (mode === "edit" && editLoadStatus === "loading") {
    return (
      <p role="status" data-testid="inventory-edit-loading">
        {t("inventory.loading")}
      </p>
    );
  }

  if (mode === "edit" && editLoadStatus === "not_found") {
    return (
      <div
        role="alert"
        data-testid="inventory-edit-not-found"
        className="mx-auto max-w-xl space-y-3"
      >
        <h1 className="font-display text-3xl">
          {t("inventory.form.editTitle")}
        </h1>
        <p>{t("inventory.error.notFound")}</p>
        <Button asChild variant="secondary">
          <Link to="/app/despensa">{t("inventory.actions.back")}</Link>
        </Button>
      </div>
    );
  }

  if (mode === "edit" && editLoadStatus === "session") {
    return (
      <div
        role="alert"
        data-testid="inventory-edit-session"
        className="mx-auto max-w-xl space-y-3"
      >
        <h1 className="font-display text-3xl">
          {t("inventory.form.editTitle")}
        </h1>
        <p>{t("inventory.error.session")}</p>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            data-testid="inventory-edit-retry"
            onClick={() => void loadExisting()}
          >
            {t("inventory.actions.retry")}
          </Button>
          <Button asChild variant="secondary">
            <Link to="/app/despensa">{t("inventory.actions.back")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  if (mode === "edit" && editLoadStatus === "error") {
    return (
      <div
        role="alert"
        data-testid="inventory-edit-error"
        className="mx-auto max-w-xl space-y-3"
      >
        <h1 className="font-display text-3xl">
          {t("inventory.form.editTitle")}
        </h1>
        <p>{t("inventory.error.loadEdit")}</p>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            data-testid="inventory-edit-retry"
            onClick={() => void loadExisting()}
          >
            {t("inventory.actions.retry")}
          </Button>
          <Button asChild variant="secondary">
            <Link to="/app/despensa">{t("inventory.actions.back")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  const editReady = mode !== "edit" || editLoadStatus === "ready";
  const staples = staplesFor(locale);
  const isPackageShare =
    quantityMode !== "measured" && quantityMode !== "qualitative";
  const estimateHint = (() => {
    if (!isPackageShare) return null;
    if (!amount.trim()) return t("inventory.estimate.noSize");
    const parsed = parseLocaleDecimal(amount, locale as SupportedDecimalLocale);
    if (!parsed.ok) return null;
    // The hint already says "about", so the amount is shown without the "~" estimate mark.
    const label = formatQuantityLabel(
      {
        kind: "measured",
        value: estimateFromPackage(parsed.value, unit, quantityMode),
        unit,
      },
      locale as SupportedDecimalLocale,
      t as (key: string) => string,
    );
    return t("inventory.estimate.hint").replace("{amount}", label);
  })();

  return (
    <form
      data-testid="production-inventory-form"
      className="mx-auto max-w-xl space-y-4"
      onSubmit={(event) => void onSubmit(event)}
      noValidate
    >
      <h1 className="font-display text-3xl">
        {mode === "create"
          ? t("inventory.form.createTitle")
          : t("inventory.form.editTitle")}
      </h1>

      {conflict && (
        <div role="alert" data-testid="inventory-form-conflict">
          <p>{t("inventory.error.staleHint")}</p>
          <Button asChild className="mt-2" variant="secondary">
            <Link to={`/app/despensa/${lotId}`}>
              {t("inventory.actions.reloadReview")}
            </Link>
          </Button>
        </div>
      )}

      {lastSaved && (
        <p
          role="status"
          data-testid="inventory-saved-another"
          className="status-pill status-success"
        >
          {t("inventory.savedAnother").replace("{name}", lastSaved)}
        </p>
      )}

      {/* Three short steps on surfaces (PLAN-0058): what, how much, where. */}
      <section className="surface space-y-3 p-4">
        <label className="block space-y-2">
          <span className="flex items-center gap-2 font-semibold">
            <StepNumber value={1} />
            {t("inventory.fields.productName")}
          </span>
          <ProductNameCombobox
            value={productName}
            onChange={setProductName}
            repository={repo}
            language={locale}
            disabled={busy || !editReady}
            invalid={Boolean(fieldErrors.productName)}
          />
        </label>
        {mode === "create" && !productName.trim() && (
          <div
            className="flex flex-wrap gap-2"
            role="group"
            aria-label={t("inventory.staples.label")}
            data-testid="inventory-staples"
          >
            {staples.map((staple) => (
              <button
                key={staple.name}
                type="button"
                disabled={busy}
                onClick={() => {
                  setProductName(staple.name);
                  setUnit(staple.unit);
                  setStorage(staple.storage);
                }}
                className="h-9 rounded-full border border-border bg-card px-3 text-sm font-medium transition-colors duration-150 hover:border-primary/40 hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                {staple.name}
              </button>
            ))}
          </div>
        )}
      </section>

      {mode === "create" && (
        <section className="surface space-y-3 p-4">
          <fieldset className="space-y-2">
            <legend className="mb-2 flex items-center gap-2 font-semibold">
              <StepNumber value={2} />
              {t("inventory.fields.quantityMode")}
            </legend>
            <div className="flex flex-wrap gap-2">
              {MODES.map((option) => (
                <label
                  key={option}
                  className={`relative inline-flex h-11 cursor-pointer items-center rounded-full px-4 text-[15px] font-semibold focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-2 ${
                    quantityMode === option
                      ? "border border-primary bg-primary text-primary-foreground"
                      : "border border-border bg-card text-foreground hover:bg-secondary"
                  }`}
                >
                  <input
                    type="radio"
                    name="qty-mode"
                    className="sr-only"
                    data-testid={`inventory-qty-${option}`}
                    checked={quantityMode === option}
                    disabled={busy}
                    onChange={() => setQuantityMode(option)}
                  />
                  {t(`inventory.quantityMode.${option}`)}
                </label>
              ))}
            </div>
          </fieldset>

          {quantityMode !== "qualitative" ? (
            <div className="flex flex-wrap gap-2">
              <label className="min-w-[8rem] flex-1 space-y-1">
                <span>
                  {isPackageShare
                    ? t("inventory.fields.packageSize")
                    : t("inventory.fields.amount")}
                </span>
                <Input
                  data-testid="inventory-amount"
                  value={amount}
                  disabled={busy}
                  onChange={(event) => setAmount(event.target.value)}
                  inputMode="decimal"
                />
              </label>
              <label className="space-y-1">
                <span>{t("inventory.fields.unit")}</span>
                <select
                  className="flex h-11 w-full rounded-xl border border-input bg-card px-3 text-[15px] focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
                  value={unit}
                  disabled={busy}
                  onChange={(event) =>
                    setUnit(event.target.value as typeof unit)
                  }
                >
                  <option value="Gram">{t("inventory.unit.Gram")}</option>
                  <option value="Milliliter">
                    {t("inventory.unit.Milliliter")}
                  </option>
                  <option value="Unit">{t("inventory.unit.Unit")}</option>
                </select>
              </label>
              {estimateHint && (
                <p
                  data-testid="inventory-estimate-hint"
                  className="w-full text-sm text-muted-foreground"
                >
                  {estimateHint}
                </p>
              )}
            </div>
          ) : (
            <label className="block space-y-1">
              <span>{t("inventory.fields.availability")}</span>
              <select
                className="flex h-11 w-full rounded-xl border border-input bg-card px-3 text-[15px] focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
                value={availability}
                disabled={busy}
                onChange={(event) =>
                  setAvailability(event.target.value as typeof availability)
                }
              >
                <option value="Available">
                  {t("inventory.availability.Available")}
                </option>
                <option value="Low">{t("inventory.availability.Low")}</option>
                <option value="Unavailable">
                  {t("inventory.availability.Unavailable")}
                </option>
              </select>
            </label>
          )}
        </section>
      )}

      <section className="surface space-y-3 p-4">
        <fieldset className="space-y-2">
          <legend className="mb-2 flex items-center gap-2 font-semibold">
            <StepNumber value={mode === "create" ? 3 : 2} />
            {t("inventory.fields.location")}
          </legend>
          <div className="flex flex-wrap gap-2">
            {(["Pantry", "Refrigerator", "Freezer", "Other"] as const).map(
              (loc) => (
                <label
                  key={loc}
                  className={`relative inline-flex h-11 cursor-pointer items-center rounded-full px-4 text-[15px] font-semibold focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-2 ${
                    storageLocation === loc
                      ? "border border-primary bg-primary text-primary-foreground"
                      : "border border-border bg-card text-foreground hover:bg-secondary"
                  }`}
                >
                  <input
                    type="radio"
                    name="storage-location"
                    className="sr-only"
                    data-testid={`inventory-location-${loc}`}
                    checked={storageLocation === loc}
                    disabled={busy || !editReady}
                    onChange={() => setStorage(loc)}
                  />
                  {t(`inventory.location.${loc}`)}
                </label>
              ),
            )}
          </div>
        </fieldset>

        {storageLocation === "Other" && (
          <label className="block space-y-1">
            <span>{t("inventory.fields.customLocation")}</span>
            <Input
              data-testid="inventory-custom-location"
              value={customLocation}
              disabled={busy || !editReady}
              maxLength={CUSTOM_LOCATION_MAX}
              onChange={(event) => setCustomLocation(event.target.value)}
              required
              aria-invalid={Boolean(fieldErrors.customLocation)}
              aria-describedby={
                fieldErrors.customLocation ? "custom-location-error" : undefined
              }
            />
            {fieldErrors.customLocation && (
              <span
                id="custom-location-error"
                role="alert"
                className="text-sm text-destructive"
              >
                {fieldErrors.customLocation}
              </span>
            )}
          </label>
        )}
      </section>
      <details
        className="surface group p-4"
        open={mode === "edit" ? true : undefined}
        data-testid="inventory-more-details"
      >
        <summary className="cursor-pointer text-[15px] font-semibold text-muted-foreground">
          {t("inventory.moreDetails")}
        </summary>
        <div className="mt-4 space-y-4">
          <label className="block space-y-1">
            <span>{t("inventory.fields.packageState")}</span>
            <select
              className="flex h-11 w-full rounded-xl border border-input bg-card px-3 text-[15px] focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
              value={packageState}
              disabled={busy || !editReady}
              onChange={(event) =>
                setPackageState(event.target.value as PackageState | "")
              }
            >
              <option value="">{t("inventory.fields.none")}</option>
              <option value="Sealed">{t("inventory.package.Sealed")}</option>
              <option value="Opened">{t("inventory.package.Opened")}</option>
              <option value="Unknown">{t("inventory.package.Unknown")}</option>
            </select>
          </label>

          <label className="block space-y-1">
            <span>{t("inventory.fields.printedDate")}</span>
            <Input
              data-testid="inventory-printed-date-input"
              type="text"
              inputMode="numeric"
              placeholder="YYYY-MM-DD"
              value={printedDate}
              disabled={busy || !editReady}
              onChange={(event) => setPrintedDate(event.target.value)}
            />
            <span className="block text-xs text-muted-foreground">
              {t("inventory.printedDateDisclaimer")}
            </span>
          </label>

          <label className="block space-y-1">
            <span>{t("inventory.fields.autoRemoveOn")}</span>
            <Input
              data-testid="inventory-auto-remove-input"
              type="date"
              value={autoRemoveOn}
              disabled={busy || !editReady}
              onChange={(event) => setAutoRemoveOn(event.target.value)}
            />
            <span className="block text-xs text-muted-foreground">
              {t("inventory.autoRemoveHint")}
            </span>
          </label>

          <label className="block space-y-1">
            <span>{t("inventory.fields.notes")}</span>
            <Input
              value={notes}
              disabled={busy || !editReady}
              onChange={(event) => setNotes(event.target.value)}
            />
          </label>
        </div>
      </details>

      {fieldError && (
        <p role="alert" data-testid="inventory-form-error">
          {fieldError}
        </p>
      )}

      <div className="flex flex-wrap gap-2">
        <Button
          type="submit"
          data-testid="inventory-save"
          disabled={busy || !session.csrfToken || !editReady}
          onClick={() => {
            addAnotherRef.current = false;
          }}
        >
          {t("inventory.actions.save")}
        </Button>
        {mode === "create" && (
          <Button
            type="submit"
            variant="secondary"
            data-testid="inventory-save-another"
            disabled={busy || !session.csrfToken}
            onClick={() => {
              addAnotherRef.current = true;
            }}
          >
            {t("inventory.actions.saveAnother")}
          </Button>
        )}
        <Button asChild type="button" variant="secondary">
          <Link
            to={mode === "edit" ? `/app/despensa/${lotId}` : "/app/despensa"}
          >
            {t("inventory.actions.cancel")}
          </Link>
        </Button>
      </div>
    </form>
  );
}

/** The number of a form step, in an accent circle; decorative, the legend carries the meaning. */
function StepNumber({ value }: { value: number }) {
  return (
    <span
      aria-hidden="true"
      className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary text-xs font-bold text-primary-foreground"
    >
      {value}
    </span>
  );
}
