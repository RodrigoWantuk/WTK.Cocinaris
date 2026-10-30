import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useInventoryRepository } from "./InventoryProvider";
import { formatQuantityLabel } from "./quantityDisplay";
import { AttentionBadge } from "./AttentionBadge";
import { localizeInventoryKey } from "./inventoryLabels";
import type { InventoryLotView } from "@/adapters/live/inventoryTypes";
import { InventoryApiError } from "@/adapters/live/inventoryTypes";
import { useSession } from "@/app/session/SessionProvider";
import {
  useProductionI18n,
  type ProductionLocale,
} from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

type LotStatusFilter = "" | "active" | "depleted" | "deleted";
type LocationFilter = "" | "Pantry" | "Refrigerator" | "Freezer" | "Other";

interface SubmittedQuery {
  search: string;
  status: LotStatusFilter;
  storageLocation: LocationFilter;
}

const EMPTY_QUERY: SubmittedQuery = {
  search: "",
  status: "",
  storageLocation: "",
};

/**
 * Authenticated production inventory list with explicit search/filter submission
 * and cursor pagination. Typing in the search box does not trigger requests.
 */
export function ProductionInventoryList() {
  const repo = useInventoryRepository();
  const { session } = useSession();
  const { t, locale } = useProductionI18n();
  const [items, setItems] = useState<InventoryLotView[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [draftSearch, setDraftSearch] = useState("");
  const [draftStatus, setDraftStatus] = useState<LotStatusFilter>("");
  const [draftLocation, setDraftLocation] = useState<LocationFilter>("");
  const [query, setQuery] = useState<SubmittedQuery>(EMPTY_QUERY);
  const [status, setStatus] = useState<"loading" | "ready" | "error">(
    "loading",
  );
  const [error, setError] = useState<string | null>(null);
  const [attention, setAttention] = useState<InventoryLotView[] | null>(null);
  const requestIdRef = useRef(0);

  // The attention panel is advisory and independent of the filters: a failure hides it
  // instead of blocking the pantry list.
  useEffect(() => {
    const controller = new AbortController();
    repo
      .listAttention(controller.signal)
      .then(setAttention)
      .catch(() => setAttention(null));
    return () => controller.abort();
  }, [repo]);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const requestId = ++requestIdRef.current;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setStatus("loading");
    setError(null);
    void (async () => {
      try {
        const page = await repo.listLots({
          search: query.search || undefined,
          status: query.status || undefined,
          storageLocation: query.storageLocation || undefined,
          pageSize: 20,
          signal: controller.signal,
        });
        if (requestId !== requestIdRef.current) return;
        setItems(page.items);
        setNextCursor(page.nextCursor);
        setStatus("ready");
      } catch (err) {
        if (controller.signal.aborted) return;
        if (err instanceof InventoryApiError && err.code === "cancelled") {
          return;
        }
        if (requestId !== requestIdRef.current) return;
        setStatus("error");
        setError(t("inventory.error.loadList"));
      }
    })();
    return () => controller.abort();
  }, [query, repo, t]);

  function submitFilters() {
    setItems([]);
    setNextCursor(null);
    setQuery({
      search: draftSearch.trim(),
      status: draftStatus,
      storageLocation: draftLocation,
    });
  }

  function clearFilters() {
    setDraftSearch("");
    setDraftStatus("");
    setDraftLocation("");
    setItems([]);
    setNextCursor(null);
    setQuery(EMPTY_QUERY);
  }

  async function loadMore() {
    if (!nextCursor) return;
    const requestId = ++requestIdRef.current;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setStatus("loading");
    try {
      const page = await repo.listLots({
        search: query.search || undefined,
        status: query.status || undefined,
        storageLocation: query.storageLocation || undefined,
        cursor: nextCursor,
        pageSize: 20,
        signal: controller.signal,
      });
      if (requestId !== requestIdRef.current) return;
      setItems((prev) => {
        const seen = new Set(prev.map((item) => item.lotId));
        const merged = [...prev];
        for (const item of page.items) {
          if (!seen.has(item.lotId)) {
            merged.push(item);
            seen.add(item.lotId);
          }
        }
        return merged;
      });
      setNextCursor(page.nextCursor);
      setStatus("ready");
    } catch (err) {
      if (controller.signal.aborted) return;
      if (err instanceof InventoryApiError && err.code === "cancelled") return;
      if (requestId !== requestIdRef.current) return;
      setStatus("error");
      setError(t("inventory.error.loadList"));
    }
  }

  if (session.status === "loading") {
    return (
      <p role="status" data-testid="inventory-loading">
        {t("inventory.loading")}
      </p>
    );
  }

  const hasActiveFilters = Boolean(
    query.search || query.status || query.storageLocation,
  );

  return (
    <div data-testid="production-inventory-list" className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="font-display text-3xl">{t("inventory.title")}</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("inventory.subtitle")}
          </p>
        </div>
        <Button asChild>
          <Link to="/app/despensa/novo" data-testid="inventory-create">
            {t("inventory.actions.create")}
          </Link>
        </Button>
      </div>

      {attention && attention.length > 0 && (
        <section
          data-testid="inventory-attention-panel"
          aria-labelledby="inventory-attention-title"
          className="space-y-2 rounded-2xl bg-warning-soft p-4"
        >
          <h2 id="inventory-attention-title" className="font-medium">
            {t("inventory.attention.title")}
          </h2>
          <ul className="space-y-2">
            {attention.map((item) => (
              <li key={item.lotId}>
                <Link
                  to={`/app/despensa/${item.lotId}`}
                  data-testid={`inventory-attention-link-${item.lotId}`}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-lg px-2 py-1 hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <span className="text-sm">{item.productName}</span>
                  <AttentionBadge attention={item.attention} />
                </Link>
              </li>
            ))}
          </ul>
          <p className="text-xs text-muted-foreground">
            {t("inventory.attention.disclaimer")}
          </p>
        </section>
      )}

      <form
        className="flex flex-wrap gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          submitFilters();
        }}
      >
        <label className="min-w-[12rem] flex-1">
          <span className="sr-only">{t("inventory.search")}</span>
          <Input
            data-testid="inventory-search"
            value={draftSearch}
            onChange={(event) => setDraftSearch(event.target.value)}
            placeholder={t("inventory.search")}
          />
        </label>
        <label>
          <span className="sr-only">{t("inventory.filter.status")}</span>
          <select
            data-testid="inventory-filter-status"
            className="flex h-11 rounded-xl border border-input bg-background px-3 text-[15px]"
            value={draftStatus}
            onChange={(event) =>
              setDraftStatus(event.target.value as LotStatusFilter)
            }
          >
            <option value="">{t("inventory.filter.statusAll")}</option>
            <option value="active">{t("inventory.filter.statusActive")}</option>
            <option value="depleted">
              {t("inventory.filter.statusDepleted")}
            </option>
            <option value="deleted">
              {t("inventory.filter.statusDeleted")}
            </option>
          </select>
        </label>
        <label>
          <span className="sr-only">{t("inventory.filter.location")}</span>
          <select
            data-testid="inventory-filter-location"
            className="flex h-11 rounded-xl border border-input bg-background px-3 text-[15px]"
            value={draftLocation}
            onChange={(event) =>
              setDraftLocation(event.target.value as LocationFilter)
            }
          >
            <option value="">{t("inventory.filter.locationAll")}</option>
            {(["Pantry", "Refrigerator", "Freezer", "Other"] as const).map(
              (loc) => (
                <option key={loc} value={loc}>
                  {t(`inventory.location.${loc}`)}
                </option>
              ),
            )}
          </select>
        </label>
        <Button
          type="submit"
          variant="secondary"
          data-testid="inventory-search-submit"
        >
          {t("inventory.actions.search")}
        </Button>
        <Button
          type="button"
          variant="ghost"
          data-testid="inventory-clear-filters"
          onClick={clearFilters}
        >
          {t("inventory.actions.clearFilters")}
        </Button>
      </form>

      <p role="status" aria-live="polite" className="sr-only">
        {status === "loading"
          ? t("inventory.loading")
          : status === "ready"
            ? t("inventory.resultsCount").replace(
                "{count}",
                String(items.length),
              )
            : ""}
      </p>

      {status === "error" && (
        <div role="alert" className="space-y-2">
          <p>{error}</p>
          <Button
            type="button"
            data-testid="inventory-retry"
            onClick={() => setQuery({ ...query })}
          >
            {t("inventory.actions.retry")}
          </Button>
        </div>
      )}

      {status === "ready" && items.length === 0 && (
        <div data-testid="inventory-empty" className="rounded-xl border p-8">
          <p>
            {hasActiveFilters
              ? t("inventory.emptyFiltered")
              : t("inventory.empty")}
          </p>
          {!hasActiveFilters && (
            <Button asChild className="mt-4">
              <Link to="/app/despensa/novo">
                {t("inventory.actions.create")}
              </Link>
            </Button>
          )}
        </div>
      )}

      <ul className="divide-y divide-border border-y border-border">
        {items.map((item) => (
          <li key={item.lotId}>
            <Link
              to={`/app/despensa/${item.lotId}`}
              data-testid={`inventory-lot-link-${item.lotId}`}
              className="-mx-2 block rounded-xl px-2 py-4 hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              <div className="flex items-start justify-between gap-3">
                <h2 className="text-[17px] font-semibold">
                  {item.productName}
                </h2>
                <span className="text-sm text-muted-foreground">
                  {localizeInventoryKey(
                    t,
                    "inventory.location",
                    item.storageLocation,
                  )}
                  {item.customLocation ? ` · ${item.customLocation}` : ""}
                </span>
              </div>
              <p className="mt-1 text-sm text-muted-foreground">
                {formatQuantityLabel(
                  item.quantity,
                  locale as ProductionLocale,
                  t,
                )}
              </p>
              {item.autoRemoveOn ? (
                <p
                  className="mt-1 text-xs text-muted-foreground"
                  data-testid={`inventory-auto-remove-${item.lotId}`}
                >
                  {t("inventory.autoRemoveOn.list", {
                    date: new Date(
                      `${item.autoRemoveOn}T12:00:00`,
                    ).toLocaleDateString(locale),
                  })}
                </p>
              ) : null}
              <div className="mt-2">
                <AttentionBadge attention={item.attention} />
              </div>
            </Link>
          </li>
        ))}
      </ul>

      {nextCursor && (
        <Button
          type="button"
          variant="secondary"
          data-testid="inventory-load-more"
          disabled={status === "loading"}
          onClick={() => void loadMore()}
        >
          {t("inventory.actions.loadMore")}
        </Button>
      )}
    </div>
  );
}
