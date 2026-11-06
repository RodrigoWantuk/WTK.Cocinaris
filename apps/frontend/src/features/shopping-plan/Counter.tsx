import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";

/**
 * A labeled number control with 44 px targets, usable by keyboard and screen readers. Each button is
 * named "Menos: {label}" or "Mais: {label}", and the value is an `output` read with its label.
 *
 * @param label Visible label, also part of the buttons' names.
 * @param hideLabel Keeps the label for assistive technology only, for a counter inside a heading row.
 * @param compact Borderless buttons, for a counter beside a heading; targets stay 44 px.
 * @param testId Test id of the value; the buttons add `-less` and `-more`.
 */
export function Counter({
  label,
  value,
  min,
  max,
  onChange,
  testId,
  hideLabel = false,
  disabled = false,
  compact = false,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  onChange: (next: number) => void;
  testId: string;
  hideLabel?: boolean;
  disabled?: boolean;
  /** Borderless buttons for a counter beside a heading; targets stay 44 px. */
  compact?: boolean;
}) {
  const { t } = useProductionI18n();
  return (
    <div
      className={`flex min-h-11 items-center ${compact ? "gap-1" : "gap-3"}`}
    >
      <span
        className={
          hideLabel ? "sr-only" : "min-w-0 flex-1 text-[15px] font-medium"
        }
      >
        {label}
      </span>
      <Button
        type="button"
        variant={compact ? "ghost" : "secondary"}
        className="h-11 w-11 p-0 text-lg"
        aria-label={t("shoppingPlan.stepper.less", { label })}
        data-testid={`${testId}-less`}
        disabled={disabled || value <= min}
        onClick={() => onChange(value - 1)}
      >
        −
      </Button>
      <output
        data-testid={testId}
        aria-label={`${label}: ${value}`}
        className={`tabular text-center font-semibold ${compact ? "w-6 text-base" : "w-8 text-lg"}`}
      >
        {value}
      </output>
      <Button
        type="button"
        variant={compact ? "ghost" : "secondary"}
        className="h-11 w-11 p-0 text-lg"
        aria-label={t("shoppingPlan.stepper.more", { label })}
        data-testid={`${testId}-more`}
        disabled={disabled || value >= max}
        onClick={() => onChange(value + 1)}
      >
        +
      </Button>
    </div>
  );
}
