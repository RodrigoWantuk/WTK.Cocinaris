import type { LotAttention } from "@/adapters/live/inventoryTypes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { formatCalendarDateForDisplay } from "@/lib/calendarDate";

const TONE: Record<string, string> = {
  LikelyDiscard: "bg-destructive/15 text-destructive",
  CheckCondition: "bg-destructive/10 text-destructive",
  UsePriority: "bg-warning/20 text-warning-foreground",
  UseSoon: "bg-warning/10 text-warning-foreground",
  EstimateUncertain: "bg-muted text-muted-foreground",
};

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
  return (
    <div data-testid="inventory-attention" className="space-y-1">
      <span
        data-attention-state={attention.state}
        className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-medium ${TONE[attention.state] ?? TONE.EstimateUncertain}`}
      >
        {label}
        {attention.estimatedUseBy
          ? ` · ${t("inventory.attention.useBy").replace("{date}", formatCalendarDateForDisplay(attention.estimatedUseBy, locale))}`
          : ""}
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
