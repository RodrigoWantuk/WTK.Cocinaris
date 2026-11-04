import type { ShoppingItem } from "@/contracts/planning";

/** Store sections in the order of a typical shopping trip. */
const SECTIONS = [
  "produce",
  "butcher_fish",
  "dairy_eggs",
  "bakery",
  "dry_goods",
  "drinks",
  "other",
];
const TO_BUY = new Set(["required", "assumed", "optional"]);
const UNIT_KEYS: Record<string, string> = {
  Gram: "recipes.unit.g",
  Milliliter: "recipes.unit.ml",
  Unit: "recipes.unit.unit",
};

/**
 * Plain text of a shopping list for sharing (PLAN-0062): lines still to buy, grouped by store
 * section. It holds nothing else, so sharing reveals no pantry, plan, or restriction data.
 *
 * @param items Shopping lines.
 * @param t Translation function.
 * @param formatNumber Formats a quantity for the interface locale.
 */
export function buildShoppingListText(
  items: ShoppingItem[],
  t: (key: string, values?: Record<string, string | number>) => string,
  formatNumber: (value: number) => string,
): string {
  const lines = [t("shopping.title")];
  for (const section of SECTIONS) {
    const group = items.filter(
      (item) =>
        TO_BUY.has(item.category) &&
        !item.checked &&
        (item.storeSection ?? "other") === section,
    );
    if (group.length === 0) continue;
    lines.push("", t(`shopping.section.${section}`));
    for (const item of group) {
      const unit = t(UNIT_KEYS[item.unit] ?? "recipes.unit.unit");
      lines.push(
        `- ${item.displayName}: ${formatNumber(item.toBuyQuantity)} ${unit}`,
      );
    }
  }
  return lines.join("\n");
}
