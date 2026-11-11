import { useEffect, useId, useMemo, useState } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import type { RecipeSummary } from "@/contracts/recipes";
import type {
  RepeatMode,
  ShoppingPlan,
  ShoppingPlanAccepted,
  ShoppingPlanCard,
  ShoppingPlanPreferences,
} from "@/contracts/shoppingPlan";
import { normalizeIngredientName } from "@/lib/kitchenMeasures";
import { BottomSheet } from "./BottomSheet";
import { RepeatPanel } from "./RepeatPanel";
import { restrictionLabel } from "./restrictionLabel";
import type { MealBatchFailure, MealBatchJob } from "./useMealBatches";

/** Longest specific request; the server enforces the same limit. */
export const REQUEST_MAX = 120;
const MAX_SAVED_MATCHES = 4;
const GOALS = ["quick", "short", "save"] as const;
const AI_OFF = new Set([
  "ai_capability_unavailable",
  "ai_budget_exhausted",
  "ai_budget_unavailable",
]);

/** What the sheet can ask the board to do; the board owns every request. */
export interface MealOptionsActions {
  choose: (card: ShoppingPlanCard) => void;
  chooseRecipe: (recipeId: string, name: string) => void;
  refuse: (card: ShoppingPlanCard) => void;
  ask: (job: MealBatchJob) => void;
  setPreferences: (next: ShoppingPlanPreferences) => void;
  /** Sets the whole repeat state of a chosen dish ("Repetir"). */
  repeat: (dish: ShoppingPlanAccepted, mode: RepeatMode, meals: number) => void;
}

/**
 * The options of one meal type (PLAN-0065): a text box first, then the switches and a filter, then
 * six dishes. Typing shows the person's own recipes at once, with no AI, and always offers to ask the
 * AI for exactly what was typed. Choosing keeps the sheet open until the meal type is complete, so
 * seven snacks take seven taps, or one choice and "Repetir" (Slice 6) on the dish chosen last.
 * Earlier options stay usable while new ones are on their way.
 *
 * @param plan The menu, as last returned by the server.
 * @param mealType The meal type being chosen.
 * @param saved The person's saved recipes, loaded once by the board.
 * @param running The request being answered, if any; only one runs at a time.
 * @param failure Why the last request for this meal type gave nothing.
 * @param exhausted Whether the server has no new dishes for this meal type.
 * @param busyCardId A dish whose choice is being saved.
 * @param notice A status line from the board, such as the undo offer.
 */
export function MealOptionsSheet({
  plan,
  mealType,
  open,
  onOpenChange,
  saved,
  running,
  failure,
  exhausted,
  busyCardId,
  actions,
  notice,
  onCloseAutoFocus,
}: {
  plan: ShoppingPlan;
  mealType: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  saved: RecipeSummary[];
  running: MealBatchJob | null;
  failure: MealBatchFailure | undefined;
  exhausted: boolean;
  busyCardId: string | null;
  actions: MealOptionsActions;
  notice?: React.ReactNode;
  /** Where focus goes when the sheet closes; Radix returns it to the opener otherwise. */
  onCloseAutoFocus?: (event: Event) => void;
}) {
  const { t, locale } = useProductionI18n();
  const [text, setText] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);
  const inputId = useId();
  const countId = useId();
  const group = plan.groups.find((item) => item.mealType === mealType);
  const missing = group ? Math.max(0, group.wanted - group.filled) : 0;
  const options = group?.options ?? [];
  const preferences = plan.preferences;
  const searching = running?.mealType === mealType;

  useEffect(() => {
    if (!open) {
      setText("");
      setExpanded(null);
    }
  }, [open]);

  const typed = text.trim();
  const matches = useMemo(() => {
    const wanted = normalizeIngredientName(typed);
    if (wanted.length < 2) return [];
    return saved
      .filter((recipe) => normalizeIngredientName(recipe.name).includes(wanted))
      .slice(0, MAX_SAVED_MATCHES);
  }, [saved, typed]);

  // Dishes already chosen that exist as recipes can simply be cooked again.
  const already = useMemo(() => {
    const seen = new Set<string>();
    return plan.accepted.filter((card) => {
      if (card.isComponent || !card.recipeId || seen.has(card.recipeId))
        return false;
      seen.add(card.recipeId);
      return true;
    });
  }, [plan.accepted]);

  // "Repetir" acts on the dish chosen last for this meal type; one whose recipe failed is not offered.
  const previous =
    [...plan.accepted]
      .reverse()
      .find(
        (card) =>
          card.mealType === mealType &&
          !card.isComponent &&
          card.expansionState !== "failed",
      ) ?? null;

  const meal = t(`shoppingPlan.meal.${mealType}`);
  const requested = options.filter((card) => card.requestText);
  const others = options.filter((card) => !card.requestText);
  const failureText = failure ? describeFailure(failure) : null;

  function describeFailure(value: MealBatchFailure): string {
    if (value.code === "request_blocked") {
      return t("menuBuild.sheet.blocked", {
        restriction: value.restriction
          ? restrictionLabel(locale, value.restriction)
          : t("menuBuild.sheet.blocked.any"),
      });
    }
    if (value.code === "no_more_options") return t("menuBuild.sheet.noMore");
    if (AI_OFF.has(value.code)) return t("menuBuild.sheet.aiOff");
    return t("menuBuild.sheet.failed");
  }

  const toggle = (change: Partial<ShoppingPlanPreferences>) =>
    actions.setPreferences({ ...preferences, ...change });

  const row = (card: ShoppingPlanCard) => {
    const isOpen = expanded === card.cardId;
    const meta = [
      t("recipes.generate.minutes", { count: card.totalMinutes }),
      card.newItemCount === 0
        ? t("shoppingPlan.card.noNewItems")
        : t(
            card.newItemCount === 1
              ? "shoppingPlan.card.newItems.one"
              : "shoppingPlan.card.newItems",
            { count: card.newItemCount },
          ),
    ].join(" · ");
    return (
      <li
        key={card.cardId}
        data-testid="meal-option"
        className="border-b border-border last:border-b-0"
      >
        <button
          type="button"
          data-testid="meal-option-choose"
          aria-label={t("menuBuild.sheet.choose", {
            dish: card.name,
            meal,
          })}
          disabled={busyCardId !== null}
          onClick={() => actions.choose(card)}
          className="flex min-h-14 w-full flex-col items-start gap-0.5 rounded-lg px-2 py-3 text-left transition-colors hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-60"
        >
          <span className="line-clamp-2 font-semibold">{card.name}</span>
          <span className="text-sm text-muted-foreground">{meta}</span>
          {card.reusedNames.length > 0 ? (
            <span className="text-sm text-muted-foreground">
              {t("shoppingPlan.card.uses", {
                names: card.reusedNames.join(", "),
              })}
            </span>
          ) : null}
          {card.guardCode ? (
            <span className="text-sm text-muted-foreground">
              {t("shoppingPlan.card.guard", {
                ingredient:
                  card.guardIngredient ?? t("shoppingPlan.card.guardAny"),
                restriction: restrictionLabel(locale, card.guardCode),
              })}
            </span>
          ) : null}
        </button>
        <div className="flex flex-wrap gap-2 px-2 pb-3">
          <Button
            type="button"
            size="sm"
            variant="ghost"
            aria-expanded={isOpen}
            data-testid="meal-option-details"
            onClick={() => setExpanded(isOpen ? null : card.cardId)}
          >
            {t("shoppingPlan.card.ingredients")}
          </Button>
        </div>
        {isOpen ? (
          <div className="space-y-3 px-2 pb-3">
            {card.ingredients.length > 0 ? (
              <ul className="space-y-0.5 text-sm text-muted-foreground">
                {card.ingredients.map((item) => (
                  <li key={`${item.name}-${item.unit}`}>{item.name}</li>
                ))}
              </ul>
            ) : null}
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                variant="secondary"
                data-testid="meal-option-like"
                disabled={exhausted}
                onClick={() =>
                  actions.ask({
                    mealType,
                    kind: "like",
                    likeCardId: card.cardId,
                  })
                }
              >
                {t("menuBuild.sheet.like")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                data-testid="meal-option-refuse"
                disabled={busyCardId !== null}
                onClick={() => actions.refuse(card)}
              >
                {t("menuBuild.sheet.refuse")}
              </Button>
            </div>
          </div>
        ) : null}
      </li>
    );
  };

  return (
    <BottomSheet
      open={open}
      onOpenChange={onOpenChange}
      testId="meal-sheet"
      onCloseAutoFocus={onCloseAutoFocus}
      closeLabel={t("menuBuild.sheet.close")}
      title={
        missing > 0
          ? `${meal} · ${t(missing === 1 ? "menuBuild.missing.one" : "menuBuild.missing", { count: missing })}`
          : meal
      }
      header={
        <form
          role="search"
          className="space-y-2"
          onSubmit={(event) => {
            event.preventDefault();
            if (typed)
              actions.ask({ mealType, kind: "request", request: typed });
          }}
        >
          <label htmlFor={inputId} className="text-sm font-medium">
            {t("menuBuild.sheet.ask")}
          </label>
          <Input
            id={inputId}
            data-testid="meal-sheet-text"
            value={text}
            maxLength={REQUEST_MAX}
            autoComplete="off"
            enterKeyHint="search"
            placeholder={t("menuBuild.sheet.ask.placeholder")}
            aria-describedby={countId}
            onChange={(event) => setText(event.target.value)}
          />
          <p id={countId} className="sr-only">
            {t("menuBuild.sheet.count", {
              used: text.length,
              max: REQUEST_MAX,
            })}
          </p>
          {typed ? (
            <ul className="space-y-1" data-testid="meal-sheet-matches">
              {matches.map((recipe) => (
                <li key={recipe.recipeId}>
                  <button
                    type="button"
                    data-testid="meal-sheet-saved"
                    disabled={busyCardId !== null}
                    onClick={() =>
                      actions.chooseRecipe(recipe.recipeId, recipe.name)
                    }
                    className="flex min-h-11 w-full items-center justify-between gap-2 rounded-lg px-2 text-left text-sm hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-60"
                  >
                    <span className="min-w-0 truncate font-medium">
                      {recipe.name}
                    </span>
                    <span className="shrink-0 text-xs text-muted-foreground">
                      {t("menuBuild.sheet.saved")}
                    </span>
                  </button>
                </li>
              ))}
              <li>
                <button
                  type="submit"
                  data-testid="meal-sheet-ask-ai"
                  disabled={exhausted && failure?.code === "no_more_options"}
                  className="flex min-h-11 w-full items-center rounded-lg px-2 text-left text-sm font-semibold text-primary hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-60"
                >
                  <span className="min-w-0 truncate">
                    {t("menuBuild.sheet.askAi", { text: typed })}
                  </span>
                </button>
              </li>
            </ul>
          ) : null}
        </form>
      }
    >
      <div className="space-y-5">
        {notice}
        {missing > 0 && previous ? (
          <RepeatPanel
            dish={previous}
            people={previous.people ?? plan.setup.people}
            missing={missing}
            busy={busyCardId !== null}
            onRepeat={(mode, meals) => actions.repeat(previous, mode, meals)}
          />
        ) : null}
        <div className="space-y-1">
          <label className="flex min-h-11 items-center justify-between gap-3 text-sm font-medium">
            {t("menuBuild.sheet.preferences")}
            <Switch
              data-testid="meal-sheet-preferences"
              checked={preferences.usePreferences}
              onCheckedChange={(checked) => toggle({ usePreferences: checked })}
            />
          </label>
          <p className="text-xs text-muted-foreground">
            {t("menuBuild.sheet.preferences.hint")}
          </p>
          <label className="flex min-h-11 items-center justify-between gap-3 text-sm font-medium">
            {t("menuBuild.sheet.pantry")}
            <Switch
              data-testid="meal-sheet-pantry"
              checked={preferences.usePantry}
              onCheckedChange={(checked) => toggle({ usePantry: checked })}
            />
          </label>
        </div>

        <div
          role="group"
          aria-label={t("menuBuild.sheet.filter")}
          className="flex flex-wrap gap-2"
        >
          {GOALS.map((goal) => {
            const on = preferences.goal === goal;
            return (
              <Button
                key={goal}
                type="button"
                size="sm"
                variant={on ? "default" : "secondary"}
                aria-pressed={on}
                data-testid={`meal-sheet-goal-${goal}`}
                onClick={() => toggle({ goal: on ? null : goal })}
              >
                {t(`menuBuild.sheet.goal.${goal}`)}
              </Button>
            );
          })}
        </div>

        {failureText ? (
          <p
            role="alert"
            data-testid="meal-sheet-failure"
            className="text-sm text-destructive"
          >
            {failureText}
          </p>
        ) : null}

        {missing === 0 ? (
          <p
            role="status"
            className="font-medium"
            data-testid="meal-sheet-done"
          >
            {t("menuBuild.sheet.done", { meal })}
          </p>
        ) : null}

        {searching ? (
          <div
            role="status"
            data-testid="meal-sheet-searching"
            className="space-y-2"
          >
            <p className="text-sm text-muted-foreground">
              {running?.kind === "request" && running.request
                ? t("menuBuild.sheet.searchingFor", { text: running.request })
                : t("menuBuild.sheet.searching")}
            </p>
            {[0, 1].map((index) => (
              <div key={index} aria-hidden="true" className="space-y-1.5 py-2">
                <div className="h-5 w-2/3 rounded bg-secondary motion-safe:animate-pulse" />
                <div className="h-4 w-1/3 rounded bg-secondary motion-safe:animate-pulse" />
              </div>
            ))}
          </div>
        ) : null}

        {missing > 0 && requested.length > 0 ? (
          <section className="space-y-1">
            <h3 className="text-sm font-semibold text-muted-foreground">
              {t("menuBuild.sheet.forRequest", {
                text: requested[0].requestText ?? "",
              })}
            </h3>
            <ul>{requested.map(row)}</ul>
          </section>
        ) : null}

        {missing > 0 && others.length > 0 ? (
          <section className="space-y-1">
            <h3 className="text-sm font-semibold text-muted-foreground">
              {t("menuBuild.sheet.options")}
            </h3>
            <ul data-testid="meal-sheet-options">{others.map(row)}</ul>
          </section>
        ) : null}

        {missing > 0 && options.length === 0 && !searching ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="meal-sheet-empty"
          >
            {t("menuBuild.sheet.empty")}
          </p>
        ) : null}

        {missing > 0 && already.length > 0 ? (
          <section className="space-y-1">
            <h3 className="text-sm font-semibold text-muted-foreground">
              {t("menuBuild.sheet.already")}
            </h3>
            <ul className="flex flex-wrap gap-2">
              {already.map((card) => (
                <li key={card.recipeId}>
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    data-testid="meal-sheet-again"
                    disabled={busyCardId !== null}
                    aria-label={t("menuBuild.sheet.again", { dish: card.name })}
                    onClick={() =>
                      actions.chooseRecipe(card.recipeId ?? "", card.name)
                    }
                  >
                    {card.name}
                  </Button>
                </li>
              ))}
            </ul>
          </section>
        ) : null}

        {missing > 0 ? (
          <Button
            type="button"
            variant="secondary"
            className="w-full"
            data-testid="meal-sheet-more"
            disabled={searching || exhausted}
            onClick={() => actions.ask({ mealType, kind: "more" })}
          >
            {t("menuBuild.sheet.more")}
          </Button>
        ) : null}
      </div>
    </BottomSheet>
  );
}
