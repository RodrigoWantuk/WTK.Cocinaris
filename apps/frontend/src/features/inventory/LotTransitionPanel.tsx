import { useEffect, useState } from "react";
import type {
  InventoryLotView,
  StorageLocation,
  TransitionKind,
  TransitionLotInput,
} from "@/adapters/live/inventoryTypes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { parseLocaleDecimal } from "@/lib/localeDecimal";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { localizeInventoryKey } from "./inventoryLabels";
import {
  allowedTransitions,
  supportsPartial,
  targetOptions,
} from "./lotTransitions";

/**
 * Lifecycle transition form for one lot: open, move, freeze, start or finish thawing, optionally
 * for part of a measured lot. It only builds the request; the parent performs the mutation and
 * owns error, conflict and history handling.
 *
 * @param lot Current lot; the offered actions follow its state.
 * @param busy Disables the form while any mutation of the lot is running.
 * @param onSubmit Performs the transition; resolves `true` on success so the form can reset.
 * @param onInvalidQuantity Reports a partial quantity that does not parse in the current locale.
 */
export function LotTransitionPanel({
  lot,
  busy,
  onSubmit,
  onInvalidQuantity,
}: {
  lot: InventoryLotView;
  busy: boolean;
  onSubmit: (input: TransitionLotInput) => Promise<boolean>;
  onInvalidQuantity: () => void;
}) {
  const { t, locale } = useProductionI18n();
  const kinds = allowedTransitions(lot);
  const [kind, setKind] = useState<TransitionKind | undefined>(kinds[0]);
  const [target, setTarget] = useState<StorageLocation | "">("");
  const [partial, setPartial] = useState("");

  // The lot changes after every mutation; keep the selection valid for its new state.
  const kindsKey = kinds.join(",");
  useEffect(() => {
    setKind((current) =>
      current && kindsKey.split(",").includes(current)
        ? current
        : (kindsKey.split(",")[0] as TransitionKind | undefined),
    );
  }, [kindsKey]);

  const options = kind ? targetOptions(kind, lot) : [];
  const optionsKey = options.map((option) => option ?? "").join(",");
  useEffect(() => {
    setTarget((optionsKey.split(",")[0] ?? "") as StorageLocation | "");
  }, [kind, optionsKey]);

  if (kinds.length === 0 || !kind) {
    return (
      <section className="surface space-y-2 p-4">
        <h2 className="font-display text-xl">
          {t("inventory.transition.title")}
        </h2>
        <p
          className="text-sm text-muted-foreground"
          data-testid="inventory-transition-none"
        >
          {t("inventory.transition.none")}
        </p>
      </section>
    );
  }

  const partialAllowed = supportsPartial(kind, lot);
  const unit = lot.quantity.kind === "measured" ? lot.quantity.unit : null;

  async function submit() {
    if (!kind) return;
    let partialValue: number | null = null;
    if (partialAllowed && partial.trim() !== "") {
      const parsed = parseLocaleDecimal(partial, locale);
      if (!parsed.ok) {
        onInvalidQuantity();
        return;
      }
      partialValue = parsed.value;
    }
    const ok = await onSubmit({
      kind,
      partialValue,
      unit: partialValue != null ? unit : null,
      targetStorageLocation: target === "" ? null : target,
    });
    if (ok) setPartial("");
  }

  return (
    <section
      className="surface space-y-3 p-4"
      data-testid="inventory-transition"
    >
      <h2 className="font-display text-xl">
        {t("inventory.transition.title")}
      </h2>
      <label className="block space-y-1">
        <span className="text-sm">{t("inventory.transition.kind")}</span>
        <select
          data-testid="inventory-transition-kind"
          className="flex h-11 w-full rounded-xl border border-input bg-card px-3 text-[15px] focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
          value={kind}
          onChange={(event) => setKind(event.target.value as TransitionKind)}
        >
          {kinds.map((option) => (
            <option key={option} value={option}>
              {t(`inventory.transition.${option}`)}
            </option>
          ))}
        </select>
      </label>
      {options.length > 0 && (
        <label className="block space-y-1">
          <span className="text-sm">{t("inventory.transition.target")}</span>
          <select
            data-testid="inventory-transition-target"
            className="flex h-11 w-full rounded-xl border border-input bg-card px-3 text-[15px] focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
            value={target}
            onChange={(event) =>
              setTarget(event.target.value as StorageLocation | "")
            }
          >
            {options.map((option) => (
              <option key={option ?? "keep"} value={option ?? ""}>
                {option
                  ? localizeInventoryKey(t, "inventory.location", option)
                  : t("inventory.transition.keepLocation")}
              </option>
            ))}
          </select>
        </label>
      )}
      {partialAllowed && unit && (
        <label className="block space-y-1">
          <span className="text-sm">
            {t("inventory.transition.partial")} (
            {localizeInventoryKey(t, "inventory.unit", unit)})
          </span>
          <Input
            data-testid="inventory-transition-partial"
            inputMode="decimal"
            value={partial}
            onChange={(event) => setPartial(event.target.value)}
            aria-describedby="inventory-transition-partial-hint"
          />
          <span
            id="inventory-transition-partial-hint"
            className="block text-xs text-muted-foreground"
          >
            {t("inventory.transition.partialHint")}
          </span>
        </label>
      )}
      <Button
        type="button"
        data-testid="inventory-transition-submit"
        disabled={busy}
        onClick={() => void submit()}
      >
        {t("inventory.transition.submit")}
      </Button>
    </section>
  );
}
