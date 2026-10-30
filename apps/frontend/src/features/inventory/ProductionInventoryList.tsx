import { useEffect, useRef, useState } from "react";
import {
  Archive,
  ChevronRight,
  MapPin,
  Refrigerator,
  SearchX,
  Snowflake,
} from "lucide-react";
import { EmptyState } from "@/components/feedback/EmptyState";
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

/** Places in the order people look for food. */
const LOCATION_ORDER = ["Refrigerator", "Freezer", "Pantry", "Other"];
const LOCATION_ICON: Record<string, typeof Refrigerator> = {
  Refrigerator,
  Freezer: Snowflake,
  Pantry: Archive,
  Other: MapPin,
};

const EMPTY_QUERY: SubmittedQuery = {
  search: "",
  status: "",
  storageLocation: "",
};

/**
 * Authenticated production inventory list with live filters and cursor pagination. The place
 * and status filters apply at once; the search applies 300 ms after the last keystroke (or on
 * Enter), so typing sends one request, not one per letter (PLAN-0057 review M3).
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

  // Debounced search: the latest text wins; status and place apply immediately.
  useEffect(() => {
    const handle = window.setTimeout(() => {
      setQuery((current) =>
        current.search === draftSearch.trim() &&
        current.status === draftStatus &&
        current.storageLocation === draftLocation
          ? current
          : {
              search: draftSearch.trim(),
              status: draftStatus,
              storageLocation: draftLocation,
            },
      );
    }, 300);
    return () => window.clearTimeout(handle);
  }, [draftSearch, draftStatus, draftLocation]);

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
          className="space-y-3 rounded-2xl border border-warning/20 bg-warning-soft p-4"
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
                  className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-card px-3 py-2 transition-colors duration-150 hover:bg-card/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <span className="font-medium">{item.productName}</span>
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
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          submitFilters();
        }}
      >
        <div className="flex gap-2">
          <label className="flex-1">
            <span className="sr-only">{t("inventory.search")}</span>
            <Input
              type="search"
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
              className="flex h-11 rounded-xl border border-input bg-card px-3 text-[15px] focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
              value={draftStatus}
              onChange={(event) =>
                setDraftStatus(event.target.value as LotStatusFilter)
              }
            >
              <option value="">{t("inventory.filter.statusAll")}</option>
              <option value="active">
                {t("inventory.filter.statusActive")}
              </option>
              <option value="depleted">
                {t("inventory.filter.statusDepleted")}
              </option>
              <option value="deleted">
                {t("inventory.filter.statusDeleted")}
              </option>
            </select>
          </label>
          {/* Enter applies the search at once; the button stays for assistive technology. */}
          <Button
            type="submit"
            variant="secondary"
            className="sr-only"
            data-testid="inventory-search-submit"
          >
            {t("inventory.actions.search")}
          </Button>
        </div>
        <div
          role="group"
          aria-label={t("inventory.filter.location")}
          className="flex flex-wrap gap-2"
          data-testid="inventory-filter-location"
        >
          {(["", "Pantry", "Refrigerator", "Freezer", "Other"] as const).map(
            (loc) => (
              <button
                key={loc || "all"}
                type="button"
                aria-pressed={draftLocation === loc}
                data-testid={`inventory-filter-location-${loc || "all"}`}
                onClick={() => setDraftLocation(loc as LocationFilter)}
                className={`h-9 rounded-full border px-3.5 text-sm font-semibold transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background ${
                  draftLocation === loc
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border bg-card text-foreground hover:bg-secondary"
                }`}
              >
                {loc
                  ? t(`inventory.location.${loc}`)
                  : t("inventory.filter.locationAll")}
              </button>
            ),
          )}
          {draftSearch || draftStatus || draftLocation ? (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              data-testid="inventory-clear-filters"
              onClick={clearFilters}
            >
              {t("inventory.actions.clearFilters")}
            </Button>
          ) : null}
        </div>
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
        <EmptyState
          testId="inventory-empty"
          icon={hasActiveFilters ? SearchX : Refrigerator}
          title={
            hasActiveFilters
              ? t("inventory.emptyFiltered")
              : t("inventory.empty")
          }
          actions={
            hasActiveFilters ? undefined : (
              <Button asChild>
                <Link to="/app/despensa/novo">
                  {t("inventory.actions.create")}
                </Link>
              </Button>
            )
          }
        />
      )}

      {/* Grouped by place, in the order people look (PLAN-0058): fridge, freezer, cupboard,
          other. Each group is one surface of rows with the amount on the right. */}
      <div className="space-y-6">
        {LOCATION_ORDER.map((location) => {
          const group = items.filter(
            (item) =>
              (LOCATION_ORDER.includes(item.storageLocation)
                ? item.storageLocation
                : "Other") === location,
          );
          if (group.length === 0) return null;
          const Icon = LOCATION_ICON[location];
          return (
            <section
              key={location}
              aria-labelledby={`inventory-group-${location}`}
              data-testid={`inventory-group-${location}`}
              className="space-y-2"
            >
              <h2
                id={`inventory-group-${location}`}
                className="flex items-center gap-2 text-sm font-semibold text-muted-foreground"
              >
                <Icon aria-hidden="true" className="h-4 w-4" />
                {localizeInventoryKey(t, "inventory.location", location)}
                <span className="tabular rounded-full bg-secondary px-2 text-xs">
                  {group.length}
                </span>
              </h2>
              <ul className="surface divide-y divide-border overflow-hidden">
                {group.map((item) => (
                  <li key={item.lotId}>
                    <Link
                      to={`/app/despensa/${item.lotId}`}
                      data-testid={`inventory-lot-link-${item.lotId}`}
                      className="flex items-center gap-3 px-4 py-3 transition-colors duration-150 hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
                    >
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-[16px] font-semibold">
                          {item.productName}
                        </span>
                        {item.customLocation ? (
                          <span className="block text-xs text-muted-foreground">
                            {item.customLocation}
                          </span>
                        ) : null}
                        {item.autoRemoveOn ? (
                          <span
                            className="block text-xs text-muted-foreground"
                            data-testid={`inventory-auto-remove-${item.lotId}`}
                          >
                            {t("inventory.autoRemoveOn.list", {
                              date: new Date(
                                `${item.autoRemoveOn}T12:00:00`,
                              ).toLocaleDateString(locale),
                            })}
                          </span>
                        ) : null}
                        {item.attention && item.attention.state !== "Normal" ? (
                          <span className="mt-1 block">
                            <AttentionBadge attention={item.attention} />
                          </span>
                        ) : null}
                      </span>
                      <span className="tabular shrink-0 text-right text-[15px] font-medium">
                        {formatQuantityLabel(
                          item.quantity,
                          locale as ProductionLocale,
                          t,
                        )}
                      </span>
                      <ChevronRight
                        aria-hidden="true"
                        className="h-4 w-4 shrink-0 text-muted-foreground"
                      />
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          );
        })}
      </div>

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
