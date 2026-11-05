import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { useOptionalShoppingPlanRepository } from "./ShoppingPlanProvider";

/**
 * Opens the preparation session of a shopping plan (PLAN-0062 Slice 6). It renders nothing when there
 * is no session left to finish, when the lookup fails, or outside the plan's provider: the page around
 * it works the same without the link.
 *
 * @param query The menu being shown, or the owner's day on "Hoje".
 */
export function SessionRouteLink({
  query,
}: {
  query: { menuId: string } | { date: string };
}) {
  const repo = useOptionalShoppingPlanRepository();
  const { t } = useProductionI18n();
  const [sessionId, setSessionId] = useState<string | null>(null);
  const menuId = "menuId" in query ? query.menuId : undefined;
  const date = "date" in query ? query.date : undefined;

  useEffect(() => {
    if (!repo) return;
    const controller = new AbortController();
    repo.findRouteLink({ menuId, date }, controller.signal).then(
      (value) => {
        if (!controller.signal.aborted) setSessionId(value);
      },
      () => undefined,
    );
    return () => controller.abort();
  }, [repo, menuId, date]);

  if (!sessionId) return null;
  return (
    <Button asChild variant="secondary" data-testid="session-route-link">
      <Link to={`/app/compras/planejar/${sessionId}/preparo`}>
        {t("shoppingPlan.route.open")}
      </Link>
    </Button>
  );
}
