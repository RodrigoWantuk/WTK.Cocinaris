import { Plus } from "lucide-react";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import type { MenuSummary } from "@/contracts/planning";
import { formatMenuPeriod } from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";

/**
 * The person's menus (PLAN-0029, PLAN-0065 Slice 4): the list, and "Novo cardápio", which builds a
 * menu on the board by meal counts. Menus are built in one way only, so the form for an empty menu
 * by dates is gone; menus made with it before still open and work.
 */
export function MenusPage() {
  const repo = usePlanningRepository();
  const { t, locale } = useProductionI18n();
  const [archived, setArchived] = useState(false);
  const [items, setItems] = useState<MenuSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
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

  return (
    <div data-testid="menus-page" className="space-y-8">
      <div>
        <h1 className="font-display text-3xl">{t("planning.title")}</h1>
        <p className="mt-1 text-muted-foreground">{t("planning.subtitle")}</p>
      </div>
      <Button asChild size="lg" className="w-full sm:w-auto">
        <Link to="/app/compras/planejar" data-testid="menus-new">
          <Plus aria-hidden="true" />
          {t("planning.create.title")}
        </Link>
      </Button>

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
                  className="surface flex items-center gap-4 p-4 transition-[background-color,border-color] duration-150 hover:border-input/60 hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
                >
                  {/* A calendar leaf with the first day reads faster than a date range. */}
                  <span
                    aria-hidden="true"
                    className="flex h-14 w-14 shrink-0 flex-col items-center justify-center rounded-2xl bg-accent text-accent-foreground"
                  >
                    <span className="text-[11px] font-semibold uppercase leading-none">
                      {new Intl.DateTimeFormat(locale, { month: "short" })
                        .format(new Date(`${menu.startDate}T12:00:00`))
                        .replace(".", "")}
                    </span>
                    <span className="tabular text-xl font-bold leading-tight">
                      {Number(menu.startDate.slice(8, 10))}
                    </span>
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-lg font-semibold">
                      {menu.name ||
                        formatMenuPeriod(menu.startDate, menu.endDate, locale)}
                    </span>
                    <span className="block text-sm text-muted-foreground">
                      {formatMenuPeriod(menu.startDate, menu.endDate, locale)} ·{" "}
                      {t("planning.list.meals", { count: menu.entryCount })}
                    </span>
                  </span>
                  <span
                    className={`shrink-0 rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                      menu.status === "accepted"
                        ? "bg-primary text-primary-foreground"
                        : "bg-secondary text-secondary-foreground"
                    }`}
                  >
                    {t(`planning.status.${menu.status}`)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
