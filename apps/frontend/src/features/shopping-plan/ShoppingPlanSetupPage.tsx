import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { ChoiceChips } from "@/components/choice/ChoiceChips";
import { Button } from "@/components/ui/button";
import { RecipeApiError } from "@/contracts/recipes";
import {
  MEAL_ORDER,
  type MealCounts,
  type MealType,
  type ShoppingPlanSetup,
  type ShoppingPlanStart,
} from "@/contracts/shoppingPlan";
import {
  addDays,
  formatMenuDay,
  localIsoDate,
} from "@/features/planning/planningDates";
import { restrictionLabel } from "./restrictionLabel";
import { useShoppingPlanRepository } from "./ShoppingPlanProvider";
import { ShoppingPlanRecentList } from "./ShoppingPlanRecent";

const DAY_CHOICES = [3, 5, 7, 14];
const MAX_DAYS = 14;
const MAX_MEALS = 40;
const MAX_PEOPLE = 12;
/** How far ahead a menu can start; the server enforces the same limit. */
const MAX_START_AHEAD = 7;

const zero = (): MealCounts => ({
  breakfast: 0,
  lunch: 0,
  dinner: 0,
  snack: 0,
});

/** The last menu's counts, or its days for each meal type when it was made before counts existed. */
function countsOf(setup: ShoppingPlanSetup): MealCounts {
  const counts = zero();
  if (setup.mealCounts) {
    for (const meal of MEAL_ORDER) counts[meal] = setup.mealCounts[meal] ?? 0;
    return counts;
  }
  for (const meal of setup.mealTypes) {
    if (meal in counts) counts[meal as MealType] = setup.days;
  }
  return counts;
}

/** A labeled number control with 44 px targets, usable by keyboard and screen readers. */
function Counter({
  label,
  value,
  min,
  max,
  onChange,
  testId,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  onChange: (next: number) => void;
  testId: string;
}) {
  const { t } = useProductionI18n();
  return (
    <div className="flex min-h-11 items-center gap-3">
      <span className="min-w-0 flex-1 text-[15px] font-medium">{label}</span>
      <Button
        type="button"
        variant="secondary"
        className="h-11 w-11 p-0 text-lg"
        aria-label={t("shoppingPlan.stepper.less", { label })}
        data-testid={`${testId}-less`}
        disabled={value <= min}
        onClick={() => onChange(value - 1)}
      >
        −
      </Button>
      <output
        data-testid={testId}
        aria-label={`${label}: ${value}`}
        className="tabular w-8 text-center text-lg font-semibold"
      >
        {value}
      </output>
      <Button
        type="button"
        variant="secondary"
        className="h-11 w-11 p-0 text-lg"
        aria-label={t("shoppingPlan.stepper.more", { label })}
        data-testid={`${testId}-more`}
        disabled={value >= max}
        onClick={() => onChange(value + 1)}
      >
        +
      </Button>
    </div>
  );
}

/**
 * The first screen of a new menu (PLAN-0065): the period, when it starts, and how many of each meal,
 * independent of the days, so seven snacks within fourteen days is a valid menu. Nothing about
 * cooking is asked here. The last answers come preselected; a counter not yet touched follows the
 * days. Answers belong to this menu and never change the profile.
 */
export function ShoppingPlanSetupPage() {
  const repo = useShoppingPlanRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const today = localIsoDate();
  const [start, setStart] = useState<ShoppingPlanStart | null>(null);
  const [failed, setFailed] = useState(false);
  const [days, setDays] = useState(7);
  const [customDays, setCustomDays] = useState(false);
  const [startDate, setStartDate] = useState(today);
  const [startChoice, setStartChoice] = useState<
    "today" | "tomorrow" | "other"
  >("today");
  const [counts, setCounts] = useState<MealCounts>({ ...zero(), dinner: 7 });
  // Counters the person changed stop following the days.
  const [touched, setTouched] = useState<Set<MealType>>(new Set());
  const [people, setPeople] = useState(2);
  const [peopleOpen, setPeopleOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const keyRef = useRef(crypto.randomUUID());

  useEffect(() => {
    const controller = new AbortController();
    repo.getStart(controller.signal).then(
      (value) => {
        if (controller.signal.aborted) return;
        setStart(value);
        setPeople(value.lastSetup?.people ?? value.people);
        const last = value.lastSetup;
        if (last) {
          setDays(last.days);
          setCustomDays(!DAY_CHOICES.includes(last.days));
          const lastCounts = countsOf(last);
          if (Object.values(lastCounts).some((count) => count > 0)) {
            setCounts(lastCounts);
            // A counter that equals the days was most likely following them.
            setTouched(
              new Set(
                MEAL_ORDER.filter((meal) => lastCounts[meal] !== last.days),
              ),
            );
          }
        }
      },
      () => !controller.signal.aborted && setFailed(true),
    );
    return () => controller.abort();
  }, [repo]);

  const changeDays = (next: number) => {
    setDays(next);
    setCounts((current) => {
      const updated = { ...current };
      for (const meal of MEAL_ORDER) {
        if (!touched.has(meal) && current[meal] > 0) updated[meal] = next;
      }
      return updated;
    });
  };

  const changeCount = (meal: MealType, next: number) => {
    setTouched((current) => new Set(current).add(meal));
    setCounts((current) => ({ ...current, [meal]: next }));
  };

  const total = MEAL_ORDER.reduce((sum, meal) => sum + counts[meal], 0);
  const tooMany = total > MAX_MEALS;

  const chooseStart = (value: "today" | "tomorrow" | "other") => {
    setStartChoice(value);
    if (value === "today") setStartDate(today);
    if (value === "tomorrow") setStartDate(addDays(today, 1));
    if (value === "other" && startDate <= addDays(today, 1))
      setStartDate(addDays(today, 2));
  };
  const laterDays = useMemo(
    () =>
      Array.from({ length: MAX_START_AHEAD - 1 }, (_, index) =>
        addDays(today, index + 2),
      ),
    [today],
  );

  const begin = async (fromMenuId: string | null = null) => {
    if (!session.csrfToken || saving) return;
    if (!fromMenuId && (tooMany || total === 0)) return;
    setSaving(true);
    setError(null);
    try {
      const plan = await repo.create(
        {
          today,
          days,
          people,
          mealCounts: counts,
          startDate,
          fromMenuId,
        },
        {
          csrfToken: session.csrfToken,
          idempotencyKey: keyRef.current,
          language: locale,
        },
      );
      navigate(`/app/compras/planejar/${plan.sessionId}`);
    } catch (err) {
      keyRef.current = crypto.randomUUID();
      setError(
        err instanceof RecipeApiError && err.code === "plan_in_progress"
          ? "menuBuild.inProgress"
          : "shoppingPlan.error.save",
      );
    } finally {
      setSaving(false);
    }
  };

  const discard = async () => {
    if (!start?.current || !session.csrfToken) return;
    setSaving(true);
    setError(null);
    try {
      await repo.abandon(start.current.sessionId, {
        csrfToken: session.csrfToken,
      });
      setStart({ ...start, current: null });
      setConfirmDiscard(false);
    } catch {
      setError("shoppingPlan.error.save");
    } finally {
      setSaving(false);
    }
  };

  if (failed) {
    return (
      <p role="alert" className="text-muted-foreground">
        {t("menuBuild.error.load")}
      </p>
    );
  }
  if (!start) return <p role="status">{t("planning.loading")}</p>;

  if (start.current) {
    return (
      <div data-testid="plan-setup-current" className="space-y-4">
        <h1 className="font-display text-3xl">{t("menuBuild.title")}</h1>
        <p className="text-muted-foreground">{t("menuBuild.current.body")}</p>
        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {t(error)}
          </p>
        ) : null}
        <Button asChild size="lg" className="w-full">
          <Link
            to={`/app/compras/planejar/${start.current.sessionId}`}
            data-testid="plan-continue"
          >
            {t("menuBuild.current.continue")}
          </Link>
        </Button>
        {confirmDiscard ? (
          <div className="surface space-y-3 p-4">
            <p className="text-sm">{t("menuBuild.current.confirm")}</p>
            <div className="flex gap-2">
              <Button
                type="button"
                variant="destructive"
                data-testid="plan-discard-confirm"
                disabled={saving}
                onClick={() => void discard()}
              >
                {t("menuBuild.current.discard")}
              </Button>
              <Button
                type="button"
                variant="ghost"
                onClick={() => setConfirmDiscard(false)}
              >
                {t("editor.cancel")}
              </Button>
            </div>
          </div>
        ) : (
          <Button
            type="button"
            variant="ghost"
            className="w-full"
            data-testid="plan-discard"
            onClick={() => setConfirmDiscard(true)}
          >
            {t("menuBuild.current.discard")}
          </Button>
        )}
      </div>
    );
  }

  const dayChoice = customDays ? "other" : String(days);
  return (
    <div className="space-y-4">
      <h1 className="font-display text-3xl">{t("menuBuild.title")}</h1>

      <form
        data-testid="plan-setup"
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          void begin();
        }}
      >
        <ChoiceChips
          label={t("menuBuild.days")}
          testId="plan-days"
          value={dayChoice}
          options={[
            ...DAY_CHOICES.map((value) => ({
              value: String(value),
              label: String(value),
            })),
            { value: "other", label: t("menuBuild.days.other") },
          ]}
          onChange={(value) => {
            setCustomDays(value === "other");
            if (value !== "other") changeDays(Number(value));
          }}
        />
        {customDays ? (
          <Counter
            label={t("menuBuild.days.count")}
            testId="plan-days-count"
            value={days}
            min={1}
            max={MAX_DAYS}
            onChange={changeDays}
          />
        ) : null}

        <ChoiceChips
          label={t("menuBuild.start")}
          testId="plan-start"
          value={startChoice}
          options={[
            { value: "today", label: t("menuBuild.start.today") },
            { value: "tomorrow", label: t("menuBuild.start.tomorrow") },
            { value: "other", label: t("menuBuild.start.other") },
          ]}
          onChange={chooseStart}
        />
        {startChoice === "other" ? (
          <ChoiceChips
            label={t("menuBuild.start.which")}
            hideLabel
            testId="plan-start-day"
            value={startDate}
            options={laterDays.map((day) => ({
              value: day,
              label: formatMenuDay(day, locale),
            }))}
            onChange={setStartDate}
          />
        ) : null}

        <fieldset className="space-y-1">
          {/* The total sits in the legend's row, so the screen fits a phone without scrolling. */}
          <legend className="flex w-full items-baseline justify-between gap-2 pb-1 text-sm font-medium">
            {t("menuBuild.meals")}
            <span
              aria-live="polite"
              data-testid="plan-meal-total"
              className={`font-normal ${tooMany ? "text-destructive" : "text-muted-foreground"}`}
            >
              {t(
                total === 1
                  ? "menuBuild.meals.total.one"
                  : "menuBuild.meals.total",
                { count: total, days },
              )}
            </span>
          </legend>
          {MEAL_ORDER.map((meal) => (
            <Counter
              key={meal}
              label={t(`shoppingPlan.meal.${meal}`)}
              testId={`plan-count-${meal}`}
              value={counts[meal]}
              min={0}
              max={MAX_MEALS}
              onChange={(next) => changeCount(meal, next)}
            />
          ))}
          {tooMany ? (
            <p role="alert" className="pt-1 text-sm text-destructive">
              {t("menuBuild.meals.tooMany", { max: MAX_MEALS })}
            </p>
          ) : null}
        </fieldset>

        <div className="space-y-2 text-sm">
          <p className="flex flex-wrap items-center gap-x-2">
            <span data-testid="plan-people-line">
              {t(people === 1 ? "menuBuild.people.one" : "menuBuild.people", {
                count: people,
              })}
            </span>
            <button
              type="button"
              data-testid="plan-people-change"
              aria-expanded={peopleOpen}
              className="min-h-11 font-medium text-primary underline-offset-4 hover:underline"
              onClick={() => setPeopleOpen((value) => !value)}
            >
              {t("menuBuild.people.change")}
            </button>
          </p>
          {peopleOpen ? (
            <Counter
              label={t("menuBuild.people.label")}
              testId="plan-people"
              value={people}
              min={1}
              max={MAX_PEOPLE}
              onChange={setPeople}
            />
          ) : null}
          {start.unverifiableRestrictions.map((code) => (
            <p
              key={code}
              data-testid="plan-unverifiable"
              className="text-warning-foreground"
            >
              {t("shoppingPlan.restrictions.unverifiable", {
                restriction: restrictionLabel(locale, code),
              })}
            </p>
          ))}
        </div>

        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {t(error)}
          </p>
        ) : null}
        <Button
          type="submit"
          size="lg"
          className="w-full"
          data-testid="plan-begin"
          disabled={saving || tooMany || total === 0 || !session.csrfToken}
        >
          {saving
            ? t("planning.saving")
            : t("menuBuild.begin", { count: total })}
        </Button>
      </form>

      {/* The list has its own form for names, and forms cannot nest. */}
      {start.recent.length > 0 ? (
        <details data-testid="plan-recent-disclosure">
          <summary className="flex min-h-11 cursor-pointer items-center text-sm font-medium">
            {t("menuBuild.recent")}
          </summary>
          <ShoppingPlanRecentList
            plans={start.recent}
            csrfToken={session.csrfToken}
            busy={saving}
            hideTitle
            onRepeat={(menuId) => void begin(menuId)}
          />
        </details>
      ) : null}
    </div>
  );
}
