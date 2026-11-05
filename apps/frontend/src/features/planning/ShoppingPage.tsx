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
  Share2,
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
import { RecipeApiError } from "@/contracts/recipes";
import { useInventoryRepository } from "@/features/inventory/InventoryProvider";
import { formatLocaleDecimal, parseLocaleDecimal } from "@/lib/localeDecimal";
import { useOfflineList } from "./offline/OfflineListProvider";
import { isUnreachable } from "./offline/offlineSync";
import { formatMenuDay, localIsoDate } from "./planningDates";
import { buildShoppingListText } from "./shoppingListText";
import { usePlanningRepository } from "./PlanningProvider";

/** Lines still to buy, grouped by store section; the rest keep their own blocks below. */
const TO_BUY = ["required", "assumed", "optional"] as const;
const OWNED = ["from_prep", "runs_out", "available"] as const;
/** Places a person may buy at, in the order of a typical trip (PLAN-0062). */
const PLACES = ["street_market", "butcher", "bakery", "market"] as const;
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
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const [list, setList] = useState<ShoppingList | null>(null);
  const [failed, setFailed] = useState(false);
  const [reload, setReload] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [impact, setImpact] = useState<PurchaseImpact | null>(null);
  const [undoHave, setUndoHave] = useState<{
    name: string;
    lotId: string;
    etag: string;
  } | null>(null);
  const [shareOpen, setShareOpen] = useState(false);
  const [byPlace, setByPlace] = useState(false);
  const [portionsByKey, setPortionsByKey] = useState<Record<string, number>>(
    {},
  );
  const [copied, setCopied] = useState(false);
  const inventoryRepo = useInventoryRepository();
  const offlineList = useOfflineList();
  const offline = offlineList?.offline ?? false;
  const syncVersion = offlineList?.syncVersion ?? 0;
  // The latest functions, so a change in connection state alone does not reload the list.
  const offlineRef = useRef(offlineList);
  offlineRef.current = offlineList;
  /** When the server sent the list on screen; set only for a list read from the device. */
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const fetchedAtRef = useRef("");

  useEffect(() => {
    const controller = new AbortController();
    const active = () => !controller.signal.aborted;
    repo.getShopping(menuId, localIsoDate(), controller.signal).then(
      async (value) => {
        fetchedAtRef.current = new Date().toISOString();
        // Marks still in the outbox stay visible over what the server knows so far.
        const shown = (await offlineRef.current?.withPending(value)) ?? value;
        if (!active()) return;
        setSavedAt(null);
        setFailed(false);
        setList(shown);
      },
      async (err) => {
        if (!active()) return;
        const saved = isUnreachable(err)
          ? await offlineRef.current?.loadList(menuId)
          : null;
        if (isUnreachable(err)) offlineRef.current?.noteUnreachable();
        if (!active()) return;
        if (saved) {
          fetchedAtRef.current = saved.savedAt;
          setSavedAt(saved.savedAt);
          setList(saved.list);
        } else {
          setFailed(true);
        }
      },
    );
    return () => controller.abort();
  }, [repo, menuId, reload, syncVersion]);

  // What is on screen is what opens next time without a connection, marks included.
  useEffect(() => {
    if (list && fetchedAtRef.current) {
      void offlineRef.current?.saveList(list, fetchedAtRef.current);
    }
  }, [list]);

  // The short hint on a line ("dividir em N porções"); the detail waits for the arrival screen.
  useEffect(() => {
    if (!repo.getArrival || offline) return;
    const controller = new AbortController();
    repo
      .getArrival(menuId, localIsoDate(), controller.signal, { all: true })
      .then(
        (plan) => {
          if (controller.signal.aborted) return;
          setPortionsByKey(
            Object.fromEntries(
              plan.lines.map((line) => [
                line.itemKey,
                line.portions.reduce((sum, portion) => sum + portion.count, 0),
              ]),
            ),
          );
        },
        () => undefined,
      );
    return () => controller.abort();
  }, [repo, menuId, reload, offline]);

  const toggle = async (item: ShoppingItem) => {
    if ((!session.csrfToken && !offlineList) || !list) return;
    setError(null);
    const checked = !item.checked;
    setList({
      ...list,
      items: list.items.map((line) =>
        line.itemKey === item.itemKey ? { ...line, checked } : line,
      ),
    });
    try {
      if (offlineList) {
        await offlineList.sendCheck(menuId, item, checked);
      } else {
        await repo.setChecked(menuId, item.itemKey, checked, {
          csrfToken: session.csrfToken ?? "",
        });
      }
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

  // "Desfazer" stays for a few seconds; the item remains editable in the pantry afterward.
  useEffect(() => {
    if (!undoHave) return;
    const timer = globalThis.setTimeout(() => setUndoHave(null), 8000);
    return () => globalThis.clearTimeout(timer);
  }, [undoHave]);

  const undoAlreadyHave = async () => {
    if (!undoHave || !session.csrfToken) return;
    const target = undoHave;
    setUndoHave(null);
    try {
      await inventoryRepo.deleteLot(target.lotId, {
        csrfToken: session.csrfToken,
        etag: target.etag,
      });
      refresh();
    } catch {
      setError("shopping.have.undoError");
    }
  };

  const shareText = () =>
    buildShoppingListText(
      list?.items ?? [],
      t,
      (value) => formatLocaleDecimal(value, locale),
      byPlace,
    );

  const share = async () => {
    setCopied(false);
    // The system share sheet is best; a browser without it gets explicit choices.
    if (typeof navigator.share === "function") {
      try {
        await navigator.share({ text: shareText() });
      } catch {
        // The person closed the sheet: nothing to report.
      }
      return;
    }
    setShareOpen((value) => !value);
  };

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(shareText());
      setCopied(true);
    } catch {
      setError("shopping.share.copyError");
    }
  };

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

  const pending = list.items.filter(
    (item) =>
      (TO_BUY as readonly string[]).includes(item.category) && !item.later,
  );
  const laterItems = list.items.filter(
    (item) =>
      (TO_BUY as readonly string[]).includes(item.category) && item.later,
  );

  /** "Buy later" and back. The only mark that works offline, because the server need not judge it. */
  const setLater = async (item: ShoppingItem, later: boolean) => {
    if ((!session.csrfToken && !offlineList) || !repo.setLineMark) return;
    const mark = {
      itemKey: item.markKey ?? item.itemKey,
      state: later ? ("later" as const) : null,
      substituteName: item.substituteFor ? item.displayName : null,
      place: item.place ?? null,
    };
    try {
      const outcome = offlineList
        ? await offlineList.sendLater(menuId, item, mark)
        : (await repo.setLineMark(menuId, mark, {
            csrfToken: session.csrfToken ?? "",
          }),
          "sent");
      if (outcome === "sent") {
        refresh();
      } else {
        setList(
          (current) =>
            current && {
              ...current,
              items: current.items.map((line) =>
                line.itemKey === item.itemKey ? { ...line, later } : line,
              ),
            },
        );
      }
    } catch {
      setError("planning.error.save");
    }
  };

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
        {savedAt ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="shopping-saved-at"
          >
            {t("offline.savedAt", {
              time: new Date(savedAt).toLocaleString(locale, {
                dateStyle: "short",
                timeStyle: "short",
              }),
            })}
          </p>
        ) : null}
        {offline && list.mealCount > 0 ? (
          <p
            className="text-sm text-muted-foreground"
            data-testid="shopping-needs-internet"
          >
            {t("offline.needsInternet")}
          </p>
        ) : null}
      </div>
      {list.mealCount > 0 ? <ShoppingSummary items={list.items} /> : null}
      {list.mealCount > 0 ? (
        <div className="space-y-2">
          <Button
            type="button"
            variant="secondary"
            data-testid="shopping-share"
            aria-expanded={shareOpen}
            onClick={() => void share()}
          >
            <Share2 aria-hidden="true" className="mr-2 h-4 w-4" />
            {t("shopping.share")}
          </Button>
          {shareOpen ? (
            <div
              className="flex flex-wrap gap-2"
              data-testid="shopping-share-options"
            >
              <Button asChild size="sm" variant="secondary">
                <a
                  href={`https://wa.me/?text=${encodeURIComponent(shareText())}`}
                  target="_blank"
                  rel="noreferrer"
                  data-testid="shopping-share-whatsapp"
                >
                  {t("shopping.share.whatsapp")}
                </a>
              </Button>
              <Button
                type="button"
                size="sm"
                variant="secondary"
                data-testid="shopping-share-copy"
                onClick={() => void copy()}
              >
                {t("shopping.share.copy")}
              </Button>
              {copied ? (
                <p
                  role="status"
                  className="self-center text-sm text-muted-foreground"
                >
                  {t("shopping.share.copied")}
                </p>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
      {list.mealCount > 0 && repo.setLineMark ? (
        <label className="flex min-h-11 items-center gap-3 text-sm font-medium">
          <input
            type="checkbox"
            className="h-5 w-5 accent-primary"
            data-testid="shopping-split"
            checked={byPlace}
            onChange={(event) => setByPlace(event.target.checked)}
          />
          {t("shopping.split")}
        </label>
      ) : null}
      {repo.getArrival &&
      list.items.some((item) => item.checked && item.toBuyQuantity > 0) ? (
        offline ? (
          <Button
            type="button"
            className="w-full"
            data-testid="shopping-arrival"
            disabled
          >
            {t("shopping.arrival")}
          </Button>
        ) : (
          <Button asChild className="w-full" data-testid="shopping-arrival">
            <Link to={`/app/cardapio/${menuId}/chegada`}>
              {t("shopping.arrival")}
            </Link>
          </Button>
        )
      ) : null}
      {undoHave ? (
        <div
          role="status"
          data-testid="shopping-have-notice"
          className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-secondary/40 p-3 text-sm"
        >
          <span>{t("shopping.have.done", { item: undoHave.name })}</span>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            data-testid="shopping-have-undo"
            onClick={() => void undoAlreadyHave()}
          >
            {t("shopping.have.undo")}
          </Button>
        </div>
      ) : null}
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
          ...(byPlace
            ? PLACES.map((place) => ({
                id: `place-${place}`,
                title: t(`shopping.place.${place}`),
                items: pending.filter(
                  (item) => (item.place ?? "market") === place,
                ),
              }))
            : STORE_SECTIONS.map((section) => ({
                id: `section-${section}`,
                title: t(`shopping.section.${section}`),
                items: pending
                  .filter((item) => (item.storeSection ?? "other") === section)
                  .sort(
                    (a, b) =>
                      TO_BUY.indexOf(a.category as (typeof TO_BUY)[number]) -
                      TO_BUY.indexOf(b.category as (typeof TO_BUY)[number]),
                  ),
              }))),
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
                    portions={portionsByKey[item.itemKey] ?? 1}
                    showPlace={byPlace}
                    menuId={menuId}
                    onChanged={refresh}
                    offline={offline}
                    onLater={() => setLater(item, true)}
                    onAlreadyHave={(lot) => {
                      setUndoHave({ name: item.displayName, ...lot });
                      setImpact(null);
                      refresh();
                    }}
                  />
                ))}
              </ul>
            </section>
          );
        })
      )}
      {laterItems.length > 0 ? (
        <section
          aria-labelledby="shopping-later"
          data-testid="shopping-later"
          className="space-y-2"
        >
          <h2
            id="shopping-later"
            className="text-sm font-semibold text-muted-foreground"
          >
            {t("shopping.later.title")}
          </h2>
          <ul className="surface divide-y divide-border overflow-hidden">
            {laterItems.map((item) => (
              <li
                key={item.itemKey}
                className="flex items-center justify-between gap-3 px-4 py-3"
              >
                <span className="min-w-0 font-medium">{item.displayName}</span>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  data-testid="shopping-later-back"
                  onClick={() => void setLater(item, false)}
                >
                  {t("shopping.later.back")}
                </Button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
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
                      offline={offline}
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
  onAlreadyHave,
  portions = 1,
  showPlace = false,
  menuId,
  onChanged,
  offline = false,
  onLater,
}: {
  item: ShoppingItem;
  onToggle: () => void;
  onPurchased: (bought: number) => void;
  onPackageChanged: () => void;
  /** Present for lines to buy: records the item as approximately at home, then offers undo. */
  onAlreadyHave?: (lot: { lotId: string; etag: string }) => void;
  /** Portions the arrival advisor would make; above one, the line shows a short hint. */
  portions?: number;
  /** Whether to offer moving the line to another place. */
  showPlace?: boolean;
  menuId?: string;
  /** The line's mark changed; the list must reload. */
  onChanged?: () => void;
  /** Without a connection, only the check box and "buy later" work (ADR-0012). */
  offline?: boolean;
  /** Leaves the line for a later purchase. */
  onLater?: () => Promise<void>;
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
  const haveKeyRef = useRef(crypto.randomUUID());
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

  // The person says they have it, not how much, so the amount is an estimate of what the plan
  // needs. The pantry shows it as approximate, and it covers the line like any other stock.
  const [notFoundOpen, setNotFoundOpen] = useState(false);
  const [substitute, setSubstitute] = useState("");
  const [markError, setMarkError] = useState<string | null>(null);

  const saveMark = async (change: {
    state?: "later" | null;
    substituteName?: string | null;
    place?: string | null;
  }) => {
    if (!session.csrfToken || !planning.setLineMark || !menuId) return;
    setMarkError(null);
    try {
      await planning.setLineMark(
        menuId,
        {
          itemKey: item.markKey ?? item.itemKey,
          state: change.state ?? (item.later ? "later" : null),
          substituteName:
            change.substituteName === undefined
              ? item.substituteFor
                ? item.displayName
                : null
              : change.substituteName,
          place:
            change.place === undefined ? (item.place ?? null) : change.place,
        },
        { csrfToken: session.csrfToken },
      );
      setNotFoundOpen(false);
      onChanged?.();
    } catch (err) {
      setMarkError(
        err instanceof RecipeApiError && err.code === "restriction_blocked"
          ? "shopping.notFound.blocked"
          : "planning.error.save",
      );
    }
  };

  const alreadyHave = async () => {
    if (!session.csrfToken || saving) return;
    setSaving(true);
    setError(false);
    try {
      const lot = await inventory.createLot(
        {
          productName: item.displayName,
          quantity: {
            kind: "measured",
            value: item.toBuyQuantity,
            unit: item.unit as MeasuredUnit,
            estimated: true,
          },
          storageLocation: storage,
        },
        { csrfToken: session.csrfToken, idempotencyKey: haveKeyRef.current },
      );
      haveKeyRef.current = crypto.randomUUID();
      onAlreadyHave?.({ lotId: lot.lotId, etag: lot.etag });
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
          {item.substituteFor ? (
            <p
              className="text-xs text-muted-foreground"
              data-testid="shopping-item-substitute"
            >
              {t("shopping.notFound.instead", { item: item.substituteFor })}{" "}
              <button
                type="button"
                className="min-h-6 underline underline-offset-4"
                data-testid="shopping-substitute-undo"
                disabled={offline}
                onClick={() => void saveMark({ substituteName: null })}
              >
                {t("shopping.have.undo")}
              </button>
            </p>
          ) : null}
          {toBuy && portions > 1 ? (
            <p
              className="text-xs font-medium"
              data-testid="shopping-item-portions"
            >
              {t("shopping.portions", { count: portions })}
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
        {toBuy && onAlreadyHave ? (
          <Button
            type="button"
            size="sm"
            variant="ghost"
            className="shrink-0"
            data-testid="shopping-item-have"
            disabled={saving || offline}
            onClick={() => void alreadyHave()}
          >
            {t("shopping.have")}
          </Button>
        ) : null}
        {toBuy ? (
          <Button
            type="button"
            size="sm"
            variant="secondary"
            className="shrink-0"
            data-testid="shopping-item-buy"
            disabled={offline}
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
      {toBuy && planning.setLineMark && menuId ? (
        <button
          type="button"
          className="ml-9 min-h-6 text-xs underline underline-offset-4"
          data-testid="shopping-not-found"
          aria-expanded={notFoundOpen}
          onClick={() => setNotFoundOpen((value) => !value)}
        >
          {t("shopping.notFound")}
        </button>
      ) : null}
      {notFoundOpen ? (
        <div
          className="space-y-3 rounded-xl bg-secondary/50 p-3"
          data-testid="shopping-not-found-sheet"
        >
          <p className="text-sm">
            {t("shopping.notFound.affects", {
              dishes: item.uses.map((use) => use.recipeName).join(", "),
            })}
          </p>
          <form
            className="flex flex-wrap items-end gap-2"
            onSubmit={(event) => {
              event.preventDefault();
              if (substitute.trim()) {
                void saveMark({ substituteName: substitute.trim() });
              }
            }}
          >
            <label className="min-w-40 flex-1 space-y-1 text-xs text-muted-foreground">
              {t("shopping.notFound.swap")}
              <Input
                maxLength={120}
                data-testid="shopping-substitute"
                disabled={offline}
                value={substitute}
                onChange={(event) => setSubstitute(event.target.value)}
              />
            </label>
            <Button
              type="submit"
              size="sm"
              variant="secondary"
              data-testid="shopping-substitute-save"
              disabled={offline}
            >
              {t("shopping.notFound.swapSave")}
            </Button>
          </form>
          <p className="text-xs text-muted-foreground">
            {t(
              offline
                ? "offline.needsInternet.short"
                : "shopping.notFound.swapHint",
            )}
          </p>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              size="sm"
              variant="secondary"
              data-testid="shopping-later-save"
              onClick={() =>
                void (onLater
                  ? onLater().then(() => setNotFoundOpen(false))
                  : saveMark({ state: "later" }))
              }
            >
              {t("shopping.notFound.later")}
            </Button>
            {offline ? null : (
              <Button asChild size="sm" variant="ghost">
                <Link
                  to={`/app/cardapio/${menuId}`}
                  data-testid="shopping-swap-dish"
                >
                  {t("shopping.notFound.dish")}
                </Link>
              </Button>
            )}
          </div>
          {markError ? (
            <p role="alert" className="text-xs text-destructive">
              {t(markError)}
            </p>
          ) : null}
        </div>
      ) : null}
      {toBuy && showPlace && planning.setLineMark && menuId ? (
        <label className="ml-9 flex items-center gap-2 text-xs text-muted-foreground">
          {t("shopping.place")}
          <select
            className="h-9 rounded-lg border border-input bg-background px-2 text-sm text-foreground"
            data-testid="shopping-place"
            disabled={offline}
            value={item.place ?? "market"}
            onChange={(event) => void saveMark({ place: event.target.value })}
          >
            {PLACES.map((place) => (
              <option key={place} value={place}>
                {t(`shopping.place.${place}`)}
              </option>
            ))}
          </select>
        </label>
      ) : null}
      {/* The package size is offered where it matters: when buying, or to change a known size
          (review M7). */}
      {toBuy &&
      !offline &&
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
            disabled={saving || offline}
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
