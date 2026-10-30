import { useCallback, useEffect, useRef, useState } from "react";
import {
  Beef,
  Carrot,
  ChevronDown,
  ChevronLeft,
  Croissant,
  CupSoda,
  Milk,
  Package,
  PackageCheck,
  ShoppingCart,
  Wheat,
} from "lucide-react";
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

/** Lines still to buy, grouped by store section; the rest keep their own blocks below. */
const TO_BUY = ["required", "assumed", "optional"] as const;
const OWNED = ["runs_out", "available"] as const;
/** Store sections in the order of a typical shopping trip (PLAN-0037 Phase 3). */
const STORE_SECTIONS = [
  "produce",
  "butcher_fish",
  "dairy_eggs",
  "bakery",
  "dry_goods",
  "drinks",
  "other",
] as const;
/** An icon per store section, so the list reads like the aisles of a shop (PLAN-0058). */
const SECTION_ICON: Record<string, typeof Package> = {
  produce: Carrot,
  butcher_fish: Beef,
  dairy_eggs: Milk,
  bakery: Croissant,
  dry_goods: Wheat,
  drinks: CupSoda,
  other: Package,
};
/** Sections whose purchases usually go to the fridge; the person can still change the place. */
const COLD_SECTIONS = new Set(["butcher_fish", "dairy_eggs"]);
/** Sections whose extra purchase may spoil before another use, so the impact suggests freezing. */
const PERISHABLE_SECTIONS = new Set([
  "produce",
  "butcher_fish",
  "dairy_eggs",
  "bakery",
]);
const UNIT_KEYS: Record<string, string> = {
  Gram: "recipes.unit.g",
  Milliliter: "recipes.unit.ml",
  Unit: "recipes.unit.unit",
};

/** A purchase just recorded, compared with the line as it was before it (PLAN-0029 Phase 4). */
interface PurchaseImpact {
  item: ShoppingItem;
  bought: number;
}

/**
 * Shopping list of a menu (PLAN-0029): derived by the backend from the meals still to cook (from
 * the owner's current day) and the pantry. Checking a line off only remembers it. Recording a
 * purchase creates a pantry lot through the normal inventory command, and the list is
 * recalculated from the new pantry. No package size is assumed: the owner may confirm one per
 * item, and the line then shows whole packages and the expected remainder (Phase 4). After a
 * purchase the page says what it means for the menu (planned versus actual) without changing the
 * menu.
 */
export function ShoppingPage({
  menuId: fixedMenuId,
}: { menuId?: string } = {}) {
  const params = useParams<{ menuId: string }>();
  const menuId = fixedMenuId ?? params.menuId ?? "";
  const repo = usePlanningRepository();
  const { t } = useProductionI18n();
  const { session } = useSession();
  const [list, setList] = useState<ShoppingList | null>(null);
  const [failed, setFailed] = useState(false);
  const [reload, setReload] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [impact, setImpact] = useState<PurchaseImpact | null>(null);

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

  const refresh = useCallback(() => setReload((value) => value + 1), []);
  const purchased = useCallback(
    (item: ShoppingItem, bought: number) => {
      setImpact({ item, bought });
      refresh();
    },
    [refresh],
  );

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
        <Link
          to={`/app/cardapio/${menuId}`}
          className="inline-flex items-center gap-1 text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline"
        >
          <ChevronLeft aria-hidden="true" className="h-4 w-4" />
          {t("shopping.back")}
        </Link>
        <h1 className="font-display text-3xl">{t("shopping.title")}</h1>
        <p className="text-sm text-muted-foreground">{t("shopping.hint")}</p>
      </div>
      {list.mealCount > 0 ? <ShoppingSummary items={list.items} /> : null}
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(error)}
        </p>
      ) : null}
      {impact ? (
        <PurchaseImpactNotice
          impact={impact}
          after={list.items.find(
            (item) => item.itemKey === impact.item.itemKey,
          )}
          menuId={menuId}
          onDismiss={() => setImpact(null)}
          onPackageSaved={refresh}
        />
      ) : null}
      {list.mealCount === 0 ? (
        <p data-testid="shopping-empty" className="text-muted-foreground">
          {t("shopping.empty")}
        </p>
      ) : (
        [
          ...STORE_SECTIONS.map((section) => ({
            id: `section-${section}`,
            title: t(`shopping.section.${section}`),
            items: list.items
              .filter(
                (item) =>
                  (TO_BUY as readonly string[]).includes(item.category) &&
                  (item.storeSection ?? "other") === section,
              )
              .sort(
                (a, b) =>
                  TO_BUY.indexOf(a.category as (typeof TO_BUY)[number]) -
                  TO_BUY.indexOf(b.category as (typeof TO_BUY)[number]),
              ),
          })),
        ].map(({ id, title, items }) => {
          if (items.length === 0) return null;
          const Icon = SECTION_ICON[id.replace("section-", "")] ?? Package;
          return (
            <section
              key={id}
              aria-labelledby={`shopping-${id}`}
              data-testid={`shopping-${id}`}
              className="space-y-2"
            >
              <h2
                id={`shopping-${id}`}
                className="flex items-center gap-2 text-sm font-semibold text-muted-foreground"
              >
                <Icon aria-hidden="true" className="h-4 w-4" />
                {title}
                <span className="tabular rounded-full bg-secondary px-2 text-xs">
                  {items.length}
                </span>
              </h2>
              <ul className="surface divide-y divide-border overflow-hidden">
                {items.map((item) => (
                  <ShoppingRow
                    key={item.itemKey}
                    item={item}
                    onToggle={() => void toggle(item)}
                    onPurchased={(bought) => purchased(item, bought)}
                    onPackageChanged={refresh}
                  />
                ))}
              </ul>
            </section>
          );
        })
      )}
      {/* What the pantry covers is reference, not a task: it stays one tap away (PLAN-0058). */}
      {list.mealCount > 0 &&
      list.items.some((item) =>
        (OWNED as readonly string[]).includes(item.category),
      ) ? (
        <details
          className="surface group overflow-hidden"
          data-testid="shopping-owned"
        >
          <summary className="flex cursor-pointer list-none items-center justify-between gap-2 p-4 font-semibold [&::-webkit-details-marker]:hidden">
            <span className="flex items-center gap-2">
              <PackageCheck
                aria-hidden="true"
                className="h-5 w-5 text-success"
              />
              {t("shopping.owned.title", {
                count: list.items.filter((item) =>
                  (OWNED as readonly string[]).includes(item.category),
                ).length,
              })}
            </span>
            <ChevronDown
              aria-hidden="true"
              className="h-5 w-5 text-muted-foreground transition-transform duration-150 group-open:rotate-180"
            />
          </summary>
          {OWNED.map((category) => {
            const items = list.items.filter(
              (item) => item.category === category,
            );
            if (items.length === 0) return null;
            return (
              <section
                key={category}
                aria-labelledby={`shopping-${category}`}
                data-testid={`shopping-${category}`}
                className="border-t border-border"
              >
                <h2
                  id={`shopping-${category}`}
                  className="px-4 pt-3 text-sm font-semibold text-muted-foreground"
                >
                  {t(`shopping.category.${category}`)}
                </h2>
                <ul className="divide-y divide-border">
                  {items.map((item) => (
                    <ShoppingRow
                      key={item.itemKey}
                      item={item}
                      onToggle={() => void toggle(item)}
                      onPurchased={(bought) => purchased(item, bought)}
                      onPackageChanged={refresh}
                    />
                  ))}
                </ul>
              </section>
            );
          })}
        </details>
      ) : null}
    </div>
  );
}

/**
 * Progress of the trip (PLAN-0058): how many lines are left to buy and how many are checked
 * off, with a bar. Purely a reading of the list; it changes nothing.
 */
function ShoppingSummary({ items }: { items: ShoppingItem[] }) {
  const { t } = useProductionI18n();
  const toBuy = items.filter((item) =>
    (TO_BUY as readonly string[]).includes(item.category),
  );
  const done = toBuy.filter((item) => item.checked).length;
  const percent =
    toBuy.length === 0 ? 100 : Math.round((done / toBuy.length) * 100);
  return (
    <div
      className="surface flex items-center gap-4 p-4"
      data-testid="shopping-summary"
    >
      <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-accent text-accent-foreground">
        <ShoppingCart aria-hidden="true" className="h-6 w-6" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="font-semibold">
          {toBuy.length === 0
            ? t("shopping.summary.none")
            : t("shopping.summary.toBuy", { count: toBuy.length })}
        </p>
        <p className="text-sm text-muted-foreground">
          {t("shopping.summary.checked", { done, total: toBuy.length })}
        </p>
        <div
          className="mt-2 h-2 overflow-hidden rounded-full bg-secondary"
          aria-hidden="true"
        >
          <div
            className="h-full rounded-full bg-primary transition-[width] duration-200"
            style={{ width: `${percent}%` }}
          />
        </div>
      </div>
    </div>
  );
}

/**
 * What a recorded purchase means for the menu: covered, still short for named meals (the rest
 * stays on the list; the menu is never changed here), or more than the menu needs (with a
 * freezing hint for perishable sections and a one-tap way to remember the amount as the
 * package size). Announced politely with `role="status"`.
 */
function PurchaseImpactNotice({
  impact,
  after,
  menuId,
  onDismiss,
  onPackageSaved,
}: {
  impact: PurchaseImpact;
  after: ShoppingItem | undefined;
  menuId: string;
  onDismiss: () => void;
  onPackageSaved: () => void;
}) {
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const repo = usePlanningRepository();
  const [saved, setSaved] = useState(false);
  const [failed, setFailed] = useState(false);
  const { item, bought } = impact;
  const unit = t(UNIT_KEYS[item.unit] ?? "recipes.unit.unit");
  const amount = (value: number) =>
    `${formatLocaleDecimal(value, locale)} ${unit}`;
  const short = after?.toBuyQuantity ?? 0;
  const surplus = Math.max(0, bought - item.toBuyQuantity);
  const shortMeals = (after?.uses ?? [])
    .filter((use) => (use.missingQuantity ?? 0) > 0)
    .map(
      (use) =>
        `${use.recipeName}${use.plannedDate ? ` (${formatMenuDay(use.plannedDate, locale)})` : ""}`,
    )
    .join(", ");

  const remember = async () => {
    if (!session.csrfToken || !repo.setPackageSize) return;
    setFailed(false);
    try {
      await repo.setPackageSize(item.itemKey, bought, {
        csrfToken: session.csrfToken,
      });
      setSaved(true);
      onPackageSaved();
    } catch {
      setFailed(true);
    }
  };

  return (
    <div
      role="status"
      data-testid="shopping-impact"
      className="space-y-2 rounded-2xl border border-border bg-secondary/40 p-4 text-sm"
    >
      {short > 0 ? (
        <>
          <p>
            {t("shopping.impact.short", {
              item: item.displayName,
              amount: amount(short),
              meals: shortMeals,
            })}
          </p>
          <p className="text-muted-foreground">
            {t("shopping.impact.shortHint")}
          </p>
        </>
      ) : surplus > 0 ? (
        <>
          <p>
            {t("shopping.impact.surplus", {
              item: item.displayName,
              amount: amount(surplus),
            })}
          </p>
          {PERISHABLE_SECTIONS.has(item.storeSection ?? "") ? (
            <p className="text-muted-foreground">
              {t("shopping.impact.perishable")}
            </p>
          ) : null}
        </>
      ) : (
        <p>{t("shopping.impact.covered", { item: item.displayName })}</p>
      )}
      {failed ? (
        <p role="alert" className="text-destructive">
          {t("shopping.package.saveError")}
        </p>
      ) : null}
      <div className="flex flex-wrap gap-2">
        {surplus > 0 && !item.packageSize && repo.setPackageSize && !saved ? (
          <Button
            type="button"
            size="sm"
            variant="secondary"
            data-testid="shopping-impact-remember"
            onClick={() => void remember()}
          >
            {t("shopping.impact.rememberPackage", { amount: amount(bought) })}
          </Button>
        ) : null}
        {short > 0 ? (
          <Button asChild size="sm" variant="secondary">
            <Link to={`/app/cardapio/${menuId}`}>
              {t("shopping.impact.openMenu")}
            </Link>
          </Button>
        ) : null}
        <Button type="button" size="sm" variant="ghost" onClick={onDismiss}>
          {t("shopping.impact.dismiss")}
        </Button>
      </div>
    </div>
  );
}

function ShoppingRow({
  item,
  onToggle,
  onPurchased,
  onPackageChanged,
}: {
  item: ShoppingItem;
  onToggle: () => void;
  onPurchased: (bought: number) => void;
  onPackageChanged: () => void;
}) {
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const inventory = useInventoryRepository();
  const planning = usePlanningRepository();
  const [buying, setBuying] = useState(false);
  // With a confirmed package size, the purchase defaults to the whole packages that cover the line.
  const suggested = formatLocaleDecimal(
    item.packageCount && item.packageSize
      ? item.packageCount * item.packageSize
      : item.toBuyQuantity,
    locale,
  );
  const [quantity, setQuantity] = useState(suggested);
  const [editingPackage, setEditingPackage] = useState(false);
  const [packageText, setPackageText] = useState(
    item.packageSize ? formatLocaleDecimal(item.packageSize, locale) : "",
  );
  const [packageError, setPackageError] = useState<string | null>(null);
  const [storage, setStorage] = useState<StorageLocation>(
    COLD_SECTIONS.has(item.storeSection ?? "") ? "Refrigerator" : "Pantry",
  );
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
      onPurchased(parsed.value);
    } catch {
      setError(true);
    } finally {
      setSaving(false);
    }
  };

  const savePackage = async (size: number | null) => {
    if (!session.csrfToken || !planning.setPackageSize) return;
    setPackageError(null);
    try {
      await planning.setPackageSize(item.itemKey, size, {
        csrfToken: session.csrfToken,
      });
      setEditingPackage(false);
      onPackageChanged();
    } catch {
      setPackageError("shopping.package.saveError");
    }
  };

  const submitPackage = () => {
    const parsed = parseLocaleDecimal(packageText, locale);
    if (!parsed.ok || parsed.value <= 0 || parsed.value > 100000) {
      setPackageError("shopping.package.error");
      return;
    }
    void savePackage(parsed.value);
  };

  return (
    <li data-testid="shopping-item" className="space-y-3 px-4 py-3.5">
      <div className="flex items-start gap-3">
        <input
          type="checkbox"
          className="mt-0.5 h-6 w-6 shrink-0 cursor-pointer accent-primary"
          data-testid="shopping-item-check"
          aria-label={item.displayName}
          checked={item.checked}
          onChange={onToggle}
        />
        <div className="min-w-0 flex-1 space-y-1">
          <p className="flex items-baseline justify-between gap-3">
            <span
              className={`text-[16px] font-semibold ${item.checked ? "text-muted-foreground line-through" : ""}`}
            >
              {item.displayName}
              {item.category === "assumed" || item.category === "optional" ? (
                <span className="ml-2 rounded-full bg-secondary px-2 py-0.5 align-middle text-xs font-medium text-secondary-foreground">
                  {t(`shopping.badge.${item.category}`)}
                </span>
              ) : null}
            </span>
            {toBuy ? (
              <span
                data-testid="shopping-item-amount"
                className="tabular shrink-0 font-semibold"
              >
                {amount(item.toBuyQuantity)}
              </span>
            ) : null}
          </p>
          <p className="text-xs text-muted-foreground">
            {t("shopping.need", {
              need: amount(item.requiredQuantity),
              have:
                (item.availableIsEstimate ? "~" : "") +
                amount(item.availableQuantity),
            })}
          </p>
          {toBuy && item.packageCount && item.packageSize ? (
            <p
              className="text-xs text-muted-foreground"
              data-testid="shopping-item-package"
            >
              {t("shopping.package.summary", {
                count: item.packageCount,
                size: amount(item.packageSize),
                remainder: amount(item.expectedRemainder ?? 0),
              })}
            </p>
          ) : null}
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
            className="shrink-0"
            data-testid="shopping-item-buy"
            aria-expanded={buying}
            onClick={() => {
              if (!buying) setQuantity(suggested);
              setBuying((value) => !value);
            }}
          >
            {t("shopping.bought")}
          </Button>
        ) : null}
      </div>
      {/* The package size is offered where it matters: when buying, or to change a known size
          (review M7). */}
      {toBuy &&
      planning.setPackageSize &&
      (item.packageSize || buying || editingPackage) ? (
        editingPackage ? (
          <form
            className="grid gap-2 sm:grid-cols-3 sm:items-end"
            data-testid="shopping-package-form"
            onSubmit={(event) => {
              event.preventDefault();
              submitPackage();
            }}
          >
            <label className="space-y-1 text-xs text-muted-foreground">
              {t("shopping.package.label", { unit })}
              <Input
                inputMode="decimal"
                data-testid="shopping-package-size"
                value={packageText}
                onChange={(event) => setPackageText(event.target.value)}
              />
            </label>
            <Button type="submit" size="sm" data-testid="shopping-package-save">
              {t("shopping.package.save")}
            </Button>
            {item.packageSize ? (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={() => void savePackage(null)}
              >
                {t("shopping.package.remove")}
              </Button>
            ) : null}
            {packageError ? (
              <p
                role="alert"
                className="text-xs text-destructive sm:col-span-3"
              >
                {t(packageError)}
              </p>
            ) : null}
          </form>
        ) : (
          <button
            type="button"
            className="ml-8 text-xs underline underline-offset-4"
            data-testid="shopping-package-edit"
            onClick={() => setEditingPackage(true)}
          >
            {t(
              item.packageSize
                ? "shopping.package.change"
                : "shopping.package.set",
            )}
          </button>
        )
      ) : null}
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
              className="h-11 w-full rounded-xl border border-input bg-background px-3 text-[15px] text-foreground"
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
