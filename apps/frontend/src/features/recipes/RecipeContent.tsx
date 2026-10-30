import {
  CheckCircle2,
  Circle,
  CircleDot,
  CookingPot,
  Hourglass,
  Timer,
} from "lucide-react";
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
  // "Você tem 3 de 5" over the ingredients the pantry can answer for (basics excluded).
  const counted = recipe.ingredients.filter(
    (ingredient) =>
      ingredient.sourceType !== "assumption" &&
      ingredient.ingredientId &&
      pantry.has(ingredient.ingredientId),
  );
  const pantrySummary =
    !checkable && availability && counted.length > 0
      ? t("recipes.detail.pantrySummary", {
          have: counted.filter(
            (ingredient) =>
              pantry.get(ingredient.ingredientId!)?.status === "available",
          ).length,
          total: counted.length,
        })
      : null;
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
        <section
          aria-labelledby="recipe-content-ingredients"
          className={checkable ? "" : "surface p-4 sm:p-5"}
        >
          <div className="flex items-baseline justify-between gap-2">
            <h2
              id="recipe-content-ingredients"
              className="font-display text-xl"
            >
              {t("recipes.detail.ingredients")}
            </h2>
            {pantrySummary ? (
              <p
                data-testid="recipe-pantry-summary"
                className="text-sm font-medium text-muted-foreground"
              >
                {pantrySummary}
              </p>
            ) : null}
          </div>
          <ul
            className={
              checkable
                ? "mt-2 space-y-1 text-sm"
                : "mt-3 divide-y divide-border"
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
                const StatusIcon =
                  status?.tone === "success"
                    ? CheckCircle2
                    : status?.tone === "warning"
                      ? CircleDot
                      : Circle;
                return (
                  <li
                    key={key}
                    data-testid="recipe-ingredient"
                    className="flex items-start gap-3 py-3"
                  >
                    {availability ? (
                      <StatusIcon
                        aria-hidden="true"
                        className={`mt-0.5 h-5 w-5 shrink-0 ${
                          status?.tone === "success"
                            ? "text-success"
                            : status?.tone === "warning"
                              ? "text-warning"
                              : "text-muted-foreground/60"
                        }`}
                      />
                    ) : null}
                    <span className="min-w-0 flex-1">
                      <span className="text-[16px] font-semibold">
                        {ingredient.displayName}
                      </span>
                      {notes.length > 0 ? (
                        <span className="block text-sm text-muted-foreground">
                          {notes.join(", ")}
                        </span>
                      ) : null}
                      {kitchen ? (
                        <span
                          data-testid="recipe-ingredient-household"
                          className="block text-sm text-muted-foreground"
                        >
                          {kitchen}
                        </span>
                      ) : null}
                      {status ? (
                        <span
                          data-testid="recipe-ingredient-pantry"
                          className={`block text-sm ${
                            status.tone === "success"
                              ? "text-success"
                              : status.tone === "warning"
                                ? "font-medium text-warning"
                                : "text-muted-foreground"
                          }`}
                        >
                          {status.text}
                        </span>
                      ) : null}
                    </span>
                    <span className="tabular shrink-0 text-right font-medium">
                      {amount}
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
          <ul className="mt-3 flex flex-wrap gap-2">
            {recipe.equipment.map((item) => (
              <li
                key={item}
                className="inline-flex items-center gap-1.5 rounded-full border border-border bg-card px-3 py-1 text-sm"
              >
                <CookingPot
                  aria-hidden="true"
                  className="h-4 w-4 text-muted-foreground"
                />
                {item}
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {recipe.stages.length > 0 ? (
        <section aria-labelledby="recipe-content-stages">
          <h2 id="recipe-content-stages" className="font-display text-xl">
            {t("recipes.detail.stages")}
          </h2>
          {/* Reading view: a numbered timeline, one card per stage (PLAN-0058); the cooking
              view keeps the compact list with checkboxes. */}
          <ol className={checkable ? "mt-3 space-y-5" : "mt-4 space-y-4"}>
            {recipe.stages.map((stage, index) => (
              <li
                key={stage.stageId ?? `${stage.title}-${index}`}
                data-testid="recipe-stage"
                className={checkable ? "flex gap-3" : "relative flex gap-4"}
              >
                {checkable ? (
                  box(`s:${index}`, stage.title || String(index + 1))
                ) : (
                  <span
                    aria-hidden="true"
                    className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary text-sm font-bold text-primary-foreground"
                  >
                    {index + 1}
                  </span>
                )}
                <div
                  className={
                    checkable
                      ? "space-y-1"
                      : "surface min-w-0 flex-1 space-y-2 p-4"
                  }
                >
                  <h3 className={checkable ? "font-medium" : "font-semibold"}>
                    {checkable ? `${index + 1}. ` : ""}
                    {stage.title}
                  </h3>
                  {stage.activeMinutes || stage.passiveMinutes ? (
                    <p className="flex flex-wrap gap-x-3 gap-y-1 text-xs font-medium text-muted-foreground">
                      {/* Zero minutes are left out (review M8). */}
                      {stage.activeMinutes ? (
                        <span className="inline-flex items-center gap-1">
                          <Timer aria-hidden="true" className="h-3.5 w-3.5" />
                          {t("recipes.detail.stageActive", {
                            minutes: stage.activeMinutes,
                          })}
                        </span>
                      ) : null}
                      {stage.passiveMinutes ? (
                        <span className="inline-flex items-center gap-1">
                          <Hourglass
                            aria-hidden="true"
                            className="h-3.5 w-3.5"
                          />
                          {t("recipes.detail.stagePassive", {
                            minutes: stage.passiveMinutes,
                          })}
                        </span>
                      ) : null}
                    </p>
                  ) : null}
                  {stage.instructions.map((instruction, stepIndex) => (
                    <p key={stepIndex} className="text-[15px] leading-relaxed">
                      {instruction}
                    </p>
                  ))}
                  {stage.sensoryCues.length > 0 ? (
                    <p
                      className={
                        checkable
                          ? "text-sm text-muted-foreground"
                          : "rounded-xl bg-accent px-3 py-2 text-sm text-accent-foreground"
                      }
                    >
                      <span className="font-semibold">
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
