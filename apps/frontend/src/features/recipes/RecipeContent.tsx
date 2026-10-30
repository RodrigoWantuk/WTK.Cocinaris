import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import {
  formatLocaleDecimal,
  type SupportedDecimalLocale,
} from "@/lib/localeDecimal";
import type { RecipeAvailabilityLine } from "@/contracts/recipes";
import {
  formatKitchenAmount,
  householdMeasure,
  type MeasurePart,
} from "@/lib/kitchenMeasures";
import type {
  ParsedIngredient,
  ParsedNormalizedRecipe,
} from "./parseNormalizedRecipe";

/** Unit labels for pantry quantities (lot units). */
const PANTRY_UNIT_KEYS: Record<string, string> = {
  Gram: "recipes.unit.g",
  Milliliter: "recipes.unit.ml",
  Unit: "recipes.unit.unit",
};

/** Formats a recipe quantity in the recipe's canonical unit (`g`, `ml`, `unit`). */
export function useRecipeQuantity() {
  const { t, locale } = useProductionI18n();
  return (ingredient: Pick<ParsedIngredient, "quantity" | "unit">) => {
    if (ingredient.quantity === undefined) return "";
    const unit = ingredient.unit ?? "";
    const label = ["g", "ml", "unit"].includes(unit)
      ? t(`recipes.unit.${unit}`)
      : unit;
    return `${formatLocaleDecimal(ingredient.quantity, locale)} ${label}`.trim();
  };
}

/**
 * The household measure that accompanies a metric amount ("≈ 1 xícara", "2 colheres de sopa"),
 * or an empty string when no compatible measure exists (see `lib/kitchenMeasures`).
 */
export function useHouseholdMeasure() {
  const { t, locale } = useProductionI18n();
  const part = ({ unit, amount }: MeasurePart) => {
    if (unit === "pinch") return t("measure.pinch");
    const n =
      unit === "g"
        ? formatLocaleDecimal(amount, locale)
        : formatKitchenAmount(amount);
    if (unit === "g") return t("measure.g", { n });
    return t(`measure.${unit}.${amount <= 1 ? "one" : "many"}`, { n });
  };
  return (
    ingredient: Pick<ParsedIngredient, "displayName" | "quantity" | "unit">,
  ) => {
    if (ingredient.quantity === undefined || !ingredient.unit) return "";
    const measure = householdMeasure(
      ingredient.displayName,
      ingredient.quantity,
      ingredient.unit,
    );
    if (!measure) return "";
    const text = measure.parts.map(part).join(t("measure.join"));
    return measure.approximate ? t("measure.approx", { measure: text }) : text;
  };
}

/**
 * Ingredients, equipment, and stages of a saved recipe revision. With `checkable`, every
 * ingredient and stage gets a checkbox for the cooking view; the parent owns the checked state.
 *
 * @param recipe Parsed revision content.
 * @param checkable Show checkboxes (cooking view).
 * @param checked Checked ingredient and stage keys (`i:<index>`, `s:<index>`).
 * @param onToggle Called with a key when its checkbox changes.
 * @param availability What the pantry covers per ingredient (PLAN-0057); no pantry status is
 *   shown while it is null or absent. Basics the recipe assumes never get a status.
 */
export function RecipeContent({
  recipe,
  checkable = false,
  checked,
  onToggle,
  availability,
}: {
  recipe: ParsedNormalizedRecipe;
  checkable?: boolean;
  checked?: ReadonlySet<string>;
  onToggle?: (key: string) => void;
  availability?: RecipeAvailabilityLine[] | null;
}) {
  const { t, locale } = useProductionI18n();
  const quantity = useRecipeQuantity();
  const household = useHouseholdMeasure();
  const pantry = new Map(
    (availability ?? []).map((line) => [line.ingredientId, line]),
  );
  const pantryStatus = (
    ingredientId: string | undefined,
    sourceType?: string,
  ) => {
    const line = ingredientId ? pantry.get(ingredientId) : undefined;
    if (!line || sourceType === "assumption") return null;
    if (line.status === "available")
      return { tone: "success", text: t("recipes.detail.inPantry") };
    if (line.status === "partial")
      return {
        tone: "warning",
        text: t("recipes.detail.partialPantry", {
          amount: `${formatLocaleDecimal(line.missingQuantity, locale as SupportedDecimalLocale)} ${t(PANTRY_UNIT_KEYS[line.unit] ?? "recipes.unit.unit")}`,
        }),
      };
    return { tone: "muted", text: t("recipes.detail.notInPantry") };
  };
  const box = (key: string, label: string) =>
    checkable ? (
      <input
        type="checkbox"
        className="mt-1 h-4 w-4 shrink-0"
        aria-label={label}
        checked={checked?.has(key) ?? false}
        onChange={() => onToggle?.(key)}
      />
    ) : null;

  return (
    <div className="space-y-6">
      {recipe.ingredients.length > 0 ? (
        <section aria-labelledby="recipe-content-ingredients">
          <h2 id="recipe-content-ingredients" className="font-display text-xl">
            {t("recipes.detail.ingredients")}
          </h2>
          <ul
            className={
              checkable
                ? "mt-2 space-y-1 text-sm"
                : "mt-2 divide-y divide-border border-y border-border"
            }
          >
            {recipe.ingredients.map((ingredient, index) => {
              const amount = quantity(ingredient);
              const kitchen = household(ingredient);
              const notes = [
                ingredient.optional ? t("recipes.detail.optional") : null,
              ].filter(Boolean);
              const status = checkable
                ? null
                : pantryStatus(ingredient.ingredientId, ingredient.sourceType);
              const label = `${ingredient.displayName}${amount ? ` — ${amount}` : ""}`;
              const key =
                ingredient.ingredientId ?? `${ingredient.displayName}-${index}`;
              if (!checkable) {
                // Reading view (PLAN-0054 design system): one row per ingredient, the name on
                // the left and both measures on the right.
                return (
                  <li
                    key={key}
                    data-testid="recipe-ingredient"
                    className="flex items-baseline justify-between gap-4 py-3"
                  >
                    <span>
                      <span className="text-[17px] font-semibold">
                        {ingredient.displayName}
                      </span>
                      {notes.length > 0 ? (
                        <span className="block text-sm text-muted-foreground">
                          {notes.join(", ")}
                        </span>
                      ) : null}
                      {status ? (
                        <span className="mt-1 block">
                          <span
                            data-testid="recipe-ingredient-pantry"
                            className={
                              status.tone === "success"
                                ? "status-pill status-success"
                                : status.tone === "warning"
                                  ? "status-pill status-warning"
                                  : "text-sm text-muted-foreground"
                            }
                          >
                            {status.text}
                          </span>
                        </span>
                      ) : null}
                    </span>
                    <span className="tabular shrink-0 text-right">
                      <span className="block">{amount}</span>
                      {kitchen ? (
                        <span
                          data-testid="recipe-ingredient-household"
                          className="block text-sm text-muted-foreground"
                        >
                          {kitchen}
                        </span>
                      ) : null}
                    </span>
                  </li>
                );
              }
              return (
                <li
                  key={key}
                  data-testid="recipe-ingredient"
                  className="flex gap-2"
                >
                  {box(`i:${index}`, label)}
                  <span>
                    <span className="font-medium">
                      {ingredient.displayName}
                    </span>
                    {amount ? ` — ${amount}` : ""}
                    {kitchen ? (
                      <span
                        data-testid="recipe-ingredient-household"
                        className="text-muted-foreground"
                      >
                        {` · ${kitchen}`}
                      </span>
                    ) : null}
                    {notes.length > 0 ? (
                      <span className="text-muted-foreground">
                        {" "}
                        ({notes.join(", ")})
                      </span>
                    ) : null}
                  </span>
                </li>
              );
            })}
          </ul>
        </section>
      ) : null}

      {recipe.equipment.length > 0 ? (
        <section aria-labelledby="recipe-content-equipment">
          <h2 id="recipe-content-equipment" className="font-display text-xl">
            {t("recipes.detail.equipment")}
          </h2>
          <p className="mt-2 text-sm">{recipe.equipment.join(", ")}</p>
        </section>
      ) : null}

      {recipe.stages.length > 0 ? (
        <section aria-labelledby="recipe-content-stages">
          <h2 id="recipe-content-stages" className="font-display text-xl">
            {t("recipes.detail.stages")}
          </h2>
          <ol className="mt-3 space-y-5">
            {recipe.stages.map((stage, index) => (
              <li
                key={stage.stageId ?? `${stage.title}-${index}`}
                data-testid="recipe-stage"
                className="flex gap-3"
              >
                {box(`s:${index}`, stage.title || String(index + 1))}
                <div className="space-y-1">
                  <h3 className="font-medium">
                    {index + 1}. {stage.title}
                  </h3>
                  {stage.activeMinutes || stage.passiveMinutes ? (
                    <p className="text-xs text-muted-foreground">
                      {/* Zero minutes are left out (review M8). */}
                      {[
                        stage.activeMinutes
                          ? t("recipes.detail.stageActive", {
                              minutes: stage.activeMinutes,
                            })
                          : null,
                        stage.passiveMinutes
                          ? t("recipes.detail.stagePassive", {
                              minutes: stage.passiveMinutes,
                            })
                          : null,
                      ]
                        .filter(Boolean)
                        .join(" · ")}
                    </p>
                  ) : null}
                  {stage.instructions.map((instruction, stepIndex) => (
                    <p key={stepIndex} className="text-sm leading-relaxed">
                      {instruction}
                    </p>
                  ))}
                  {stage.sensoryCues.length > 0 ? (
                    <p className="text-sm text-muted-foreground">
                      <span className="font-medium">
                        {t("recipes.detail.cues")}:
                      </span>{" "}
                      {stage.sensoryCues.join("; ")}
                    </p>
                  ) : null}
                </div>
              </li>
            ))}
          </ol>
        </section>
      ) : null}

      {recipe.assumptions.length > 0 ? (
        <section aria-labelledby="recipe-content-assumptions">
          <h2 id="recipe-content-assumptions" className="font-display text-xl">
            {t("recipes.detail.assumptions")}
          </h2>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted-foreground">
            {recipe.assumptions.map((assumption) => (
              <li key={assumption}>{assumption}</li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
}
