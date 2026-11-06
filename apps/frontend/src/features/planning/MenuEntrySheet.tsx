import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { ChoiceChips } from "@/components/choice/ChoiceChips";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import type { Menu, MenuEntry } from "@/contracts/planning";
import { BottomSheet } from "@/features/shopping-plan/BottomSheet";
import { Counter } from "@/features/shopping-plan/Counter";
import { daysBetween, formatMenuDay } from "./planningDates";

const MAX_SERVINGS = 24;

/** What can be done to one dish of a menu; the page owns every request. */
export interface MenuEntryActions {
  cook: (entry: MenuEntry) => void;
  swap: (entry: MenuEntry) => void;
  setDay: (entry: MenuEntry, day: string | null) => void;
  setServings: (entry: MenuEntry, servings: number) => void;
  setReserve: (entry: MenuEntry, reserve: boolean) => void;
  toggleSkip: (entry: MenuEntry) => void;
  remove: (entry: MenuEntry) => void;
}

/**
 * Actions on one dish of a menu (PLAN-0065 Slice 4): cook it, see its recipe, swap it, give it a day,
 * change its portions, keep or release its pantry reservation, skip it, or remove it. Each is one
 * explicit command on this dish only. An archived menu only shows the recipe.
 *
 * @param servings The portions as shown, which may run ahead of the server while a change waits.
 * @param canSwap Whether this dish can be swapped through the options sheet.
 * @param busy Disables the actions while one is being saved.
 */
export function MenuEntrySheet({
  menu,
  entry,
  open,
  onOpenChange,
  servings,
  canSwap,
  busy,
  notice,
  actions,
}: {
  menu: Menu;
  entry: MenuEntry | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  servings: number;
  canSwap: boolean;
  busy: boolean;
  notice?: React.ReactNode;
  actions: MenuEntryActions;
}) {
  const { t, locale } = useProductionI18n();
  const editable = menu.status !== "archived";
  const toCook =
    entry !== null &&
    (entry.status === "planned" || entry.status === "cooking");
  const changeable =
    editable &&
    entry !== null &&
    entry.status !== "cooked" &&
    entry.status !== "cooking";

  return (
    <BottomSheet
      open={open}
      onOpenChange={onOpenChange}
      testId="menu-entry-sheet"
      closeLabel={t("menuBuild.sheet.close")}
      title={entry?.recipeName ?? ""}
    >
      {entry ? (
        <div className="space-y-5">
          {notice}
          {editable && toCook ? (
            <Button
              type="button"
              size="lg"
              className="w-full"
              data-testid="menu-entry-cook"
              disabled={busy}
              onClick={() => actions.cook(entry)}
            >
              {t(
                entry.status === "cooking"
                  ? "today.cooking.resume"
                  : "recipes.detail.cook",
              )}
            </Button>
          ) : null}
          <div className="grid grid-cols-2 gap-2">
            <Button asChild variant="secondary">
              <Link
                to={`/app/receitas/${entry.recipeId}`}
                data-testid="menu-entry-recipe"
              >
                {t("menuBuild.dish.recipe")}
              </Link>
            </Button>
            {canSwap ? (
              <Button
                type="button"
                variant="secondary"
                data-testid="menu-entry-swap"
                disabled={busy}
                onClick={() => actions.swap(entry)}
              >
                {t("menuBuild.dish.swap")}
              </Button>
            ) : null}
          </div>

          {changeable ? (
            <>
              <ChoiceChips
                label={t("menuBuild.dish.day")}
                testId="menu-entry-day"
                disabled={busy}
                value={entry.plannedDate ?? "none"}
                options={[
                  { value: "none", label: t("menuBuild.dish.day.none") },
                  ...daysBetween(menu.startDate, menu.endDate).map((day) => ({
                    value: day,
                    label: formatMenuDay(day, locale),
                  })),
                ]}
                onChange={(value) =>
                  actions.setDay(entry, value === "none" ? null : value)
                }
              />
              <Counter
                label={t("editor.servings")}
                value={servings}
                min={1}
                max={MAX_SERVINGS}
                onChange={(next) => actions.setServings(entry, next)}
                testId="menu-entry-servings"
              />
              <label className="flex min-h-11 items-center justify-between gap-3 text-sm">
                <span>{t("planning.field.reserve")}</span>
                <Switch
                  data-testid="menu-entry-reserve"
                  checked={entry.reserveIngredients}
                  disabled={busy}
                  onCheckedChange={(checked) =>
                    actions.setReserve(entry, checked)
                  }
                />
              </label>
            </>
          ) : null}

          {editable &&
          entry.status !== "cooked" &&
          entry.status !== "cooking" ? (
            <div className="grid grid-cols-2 gap-2">
              <Button
                type="button"
                variant="ghost"
                data-testid="menu-entry-skip"
                disabled={busy}
                onClick={() => actions.toggleSkip(entry)}
              >
                {t(
                  entry.status === "skipped"
                    ? "menuUse.unskip"
                    : "planning.skip",
                )}
              </Button>
              <Button
                type="button"
                variant="ghost"
                className="text-destructive"
                data-testid="menu-entry-remove"
                disabled={busy}
                onClick={() => actions.remove(entry)}
              >
                {t("shoppingPlan.review.remove")}
              </Button>
            </div>
          ) : null}
        </div>
      ) : null}
    </BottomSheet>
  );
}
