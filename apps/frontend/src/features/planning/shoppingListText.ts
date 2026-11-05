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

/** Places in the order of a typical trip (PLAN-0062). */
const PLACES = ["street_market", "butcher", "bakery", "market"];

/**
 * Plain text of a shopping list for sharing (PLAN-0062): lines still to buy, grouped by store section
 * or, when the person splits the list, by place. It holds nothing else, so sharing reveals no pantry,
 * plan, or restriction data. Lines left for later stay out.
 *
 * @param items Shopping lines.
 * @param t Translation function.
 * @param formatNumber Formats a quantity for the interface locale.
 * @param byPlace Whether to group by place.
 */
export function buildShoppingListText(
  items: ShoppingItem[],
  t: (key: string, values?: Record<string, string | number>) => string,
  formatNumber: (value: number) => string,
  byPlace = false,
): string {
  const lines = [t("shopping.title")];
  const toBuy = items.filter(
    (item) => TO_BUY.has(item.category) && !item.checked && !item.later,
  );
  const groups = byPlace
    ? PLACES.map((place) => ({
        title: t(`shopping.place.${place}`),
        group: toBuy.filter((item) => (item.place ?? "market") === place),
      }))
    : SECTIONS.map((section) => ({
        title: t(`shopping.section.${section}`),
        group: toBuy.filter(
          (item) => (item.storeSection ?? "other") === section,
        ),
      }));
  for (const { title, group } of groups) {
    if (group.length === 0) continue;
    lines.push("", title);
    for (const item of group) {
      const unit = t(UNIT_KEYS[item.unit] ?? "recipes.unit.unit");
      lines.push(
        `- ${item.displayName}: ${formatNumber(item.toBuyQuantity)} ${unit}`,
      );
    }
  }
  return lines.join("\n");
}
