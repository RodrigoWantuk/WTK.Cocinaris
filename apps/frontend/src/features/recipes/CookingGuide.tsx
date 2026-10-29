import { useEffect, useRef, useState } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import type { ParsedNormalizedRecipe } from "./parseNormalizedRecipe";
import { useHouseholdMeasure, useRecipeQuantity } from "./RecipeContent";

/**
 * Keeps the screen on while `active` (PLAN-0054 Phase 4: hands are busy while cooking). Uses the
 * Screen Wake Lock API where the browser offers it and does nothing elsewhere. The lock is
 * re-requested when the page becomes visible again, because browsers release it on hide.
 */
export function useWakeLock(active: boolean) {
  useEffect(() => {
    if (!active || typeof navigator === "undefined") return;
    const wakeLock = (
      navigator as Navigator & {
        wakeLock?: { request: (type: "screen") => Promise<WakeLockSentinel> };
      }
    ).wakeLock;
    if (!wakeLock) return;
    let sentinel: WakeLockSentinel | null = null;
    let cancelled = false;
    const request = () => {
      wakeLock
        .request("screen")
        .then((lock) => {
          if (cancelled) void lock.release();
          else sentinel = lock;
        })
        .catch(() => {
          // Refused (battery saver, permissions): cooking works without it.
        });
    };
    const onVisible = () => {
      if (document.visibilityState === "visible") request();
    };
    request();
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      cancelled = true;
      document.removeEventListener("visibilitychange", onVisible);
      void sentinel?.release().catch(() => undefined);
    };
  }, [active]);
}

/** Short beep and vibration when a timer ends; both are best effort. */
function alertTimerDone() {
  try {
    navigator.vibrate?.([300, 150, 300]);
  } catch {
    // Not supported.
  }
  try {
    const AudioContextClass =
      window.AudioContext ??
      (window as Window & { webkitAudioContext?: typeof AudioContext })
        .webkitAudioContext;
    if (!AudioContextClass) return;
    const context = new AudioContextClass();
    const oscillator = context.createOscillator();
    oscillator.frequency.value = 880;
    oscillator.connect(context.destination);
    oscillator.start();
    oscillator.stop(context.currentTime + 0.6);
    oscillator.onended = () => void context.close();
  } catch {
    // Audio blocked; the on-screen message remains.
  }
}

function formatClock(totalSeconds: number) {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

/**
 * Countdown for one step. It counts against a target time, not interval ticks, so it stays right
 * when the phone throttles a background tab.
 */
export function StageTimer({ minutes }: { minutes: number }) {
  const { t } = useProductionI18n();
  const total = Math.round(minutes * 60);
  const [endsAt, setEndsAt] = useState<number | null>(null);
  const [pausedLeft, setPausedLeft] = useState<number | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const alerted = useRef(false);

  useEffect(() => {
    if (endsAt === null) return;
    const id = window.setInterval(() => setNow(Date.now()), 500);
    return () => window.clearInterval(id);
  }, [endsAt]);

  const left =
    endsAt !== null
      ? Math.max(0, Math.ceil((endsAt - now) / 1000))
      : (pausedLeft ?? total);
  const finished = endsAt !== null && left === 0;

  useEffect(() => {
    if (finished && !alerted.current) {
      alerted.current = true;
      alertTimerDone();
    }
  }, [finished]);

  const start = () => {
    alerted.current = false;
    const current = Date.now();
    setNow(current);
    setEndsAt(current + (pausedLeft ?? total) * 1000);
    setPausedLeft(null);
  };
  const pause = () => {
    setPausedLeft(left);
    setEndsAt(null);
  };
  const reset = () => {
    setEndsAt(null);
    setPausedLeft(null);
    alerted.current = false;
  };

  return (
    <div
      data-testid="cooking-timer"
      className="flex flex-wrap items-center gap-3 rounded-2xl bg-secondary p-4"
    >
      <span
        className="font-display text-3xl tabular-nums"
        aria-hidden={endsAt !== null && !finished}
      >
        {formatClock(left)}
      </span>
      <span role="status" aria-live="polite" className="sr-only">
        {finished ? t("cooking.timer.done") : ""}
      </span>
      {finished ? (
        <p className="w-full font-semibold" data-testid="cooking-timer-done">
          {t("cooking.timer.done")}
        </p>
      ) : null}
      <div className="flex gap-2">
        {endsAt === null && !finished ? (
          <Button
            type="button"
            data-testid="cooking-timer-start"
            onClick={start}
          >
            {pausedLeft !== null
              ? t("cooking.timer.resume")
              : t("cooking.timer.start", { minutes })}
          </Button>
        ) : null}
        {endsAt !== null && !finished ? (
          <Button type="button" variant="secondary" onClick={pause}>
            {t("cooking.timer.pause")}
          </Button>
        ) : null}
        {endsAt !== null || pausedLeft !== null ? (
          <Button type="button" variant="ghost" onClick={reset}>
            {t("cooking.timer.reset")}
          </Button>
        ) : null}
      </div>
    </div>
  );
}

/**
 * "Antes de começar": everything set out before the heat goes on, with both measures, and the
 * equipment. Checks share the cooking checklist keys (`i:<index>`).
 */
export function PrepScreen({
  recipe,
  checked,
  onToggle,
  onStart,
}: {
  recipe: ParsedNormalizedRecipe;
  checked: ReadonlySet<string>;
  onToggle: (key: string) => void;
  onStart: () => void;
}) {
  const { t } = useProductionI18n();
  const quantity = useRecipeQuantity();
  const household = useHouseholdMeasure();
  return (
    <section data-testid="cooking-prep" className="space-y-6">
      <div className="space-y-1">
        <h2 className="font-display text-2xl">
          {t("cooking.guide.prepTitle")}
        </h2>
        <p className="text-muted-foreground">{t("cooking.guide.prepHint")}</p>
      </div>
      <ul className="divide-y divide-border border-y border-border">
        {recipe.ingredients.map((ingredient, index) => {
          const key = `i:${index}`;
          const amount = quantity(ingredient);
          const kitchen = household(ingredient);
          return (
            <li
              key={
                ingredient.ingredientId ?? `${ingredient.displayName}-${index}`
              }
            >
              <label className="flex cursor-pointer items-start gap-3 py-3">
                <input
                  type="checkbox"
                  className="mt-1 h-5 w-5 shrink-0 accent-primary"
                  checked={checked.has(key)}
                  onChange={() => onToggle(key)}
                />
                <span className="flex-1">
                  <span
                    className={`block text-[17px] font-semibold ${checked.has(key) ? "text-muted-foreground line-through" : ""}`}
                  >
                    {ingredient.displayName}
                  </span>
                  <span className="block text-sm text-muted-foreground">
                    {[amount, kitchen].filter(Boolean).join(" · ")}
                    {ingredient.optional
                      ? ` · ${t("recipes.detail.optional")}`
                      : ""}
                  </span>
                </span>
              </label>
            </li>
          );
        })}
      </ul>
      {recipe.equipment.length > 0 ? (
        <div className="space-y-1">
          <h3 className="font-semibold">{t("cooking.guide.equipment")}</h3>
          <p className="text-muted-foreground">{recipe.equipment.join(", ")}</p>
        </div>
      ) : null}
      <Button
        type="button"
        size="lg"
        className="w-full"
        data-testid="cooking-start"
        onClick={onStart}
      >
        {t("cooking.guide.start")}
      </Button>
    </section>
  );
}

/** One stage of the recipe, large and alone on screen, with its look and an optional timer. */
export function StepScreen({
  recipe,
  index,
  onPrevious,
  onNext,
}: {
  recipe: ParsedNormalizedRecipe;
  index: number;
  onPrevious: () => void;
  onNext: () => void;
}) {
  const { t } = useProductionI18n();
  const stage = recipe.stages[index];
  const total = recipe.stages.length;
  const last = index === total - 1;
  if (!stage) return null;
  const timerMinutes =
    (stage.passiveMinutes ?? 0) > 0
      ? stage.passiveMinutes!
      : (stage.activeMinutes ?? 0);
  return (
    <section
      data-testid="cooking-step"
      aria-labelledby="cooking-step-title"
      className="space-y-6"
    >
      <div className="space-y-2">
        <p className="text-sm font-semibold text-muted-foreground">
          {t("cooking.guide.step", { n: index + 1, total })}
        </p>
        <div
          className="h-1.5 overflow-hidden rounded-full bg-secondary"
          aria-hidden="true"
        >
          <div
            className="h-full rounded-full bg-primary"
            style={{ width: `${((index + 1) / total) * 100}%` }}
          />
        </div>
        <h2 id="cooking-step-title" className="font-display text-3xl">
          {stage.title}
        </h2>
      </div>
      <div className="space-y-3">
        {stage.instructions.map((instruction, stepIndex) => (
          <p key={stepIndex} className="text-[19px] leading-relaxed">
            {instruction}
          </p>
        ))}
      </div>
      {stage.sensoryCues.length > 0 ? (
        <div className="rounded-2xl bg-accent p-4">
          <p className="font-semibold">{t("cooking.guide.look")}</p>
          <p className="mt-1">{stage.sensoryCues.join("; ")}</p>
        </div>
      ) : null}
      {timerMinutes > 0 ? (
        <StageTimer key={index} minutes={timerMinutes} />
      ) : null}
      <div className="flex gap-2">
        <Button
          type="button"
          variant="secondary"
          size="lg"
          data-testid="cooking-step-previous"
          onClick={onPrevious}
        >
          {t("cooking.guide.previous")}
        </Button>
        <Button
          type="button"
          size="lg"
          className="flex-1"
          data-testid={last ? "cooking-finish" : "cooking-step-next"}
          onClick={onNext}
        >
          {last ? t("cooking.guide.last") : t("cooking.guide.next")}
        </Button>
      </div>
    </section>
  );
}

/** Choice pills used by the finish screen; a radio group under the hood. */
function ChoicePills<T extends string>({
  legend,
  name,
  value,
  options,
  onChange,
}: {
  legend: string;
  name: string;
  value: T;
  options: Array<{ value: T; label: string }>;
  onChange: (value: T) => void;
}) {
  return (
    <fieldset>
      <legend className="mb-2 text-[17px] font-semibold">{legend}</legend>
      <div className="flex flex-wrap gap-2">
        {options.map((option) => (
          <label
            key={option.value}
            className={`relative inline-flex h-11 cursor-pointer items-center rounded-full px-4 text-[15px] font-semibold focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-2 ${
              value === option.value
                ? "bg-primary text-primary-foreground"
                : "bg-secondary text-secondary-foreground"
            }`}
          >
            <input
              type="radio"
              name={name}
              className="sr-only"
              data-testid={`${name}-${option.value}`}
              checked={value === option.value}
              onChange={() => onChange(option.value)}
            />
            {option.label}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/**
 * "Bom apetite!": one tap completes with the proposed pantry use; "Ajustar" opens the detailed
 * review. Leftovers and the rating are optional and shared with the review.
 */
export function FinishScreen({
  usedNames,
  servings,
  leftoverPortions,
  onLeftover,
  rating,
  onRating,
  busy,
  canSubmit,
  onDone,
  onAdjust,
  message,
}: {
  usedNames: string[];
  servings: number;
  leftoverPortions: number;
  onLeftover: (portions: number) => void;
  rating: string;
  onRating: (rating: string) => void;
  busy: boolean;
  canSubmit: boolean;
  onDone: () => void;
  onAdjust: () => void;
  message: string | null;
}) {
  const { t } = useProductionI18n();
  const portions = Array.from(
    { length: Math.min(servings, 3) },
    (_, index) => index + 1,
  );
  return (
    <section data-testid="cooking-finish-screen" className="space-y-7">
      <div className="space-y-3">
        <div
          aria-hidden="true"
          className="flex h-16 w-16 items-center justify-center rounded-full bg-success-soft text-3xl text-success"
        >
          ✓
        </div>
        <h2 className="font-display text-3xl">{t("cooking.finish.title")}</h2>
        <p
          className="text-[17px] text-muted-foreground"
          data-testid="cooking-finish-uses"
        >
          {usedNames.length > 0
            ? t("cooking.finish.uses", { items: usedNames.join(", ") })
            : t("cooking.finish.usesNothing")}
        </p>
      </div>
      <ChoicePills
        legend={t("cooking.finish.leftover")}
        name="cooking-leftover"
        value={String(leftoverPortions)}
        options={[
          { value: "0", label: t("cooking.finish.noLeftover") },
          ...portions.map((count) => ({
            value: String(count),
            label: t(
              count === 1
                ? "cooking.finish.portion.one"
                : "cooking.finish.portion.many",
              { count },
            ),
          })),
        ]}
        onChange={(value) => onLeftover(Number(value))}
      />
      <ChoicePills
        legend={t("cooking.finish.rating")}
        name="cooking-rating"
        value={rating}
        options={[
          { value: "2", label: t("cooking.finish.bad") },
          { value: "4", label: t("cooking.finish.good") },
          { value: "5", label: t("cooking.finish.great") },
        ]}
        onChange={onRating}
      />
      {message ? (
        <p role="alert" className="text-sm text-destructive">
          {t(message)}
        </p>
      ) : null}
      <div className="space-y-3">
        <Button
          type="button"
          size="lg"
          className="w-full"
          data-testid="cooking-done"
          disabled={busy || !canSubmit}
          onClick={onDone}
        >
          {busy ? t("cooking.confirming") : t("cooking.finish.done")}
        </Button>
        <button
          type="button"
          data-testid="cooking-adjust"
          className="w-full text-center text-[15px] font-semibold text-muted-foreground underline-offset-4 hover:underline"
          onClick={onAdjust}
        >
          {t("cooking.finish.adjust")}
        </button>
      </div>
    </section>
  );
}
