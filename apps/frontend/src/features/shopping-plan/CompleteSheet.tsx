import { useEffect, useRef, useState, type ReactNode } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import type { ShoppingPlan, ShoppingPlanCard } from "@/contracts/shoppingPlan";
import { BottomSheet } from "./BottomSheet";

/** Chosen card ids per meal type, one per missing meal. */
export type CompletePicks = Record<string, string[]>;

/**
 * One proposal per missing meal, from each meal type's offers in their order. A card is proposed at
 * most once across the menu, because lunch and dinner share offers. Earlier picks survive while
 * their card is still offered, so a swap is not undone when new offers arrive.
 */
export function proposePicks(
  plan: ShoppingPlan,
  previous: CompletePicks = {},
): CompletePicks {
  const used = new Set<string>();
  const picks: CompletePicks = {};
  for (const group of plan.groups) {
    const missing = Math.max(0, group.wanted - group.filled);
    const offered = new Set(group.options.map((card) => card.cardId));
    const ids = (previous[group.mealType] ?? [])
      .filter((id) => offered.has(id) && !used.has(id))
      .slice(0, missing);
    ids.forEach((id) => used.add(id));
    for (const card of group.options) {
      if (ids.length >= missing) break;
      if (used.has(card.cardId)) continue;
      ids.push(card.cardId);
      used.add(card.cardId);
    }
    picks[group.mealType] = ids;
  }
  return picks;
}

/** The next offer of the meal type after the current pick that is not proposed anywhere, or null. */
export function nextPick(
  plan: ShoppingPlan,
  picks: CompletePicks,
  mealType: string,
  index: number,
): string | null {
  const options = plan.groups.find(
    (group) => group.mealType === mealType,
  )?.options;
  if (!options) return null;
  const used = new Set(Object.values(picks).flat());
  const current = options.findIndex(
    (card) => card.cardId === picks[mealType]?.[index],
  );
  for (let step = 1; step <= options.length; step += 1) {
    const card = options[(current + step) % options.length];
    if (!used.has(card.cardId)) return card.cardId;
  }
  return null;
}

/**
 * "Completar" (PLAN-0065 Slice 3): proposes a dish for everything missing, each with a swap, and
 * changes nothing until "Usar estes". A meal type with fewer offers than missing meals asks for more
 * once when the sheet opens; what still has no offer says so. When some dishes cannot be added, the
 * sheet stays open and names them; the others stay chosen.
 *
 * @param running Whether a request for more options is under way.
 * @param onAsk Asks for more options of one meal type.
 * @param onConfirm Receives the dishes to choose.
 * @param notice Shown above the proposals, such as what could not be added.
 */
export function CompleteSheet({
  plan,
  open,
  onOpenChange,
  running,
  busy,
  onAsk,
  onConfirm,
  notice,
}: {
  plan: ShoppingPlan;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  running: boolean;
  busy: boolean;
  onAsk: (mealType: string, kind: "auto" | "more") => void;
  onConfirm: (
    choices: Array<{ cardId: string; mealType: string; name: string }>,
  ) => void;
  notice?: ReactNode;
}) {
  const { t } = useProductionI18n();
  const [picks, setPicks] = useState<CompletePicks>({});
  const asked = useRef(false);

  useEffect(() => {
    if (open) setPicks((previous) => proposePicks(plan, previous));
  }, [plan, open]);

  // Ask once per opening for each meal type that has fewer offers than it needs.
  useEffect(() => {
    if (!open) {
      asked.current = false;
      return;
    }
    if (asked.current) return;
    asked.current = true;
    for (const group of plan.groups) {
      const missing = group.wanted - group.filled;
      if (missing > group.options.length) onAsk(group.mealType, "auto");
    }
    // Only the opening matters; later offers arrive through `plan`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const cards = new Map<string, ShoppingPlanCard>(
    plan.groups.flatMap((group) =>
      group.options.map((card) => [card.cardId, card] as const),
    ),
  );
  const choices = plan.groups.flatMap((group) =>
    (picks[group.mealType] ?? [])
      .filter((id) => cards.has(id))
      .map((id) => ({
        cardId: id,
        mealType: group.mealType,
        name: cards.get(id)!.name,
      })),
  );

  const swap = (mealType: string, index: number) => {
    const next = nextPick(plan, picks, mealType, index);
    if (!next) {
      onAsk(mealType, "more");
      return;
    }
    setPicks((previous) => ({
      ...previous,
      [mealType]: (previous[mealType] ?? []).map((id, at) =>
        at === index ? next : id,
      ),
    }));
  };

  return (
    <BottomSheet
      open={open}
      onOpenChange={onOpenChange}
      testId="complete-sheet"
      closeLabel={t("menuBuild.sheet.close")}
      title={t("menuBuild.complete.title")}
      footer={
        <div className="pb-3">
          <Button
            type="button"
            size="lg"
            className="w-full"
            data-testid="complete-confirm"
            disabled={busy || choices.length === 0}
            onClick={() => onConfirm(choices)}
          >
            {t("menuBuild.complete.confirm", { count: choices.length })}
          </Button>
        </div>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-muted-foreground">
          {t("menuBuild.complete.intro")}
        </p>
        {notice}
        {plan.groups.map((group) => {
          const missing = Math.max(0, group.wanted - group.filled);
          if (missing === 0) return null;
          const ids = picks[group.mealType] ?? [];
          return (
            <section
              key={group.mealType}
              aria-labelledby={`complete-${group.mealType}`}
              className="space-y-1"
            >
              <h3
                id={`complete-${group.mealType}`}
                className="text-sm font-semibold text-muted-foreground"
              >
                {t(`menuBuild.meal.plural.${group.mealType}`)}
              </h3>
              <ul className="divide-y divide-border">
                {Array.from({ length: missing }, (_, index) => {
                  const card = cards.get(ids[index] ?? "");
                  return (
                    <li
                      key={index}
                      data-testid="complete-row"
                      className="flex min-h-14 items-center gap-3 py-2"
                    >
                      {card ? (
                        <>
                          <div className="min-w-0 flex-1">
                            <p className="line-clamp-2 font-medium">
                              {card.name}
                            </p>
                            <p className="text-sm text-muted-foreground">
                              {card.totalMinutes
                                ? t("recipes.generate.minutes", {
                                    count: card.totalMinutes,
                                  })
                                : null}
                            </p>
                          </div>
                          <Button
                            type="button"
                            variant="secondary"
                            size="sm"
                            data-testid="complete-swap"
                            aria-label={t("menuBuild.complete.swap.label", {
                              dish: card.name,
                            })}
                            disabled={busy}
                            onClick={() => swap(group.mealType, index)}
                          >
                            {t("menuBuild.complete.swap")}
                          </Button>
                        </>
                      ) : (
                        <p className="text-sm text-muted-foreground">
                          {running
                            ? t("menuBuild.complete.searching")
                            : t("menuBuild.complete.none")}
                        </p>
                      )}
                    </li>
                  );
                })}
              </ul>
            </section>
          );
        })}
      </div>
    </BottomSheet>
  );
}
