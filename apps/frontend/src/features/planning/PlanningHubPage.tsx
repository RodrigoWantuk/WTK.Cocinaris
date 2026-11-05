import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { useOfflineList } from "./offline/OfflineListProvider";
import { isUnreachable } from "./offline/offlineSync";
import {
  usePlanningOverview,
  type PlanningOverview,
  type PlanningStep,
} from "./planningOverview";
import { formatMenuPeriod } from "./planningDates";
import { ShoppingPage } from "./ShoppingPage";

interface HubLink {
  to: string;
  labelKey: string;
  testId: string;
  count?: number;
}

/** The main action of each step; the rest of the page holds what is not the next step. */
function primaryLink(
  step: PlanningStep,
  overview: PlanningOverview,
): HubLink | null {
  const menuPath = overview.menu ? `/app/cardapio/${overview.menu.menuId}` : "";
  switch (step.kind) {
    case "start":
      return null;
    case "resume":
      return {
        to: overview.resumePath ?? "/app/compras/planejar",
        labelKey: "shoppingPlan.current.continue",
        testId: "planning-hub-resume",
      };
    case "draft":
      return {
        to: menuPath,
        labelKey: "planningHub.continueMenu",
        testId: "planning-hub-draft",
      };
    case "buy":
      return {
        to: `${menuPath}/compras`,
        labelKey: "planningHub.buy",
        testId: "planning-hub-buy",
        count: step.count,
      };
    case "arrival":
      return {
        to: `${menuPath}/chegada`,
        labelKey: "shopping.arrival",
        testId: "planning-hub-arrival",
      };
    case "prepare":
      return {
        to: `/app/compras/planejar/${overview.prepareSessionId}/preparo`,
        labelKey: "shoppingPlan.route.open",
        testId: "planning-hub-prepare",
      };
    case "view":
      return {
        to: menuPath,
        labelKey: "planningHub.viewMenu",
        testId: "planning-hub-view",
      };
  }
}

/** The menu's other destinations, without repeating the main action. */
function secondaryLinks(overview: PlanningOverview, main: HubLink | null) {
  const { menu, boughtCount, prepareSessionId } = overview;
  if (!menu) return [];
  const path = `/app/cardapio/${menu.menuId}`;
  const links: HubLink[] = [];
  if (menu.status === "accepted") {
    links.push({
      to: `${path}/compras`,
      labelKey: "shopping.title",
      testId: "planning-hub-list",
    });
    if (boughtCount > 0) {
      links.push({
        to: `${path}/chegada`,
        labelKey: "shopping.arrival",
        testId: "planning-hub-arrival-link",
      });
    }
    if (prepareSessionId) {
      links.push({
        to: `/app/compras/planejar/${prepareSessionId}/preparo`,
        labelKey: "shoppingPlan.route.open",
        testId: "planning-hub-prepare-link",
      });
    }
  }
  links.push({
    to: path,
    labelKey: "planningHub.viewMenu",
    testId: "planning-hub-menu-link",
  });
  return links.filter((link) => link.to !== main?.to);
}

/**
 * The "Planejar" tab (PLAN-0063): one place for planning, whatever stage the person is in. It shows the
 * current menu and leads with the next step, instead of opening a list that could be mistaken for a
 * place to register loose items.
 *
 * Without a connection the menus cannot be listed, so the list opened last stands in for the page
 * (ADR-0012). The offline shell depends on that.
 */
export function PlanningHubPage() {
  const { t, locale } = useProductionI18n();
  const state = usePlanningOverview();
  const offlineList = useOfflineList();
  const lastMenuId = offlineList?.lastMenuId;
  const noteUnreachable = offlineList?.noteUnreachable;
  const [savedMenuId, setSavedMenuId] = useState<string | null | undefined>();
  const unreachable = state.status === "failed" && isUnreachable(state.error);

  useEffect(() => {
    if (!unreachable || !lastMenuId) return;
    let cancelled = false;
    noteUnreachable?.();
    void lastMenuId().then((id) => {
      if (!cancelled) setSavedMenuId(id);
    });
    return () => {
      cancelled = true;
    };
    // The callbacks change with the connection state, which must not read the saved list again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [unreachable]);

  if (savedMenuId) return <ShoppingPage menuId={savedMenuId} />;
  if (savedMenuId === null) {
    return (
      <p data-testid="shopping-hub-offline" className="text-muted-foreground">
        {t("offline.noList")}
      </p>
    );
  }
  if (state.status === "failed") {
    // With the offline list available, the saved list is still being read.
    if (unreachable && lastMenuId) {
      return <p role="status">{t("planning.loading")}</p>;
    }
    return (
      <p role="alert" className="text-muted-foreground">
        {t("planning.error.load")}
      </p>
    );
  }
  if (state.status === "loading") {
    return <p role="status">{t("planning.loading")}</p>;
  }

  const { overview } = state;
  const { menu, step } = overview;
  const main = primaryLink(step, overview);
  const others = secondaryLinks(overview, main);

  return (
    <div data-testid="planning-hub" className="space-y-6 pt-2">
      <div>
        <h1 className="font-display text-3xl">{t("planningHub.title")}</h1>
        {step.kind === "start" ? (
          <p className="mt-1 text-muted-foreground">{t("planningHub.lead")}</p>
        ) : null}
        {step.kind === "resume" ? (
          <p className="mt-1 text-muted-foreground">
            {t("shoppingPlan.current.body")}
          </p>
        ) : null}
      </div>

      {menu ? (
        <section
          data-testid="planning-hub-menu"
          aria-label={t("planningHub.currentMenu")}
          className="surface space-y-1 p-4"
        >
          <p className="font-semibold">
            {t(
              menu.status === "accepted"
                ? "planningHub.menuPeriod"
                : "planningHub.draftPeriod",
              {
                period: formatMenuPeriod(menu.startDate, menu.endDate, locale),
              },
            )}
          </p>
          <p className="text-sm text-muted-foreground">
            {t(
              menu.entryCount === 1
                ? "planningHub.dishes.one"
                : "planningHub.dishes.other",
              { count: menu.entryCount },
            )}
          </p>
        </section>
      ) : null}

      {main ? (
        <Button asChild size="lg" className="w-full">
          <Link to={main.to} data-testid={main.testId}>
            {t(main.labelKey, { count: main.count ?? 0 })}
          </Link>
        </Button>
      ) : (
        <div className="space-y-2">
          <Button asChild size="lg" className="w-full">
            <Link to="/app/compras/planejar" data-testid="planning-hub-start">
              {t("shoppingPlan.entry")}
            </Link>
          </Button>
          <Button asChild variant="ghost" className="w-full">
            <Link to="/app/cardapio" data-testid="planning-hub-by-hand">
              {t("planningHub.byHand")}
            </Link>
          </Button>
        </div>
      )}

      {others.length > 0 ? (
        <ul className="divide-y divide-border">
          {others.map((link) => (
            <li key={link.testId}>
              <Link
                to={link.to}
                data-testid={link.testId}
                className="flex min-h-11 items-center py-2 font-medium text-primary hover:underline"
              >
                {t(link.labelKey)}
              </Link>
            </li>
          ))}
        </ul>
      ) : null}

      {step.kind !== "start" ? (
        <div className="flex flex-wrap gap-x-6">
          {step.kind !== "resume" ? (
            <Button asChild variant="ghost" className="px-0">
              <Link to="/app/compras/planejar" data-testid="planning-hub-new">
                {t("planningHub.newPlan")}
              </Link>
            </Button>
          ) : null}
          <Button asChild variant="ghost" className="px-0">
            <Link to="/app/cardapio" data-testid="planning-hub-all-menus">
              {t("planningHub.allMenus")}
            </Link>
          </Button>
        </div>
      ) : null}
    </div>
  );
}
