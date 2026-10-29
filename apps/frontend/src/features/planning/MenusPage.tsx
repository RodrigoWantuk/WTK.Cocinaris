import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { MenuSummary } from "@/contracts/planning";
import { RecipeApiError } from "@/contracts/recipes";
import { addDays, formatMenuPeriod, localIsoDate } from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";

/**
 * Menu list and creation (PLAN-0029). Menus are optional; a new menu starts as a draft for the
 * next seven days from today, and both dates can be changed before creating it.
 */
export function MenusPage() {
  const repo = usePlanningRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const [archived, setArchived] = useState(false);
  const [items, setItems] = useState<MenuSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const today = localIsoDate();
  const [name, setName] = useState("");
  const [startDate, setStartDate] = useState(today);
  const [endDate, setEndDate] = useState(addDays(today, 6));
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const keyRef = useRef(crypto.randomUUID());

  useEffect(() => {
    const controller = new AbortController();
    setItems(null);
    setFailed(false);
    repo.listMenus(controller.signal, { archived }).then(
      (menus) => !controller.signal.aborted && setItems(menus),
      () => !controller.signal.aborted && setFailed(true),
    );
    return () => controller.abort();
  }, [repo, archived]);

  const create = async () => {
    if (!session.csrfToken || creating) return;
    if (!startDate || !endDate || endDate < startDate) {
      setCreateError("planning.create.invalidPeriod");
      return;
    }
    setCreating(true);
    setCreateError(null);
    try {
      const menu = await repo.createMenu(
        { name: name.trim() || null, startDate, endDate },
        { csrfToken: session.csrfToken, idempotencyKey: keyRef.current },
      );
      navigate(`/app/cardapio/${menu.menuId}`);
    } catch (err) {
      setCreateError(
        err instanceof RecipeApiError && err.code === "validation_failed"
          ? "planning.create.invalidPeriod"
          : "planning.error.save",
      );
      keyRef.current = crypto.randomUUID();
    } finally {
      setCreating(false);
    }
  };

  return (
    <div data-testid="menus-page" className="space-y-8">
      <div>
        <h1 className="font-display text-3xl">{t("planning.title")}</h1>
        <p className="mt-1 text-muted-foreground">{t("planning.subtitle")}</p>
      </div>

      <form
        className="space-y-3 rounded-2xl border border-border p-4"
        onSubmit={(event) => {
          event.preventDefault();
          void create();
        }}
      >
        <h2 className="font-display text-xl">{t("planning.create.title")}</h2>
        <div className="grid gap-3 sm:grid-cols-3">
          <div className="space-y-1">
            <label htmlFor="menu-name" className="text-sm font-medium">
              {t("planning.create.name")}
            </label>
            <Input
              id="menu-name"
              data-testid="menu-create-name"
              value={name}
              maxLength={80}
              placeholder={t("planning.create.namePlaceholder")}
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="space-y-1">
            <label htmlFor="menu-start" className="text-sm font-medium">
              {t("planning.create.start")}
            </label>
            <Input
              id="menu-start"
              type="date"
              data-testid="menu-create-start"
              value={startDate}
              onChange={(event) => {
                setStartDate(event.target.value);
                if (event.target.value)
                  setEndDate(addDays(event.target.value, 6));
              }}
            />
          </div>
          <div className="space-y-1">
            <label htmlFor="menu-end" className="text-sm font-medium">
              {t("planning.create.end")}
            </label>
            <Input
              id="menu-end"
              type="date"
              data-testid="menu-create-end"
              value={endDate}
              min={startDate}
              max={startDate ? addDays(startDate, 30) : undefined}
              onChange={(event) => setEndDate(event.target.value)}
            />
          </div>
        </div>
        {createError ? (
          <p role="alert" className="text-sm text-destructive">
            {t(createError)}
          </p>
        ) : null}
        <Button
          type="submit"
          data-testid="menu-create"
          disabled={creating || !session.csrfToken}
        >
          {creating ? t("planning.saving") : t("planning.create.submit")}
        </Button>
      </form>

      <section aria-labelledby="menus-list" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="menus-list" className="font-display text-xl">
            {t(archived ? "planning.list.archived" : "planning.list.title")}
          </h2>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            data-testid="menus-toggle-archived"
            onClick={() => setArchived((value) => !value)}
          >
            {t(
              archived
                ? "planning.list.showActive"
                : "planning.list.showArchived",
            )}
          </Button>
        </div>
        {failed ? (
          <p role="alert" className="text-sm text-muted-foreground">
            {t("planning.error.load")}
          </p>
        ) : items === null ? (
          <p role="status">{t("planning.loading")}</p>
        ) : items.length === 0 ? (
          <p
            data-testid="menus-empty"
            className="text-sm text-muted-foreground"
          >
            {t(
              archived ? "planning.list.emptyArchived" : "planning.list.empty",
            )}
          </p>
        ) : (
          <ul className="space-y-3">
            {items.map((menu) => (
              <li key={menu.menuId}>
                <Link
                  to={`/app/cardapio/${menu.menuId}`}
                  data-testid={`menus-item-${menu.menuId}`}
                  className="block rounded-2xl border border-border bg-card p-4 hover:bg-accent/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <p className="font-display text-xl">
                    {menu.name ||
                      formatMenuPeriod(menu.startDate, menu.endDate, locale)}
                  </p>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {formatMenuPeriod(menu.startDate, menu.endDate, locale)} ·{" "}
                    {t(`planning.status.${menu.status}`)} ·{" "}
                    {t("planning.list.meals", { count: menu.entryCount })}
                  </p>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
