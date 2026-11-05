import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import type { MenuSummary } from "@/contracts/planning";
import { useOfflineList } from "./offline/OfflineListProvider";
import { isUnreachable } from "./offline/offlineSync";
import { localIsoDate } from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";
import { ShoppingPage } from "./ShoppingPage";

/**
 * The "Compras" tab (PLAN-0054 direction 2): the shopping list of the menu in use today, else the
 * next accepted menu, else the latest draft. Without any menu it explains that the list is built
 * from the menu and links to it.
 */
export function pickShoppingMenu(
  menus: MenuSummary[],
  today: string,
): MenuSummary | null {
  const accepted = menus.filter((menu) => menu.status === "accepted");
  return (
    accepted.find((menu) => menu.startDate <= today && menu.endDate >= today) ??
    accepted
      .filter((menu) => menu.startDate > today)
      .sort((a, b) => a.startDate.localeCompare(b.startDate))[0] ??
    menus.find((menu) => menu.status === "draft" && menu.endDate >= today) ??
    null
  );
}

export function ShoppingHubPage() {
  const repo = usePlanningRepository();
  const { t } = useProductionI18n();
  const [menus, setMenus] = useState<MenuSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const offlineList = useOfflineList();
  const lastMenuId = offlineList?.lastMenuId;
  const noteUnreachable = offlineList?.noteUnreachable;
  // Without a connection the menus cannot be listed, so the list opened last stands in for them.
  const [savedMenuId, setSavedMenuId] = useState<string | null | undefined>();

  useEffect(() => {
    const controller = new AbortController();
    repo.listMenus(controller.signal).then(
      (items) => !controller.signal.aborted && setMenus(items),
      async (error) => {
        if (controller.signal.aborted) return;
        if (isUnreachable(error) && lastMenuId) {
          noteUnreachable?.();
          const id = await lastMenuId();
          if (!controller.signal.aborted) setSavedMenuId(id);
          return;
        }
        setFailed(true);
      },
    );
    return () => controller.abort();
    // The callbacks change with the connection state, which must not list the menus again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [repo]);

  if (savedMenuId) return <ShoppingPage menuId={savedMenuId} />;
  if (savedMenuId === null) {
    return (
      <p data-testid="shopping-hub-offline" className="text-muted-foreground">
        {t("offline.noList")}
      </p>
    );
  }
  if (failed) {
    return (
      <p role="alert" className="text-muted-foreground">
        {t("planning.error.load")}
      </p>
    );
  }
  if (!menus) return <p role="status">{t("planning.loading")}</p>;

  const menu = pickShoppingMenu(menus, localIsoDate());
  if (menu) return <ShoppingPage menuId={menu.menuId} />;

  return (
    <div data-testid="shopping-hub-empty" className="space-y-4 pt-6">
      <h1 className="font-display text-3xl">{t("shopping.title")}</h1>
      <p className="text-muted-foreground">{t("shopping.hub.empty")}</p>
      <Button asChild size="lg">
        <Link
          to="/app/compras/planejar"
          data-testid="shopping-hub-plan-purchases"
        >
          {t("shoppingPlan.entry")}
        </Link>
      </Button>
      <Button asChild variant="ghost">
        <Link to="/app/cardapio">{t("shopping.hub.plan")}</Link>
      </Button>
    </div>
  );
}
