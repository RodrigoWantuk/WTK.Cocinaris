import { useEffect, useMemo, useState } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { ChoiceChips } from "@/components/choice/ChoiceChips";
import { Button } from "@/components/ui/button";
import type { Menu, MenuEntry } from "@/contracts/planning";
import { BottomSheet } from "@/features/shopping-plan/BottomSheet";
import { addDays, daysBetween, formatMenuDay } from "./planningDates";

/** Longest list of days offered; the server accepts up to a week before the menu. */
const MAX_DAYS = 14;

/** Whether a dish can be cooked on `day`: still to cook, and not after the day it is eaten. */
export function canCookOn(entry: MenuEntry, day: string): boolean {
  return (
    entry.status === "planned" &&
    (entry.plannedDate === null || entry.plannedDate >= day)
  );
}

/**
 * "Preparar vários de uma vez" (PLAN-0065 Slice 4): choose a day and the dishes to cook together. It
 * only sets the day they are cooked; the day each is eaten, its reservation, and "Hoje" stay as they
 * were. Days run from today, or the menu's start if later, to its end. Dishes already set for the
 * chosen day start ticked, so the sheet also changes or clears an earlier choice.
 *
 * @param today The owner's day (`YYYY-MM-DD`).
 * @param busy Disables the confirmation while it is being saved.
 * @param onConfirm Receives the day and the dishes cooked on it.
 */
export function CookAheadSheet({
  menu,
  today,
  open,
  onOpenChange,
  busy,
  notice,
  onConfirm,
}: {
  menu: Menu;
  today: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  busy: boolean;
  notice?: React.ReactNode;
  onConfirm: (day: string, entryIds: string[]) => void;
}) {
  const { t, locale } = useProductionI18n();
  const first = today > menu.startDate ? today : menu.startDate;
  const days = useMemo(
    () =>
      first > menu.endDate
        ? []
        : daysBetween(first, menu.endDate).slice(0, MAX_DAYS),
    [first, menu.endDate],
  );
  const [day, setDay] = useState<string>(days[0] ?? today);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const dishes = menu.entries.filter(
    (entry) => entry.kind !== "component" && canCookOn(entry, day),
  );

  // Opening or changing the day starts from what is already set for that day.
  useEffect(() => {
    if (!open) return;
    setPicked(
      new Set(
        menu.entries
          .filter((entry) => entry.cookOnDate === day)
          .map((entry) => entry.entryId),
      ),
    );
  }, [open, day, menu.entries]);

  useEffect(() => {
    if (open && !days.includes(day) && days[0]) setDay(days[0]);
  }, [open, days, day]);

  const ids = dishes
    .filter((entry) => picked.has(entry.entryId))
    .map((entry) => entry.entryId);
  const hadDay = menu.entries.some((entry) => entry.cookOnDate === day);

  return (
    <BottomSheet
      open={open}
      onOpenChange={onOpenChange}
      testId="menu-prepare-sheet"
      closeLabel={t("menuBuild.sheet.close")}
      title={t("menuUse.prepare.title")}
      footer={
        <div className="pb-3">
          <Button
            type="button"
            size="lg"
            className="w-full"
            data-testid="menu-prepare-confirm"
            disabled={
              busy || days.length === 0 || (ids.length === 0 && !hadDay)
            }
            onClick={() => onConfirm(day, ids)}
          >
            {ids.length === 0
              ? t("menuUse.prepare.clear")
              : t(
                  ids.length === 1
                    ? "menuUse.prepare.confirm.one"
                    : "menuUse.prepare.confirm.other",
                  { count: ids.length, day: formatMenuDay(day, locale) },
                )}
          </Button>
        </div>
      }
    >
      <div className="space-y-5">
        <p className="text-sm text-muted-foreground">
          {t("menuUse.prepare.hint")}
        </p>
        {notice}
        {days.length > 0 ? (
          <ChoiceChips
            label={t("menuUse.prepare.day")}
            testId="menu-prepare-day"
            value={day}
            options={days.map((value) => ({
              value,
              label:
                value === today
                  ? t("today.menu.today")
                  : value === addDays(today, 1)
                    ? t("menuUse.tomorrow")
                    : formatMenuDay(value, locale),
            }))}
            onChange={setDay}
          />
        ) : null}
        <fieldset className="space-y-1">
          <legend className="text-sm font-medium">
            {t("menuUse.prepare.dishes")}
          </legend>
          {dishes.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {t("menuUse.prepare.none")}
            </p>
          ) : (
            <ul className="divide-y divide-border">
              {dishes.map((entry) => (
                <li key={entry.entryId}>
                  <label className="flex min-h-12 cursor-pointer items-center gap-3 py-2">
                    <input
                      type="checkbox"
                      className="h-5 w-5 shrink-0 accent-primary"
                      data-testid="menu-prepare-dish"
                      checked={picked.has(entry.entryId)}
                      onChange={(event) =>
                        setPicked((current) => {
                          const next = new Set(current);
                          if (event.target.checked) next.add(entry.entryId);
                          else next.delete(entry.entryId);
                          return next;
                        })
                      }
                    />
                    <span className="min-w-0">
                      <span className="block font-medium">
                        {entry.recipeName}
                      </span>
                      {entry.plannedDate ? (
                        <span className="text-sm text-muted-foreground">
                          {t("menuUse.eatenOn", {
                            day: formatMenuDay(entry.plannedDate, locale),
                          })}
                        </span>
                      ) : null}
                    </span>
                  </label>
                </li>
              ))}
            </ul>
          )}
        </fieldset>
      </div>
    </BottomSheet>
  );
}
