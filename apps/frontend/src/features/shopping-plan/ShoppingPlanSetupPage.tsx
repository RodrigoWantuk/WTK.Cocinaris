import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { ChoiceChips } from "@/components/choice/ChoiceChips";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { RecipeApiError } from "@/contracts/recipes";
import type { ShoppingPlanStart } from "@/contracts/shoppingPlan";
import { useInventoryRepository } from "@/features/inventory/InventoryProvider";
import {
  addDays,
  formatMenuDay,
  localIsoDate,
} from "@/features/planning/planningDates";
import { resolveLabel } from "@/features/profile/catalog/profileCatalog";
import type { PreferenceCategory } from "@/contracts/profile";
import { restrictionLabel } from "./restrictionLabel";
import { useShoppingPlanRepository } from "./ShoppingPlanProvider";

const MEALS = ["lunch", "dinner", "snack", "breakfast"] as const;
const MAX_DAYS = 14;
const MAX_MEALS = 40;
const MAX_PEOPLE = 12;
const MAX_PREP_DAYS = 7;
/** A labeled number control with large targets, usable by keyboard and screen readers. */
function Stepper({
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
    <div className="flex items-center gap-3">
      <span className="min-w-0 flex-1 text-sm font-medium">{label}</span>
      <Button
        type="button"
        variant="secondary"
        aria-label={t("shoppingPlan.stepper.less", { label })}
        data-testid={`${testId}-less`}
        disabled={value <= min}
        onClick={() => onChange(value - 1)}
      >
        −
      </Button>
      <span
        role="status"
        data-testid={testId}
        className="tabular w-8 text-center font-semibold"
      >
        {value}
      </span>
      <Button
        type="button"
        variant="secondary"
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
 * The setup screen of a shopping plan (PLAN-0062): one screen, no steps, with the last answers
 * already chosen, so returning people can often just tap "Começar". Answers belong to this plan and
 * never change the profile; restrictions are shown here and changed only in the profile.
 */
export function ShoppingPlanSetupPage() {
  const repo = useShoppingPlanRepository();
  const inventory = useInventoryRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const today = localIsoDate();
  const [start, setStart] = useState<ShoppingPlanStart | null>(null);
  const [failed, setFailed] = useState(false);
  const [days, setDays] = useState(5);
  const [customDays, setCustomDays] = useState(false);
  const [meals, setMeals] = useState<string[]>(["lunch", "dinner"]);
  const [mealsOut, setMealsOut] = useState(0);
  const [style, setStyle] = useState("mix");
  const [priority, setPriority] = useState("waste");
  const [prepDay, setPrepDay] = useState<"today" | "tomorrow" | "other">(
    "today",
  );
  const [prepDate, setPrepDate] = useState(today);
  const [prepMinutes, setPrepMinutes] = useState("120");
  const [anchor, setAnchor] = useState("");
  const [anchorOpen, setAnchorOpen] = useState(false);
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const [people, setPeople] = useState(2);
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
          setCustomDays(![3, 5, 7].includes(last.days));
          setMeals(last.mealTypes);
          setStyle(last.cookingStyle);
          setPriority(last.priority);
          if (last.prepMinutes) setPrepMinutes(String(last.prepMinutes));
        }
      },
      () => !controller.signal.aborted && setFailed(true),
    );
    return () => controller.abort();
  }, [repo]);

  useEffect(() => {
    const text = anchor.trim();
    if (text.length < 2 || !inventory.suggestNames) {
      setSuggestions([]);
      return;
    }
    const controller = new AbortController();
    const timer = globalThis.setTimeout(() => {
      inventory
        .suggestNames?.(text, { language: locale, signal: controller.signal })
        .then(
          (items) =>
            !controller.signal.aborted &&
            setSuggestions(items.map((item) => item.name).slice(0, 6)),
          () => undefined,
        );
    }, 250);
    return () => {
      globalThis.clearTimeout(timer);
      controller.abort();
    };
  }, [anchor, inventory, locale]);

  const total = days * meals.length;
  const mealsToPlan = total - mealsOut;
  const tooMany = mealsToPlan > MAX_MEALS;

  // Fewer days or meals can leave more meals out than the plan has.
  useEffect(() => {
    if (total > 0 && mealsOut > total - 1) setMealsOut(total - 1);
  }, [total, mealsOut]);

  const restrictionsText = useMemo(
    () =>
      (start?.restrictions ?? [])
        .map((item) =>
          resolveLabel(locale, item.category as PreferenceCategory, item.code),
        )
        .join(" · "),
    [start, locale],
  );

  const chooseDay = (value: "today" | "tomorrow" | "other") => {
    setPrepDay(value);
    if (value === "today") setPrepDate(today);
    if (value === "tomorrow") setPrepDate(addDays(today, 1));
  };

  const begin = async () => {
    if (!session.csrfToken || saving || tooMany || meals.length === 0) return;
    setSaving(true);
    setError(null);
    const daily = style === "daily";
    try {
      const plan = await repo.create(
        {
          today,
          days,
          mealTypes: meals,
          mealsOut,
          cookingStyle: style,
          priority,
          prepDate: daily ? null : prepDate,
          prepMinutes: daily ? null : Number(prepMinutes),
          anchorIngredient: anchor.trim() || null,
          people,
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
          ? "shoppingPlan.setup.inProgress"
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
        {t("shoppingPlan.error.load")}
      </p>
    );
  }
  if (!start) return <p role="status">{t("planning.loading")}</p>;

  if (start.current) {
    return (
      <div data-testid="plan-setup-current" className="space-y-4">
        <h1 className="font-display text-3xl">{t("shoppingPlan.title")}</h1>
        <p className="text-muted-foreground">
          {t("shoppingPlan.current.body")}
        </p>
        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {t(error)}
          </p>
        ) : null}
        <Button asChild size="lg" className="w-full">
          <Link
            to={`/app/compras/planejar/${start.current.sessionId}${start.current.status === "reviewing" ? "/revisar" : ""}`}
            data-testid="plan-continue"
          >
            {t("shoppingPlan.current.continue")}
          </Link>
        </Button>
        {confirmDiscard ? (
          <div className="surface space-y-3 p-4">
            <p className="text-sm">{t("shoppingPlan.current.confirm")}</p>
            <div className="flex gap-2">
              <Button
                type="button"
                variant="destructive"
                data-testid="plan-discard-confirm"
                disabled={saving}
                onClick={() => void discard()}
              >
                {t("shoppingPlan.current.discard")}
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
            {t("shoppingPlan.current.discard")}
          </Button>
        )}
      </div>
    );
  }

  const dayChoice = customDays ? "other" : String(days);
  return (
    <form
      data-testid="plan-setup"
      className="space-y-6"
      onSubmit={(event) => {
        event.preventDefault();
        void begin();
      }}
    >
      <header>
        <h1 className="font-display text-3xl">{t("shoppingPlan.title")}</h1>
        <p className="mt-1 text-muted-foreground">
          {t("shoppingPlan.subtitle")}
        </p>
      </header>

      <ChoiceChips
        label={t("shoppingPlan.days")}
        testId="plan-days"
        value={dayChoice}
        options={[
          { value: "3", label: "3" },
          { value: "5", label: "5" },
          { value: "7", label: "7" },
          { value: "other", label: t("shoppingPlan.days.other") },
        ]}
        onChange={(value) => {
          setCustomDays(value === "other");
          if (value !== "other") setDays(Number(value));
        }}
      />
      {customDays ? (
        <Stepper
          label={t("shoppingPlan.days.count")}
          testId="plan-days-count"
          value={days}
          min={1}
          max={MAX_DAYS}
          onChange={setDays}
        />
      ) : null}

      <div className="space-y-1.5">
        <p className="text-sm font-medium">{t("shoppingPlan.meals")}</p>
        <div
          role="group"
          aria-label={t("shoppingPlan.meals")}
          className="flex flex-wrap gap-2"
        >
          {MEALS.map((meal) => {
            const chosen = meals.includes(meal);
            return (
              <Button
                key={meal}
                type="button"
                size="sm"
                variant={chosen ? "default" : "secondary"}
                aria-pressed={chosen}
                data-testid={`plan-meal-${meal}`}
                onClick={() =>
                  setMeals((current) =>
                    chosen
                      ? current.filter((item) => item !== meal)
                      : [...current, meal],
                  )
                }
              >
                {t(`shoppingPlan.meal.${meal}`)}
              </Button>
            );
          })}
        </div>
        {meals.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {t("shoppingPlan.meals.none")}
          </p>
        ) : null}
      </div>

      {total > 1 ? (
        <Stepper
          label={t("shoppingPlan.mealsOut")}
          testId="plan-meals-out"
          value={mealsOut}
          min={0}
          max={total - 1}
          onChange={setMealsOut}
        />
      ) : null}
      <p
        role="status"
        data-testid="plan-meal-total"
        className={`text-sm ${tooMany ? "text-destructive" : "text-muted-foreground"}`}
      >
        {tooMany
          ? t("shoppingPlan.meals.tooMany", { max: MAX_MEALS })
          : t("shoppingPlan.meals.total", { count: Math.max(0, mealsToPlan) })}
      </p>

      <ChoiceChips
        label={t("shoppingPlan.style")}
        testId="plan-style"
        value={style}
        options={[
          { value: "once", label: t("shoppingPlan.style.once") },
          { value: "mix", label: t("shoppingPlan.style.mix") },
          { value: "daily", label: t("shoppingPlan.style.daily") },
        ]}
        onChange={setStyle}
      />
      <ChoiceChips
        label={t("shoppingPlan.priority")}
        testId="plan-priority"
        value={priority}
        options={[
          { value: "waste", label: t("shoppingPlan.priority.waste") },
          { value: "short", label: t("shoppingPlan.priority.short") },
          { value: "save", label: t("shoppingPlan.priority.save") },
          { value: "time", label: t("shoppingPlan.priority.time") },
        ]}
        onChange={setPriority}
      />

      {style !== "daily" ? (
        <>
          <ChoiceChips
            label={t("shoppingPlan.prepDay")}
            testId="plan-prep-day"
            value={prepDay}
            options={[
              { value: "today", label: t("shoppingPlan.prepDay.today") },
              { value: "tomorrow", label: t("shoppingPlan.prepDay.tomorrow") },
              { value: "other", label: t("shoppingPlan.prepDay.other") },
            ]}
            onChange={chooseDay}
          />
          {prepDay === "other" ? (
            <label className="block space-y-1 text-sm font-medium">
              {t("shoppingPlan.prepDay.date")}
              <Input
                type="date"
                data-testid="plan-prep-date"
                value={prepDate}
                min={today}
                max={addDays(today, MAX_PREP_DAYS)}
                onChange={(event) => setPrepDate(event.target.value)}
              />
              <span className="block text-xs font-normal text-muted-foreground">
                {formatMenuDay(prepDate, locale)}
              </span>
            </label>
          ) : null}
          <ChoiceChips
            label={t("shoppingPlan.prepTime")}
            testId="plan-prep-time"
            value={prepMinutes}
            options={[
              { value: "60", label: t("shoppingPlan.prepTime.60") },
              { value: "120", label: t("shoppingPlan.prepTime.120") },
              { value: "240", label: t("shoppingPlan.prepTime.240") },
            ]}
            onChange={setPrepMinutes}
          />
        </>
      ) : null}

      {anchorOpen ? (
        <label className="block space-y-1 text-sm font-medium">
          {t("shoppingPlan.anchor")}
          <Input
            data-testid="plan-anchor"
            maxLength={60}
            list="plan-anchor-options"
            placeholder={t("shoppingPlan.anchor.example")}
            value={anchor}
            onChange={(event) => setAnchor(event.target.value)}
          />
          <datalist id="plan-anchor-options">
            {suggestions.map((name) => (
              <option key={name} value={name} />
            ))}
          </datalist>
        </label>
      ) : (
        <button
          type="button"
          data-testid="plan-anchor-open"
          className="min-h-11 text-sm font-medium text-primary underline-offset-4 hover:underline"
          onClick={() => setAnchorOpen(true)}
        >
          {t("shoppingPlan.anchor")}
        </button>
      )}

      <div className="surface space-y-3 p-4">
        <Stepper
          label={t("shoppingPlan.people")}
          testId="plan-people"
          value={people}
          min={1}
          max={MAX_PEOPLE}
          onChange={setPeople}
        />
        <p
          className="text-sm text-muted-foreground"
          data-testid="plan-restrictions"
        >
          {restrictionsText || t("shoppingPlan.restrictions.none")}{" "}
          <Link
            to="/app/perfil"
            className="font-medium text-primary underline-offset-4 hover:underline"
          >
            {t("shoppingPlan.restrictions.change")}
          </Link>
        </p>
        {start.restrictions.length > 0 ? (
          <p className="text-xs text-muted-foreground">
            {t("shoppingPlan.restrictions.labels")}
          </p>
        ) : null}
        {start.unverifiableRestrictions.map((code) => (
          <p
            key={code}
            data-testid="plan-unverifiable"
            className="text-sm text-warning-foreground"
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
        disabled={saving || tooMany || meals.length === 0 || !session.csrfToken}
      >
        {saving ? t("planning.saving") : t("shoppingPlan.begin")}
      </Button>
    </form>
  );
}
