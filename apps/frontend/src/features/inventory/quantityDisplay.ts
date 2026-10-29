import type { InventoryQuantity } from "@/adapters/live/inventoryTypes";
import {
  formatLocaleDecimal,
  type SupportedDecimalLocale,
} from "@/lib/localeDecimal";

/**
 * Localized quantity text. An estimated amount is prefixed with "~" so an approximation is never
 * shown as a precise figure (PLAN-0054 Phase 2).
 */
export function formatQuantityLabel(
  quantity: InventoryQuantity,
  locale: SupportedDecimalLocale,
  t: (key: string) => string,
): string {
  if (quantity.kind === "measured") {
    const prefix = quantity.estimated ? "~" : "";
    return `${prefix}${formatLocaleDecimal(quantity.value, locale)} ${t(`inventory.unit.${quantity.unit}`)}`;
  }
  return t(`inventory.availability.${quantity.availability}`);
}

/** Share of a package each approximate choice stands for. */
export const PACKAGE_FRACTIONS = { full: 1, half: 0.5, little: 0.25 } as const;

/** An approximate choice: the whole package, about half, or a little. */
export type PackageFraction = keyof typeof PACKAGE_FRACTIONS;

/**
 * Estimated amount for a share of a package. Counts round to whole units (at least one), since
 * half a box of six eggs is three eggs. Mass and volume keep three decimals, the API's precision.
 */
export function estimateFromPackage(
  packageSize: number,
  unit: "Gram" | "Milliliter" | "Unit",
  fraction: PackageFraction,
): number {
  const raw = packageSize * PACKAGE_FRACTIONS[fraction];
  if (unit === "Unit") return Math.max(1, Math.round(raw));
  return Math.round(raw * 1000) / 1000;
}
