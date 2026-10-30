import type { LotAttention } from "@/adapters/live/inventoryTypes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";

const TONE: Record<string, string> = {
  LikelyDiscard: "bg-destructive/10 text-destructive",
  CheckCondition: "bg-destructive/10 text-destructive",
  UsePriority: "bg-warning-soft text-warning",
  UseSoon: "bg-warning-soft text-warning",
  EstimateUncertain: "bg-muted text-muted-foreground",
};

/** Whole days from the person's local today to a `YYYY-MM-DD` date. */
function daysFromToday(iso: string): number {
  const [year, month, day] = iso.split("-").map(Number);
  const now = new Date();
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  return Math.round((Date.UTC(year, month - 1, day) - today) / 86_400_000);
}

/**
 * A short use-by phrase (review L1): "today" and "tomorrow" in words, otherwise weekday, day,
 * and month, with the year only when it is not the current one. Null when the state label
 * already says the same ("use today or tomorrow").
 */
function formatUseBy(
  iso: string,
  state: string,
  locale: string,
  t: (key: string) => string,
): string | null {
  const days = daysFromToday(iso);
  if (state === "UsePriority" && days >= 0 && days <= 1) return null;
  if (days === 0) return t("inventory.attention.useByToday");
  if (days === 1) return t("inventory.attention.useByTomorrow");
  const [year, month, day] = iso.split("-").map(Number);
  const date = new Date(year, month - 1, day);
  const text = new Intl.DateTimeFormat(locale, {
    weekday: "short",
    day: "numeric",
    month: "short",
    ...(year !== new Date().getFullYear() ? { year: "numeric" } : {}),
  }).format(date);
  return t("inventory.attention.useBy").replace("{date}", text);
}

/**
 * Advisory attention label for a lot (PLAN-0037 Phase 2). Renders nothing for the normal state.
 * The label is text, not only color, so it stays meaningful for screen readers and in
 * high-contrast modes.
 *
 * @param attention Backend-computed attention; absent from older APIs.
 * @param detailed Also lists the localized reasons and the estimated date.
 */
export function AttentionBadge({
  attention,
  detailed = false,
}: {
  attention: LotAttention | undefined;
  detailed?: boolean;
}) {
  const { t, locale } = useProductionI18n();
  if (!attention || attention.state === "Normal") return null;
  const label = t(`inventory.attention.state.${attention.state}`);
  const useBy = attention.estimatedUseBy
    ? formatUseBy(attention.estimatedUseBy, attention.state, locale, t)
    : null;
  return (
    <div data-testid="inventory-attention" className="space-y-1">
      <span
        data-attention-state={attention.state}
        className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold ${TONE[attention.state] ?? TONE.EstimateUncertain}`}
      >
        {label}
        {useBy ? ` · ${useBy}` : ""}
      </span>
      {detailed && (
        <ul
          data-testid="inventory-attention-reasons"
          className="list-disc pl-5 text-xs text-muted-foreground"
        >
          {attention.reasons.map((reason) => {
            const key = `inventory.attention.reason.${reason}`;
            const text = t(key);
            // Unknown codes from a newer backend are not shown raw.
            return text === key ? null : <li key={reason}>{text}</li>;
          })}
        </ul>
      )}
    </div>
  );
}
