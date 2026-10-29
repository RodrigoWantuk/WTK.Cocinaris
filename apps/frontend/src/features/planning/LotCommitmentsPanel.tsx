import { useEffect, useState } from "react";
import type { InventoryLotView } from "@/adapters/live/inventoryTypes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { LotCommitment } from "@/contracts/planning";
import { formatLocaleDecimal, parseLocaleDecimal } from "@/lib/localeDecimal";
import { formatMenuDay, localIsoDate } from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";

type Choice = "none" | "whole" | "minimum";

/**
 * Reservations and protection of one pantry lot (PLAN-0029 Phase 3), on the lot page. Shows which
 * menu meals count on the lot and lets the owner protect the whole lot or keep a minimum. Neither
 * changes the lot's quantity; a failure hides only this section's content.
 */
export function LotCommitmentsPanel({ lot }: { lot: InventoryLotView }) {
  const planning = usePlanningRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const [commitment, setCommitment] = useState<LotCommitment | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "failed">(
    "loading",
  );
  const [choice, setChoice] = useState<Choice>("none");
  const [minimum, setMinimum] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const measured = lot.quantity.kind === "measured";
  const unit =
    lot.quantity.kind === "measured"
      ? t(`inventory.unit.${lot.quantity.unit}`)
      : "";

  const apply = (found: LotCommitment | null) => {
    setCommitment(found);
    setChoice(
      found?.protection
        ? found.protection.kind === "whole"
          ? "whole"
          : "minimum"
        : "none",
    );
    setMinimum(
      found?.protection?.minimumQuantity
        ? formatLocaleDecimal(found.protection.minimumQuantity, locale)
        : "",
    );
  };

  useEffect(() => {
    const controller = new AbortController();
    planning.getReservations(localIsoDate(), controller.signal).then(
      (value) => {
        if (controller.signal.aborted) return;
        apply(value.lots.find((item) => item.lotId === lot.lotId) ?? null);
        setStatus("ready");
      },
      () => !controller.signal.aborted && setStatus("failed"),
    );
    return () => controller.abort();
    // `apply` only formats; reloading on locale change is unnecessary.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [planning, lot.lotId]);

  const save = async () => {
    if (!session.csrfToken) return;
    setError(null);
    let value: number | null = null;
    if (choice === "minimum") {
      const parsed = parseLocaleDecimal(minimum, locale);
      if (!parsed.ok || parsed.value <= 0) {
        setError("lotCommitments.error.minimum");
        return;
      }
      value = parsed.value;
    }
    setSaving(true);
    try {
      const options = { csrfToken: session.csrfToken };
      const result =
        choice === "none"
          ? await planning.unprotectLot(lot.lotId, options)
          : await planning.protectLot(
              lot.lotId,
              { kind: choice, minimumQuantity: value },
              options,
            );
      apply(result.lots.find((item) => item.lotId === lot.lotId) ?? null);
    } catch {
      setError("planning.error.save");
    } finally {
      setSaving(false);
    }
  };

  const amount = (value: number) =>
    `${formatLocaleDecimal(value, locale)} ${unit}`.trim();

  return (
    <section
      aria-labelledby="lot-commitments"
      data-testid="lot-commitments"
      className="space-y-3 rounded-xl border p-4"
    >
      <h2 id="lot-commitments" className="font-display text-xl">
        {t("lotCommitments.title")}
      </h2>
      {status === "failed" ? (
        <p className="text-sm text-muted-foreground">
          {t("lotCommitments.unavailable")}
        </p>
      ) : status === "loading" ? (
        <p role="status" className="text-sm">
          {t("planning.loading")}
        </p>
      ) : (
        <>
          {commitment && commitment.reservations.length > 0 ? (
            <div className="space-y-1 text-sm">
              <p>
                {t("lotCommitments.reserved", {
                  value: amount(commitment.reservedQuantity),
                  free: amount(commitment.freeQuantity),
                })}
              </p>
              <ul className="list-disc pl-5 text-muted-foreground">
                {commitment.reservations.map((item) => (
                  <li key={item.menuEntryId} data-testid="lot-commitments-meal">
                    {item.recipeName}
                    {item.plannedDate
                      ? ` (${formatMenuDay(item.plannedDate, locale)})`
                      : ""}{" "}
                    — {amount(item.quantity)}
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              {t("lotCommitments.none")}
            </p>
          )}
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">
              {t("lotCommitments.protect")}
            </legend>
            <p className="text-xs text-muted-foreground">
              {t("lotCommitments.protectHint")}
            </p>
            {(["none", "whole", "minimum"] as const)
              .filter((option) => option !== "minimum" || measured)
              .map((option) => (
                <label key={option} className="flex items-center gap-2 text-sm">
                  <input
                    type="radio"
                    name={`protect-${lot.lotId}`}
                    data-testid={`lot-protect-${option}`}
                    checked={choice === option}
                    onChange={() => setChoice(option)}
                  />
                  {t(`lotCommitments.option.${option}`)}
                </label>
              ))}
            {choice === "minimum" ? (
              <label className="block max-w-xs text-sm">
                {t("lotCommitments.minimum", { unit })}
                <Input
                  inputMode="decimal"
                  data-testid="lot-protect-quantity"
                  value={minimum}
                  onChange={(event) => setMinimum(event.target.value)}
                />
              </label>
            ) : null}
          </fieldset>
          {error ? (
            <p role="alert" className="text-sm text-destructive">
              {t(error)}
            </p>
          ) : null}
          <Button
            type="button"
            size="sm"
            variant="secondary"
            data-testid="lot-protect-save"
            disabled={saving || !session.csrfToken}
            onClick={() => void save()}
          >
            {saving ? t("planning.saving") : t("planning.save")}
          </Button>
        </>
      )}
    </section>
  );
}
