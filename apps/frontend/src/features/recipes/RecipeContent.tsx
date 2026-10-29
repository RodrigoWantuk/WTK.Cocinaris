import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { formatLocaleDecimal } from "@/lib/localeDecimal";
import type {
  ParsedIngredient,
  ParsedNormalizedRecipe,
} from "./parseNormalizedRecipe";

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
 * Ingredients, equipment, and stages of a saved recipe revision. With `checkable`, every
 * ingredient and stage gets a checkbox for the cooking view; the parent owns the checked state.
 *
 * @param recipe Parsed revision content.
 * @param checkable Show checkboxes (cooking view).
 * @param checked Checked ingredient and stage keys (`i:<index>`, `s:<index>`).
 * @param onToggle Called with a key when its checkbox changes.
 */
export function RecipeContent({
  recipe,
  checkable = false,
  checked,
  onToggle,
}: {
  recipe: ParsedNormalizedRecipe;
  checkable?: boolean;
  checked?: ReadonlySet<string>;
  onToggle?: (key: string) => void;
}) {
  const { t } = useProductionI18n();
  const quantity = useRecipeQuantity();
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
            className={`mt-2 space-y-1 text-sm ${checkable ? "" : "list-disc pl-5"}`}
          >
            {recipe.ingredients.map((ingredient, index) => {
              const amount = quantity(ingredient);
              const notes = [
                ingredient.optional ? t("recipes.detail.optional") : null,
                ingredient.sourceType === "additional"
                  ? t("recipes.detail.notInPantry")
                  : null,
              ].filter(Boolean);
              const label = `${ingredient.displayName}${amount ? ` — ${amount}` : ""}`;
              return (
                <li
                  key={
                    ingredient.ingredientId ??
                    `${ingredient.displayName}-${index}`
                  }
                  data-testid="recipe-ingredient"
                  className={checkable ? "flex gap-2" : undefined}
                >
                  {box(`i:${index}`, label)}
                  <span>
                    <span className="font-medium">
                      {ingredient.displayName}
                    </span>
                    {amount ? ` — ${amount}` : ""}
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
                  {stage.activeMinutes !== undefined ||
                  stage.passiveMinutes !== undefined ? (
                    <p className="text-xs text-muted-foreground">
                      {t("recipes.detail.stageTime", {
                        active: stage.activeMinutes ?? 0,
                        passive: stage.passiveMinutes ?? 0,
                      })}
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
