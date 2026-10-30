/**
 * One choice among a few options, shown as tappable chips instead of a native select (PLAN-0057
 * review L2): every option is visible, one tap chooses, and the chosen one carries the accent.
 * Rendered as a labelled group of toggle buttons with `aria-pressed`, so assistive technology
 * announces the label and which option is chosen. Use it for up to about six short options; a
 * longer or open-ended list still belongs in a select or combobox.
 *
 * @param label Accessible name of the group (also shown above the chips unless `hideLabel`).
 * @param options Values and their visible labels, in display order.
 * @param value The chosen value.
 * @param onChange Receives the value of the chip tapped.
 * @param testId Test id of the group; each chip gets `<testId>-<value>`.
 * @param disabled Disables every chip.
 * @param hideLabel Keeps the label for assistive technology only.
 */
export function ChoiceChips<T extends string>({
  label,
  options,
  value,
  onChange,
  testId,
  disabled = false,
  hideLabel = false,
}: {
  label: string;
  options: ReadonlyArray<{ value: T; label: string }>;
  value: T | undefined;
  onChange: (next: T) => void;
  testId?: string;
  disabled?: boolean;
  hideLabel?: boolean;
}) {
  return (
    <div className="space-y-1.5">
      <p className={hideLabel ? "sr-only" : "text-sm font-medium"}>{label}</p>
      <div
        role="group"
        aria-label={label}
        data-testid={testId}
        className="flex flex-wrap gap-2"
      >
        {options.map((option) => {
          const chosen = option.value === value;
          return (
            <button
              key={option.value}
              type="button"
              aria-pressed={chosen}
              disabled={disabled}
              data-testid={testId ? `${testId}-${option.value}` : undefined}
              onClick={() => onChange(option.value)}
              className={`h-10 rounded-full border px-4 text-sm font-semibold transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background disabled:opacity-50 ${
                chosen
                  ? "border-primary bg-primary text-primary-foreground"
                  : "border-border bg-card text-foreground hover:bg-secondary"
              }`}
            >
              {option.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}
