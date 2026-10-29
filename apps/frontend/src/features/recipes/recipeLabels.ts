/** Meal types the backend accepts, in display order. */
export const MEAL_TYPES = ["breakfast", "lunch", "dinner", "snack"] as const;

/** Localized meal type label; unknown values are shown as written. */
export function mealTypeLabel(
  t: (key: string) => string,
  mealType: string,
): string {
  return (MEAL_TYPES as readonly string[]).includes(mealType)
    ? t(`recipes.mealType.${mealType}`)
    : mealType;
}

/** Localized, comma-separated meal types. */
export function mealTypesLabel(
  t: (key: string) => string,
  mealTypes: readonly string[],
): string {
  return mealTypes.map((item) => mealTypeLabel(t, item)).join(", ");
}

/** Medium date in the interface locale; an unparsable value is shown as written. */
export function formatRecipeDate(iso: string, locale: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? iso
    : new Intl.DateTimeFormat(locale, { dateStyle: "medium" }).format(date);
}
