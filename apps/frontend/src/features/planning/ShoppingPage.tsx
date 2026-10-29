import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import type {
  MeasuredUnit,
  StorageLocation,
} from "@/adapters/live/inventoryTypes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import type { ShoppingItem, ShoppingList } from "@/contracts/planning";
import { useInventoryRepository } from "@/features/inventory/InventoryProvider";
import { formatLocaleDecimal, parseLocaleDecimal } from "@/lib/localeDecimal";
import { formatMenuDay, localIsoDate } from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";

const CATEGORIES = [
  "required",
  "assumed",
  "optional",
  "runs_out",
  "available",
] as const;
const UNIT_KEYS: Record<string, string> = {
  Gram: "recipes.unit.g",
  Milliliter: "recipes.unit.ml",
  Unit: "recipes.unit.unit",
};

/**
 * Shopping list of a menu (PLAN-0029): derived by the backend from the meals still to cook (from
 * the owner's current day) and the pantry. Checking a line off only remembers it. Recording a
 * purchase creates a pantry lot through the normal inventory command, and the list is
 * recalculated from the new pantry. No package size is assumed.
 */
export function ShoppingPage() {
  const { menuId = "" } = useParams<{ menuId: string }>();
  const repo = usePlanningRepository();
  const { t } = useProductionI18n();
  const { session } = useSession();
  const [list, setList] = useState<ShoppingList | null>(null);
  const [failed, setFailed] = useState(false);
  const [reload, setReload] = useState(0);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    repo.getShopping(menuId, localIsoDate(), controller.signal).then(
      (value) => !controller.signal.aborted && setList(value),
      () => !controller.signal.aborted && setFailed(true),
    );
    return () => controller.abort();
  }, [repo, menuId, reload]);

  const toggle = async (item: ShoppingItem) => {
    if (!session.csrfToken || !list) return;
    setError(null);
    const checked = !item.checked;
    setList({
      ...list,
      items: list.items.map((line) =>
        line.itemKey === item.itemKey ? { ...line, checked } : line,
      ),
    });
    try {
      await repo.setChecked(menuId, item.itemKey, checked, {
        csrfToken: session.csrfToken,
      });
    } catch {
      setError("planning.error.save");
      setReload((value) => value + 1);
    }
  };

  const purchased = useCallback(() => setReload((value) => value + 1), []);

  if (failed) {
    return (
      <div data-testid="shopping-error" className="space-y-4">
        <FeatureUnavailable
          feature="planning-shopping"
          title={t("feature.unavailable")}
          detail={t("planning.error.load")}
        />
        <Button asChild variant="secondary">
          <Link to={`/app/cardapio/${menuId}`}>{t("shopping.back")}</Link>
        </Button>
      </div>
    );
  }
  if (!list) return <p role="status">{t("planning.loading")}</p>;

  return (
    <div data-testid="shopping-page" className="space-y-6">
      <div className="space-y-2">
        <Link to={`/app/cardapio/${menuId}`} className="text-sm underline">
          {t("shopping.back")}
        </Link>
        <h1 className="font-display text-3xl">{t("shopping.title")}</h1>
        <p className="text-sm text-muted-foreground">{t("shopping.hint")}</p>
      </div>
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(error)}
        </p>
      ) : null}
      {list.mealCount === 0 ? (
        <p data-testid="shopping-empty" className="text-muted-foreground">
          {t("shopping.empty")}
        </p>
      ) : (
        CATEGORIES.map((category) => {
          const items = list.items.filter((item) => item.category === category);
          if (items.length === 0) return null;
          return (
            <section
              key={category}
              aria-labelledby={`shopping-${category}`}
              data-testid={`shopping-${category}`}
              className="space-y-2"
            >
              <h2 id={`shopping-${category}`} className="font-display text-xl">
                {t(`shopping.category.${category}`)}
              </h2>
              <ul className="space-y-2">
                {items.map((item) => (
                  <ShoppingRow
                    key={item.itemKey}
                    item={item}
                    onToggle={() => void toggle(item)}
                    onPurchased={purchased}
                  />
                ))}
              </ul>
            </section>
          );
        })
      )}
    </div>
  );
}

function ShoppingRow({
  item,
  onToggle,
  onPurchased,
}: {
  item: ShoppingItem;
  onToggle: () => void;
  onPurchased: () => void;
}) {
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const inventory = useInventoryRepository();
  const [buying, setBuying] = useState(false);
  const [quantity, setQuantity] = useState(
    formatLocaleDecimal(item.toBuyQuantity, locale),
  );
  const [storage, setStorage] = useState<StorageLocation>("Pantry");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(false);
  // One key per purchase form, so a retried request creates one lot.
  const keyRef = useRef(crypto.randomUUID());
  const unit = t(UNIT_KEYS[item.unit] ?? "recipes.unit.unit");
  const amount = (value: number) =>
    `${formatLocaleDecimal(value, locale)} ${unit}`;
  const toBuy = item.toBuyQuantity > 0;

  const record = async () => {
    const parsed = parseLocaleDecimal(quantity, locale);
    if (!session.csrfToken || !parsed.ok || parsed.value <= 0) {
      setError(true);
      return;
    }
    setSaving(true);
    setError(false);
    try {
      await inventory.createLot(
        {
          productName: item.displayName,
          quantity: {
            kind: "measured",
            value: parsed.value,
            unit: item.unit as MeasuredUnit,
          },
          storageLocation: storage,
        },
        { csrfToken: session.csrfToken, idempotencyKey: keyRef.current },
      );
      setBuying(false);
      keyRef.current = crypto.randomUUID();
      onPurchased();
    } catch {
      setError(true);
    } finally {
      setSaving(false);
    }
  };

  return (
    <li
      data-testid="shopping-item"
      className="space-y-2 rounded-xl border border-border p-3"
    >
      <div className="flex items-start gap-3">
        <input
          type="checkbox"
          className="mt-1 h-4 w-4 shrink-0"
          data-testid="shopping-item-check"
          aria-label={item.displayName}
          checked={item.checked}
          onChange={onToggle}
        />
        <div className="flex-1 space-y-1">
          <p className={`font-medium ${item.checked ? "line-through" : ""}`}>
            {item.displayName}
            {toBuy ? ` — ${amount(item.toBuyQuantity)}` : ""}
          </p>
          <p className="text-xs text-muted-foreground">
            {t("shopping.need", {
              need: amount(item.requiredQuantity),
              have: amount(item.availableQuantity),
            })}
          </p>
          <p className="text-xs text-muted-foreground">
            {item.uses
              .map(
                (use) =>
                  `${use.recipeName}${use.plannedDate ? ` (${formatMenuDay(use.plannedDate, locale)})` : ""}`,
              )
              .join(" · ")}
          </p>
        </div>
        {toBuy ? (
          <Button
            type="button"
            size="sm"
            variant="secondary"
            data-testid="shopping-item-buy"
            aria-expanded={buying}
            onClick={() => setBuying((value) => !value)}
          >
            {t("shopping.bought")}
          </Button>
        ) : null}
      </div>
      {buying ? (
        <form
          className="grid gap-2 sm:grid-cols-3 sm:items-end"
          onSubmit={(event) => {
            event.preventDefault();
            void record();
          }}
        >
          <label className="space-y-1 text-xs text-muted-foreground">
            {t("shopping.boughtQuantity", { unit })}
            <Input
              inputMode="decimal"
              data-testid="shopping-buy-quantity"
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
            />
          </label>
          <label className="space-y-1 text-xs text-muted-foreground">
            {t("shopping.storage")}
            <select
              className="h-10 w-full rounded-md border border-input bg-background px-2 text-sm text-foreground"
              value={storage}
              onChange={(event) =>
                setStorage(event.target.value as StorageLocation)
              }
            >
              {(["Pantry", "Refrigerator", "Freezer"] as const).map(
                (location) => (
                  <option key={location} value={location}>
                    {t(`inventory.location.${location}`)}
                  </option>
                ),
              )}
            </select>
          </label>
          <Button
            type="submit"
            size="sm"
            data-testid="shopping-buy-confirm"
            disabled={saving}
          >
            {saving ? t("planning.saving") : t("shopping.addToPantry")}
          </Button>
          {error ? (
            <p role="alert" className="text-xs text-destructive sm:col-span-3">
              {t("shopping.buyError")}
            </p>
          ) : null}
        </form>
      ) : null}
    </li>
  );
}
