import { useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { ChoiceChips } from "@/components/choice/ChoiceChips";
import { Button } from "@/components/ui/button";
import type {
  ShoppingPlan,
  ShoppingPlanAccepted,
} from "@/contracts/shoppingPlan";
import { daysBetween, formatMenuDay } from "@/features/planning/planningDates";
import { BottomSheet } from "./BottomSheet";
import { Counter } from "./Counter";
import { restrictionLabel } from "./restrictionLabel";

/** One cooking makes at most this many portions; the server enforces the same limit. */
export const MAX_PORTIONS = 24;
const MAX_MEALS = 4;
const MAX_PEOPLE = 12;

/** A higher count for one meal type, asked for before a dish may cover more than was wanted. */
export interface MealRaise {
  mealType: string;
  count: number;
}

/**
 * How many more meals a dish of this type can cover before passing what the menu wants. Lunch and
 * dinner share their room, as the server counts them.
 */
export function roomFor(
  plan: ShoppingPlan,
  mealType: string,
): { room: number; wanted: number } {
  const main = mealType === "lunch" || mealType === "dinner";
  const groups = plan.groups.filter((group) =>
    main
      ? group.mealType === "lunch" || group.mealType === "dinner"
      : group.mealType === mealType,
  );
  const own = plan.groups.find((group) => group.mealType === mealType);
  return {
    room: groups.reduce((sum, group) => sum + group.wanted - group.filled, 0),
    wanted: own?.wanted ?? 0,
  };
}

/**
 * Actions on a chosen dish (PLAN-0065 Slice 3): see the recipe, swap it, give it a day, make it cover
 * more meals, change its people, retry a failed recipe, or remove it. Covering more meals than the
 * menu wants asks first and raises the count only on confirmation. Every change is one explicit
 * command on this dish and leaves the others as they are.
 *
 * @param people The dish's people as shown, which may run ahead of the server while a change waits.
 * @param busy Disables the actions while one is being saved.
 * @param notice Shown at the top, such as an error.
 */
export function DishActionsSheet({
  plan,
  dish,
  open,
  onOpenChange,
  people,
  busy,
  canRetry,
  notice,
  actions,
}: {
  plan: ShoppingPlan;
  dish: ShoppingPlanAccepted | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  people: number;
  busy: boolean;
  canRetry: boolean;
  notice?: ReactNode;
  actions: {
    swap: (dish: ShoppingPlanAccepted) => void;
    setDay: (dish: ShoppingPlanAccepted, day: string | null) => void;
    setYield: (
      dish: ShoppingPlanAccepted,
      meals: number,
      raise?: MealRaise,
    ) => void;
    setPeople: (dish: ShoppingPlanAccepted, people: number) => void;
    retry: (dish: ShoppingPlanAccepted) => void;
    remove: (dish: ShoppingPlanAccepted) => void;
  };
}) {
  const { t, locale } = useProductionI18n();
  const [raise, setRaise] = useState<{
    meals: number;
    raise: MealRaise;
    wanted: number;
  } | null>(null);
  const start = plan.setup.startDate;
  const end = plan.setup.endDate;

  const chooseMeals = (current: ShoppingPlanAccepted, meals: number) => {
    const extra = meals - current.repeats;
    const { room, wanted } = roomFor(plan, current.mealType);
    if (extra > room) {
      // It would pass what was asked for, so the count goes up only if the person says so.
      setRaise({
        meals,
        wanted,
        raise: { mealType: current.mealType, count: wanted + extra - room },
      });
      return;
    }
    setRaise(null);
    actions.setYield(current, meals);
  };

  const maxMeals = Math.min(MAX_MEALS, Math.floor(MAX_PORTIONS / people));
  const yields = dish ? dish.mode !== "again" : false;
  const coverage = dish && yields ? dish.repeats : 1;
  const maxPeople = Math.min(
    MAX_PEOPLE,
    Math.floor(MAX_PORTIONS / Math.max(1, coverage)),
  );

  return (
    <BottomSheet
      open={open}
      onOpenChange={(next) => {
        if (!next) setRaise(null);
        onOpenChange(next);
      }}
      testId="board-dish-sheet"
      closeLabel={t("menuBuild.sheet.close")}
      title={dish?.name ?? ""}
    >
      {dish ? (
        <div className="space-y-5">
          {notice}
          {dish.guardCode ? (
            <p className="text-sm text-muted-foreground">
              {t("shoppingPlan.card.guard", {
                ingredient:
                  dish.guardIngredient ?? t("shoppingPlan.card.guardAny"),
                restriction: restrictionLabel(locale, dish.guardCode),
              })}
            </p>
          ) : null}
          <div className="grid grid-cols-2 gap-2">
            {dish.recipeId ? (
              <Button asChild variant="secondary">
                <Link
                  to={`/app/receitas/${dish.recipeId}`}
                  data-testid="board-dish-recipe"
                >
                  {t("menuBuild.dish.recipe")}
                </Link>
              </Button>
            ) : null}
            <Button
              type="button"
              variant="secondary"
              data-testid="board-dish-swap"
              disabled={busy}
              onClick={() => actions.swap(dish)}
            >
              {t("menuBuild.dish.swap")}
            </Button>
          </div>

          {canRetry ? (
            <Button
              type="button"
              variant="secondary"
              className="w-full"
              data-testid="board-dish-retry"
              disabled={busy}
              onClick={() => actions.retry(dish)}
            >
              {t("shoppingPlan.review.retry")}
            </Button>
          ) : null}

          {start && end ? (
            <ChoiceChips
              label={t("menuBuild.dish.day")}
              testId="board-dish-day"
              disabled={busy}
              value={dish.plannedDate ?? "none"}
              options={[
                { value: "none", label: t("menuBuild.dish.day.none") },
                ...daysBetween(start, end).map((day) => ({
                  value: day,
                  label: formatMenuDay(day, locale),
                })),
              ]}
              onChange={(value) =>
                actions.setDay(dish, value === "none" ? null : value)
              }
            />
          ) : null}

          {yields ? (
            <div className="space-y-2">
              <ChoiceChips
                label={t("menuBuild.dish.yield")}
                testId="board-dish-yield"
                disabled={busy}
                value={String(raise?.meals ?? dish.repeats)}
                options={Array.from(
                  { length: Math.max(1, maxMeals) },
                  (_, index) => ({
                    value: String(index + 1),
                    label: String(index + 1),
                  }),
                )}
                onChange={(value) => chooseMeals(dish, Number(value))}
              />
              {raise ? (
                <div
                  role="alert"
                  data-testid="board-dish-raise"
                  className="space-y-2 rounded-xl bg-secondary p-3 text-sm"
                >
                  <p>
                    {t("menuBuild.dish.yield.raise", {
                      wanted: raise.wanted,
                      next: raise.raise.count,
                    })}
                  </p>
                  <Button
                    type="button"
                    size="sm"
                    data-testid="board-dish-raise-confirm"
                    disabled={busy}
                    onClick={() => {
                      actions.setYield(dish, raise.meals, raise.raise);
                      setRaise(null);
                    }}
                  >
                    {t("menuBuild.dish.yield.raise.action", {
                      next: raise.raise.count,
                    })}
                  </Button>
                </div>
              ) : null}
            </div>
          ) : null}

          <div className="space-y-1">
            <Counter
              label={t("menuBuild.dish.people")}
              value={people}
              min={1}
              max={maxPeople}
              onChange={(next) => actions.setPeople(dish, next)}
              testId="board-dish-people"
            />
            {people >= maxPeople && maxPeople < MAX_PEOPLE ? (
              <p className="text-sm text-muted-foreground">
                {t("menuBuild.dish.limit")}
              </p>
            ) : null}
          </div>

          <Button
            type="button"
            variant="ghost"
            className="w-full text-destructive"
            data-testid="board-dish-remove"
            disabled={busy}
            onClick={() => actions.remove(dish)}
          >
            {t("shoppingPlan.review.remove")}
          </Button>
        </div>
      ) : null}
    </BottomSheet>
  );
}
