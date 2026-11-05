import { useState } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { ShoppingPlanRecent as RecentPlan } from "@/contracts/shoppingPlan";
import { usePlanningRepository } from "@/features/planning/PlanningProvider";

/** Longest menu name the backend accepts. */
const MAX_NAME = 80;

/**
 * Accepted plans to repeat (PLAN-0062). Repeating brings the plan's answers and its dishes that can
 * still be cooked. A name is the menu's own, so a plan worth keeping stays easy to find; finished
 * plans stay available either way.
 */
export function ShoppingPlanRecentList({
  plans,
  csrfToken,
  busy,
  onRepeat,
  hideTitle = false,
}: {
  plans: RecentPlan[];
  csrfToken: string | null | undefined;
  busy: boolean;
  /** Starts a new plan from this menu. */
  onRepeat: (menuId: string) => void;
  /** Keeps the title for assistive technology only, when a disclosure already shows it. */
  hideTitle?: boolean;
}) {
  const { t } = useProductionI18n();
  return (
    <section
      aria-labelledby="plan-recent-title"
      data-testid="plan-recent"
      className="space-y-2"
    >
      <h2
        id="plan-recent-title"
        className={hideTitle ? "sr-only" : "text-sm font-medium"}
      >
        {t("shoppingPlan.recent.title")}
      </h2>
      <ul className="surface divide-y divide-border overflow-hidden">
        {plans.map((plan) => (
          <RecentRow
            key={plan.menuId}
            plan={plan}
            csrfToken={csrfToken}
            busy={busy}
            onRepeat={() => onRepeat(plan.menuId)}
          />
        ))}
      </ul>
    </section>
  );
}

function RecentRow({
  plan,
  csrfToken,
  busy,
  onRepeat,
}: {
  plan: RecentPlan;
  csrfToken: string | null | undefined;
  busy: boolean;
  onRepeat: () => void;
}) {
  const { t } = useProductionI18n();
  const planning = usePlanningRepository();
  const [name, setName] = useState(plan.name);
  const [text, setText] = useState(plan.name);
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);
  const dishes = plan.dishes.join(", ");

  const save = async () => {
    if (!csrfToken || saving) return;
    setSaving(true);
    setFailed(false);
    try {
      const menu = await planning.changeMenu(
        plan.menuId,
        {
          name: text.trim() || null,
          startDate: plan.startDate,
          endDate: plan.endDate,
        },
        { csrfToken },
      );
      setName(menu.name);
      setEditing(false);
    } catch {
      setFailed(true);
    } finally {
      setSaving(false);
    }
  };

  return (
    <li data-testid="plan-recent-row" className="space-y-2 p-4">
      <p className="font-semibold" data-testid="plan-recent-name">
        {name || dishes}
      </p>
      <p className="text-sm text-muted-foreground">
        {name
          ? t("shoppingPlan.recent.meals", {
              count: plan.mealsTotal,
              dishes,
            })
          : t("shoppingPlan.meals.total", { count: plan.mealsTotal })}
      </p>
      {editing ? (
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void save();
          }}
        >
          <label className="min-w-40 flex-1 space-y-1 text-xs text-muted-foreground">
            {t("shoppingPlan.recent.nameLabel")}
            <Input
              autoFocus
              maxLength={MAX_NAME}
              data-testid="plan-recent-name-input"
              value={text}
              onChange={(event) => setText(event.target.value)}
            />
          </label>
          <Button
            type="submit"
            size="sm"
            variant="secondary"
            data-testid="plan-recent-name-save"
            disabled={saving}
          >
            {t("shoppingPlan.recent.nameSave")}
          </Button>
        </form>
      ) : null}
      {failed ? (
        <p role="alert" className="text-sm text-destructive">
          {t("shoppingPlan.error.save")}
        </p>
      ) : null}
      {editing ? null : (
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            size="sm"
            variant="secondary"
            data-testid="plan-repeat"
            disabled={busy || !csrfToken}
            onClick={onRepeat}
          >
            {t("shoppingPlan.recent.repeat")}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            data-testid="plan-recent-name-edit"
            onClick={() => setEditing(true)}
          >
            {t(
              name
                ? "shoppingPlan.recent.rename"
                : "shoppingPlan.recent.nameAction",
            )}
          </Button>
        </div>
      )}
    </li>
  );
}
