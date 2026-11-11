import { useEffect, useState } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { ChoiceChips } from "@/components/choice/ChoiceChips";
import { Button } from "@/components/ui/button";
import type {
  RepeatMode,
  ShoppingPlanAccepted,
} from "@/contracts/shoppingPlan";
import { Counter } from "./Counter";

/** Meals one cooking covers at most; the server enforces the same limit. */
export const MAX_YIELD_MEALS = 4;
/** Separate cookings of one dish at most; the server enforces the same limit. */
export const MAX_COOKINGS = 14;
/** Portions one cooking makes at most; the server enforces the same limit. */
export const MAX_PORTIONS = 24;

const MAIN_MEALS = new Set(["lunch", "dinner"]);

/**
 * The most a dish can fill with the given mode, before what its meal type still wants.
 *
 * @param mode How the repeats are cooked.
 * @param people The dish's people; one cooking makes at most {@link MAX_PORTIONS} portions.
 */
export function repeatLimit(mode: RepeatMode, people: number): number {
  return mode === "again"
    ? MAX_COOKINGS
    : Math.min(MAX_YIELD_MEALS, Math.floor(MAX_PORTIONS / Math.max(1, people)));
}

/**
 * The mode offered first. A dish already repeated keeps its mode. Otherwise lunch and dinner are made
 * once for more meals, the way leftovers usually work, while snacks and breakfasts are made again,
 * because they are usually eaten fresh. Nothing per dish says how well it keeps, so this is only the
 * first choice shown; the person can switch.
 */
export function defaultRepeatMode(
  dish: ShoppingPlanAccepted,
  people: number,
): RepeatMode {
  if (dish.mode === "yield" || dish.mode === "again") return dish.mode;
  return MAIN_MEALS.has(dish.mealType) &&
    repeatLimit("yield", people) > dish.repeats
    ? "yield"
    : "again";
}

/**
 * "Repetir" (PLAN-0065 Slice 6): fills more of a meal type with the dish chosen last, instead of
 * choosing another. The person picks how it is cooked and how many more times, and one tap saves the
 * whole new state.
 *
 * @param dish The dish chosen last in this meal type.
 * @param people The dish's people, for the portion limit of one cooking.
 * @param missing Meals the meal type still wants; a repeat never passes it.
 * @param busy Disables the action while a choice is being saved.
 * @param onRepeat Receives the new whole state: the mode and the meals the dish fills in all.
 */
export function RepeatPanel({
  dish,
  people,
  missing,
  busy,
  onRepeat,
}: {
  dish: ShoppingPlanAccepted;
  people: number;
  missing: number;
  busy: boolean;
  onRepeat: (mode: RepeatMode, meals: number) => void;
}) {
  const { t } = useProductionI18n();
  const [mode, setMode] = useState<RepeatMode>(() =>
    defaultRepeatMode(dish, people),
  );
  const [extra, setExtra] = useState(1);

  // A new dish, or the same one after a repeat, starts again from one more.
  useEffect(() => {
    setMode(defaultRepeatMode(dish, people));
    setExtra(1);
    // Mode and repeats are what change after a save; the rest of the dish does not matter here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dish.cardId, dish.repeats, dish.mode]);

  const most = Math.min(missing, repeatLimit(mode, people) - dish.repeats);
  const count = Math.min(extra, Math.max(1, most));

  return (
    <section
      aria-labelledby={`repeat-${dish.cardId}`}
      data-testid="meal-sheet-repeat"
      className="space-y-3 rounded-xl border border-border p-3"
    >
      <div>
        <h3 id={`repeat-${dish.cardId}`} className="font-semibold">
          {t("menuBuild.repeat.title", { dish: dish.name })}
        </h3>
        {dish.repeats > 1 ? (
          <p className="text-sm text-muted-foreground">
            {t(
              dish.mode === "again"
                ? "menuBuild.repeat.current.again"
                : "menuBuild.repeat.current.yield",
              { count: dish.repeats },
            )}
          </p>
        ) : null}
      </div>
      <ChoiceChips
        label={t("menuBuild.repeat.how")}
        hideLabel
        testId="meal-sheet-repeat-mode"
        disabled={busy}
        value={mode}
        options={[
          { value: "yield", label: t("menuBuild.repeat.mode.yield") },
          { value: "again", label: t("menuBuild.repeat.mode.again") },
        ]}
        onChange={(next) => {
          setMode(next);
          setExtra(1);
        }}
      />
      <p className="text-sm text-muted-foreground">
        {t(
          mode === "yield"
            ? "menuBuild.repeat.hint.yield"
            : "menuBuild.repeat.hint.again",
          { max: repeatLimit("yield", people) },
        )}
      </p>
      {most >= 1 ? (
        <div className="flex flex-wrap items-center justify-between gap-2">
          <Counter
            label={t("menuBuild.repeat.extra")}
            value={count}
            min={1}
            max={most}
            onChange={setExtra}
            testId="meal-sheet-repeat-count"
            disabled={busy}
          />
          <Button
            type="button"
            data-testid="meal-sheet-repeat-action"
            disabled={busy}
            onClick={() => onRepeat(mode, dish.repeats + count)}
          >
            {t(
              count === 1
                ? "menuBuild.repeat.action.one"
                : "menuBuild.repeat.action",
              { count },
            )}
          </Button>
        </div>
      ) : (
        <p className="text-sm" data-testid="meal-sheet-repeat-full">
          {mode === "yield"
            ? t("menuBuild.repeat.full.yield", {
                max: repeatLimit("yield", people),
              })
            : t("menuBuild.repeat.full.again", { max: MAX_COOKINGS })}
        </p>
      )}
    </section>
  );
}
