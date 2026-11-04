import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type {
  ArrivalLine,
  ArrivalPlan,
  ArrivalResult,
} from "@/contracts/planning";
import { formatLocaleDecimal, parseLocaleDecimal } from "@/lib/localeDecimal";
import { formatMenuDay, localIsoDate } from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";

const LOCATIONS = ["Pantry", "Refrigerator", "Freezer"] as const;
const UNIT_KEYS: Record<string, string> = {
  Gram: "recipes.unit.g",
  Milliliter: "recipes.unit.ml",
  Unit: "recipes.unit.unit",
};

/**
 * "Cheguei do mercado" (PLAN-0062): where each purchase goes and how to divide it, recorded with one
 * confirmation. Portions come from the backend's advisor and curated table; with the table off, a
 * purchase stays whole and nothing mentions freezing or dates. The advice is guidance: any place can be
 * changed, and a repeated "Guardar tudo" with the same request key never counts anything twice.
 */
export function ArrivalPage() {
  const { menuId = "" } = useParams<{ menuId: string }>();
  const repo = usePlanningRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const today = localIsoDate();
  const [plan, setPlan] = useState<ArrivalPlan | null>(null);
  const [failed, setFailed] = useState(false);
  const [texts, setTexts] = useState<Record<string, string>>({});
  const [places, setPlaces] = useState<Record<string, string>>({});
  const [packs, setPacks] = useState<Set<string>>(new Set());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ArrivalResult | null>(null);
  const keyRef = useRef(crypto.randomUUID());

  useEffect(() => {
    const controller = new AbortController();
    repo.getArrival?.(menuId, today, controller.signal).then(
      (value) => !controller.signal.aborted && setPlan(value),
      () => !controller.signal.aborted && setFailed(true),
    );
    return () => controller.abort();
  }, [repo, menuId, today]);

  const amount = useCallback(
    (quantity: number, unit: string) =>
      `${formatLocaleDecimal(quantity, locale)} ${t(UNIT_KEYS[unit] ?? "recipes.unit.unit")}`,
    [locale, t],
  );

  /** Recomputes portions for what the person says came. It changes nothing on the server. */
  const correct = async (line: ArrivalLine) => {
    if (!session.csrfToken || !plan) return;
    const parsed = parseLocaleDecimal(texts[line.itemKey] ?? "", locale);
    if (
      !parsed.ok ||
      parsed.value <= 0 ||
      parsed.value === line.boughtQuantity
    ) {
      return;
    }
    const quantities = plan.lines
      .filter((item) => item.itemKey !== line.itemKey)
      .map((item) => ({
        itemKey: item.itemKey,
        boughtQuantity: item.boughtQuantity,
      }))
      .concat({ itemKey: line.itemKey, boughtQuantity: parsed.value });
    try {
      setPlan(
        await repo.previewArrival!(menuId, today, quantities, {
          csrfToken: session.csrfToken,
        }),
      );
      setPlaces({});
    } catch {
      setError("arrival.error");
    }
  };

  const place = useCallback(
    (itemKey: string, index: number, fallback: string) =>
      places[`${itemKey}#${index}`] ?? fallback,
    [places],
  );

  const portions = useMemo(
    () =>
      (plan?.lines ?? []).flatMap((line) =>
        line.portions.map((portion, index) => ({ line, portion, index })),
      ),
    [plan],
  );

  const save = async () => {
    if (!plan || !session.csrfToken || saving) return;
    setSaving(true);
    setError(null);
    try {
      const outcome = await repo.recordArrival!(
        menuId,
        plan.lines.map((line) => ({
          itemKey: line.itemKey,
          displayName: line.displayName,
          unit: line.unit,
          portions: line.portions.map((portion, index) => ({
            location: place(line.itemKey, index, portion.location),
            quantity: portion.quantity,
            count: portion.count,
            notes: portion.dish
              ? t("arrival.note", { dish: portion.dish })
              : null,
          })),
        })),
        { csrfToken: session.csrfToken, idempotencyKey: keyRef.current },
      );
      for (const line of plan.lines) {
        if (packs.has(line.itemKey)) {
          await repo.setPackageSize?.(line.itemKey, line.boughtQuantity, {
            csrfToken: session.csrfToken,
          });
        }
      }
      setResult(outcome);
    } catch {
      // The same key stays, so trying again replays what was already recorded.
      setError("arrival.error");
    } finally {
      setSaving(false);
    }
  };

  if (failed) {
    return (
      <p role="alert" className="text-muted-foreground">
        {t("arrival.loadError")}
      </p>
    );
  }
  if (!plan) return <p role="status">{t("planning.loading")}</p>;

  if (result && result.failed.length === 0) {
    return (
      <div data-testid="arrival-done" className="space-y-4">
        <h1 className="font-display text-3xl">{t("arrival.done.title")}</h1>
        <p role="status" className="text-muted-foreground">
          {t("arrival.done.body", { count: result.recorded })}
        </p>
        <Button asChild size="lg" className="w-full">
          <Link to={`/app/cardapio/${menuId}`}>{t("arrival.done.menu")}</Link>
        </Button>
        <Button asChild variant="secondary" className="w-full">
          <Link to="/app/despensa">{t("arrival.done.pantry")}</Link>
        </Button>
      </div>
    );
  }

  const header = (
    <header className="space-y-1">
      <h1 className="font-display text-3xl">{t("arrival.title")}</h1>
      <p className="text-muted-foreground">{t("arrival.subtitle")}</p>
    </header>
  );
  if (plan.lines.length === 0) {
    return (
      <div data-testid="arrival-empty" className="space-y-4">
        {header}
        <p className="text-muted-foreground">{t("arrival.empty")}</p>
        <Button asChild variant="secondary">
          <Link to={`/app/cardapio/${menuId}/compras`}>
            {t("arrival.toList")}
          </Link>
        </Button>
      </div>
    );
  }

  const asked = plan.lines.filter((line) => line.needsRealQuantity);
  const frozen = portions.filter(
    ({ line, portion, index }) =>
      place(line.itemKey, index, portion.location) === "Freezer",
  );

  return (
    <div data-testid="arrival-page" className="space-y-6">
      {header}

      {asked.length > 0 ? (
        <section aria-labelledby="arrival-amounts" className="space-y-2">
          <h2 id="arrival-amounts" className="font-semibold">
            {t("arrival.amounts")}
          </h2>
          <p className="text-sm text-muted-foreground">
            {t("arrival.amounts.hint")}
          </p>
          <ul className="surface divide-y divide-border">
            {asked.map((line) => {
              const unit = t(UNIT_KEYS[line.unit] ?? "recipes.unit.unit");
              const differs = line.boughtQuantity !== line.requiredQuantity;
              return (
                <li
                  key={line.itemKey}
                  className="space-y-2 p-4"
                  data-testid="arrival-amount"
                >
                  <label className="block space-y-1 text-sm font-medium">
                    {t("arrival.amount", { item: line.displayName, unit })}
                    <Input
                      inputMode="decimal"
                      data-testid="arrival-quantity"
                      value={
                        texts[line.itemKey] ??
                        formatLocaleDecimal(line.boughtQuantity, locale)
                      }
                      onChange={(event) =>
                        setTexts((current) => ({
                          ...current,
                          [line.itemKey]: event.target.value,
                        }))
                      }
                      onBlur={() => void correct(line)}
                    />
                  </label>
                  {differs && !line.packageSize ? (
                    <label className="flex min-h-11 items-center gap-3 text-sm">
                      <input
                        type="checkbox"
                        className="h-5 w-5 accent-primary"
                        data-testid="arrival-remember-package"
                        checked={packs.has(line.itemKey)}
                        onChange={(event) =>
                          setPacks((current) => {
                            const next = new Set(current);
                            if (event.target.checked) next.add(line.itemKey);
                            else next.delete(line.itemKey);
                            return next;
                          })
                        }
                      />
                      {t("arrival.rememberPackage")}
                    </label>
                  ) : null}
                </li>
              );
            })}
          </ul>
        </section>
      ) : null}

      {LOCATIONS.map((location) => {
        const rows = portions.filter(
          ({ line, portion, index }) =>
            place(line.itemKey, index, portion.location) === location,
        );
        if (rows.length === 0) return null;
        return (
          <section
            key={location}
            aria-labelledby={`arrival-${location}`}
            data-testid={`arrival-${location}`}
            className="space-y-2"
          >
            <h2 id={`arrival-${location}`} className="font-semibold">
              {t(`inventory.location.${location}`)}
            </h2>
            <ul className="surface divide-y divide-border">
              {rows.map(({ line, portion, index }) => {
                const size = amount(portion.quantity, line.unit);
                return (
                  <li
                    key={`${line.itemKey}#${index}`}
                    className="space-y-2 p-4"
                    data-testid="arrival-portion"
                  >
                    <p className="flex flex-wrap items-baseline justify-between gap-2">
                      <span className="font-semibold">{line.displayName}</span>
                      <span className="tabular font-semibold">
                        {portion.count > 1
                          ? `${portion.count} × ${size}`
                          : size}
                      </span>
                    </p>
                    {portion.dish ? (
                      <p className="text-sm text-muted-foreground">
                        {t("arrival.note", { dish: portion.dish })}
                      </p>
                    ) : null}
                    {portion.useFirst ? (
                      <p className="status-pill status-warning rounded-xl py-1.5 leading-snug">
                        {t("arrival.useFirst")}
                      </p>
                    ) : null}
                    {location === "Freezer" ? (
                      <p
                        className="text-xs text-muted-foreground"
                        data-testid="arrival-label"
                      >
                        {t("arrival.label", {
                          item: line.displayName,
                          amount: size,
                          date: formatMenuDay(today, locale),
                        })}
                      </p>
                    ) : null}
                    <label className="block space-y-1 text-xs text-muted-foreground">
                      {t("arrival.place")}
                      <select
                        className="h-11 w-full rounded-xl border border-input bg-background px-3 text-[15px] text-foreground"
                        value={location}
                        data-testid="arrival-place"
                        onChange={(event) =>
                          setPlaces((current) => ({
                            ...current,
                            [`${line.itemKey}#${index}`]: event.target.value,
                          }))
                        }
                      >
                        {LOCATIONS.map((option) => (
                          <option key={option} value={option}>
                            {t(`inventory.location.${option}`)}
                          </option>
                        ))}
                      </select>
                    </label>
                  </li>
                );
              })}
            </ul>
          </section>
        );
      })}

      {plan.containerCount > 0 ? (
        <p data-testid="arrival-containers" className="font-medium">
          {t("arrival.containers", { count: plan.containerCount })}
        </p>
      ) : null}
      {frozen.length > 0 ? (
        <section className="space-y-1" data-testid="arrival-ahead">
          <h2 className="font-semibold">{t("arrival.ahead")}</h2>
          <p className="text-sm text-muted-foreground">
            {t("arrival.ahead.portion")}
          </p>
        </section>
      ) : null}
      {plan.guidanceEnabled ? (
        <p className="text-xs text-muted-foreground">{t("arrival.advisory")}</p>
      ) : null}
      {result ? (
        <div
          role="alert"
          data-testid="arrival-partial"
          className="text-sm text-destructive"
        >
          {t("arrival.partial", {
            recorded: result.recorded,
            failed: result.failed.length,
          })}
        </div>
      ) : null}
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(error)}
        </p>
      ) : null}
      <Button
        type="button"
        size="lg"
        className="w-full"
        data-testid="arrival-save"
        disabled={saving || !session.csrfToken}
        onClick={() => void save()}
      >
        {saving ? t("planning.saving") : t("arrival.save")}
      </Button>
    </div>
  );
}
