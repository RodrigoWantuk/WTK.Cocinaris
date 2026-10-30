import { useId } from "react";
import { Button } from "@/components/ui/button";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import type { SuggestionGuidance } from "@/contracts/recipes";

/** Meal kinds offered, in display order (codes match the API). */
export const MEAL_KINDS = ["full_meal", "light_meal", "snack"] as const;
/** Dish families offered (codes match the API). */
export const DISH_FAMILIES = [
  "rice",
  "pasta",
  "meat",
  "poultry",
  "fish_seafood",
  "eggs",
  "vegetarian",
  "soup_broth",
  "salad",
  "sandwich_wrap",
  "baked",
  "dessert",
] as const;
/** Cuisines offered (codes match the API). */
export const CUISINES = [
  "brazilian",
  "portuguese",
  "italian",
  "spanish",
  "french",
  "mediterranean",
  "middle_eastern",
  "indian",
  "chinese",
  "japanese",
  "mexican",
  "american",
] as const;
/** Time limits offered (codes match the API). */
export const TIMES = ["quick", "medium"] as const;
/** The API accepts at most this many dish families and required items. */
export const MAX_FAMILIES = 3;
export const MAX_REQUIRED = 5;

/** A pantry lot the person can require or exclude. */
export interface GuidanceLot {
  lotId: string;
  name: string;
  amount: string;
}

/** The empty guidance: the AI decides everything, as before PLAN-0056. */
export const EMPTY_GUIDANCE: SuggestionGuidance = {
  mealKind: null,
  dishFamilies: [],
  cuisine: null,
  time: null,
  requiredLotIds: [],
  excludedLotIds: [],
};

/** Number of choices made, shown on the collapsed panel. */
export function guidanceCount(value: SuggestionGuidance): number {
  return (
    (value.mealKind ? 1 : 0) +
    value.dishFamilies.length +
    (value.cuisine ? 1 : 0) +
    (value.time ? 1 : 0) +
    value.requiredLotIds.length +
    value.excludedLotIds.length
  );
}

/**
 * Optional direction for the three suggestions (PLAN-0056 Phase 2, owner reports 3 and 6):
 * meal kind, up to three dish families, a cuisine, a time limit, and, per pantry item, "must use",
 * "may use" (default), or "don't use". Excluded items are never sent to the AI; required items must
 * appear in every candidate (the backend enforces it).
 *
 * Meal kind and time are visible chips (the quick choices); dish families, cuisine, and pantry
 * items sit in a native `<details>` ("Mais opções") so the page stays short.
 * Toggle chips expose `aria-pressed`; each pantry item is a labelled radio group.
 *
 * @param lots Measured pantry lots (the only ones the AI sees).
 * @param value Current guidance.
 * @param onChange Receives the next guidance.
 * @param disabled Disables every control while a request runs.
 */
export function SuggestionGuidancePanel({
  lots,
  value,
  onChange,
  disabled = false,
}: {
  lots: GuidanceLot[];
  value: SuggestionGuidance;
  onChange: (next: SuggestionGuidance) => void;
  disabled?: boolean;
}) {
  const { t } = useProductionI18n();
  const groupId = useId();
  const count = guidanceCount(value);
  const required = new Set(value.requiredLotIds);
  const excluded = new Set(value.excludedLotIds);

  const toggleFamily = (code: string) =>
    onChange({
      ...value,
      dishFamilies: value.dishFamilies.includes(code)
        ? value.dishFamilies.filter((item) => item !== code)
        : [...value.dishFamilies, code],
    });

  const setLotChoice = (lotId: string, choice: "must" | "may" | "never") =>
    onChange({
      ...value,
      requiredLotIds:
        choice === "must"
          ? [...value.requiredLotIds.filter((id) => id !== lotId), lotId]
          : value.requiredLotIds.filter((id) => id !== lotId),
      excludedLotIds:
        choice === "never"
          ? [...value.excludedLotIds.filter((id) => id !== lotId), lotId]
          : value.excludedLotIds.filter((id) => id !== lotId),
    });

  const chip = (
    pressed: boolean,
    label: string,
    onClick: () => void,
    testId: string,
    blocked = false,
  ) => (
    <Button
      key={testId}
      type="button"
      size="sm"
      variant={pressed ? "default" : "secondary"}
      aria-pressed={pressed}
      disabled={disabled || (blocked && !pressed)}
      data-testid={testId}
      onClick={onClick}
    >
      {label}
    </Button>
  );

  return (
    <div className="space-y-5" data-testid="recipes-guidance">
      <p className="text-sm text-muted-foreground">
        {t("recipes.guidance.hint")}
      </p>
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">
          {t("recipes.guidance.mealKind")}
        </legend>
        <div className="flex flex-wrap gap-2">
          {MEAL_KINDS.map((code) =>
            chip(
              value.mealKind === code,
              t(`recipes.guidance.mealKind.${code}`),
              () =>
                onChange({
                  ...value,
                  mealKind: value.mealKind === code ? null : code,
                }),
              `recipes-guidance-meal-${code}`,
            ),
          )}
        </div>
      </fieldset>
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">
          {t("recipes.guidance.time")}
        </legend>
        <div className="flex flex-wrap gap-2">
          {TIMES.map((code) =>
            chip(
              value.time === code,
              t(`recipes.guidance.time.${code}`),
              () =>
                onChange({
                  ...value,
                  time: value.time === code ? null : code,
                }),
              `recipes-guidance-time-${code}`,
            ),
          )}
        </div>
      </fieldset>
      <details className="surface p-4">
        <summary className="cursor-pointer font-medium">
          {t("recipes.guidance.title")}
          {count > 0 ? (
            <span className="ml-2 text-sm text-muted-foreground">
              {t("recipes.guidance.count", { count })}
            </span>
          ) : null}
        </summary>
        <div className="mt-4 space-y-5">
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">
              {t("recipes.guidance.families", { max: MAX_FAMILIES })}
            </legend>
            <div className="flex flex-wrap gap-2">
              {DISH_FAMILIES.map((code) =>
                chip(
                  value.dishFamilies.includes(code),
                  t(`recipes.guidance.family.${code}`),
                  () => toggleFamily(code),
                  `recipes-guidance-family-${code}`,
                  value.dishFamilies.length >= MAX_FAMILIES,
                ),
              )}
            </div>
          </fieldset>

          <div className="space-y-2">
            <label
              htmlFor={`${groupId}-cuisine`}
              className="text-sm font-medium"
            >
              {t("recipes.guidance.cuisine")}
            </label>
            <select
              id={`${groupId}-cuisine`}
              className="block h-11 w-full rounded-xl border border-input bg-card px-3 text-[15px] focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
              value={value.cuisine ?? ""}
              disabled={disabled}
              data-testid="recipes-guidance-cuisine"
              onChange={(event) =>
                onChange({ ...value, cuisine: event.target.value || null })
              }
            >
              <option value="">{t("recipes.guidance.cuisine.any")}</option>
              {CUISINES.map((code) => (
                <option key={code} value={code}>
                  {t(`recipes.guidance.cuisine.${code}`)}
                </option>
              ))}
            </select>
          </div>

          {lots.length > 0 ? (
            <fieldset className="space-y-2">
              <legend className="text-sm font-medium">
                {t("recipes.guidance.pantry")}
              </legend>
              <p className="text-xs text-muted-foreground">
                {t("recipes.guidance.pantryHint", { max: MAX_REQUIRED })}
              </p>
              <ul className="divide-y divide-border">
                {lots.map((lot) => {
                  const choice = required.has(lot.lotId)
                    ? "must"
                    : excluded.has(lot.lotId)
                      ? "never"
                      : "may";
                  return (
                    <li
                      key={lot.lotId}
                      className="flex flex-wrap items-center justify-between gap-2 py-2"
                    >
                      <span className="min-w-0">
                        <span className="font-medium">{lot.name}</span>{" "}
                        <span className="text-xs text-muted-foreground">
                          {lot.amount}
                        </span>
                      </span>
                      <span
                        role="radiogroup"
                        aria-label={t("recipes.guidance.pantryItem", {
                          name: lot.name,
                        })}
                        className="flex gap-3 text-sm"
                        data-testid={`recipes-guidance-lot-${lot.lotId}`}
                      >
                        {(["must", "may", "never"] as const).map((option) => (
                          <label
                            key={option}
                            className="flex items-center gap-1"
                          >
                            <input
                              type="radio"
                              name={`${groupId}-${lot.lotId}`}
                              value={option}
                              checked={choice === option}
                              disabled={
                                disabled ||
                                (option === "must" &&
                                  choice !== "must" &&
                                  value.requiredLotIds.length >= MAX_REQUIRED)
                              }
                              onChange={() => setLotChoice(lot.lotId, option)}
                            />
                            {t(`recipes.guidance.lot.${option}`)}
                          </label>
                        ))}
                      </span>
                    </li>
                  );
                })}
              </ul>
            </fieldset>
          ) : null}

          {count > 0 ? (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={disabled}
              data-testid="recipes-guidance-clear"
              onClick={() => onChange(EMPTY_GUIDANCE)}
            >
              {t("recipes.guidance.clear")}
            </Button>
          ) : null}
        </div>
      </details>
    </div>
  );
}
