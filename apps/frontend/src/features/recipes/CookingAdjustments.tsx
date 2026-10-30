import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import {
  formatLocaleDecimal,
  parseLocaleDecimal,
  type SupportedDecimalLocale,
} from "@/lib/localeDecimal";
import { useRecipeRepository } from "./RecipeProvider";
import type {
  RecipeExecution,
  RecipeExecutionAdjustment,
} from "@/contracts/recipes";

type Choice = "none" | RecipeExecutionAdjustment["kind"];

const UNIT_KEYS: Record<string, string> = {
  Gram: "inventory.unit.Gram",
  Milliliter: "inventory.unit.Milliliter",
  Unit: "inventory.unit.Unit",
};

/**
 * "Mudou alguma coisa?" while cooking (PLAN-0042 Phase 2): for each ingredient the cook can say
 * they used something else, used a different amount, or left it out. Every change is saved on the
 * session right away, changes what the completion proposes, and can later be carried into the
 * recipe from the finished screen. The recipe itself never changes here.
 *
 * Collapsed by default in a native `<details>`; each ingredient opens its own small form with a
 * labelled radio group. Errors are announced with `role="alert"`.
 *
 * @param execution The in-progress session (its proposal lists the ingredients).
 * @param onChanged Receives the session returned after a save, with its new proposal.
 */
export function CookingAdjustments({
  execution,
  onChanged,
}: {
  execution: RecipeExecution;
  onChanged: (next: RecipeExecution) => void;
}) {
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const repo = useRecipeRepository();
  const formId = useId();
  const [open, setOpen] = useState<string | null>(null);
  const [choice, setChoice] = useState<Choice>("none");
  const [name, setName] = useState("");
  const [amount, setAmount] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const lines = execution.proposal?.lines ?? [];
  const byId = new Map(
    execution.adjustments.map((item) => [item.ingredientId, item]),
  );
  const decimal = (value: number) =>
    formatLocaleDecimal(value, locale as SupportedDecimalLocale);

  if (lines.length === 0) return null;

  function edit(ingredientId: string) {
    const current = byId.get(ingredientId);
    setOpen(ingredientId);
    setChoice(current?.kind ?? "none");
    setName(current?.substituteName ?? "");
    setAmount(
      current?.quantity !== null && current?.quantity !== undefined
        ? decimal(current.quantity)
        : "",
    );
    setError(null);
  }

  async function save(ingredientId: string) {
    if (!session.csrfToken) return;
    let next: RecipeExecutionAdjustment | null = null;
    const parsed = amount.trim()
      ? parseLocaleDecimal(amount, locale as SupportedDecimalLocale)
      : null;
    if (parsed && (!parsed.ok || parsed.value <= 0)) {
      setError("cooking.adjust.error.amount");
      return;
    }
    const quantity = parsed && parsed.ok ? parsed.value : null;
    if (choice === "substitute") {
      if (!name.trim()) {
        setError("cooking.adjust.error.name");
        return;
      }
      next = {
        ingredientId,
        kind: "substitute",
        substituteName: name.trim(),
        quantity,
      };
    } else if (choice === "quantity") {
      if (quantity === null) {
        setError("cooking.adjust.error.amount");
        return;
      }
      next = { ingredientId, kind: "quantity", substituteName: null, quantity };
    } else if (choice === "skip") {
      next = {
        ingredientId,
        kind: "skip",
        substituteName: null,
        quantity: null,
      };
    }
    const adjustments = [
      ...execution.adjustments.filter(
        (item) => item.ingredientId !== ingredientId,
      ),
      ...(next ? [next] : []),
    ];
    setSaving(true);
    setError(null);
    try {
      onChanged(
        await repo.setExecutionAdjustments(execution.executionId, adjustments, {
          csrfToken: session.csrfToken,
        }),
      );
      setOpen(null);
    } catch {
      setError("cooking.adjust.error.save");
    } finally {
      setSaving(false);
    }
  }

  function summary(adjustment: RecipeExecutionAdjustment, unit: string) {
    const amountText =
      adjustment.quantity !== null
        ? `${decimal(adjustment.quantity)} ${t(UNIT_KEYS[unit] ?? "inventory.unit.Unit")}`
        : "";
    if (adjustment.kind === "skip") return t("cooking.adjust.summary.skip");
    if (adjustment.kind === "quantity")
      return t("cooking.adjust.summary.quantity", { amount: amountText });
    return (
      t("cooking.adjust.summary.substitute", {
        name: adjustment.substituteName ?? "",
      }) + (amountText ? ` · ${amountText}` : "")
    );
  }

  return (
    <details
      className="rounded-2xl border border-border bg-card p-4"
      data-testid="cooking-adjustments"
    >
      <summary className="cursor-pointer font-medium">
        {t("cooking.adjust.title")}
        {execution.adjustments.length > 0 ? (
          <span className="ml-2 text-sm text-muted-foreground">
            {t("cooking.adjust.count", { count: execution.adjustments.length })}
          </span>
        ) : null}
      </summary>
      <p className="mt-2 text-sm text-muted-foreground">
        {t("cooking.adjust.hint")}
      </p>
      <ul className="mt-3 divide-y divide-border">
        {lines.map((line) => {
          const current = byId.get(line.ingredientId);
          const label = line.originalName ?? line.displayName;
          return (
            <li
              key={line.ingredientId}
              className="space-y-2 py-3"
              data-testid={`cooking-adjust-${line.ingredientId}`}
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span>
                  <span className="font-medium">{label}</span>
                  {current ? (
                    <span className="ml-2 text-sm text-muted-foreground">
                      {summary(current, line.unit)}
                    </span>
                  ) : null}
                </span>
                {open !== line.ingredientId ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    aria-label={`${t("cooking.adjust.edit")}: ${label}`}
                    onClick={() => edit(line.ingredientId)}
                  >
                    {t("cooking.adjust.edit")}
                  </Button>
                ) : null}
              </div>
              {open === line.ingredientId ? (
                <div className="space-y-3 rounded-xl bg-secondary/40 p-3">
                  <div
                    role="radiogroup"
                    aria-label={t("cooking.adjust.what", { name: label })}
                    className="flex flex-col gap-2"
                  >
                    {(["substitute", "quantity", "skip", "none"] as const).map(
                      (option) => (
                        <label key={option} className="flex items-center gap-2">
                          <input
                            type="radio"
                            name={`${formId}-${line.ingredientId}`}
                            checked={choice === option}
                            onChange={() => setChoice(option)}
                          />
                          {t(`cooking.adjust.choice.${option}`)}
                        </label>
                      ),
                    )}
                  </div>
                  {choice === "substitute" ? (
                    <label className="block space-y-1 text-sm">
                      {t("cooking.adjust.substituteName")}
                      <Input
                        value={name}
                        maxLength={120}
                        onChange={(event) => setName(event.target.value)}
                        data-testid="cooking-adjust-name"
                      />
                    </label>
                  ) : null}
                  {choice === "substitute" || choice === "quantity" ? (
                    <label className="block space-y-1 text-sm">
                      {t(
                        choice === "quantity"
                          ? "cooking.adjust.amount"
                          : "cooking.adjust.amountOptional",
                        {
                          unit: t(
                            UNIT_KEYS[line.unit] ?? "inventory.unit.Unit",
                          ),
                        },
                      )}
                      <Input
                        inputMode="decimal"
                        value={amount}
                        onChange={(event) => setAmount(event.target.value)}
                        data-testid="cooking-adjust-amount"
                      />
                    </label>
                  ) : null}
                  {error ? (
                    <p role="alert" className="text-sm text-destructive">
                      {t(error)}
                    </p>
                  ) : null}
                  <div className="flex gap-2">
                    <Button
                      type="button"
                      size="sm"
                      disabled={saving}
                      onClick={() => void save(line.ingredientId)}
                      data-testid="cooking-adjust-save"
                    >
                      {t("cooking.adjust.save")}
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      disabled={saving}
                      onClick={() => setOpen(null)}
                    >
                      {t("cooking.adjust.cancel")}
                    </Button>
                  </div>
                </div>
              ) : null}
            </li>
          );
        })}
      </ul>
    </details>
  );
}
