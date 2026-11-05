import { WifiOff } from "lucide-react";
import type { ReactNode } from "react";
import { Link, useLocation } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { useOfflineList } from "./OfflineListProvider";

/** Routes that work with no connection and no session check (ADR-0012). */
export function isOfflinePath(pathname: string): boolean {
  return (
    pathname === "/app/compras" ||
    /^\/app\/cardapio\/[^/]+\/compras$/.test(pathname)
  );
}

/**
 * What the shell says about the connection: a banner while offline, announced politely, and the
 * marks that stopped applying once they were delivered.
 */
export function OfflineNotices() {
  const { t } = useProductionI18n();
  const offlineList = useOfflineList();
  if (!offlineList) return null;
  const { offline, conflicts, dismissConflicts } = offlineList;
  return (
    <>
      {offline ? (
        <p
          role="status"
          data-testid="offline-banner"
          className="mb-4 flex items-start gap-2 rounded-2xl border border-border bg-secondary/40 p-3 text-sm"
        >
          <WifiOff aria-hidden="true" className="mt-0.5 h-4 w-4 shrink-0" />
          {t("offline.banner")}
        </p>
      ) : null}
      {conflicts.length > 0 ? (
        <div
          role="status"
          data-testid="offline-conflicts"
          className="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-secondary/40 p-3 text-sm"
        >
          <span className="min-w-0">
            {t(
              conflicts.length === 1
                ? "offline.conflicts.one"
                : "offline.conflicts.other",
              { count: conflicts.length, names: conflicts.join(", ") },
            )}
          </span>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            data-testid="offline-conflicts-dismiss"
            onClick={dismissConflicts}
          >
            {t("offline.conflicts.dismiss")}
          </Button>
        </div>
      ) : null}
    </>
  );
}

/**
 * Limits the offline session to the shopping list. The other routes need the server, and rendering
 * them would only show loading failures, so they point to the list.
 */
export function OfflineGate({ children }: { children: ReactNode }) {
  const { t } = useProductionI18n();
  const { session } = useSession();
  const { pathname } = useLocation();
  if (session.status !== "offline" || isOfflinePath(pathname)) {
    return <>{children}</>;
  }
  return (
    <div data-testid="offline-route" className="space-y-4">
      <h1 className="font-display text-3xl">{t("offline.route.title")}</h1>
      <p className="text-muted-foreground">{t("offline.route.detail")}</p>
      <Button asChild size="lg">
        <Link to="/app/compras" data-testid="offline-open-list">
          {t("offline.route.action")}
        </Link>
      </Button>
    </div>
  );
}
