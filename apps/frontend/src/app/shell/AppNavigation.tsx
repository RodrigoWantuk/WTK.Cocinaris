import { BookOpen, Home, Plus, Refrigerator, ShoppingBag } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";

const ITEMS = [
  { to: "/app/hoje", key: "home", testId: "production-nav-home", icon: Home },
  {
    to: "/app/despensa",
    key: "inventory",
    testId: "production-nav-despensa",
    icon: Refrigerator,
  },
  {
    to: "/app/despensa/novo",
    key: "add",
    testId: "production-nav-add",
    icon: Plus,
  },
  {
    to: "/app/receitas",
    key: "recipes",
    testId: "production-nav-receitas",
    icon: BookOpen,
  },
  {
    to: "/app/compras",
    key: "shopping",
    testId: "production-nav-compras",
    icon: ShoppingBag,
  },
] as const;

type ItemKey = (typeof ITEMS)[number]["key"];

/**
 * Which tab a path belongs to. The menu lives inside "Hoje" (PLAN-0054 direction 2), so menu
 * pages light up "Hoje", except a menu's shopping list, which belongs to "Compras".
 */
export function activeTabFor(pathname: string): ItemKey | null {
  if (pathname === "/app/despensa/novo") return "add";
  if (pathname.startsWith("/app/compras") || /\/compras$/.test(pathname))
    return "shopping";
  if (pathname.startsWith("/app/hoje") || pathname.startsWith("/app/cardapio"))
    return "home";
  if (pathname.startsWith("/app/despensa")) return "inventory";
  if (pathname.startsWith("/app/receitas")) return "recipes";
  return null;
}

/**
 * Primary navigation: four destinations and the add action. One element serves both layouts: a
 * bar fixed to the bottom of the screen on phones, within thumb reach, and an inline row in the
 * header from `md` up. The header must not create a containing block (no backdrop filter or
 * transform), or the fixed bar would attach to it instead of the viewport.
 */
export function AppNavigation() {
  const { t } = useProductionI18n();
  const { pathname } = useLocation();
  const active = activeTabFor(pathname);
  const labels: Record<ItemKey, string> = {
    home: t("nav.home"),
    inventory: t("inventory.title"),
    add: t("nav.add"),
    recipes: t("nav.recipes"),
    shopping: t("nav.shopping"),
  };

  return (
    <nav
      aria-label={t("nav.primary")}
      data-testid="production-primary-nav"
      className="safe-b fixed inset-x-0 bottom-0 z-40 border-t border-border bg-background px-2 pt-2 md:static md:border-0 md:bg-transparent md:p-0"
    >
      <ul className="mx-auto flex max-w-md items-center justify-between md:max-w-none md:justify-start md:gap-1">
        {ITEMS.map((item) => {
          const Icon = item.icon;
          const current = active === item.key;
          if (item.key === "add") {
            return (
              <li key={item.key}>
                <Link
                  to={item.to}
                  data-testid={item.testId}
                  aria-label={labels.add}
                  aria-current={current ? "page" : undefined}
                  className="flex h-12 w-12 items-center justify-center rounded-full bg-primary text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 md:ml-2 md:h-10 md:w-auto md:gap-1.5 md:px-4 md:text-sm md:font-semibold"
                >
                  <Icon aria-hidden="true" className="h-6 w-6 md:h-4 md:w-4" />
                  <span className="hidden md:inline">{labels.add}</span>
                </Link>
              </li>
            );
          }
          return (
            <li key={item.key} className="md:order-none">
              <Link
                to={item.to}
                data-testid={item.testId}
                aria-current={current ? "page" : undefined}
                className={`flex w-16 flex-col items-center gap-1 rounded-xl py-1 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring md:w-auto md:flex-row md:gap-2 md:rounded-full md:px-3 md:py-2 md:text-sm ${
                  current
                    ? "font-semibold text-foreground md:bg-secondary"
                    : "font-medium text-muted-foreground hover:text-foreground"
                }`}
              >
                <Icon aria-hidden="true" className="h-6 w-6 md:h-4 md:w-4" />
                {labels[item.key]}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
