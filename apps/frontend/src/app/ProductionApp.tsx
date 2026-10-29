import { useCallback, useEffect, useMemo, useRef, type ReactNode } from "react";
import {
  createBrowserRouter,
  RouterProvider,
  Navigate,
  Link,
  Outlet,
  useLocation,
} from "react-router-dom";
import { RuntimeProvider, useRuntime } from "@/app/runtime/RuntimeProvider";
import { SessionProvider, useSession } from "@/app/session/SessionProvider";
import { createProductionRuntime } from "@/app/runtime/createProductionRuntime";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import { Button } from "@/components/ui/button";
import {
  ProductionI18nProvider,
  useProductionI18n,
} from "@/app/i18n/ProductionI18nProvider";
import type { ProductionLocale } from "@/app/i18n/productionCatalog";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import { ProductionInventoryList } from "@/features/inventory/ProductionInventoryList";
import { ProductionInventoryDetail } from "@/features/inventory/ProductionInventoryDetail";
import { ProductionInventoryForm } from "@/features/inventory/ProductionInventoryForm";
import { RecipeProvider } from "@/features/recipes/RecipeProvider";
import { RecipesListPage } from "@/features/recipes/RecipesListPage";
import { RecipeGeneratePage } from "@/features/recipes/RecipeGeneratePage";
import { RecipeDetailPage } from "@/features/recipes/RecipeDetailPage";
import { PublicEntryPage } from "@/features/entry/PublicEntryPage";
import { ContextualHomePage } from "@/features/home/ContextualHomePage";
import { ContextualHomeProvider } from "@/features/home/ContextualHomeProvider";
import { ProfileProvider } from "@/features/profile/ProfileProvider";
import { ProfileOverviewPage } from "@/features/profile/ProfileOverviewPage";
import { ProfileDataPage } from "@/features/profile/ProfileDataPage";
import { ProfilePreferencesPage } from "@/features/profile/ProfilePreferencesPage";
import { ProfileEquipmentPage } from "@/features/profile/ProfileEquipmentPage";
import {
  UnsavedChangesCoordinatorProvider,
  useOptionalUnsavedChangesCoordinator,
} from "@/features/profile/UnsavedChangesCoordinator";
import { ProfileWorkspaceStatusBanner } from "@/features/profile/ProfileWorkspaceStatusBanner";

function LocaleSwitcher() {
  const { locale, locales, setLocale, t } = useProductionI18n();
  return (
    <>
      <div
        className="hidden gap-1 sm:flex"
        role="group"
        aria-label={t("lang.label")}
      >
        {locales.map((code) => (
          <button
            key={code}
            type="button"
            data-testid={`production-lang-${code}`}
            aria-pressed={locale === code}
            onClick={() => setLocale(code as ProductionLocale)}
            className={`rounded-full px-3 py-1 text-xs ${
              locale === code ? "bg-secondary" : "text-muted-foreground"
            }`}
          >
            {code === "pt-BR" ? "PT-BR" : code.toUpperCase()}
          </button>
        ))}
      </div>
      <label className="sm:hidden">
        <span className="sr-only">{t("lang.label")}</span>
        <select
          data-testid="production-lang-select"
          aria-label={t("lang.label")}
          className="max-w-[5.5rem] rounded-md border border-border bg-background px-2 py-1 text-xs"
          value={locale}
          onChange={(event) =>
            setLocale(event.target.value as ProductionLocale)
          }
        >
          {locales.map((code) => (
            <option key={code} value={code}>
              {code === "pt-BR" ? "PT-BR" : code.toUpperCase()}
            </option>
          ))}
        </select>
      </label>
    </>
  );
}

/**
 * Public entry route. A visitor who already has a session goes straight to the home. Everyone
 * else, including while the session check is still loading or when it fails, sees the public
 * page at once; the page itself never reads the session, so it cannot render personal data.
 */
function EntryRoute() {
  const { session } = useSession();
  if (session.status === "authenticated") {
    return <Navigate to="/app/hoje" replace />;
  }
  return <PublicEntryPage />;
}

function ProductionAccess() {
  const { session, beginLogin } = useSession();
  const { t } = useProductionI18n();
  const location = useLocation();
  const returnUrl =
    (location.state as { from?: string } | null)?.from ?? "/app/hoje";

  if (session.status === "loading") {
    return (
      <main className="mx-auto flex min-h-screen max-w-lg items-center p-8">
        <p role="status">{t("inventory.loading")}</p>
      </main>
    );
  }

  if (session.status === "authenticated") {
    return <Navigate to="/app/hoje" replace />;
  }

  if (session.status === "unavailable") {
    return (
      <main className="mx-auto flex min-h-screen max-w-lg items-center p-8">
        <FeatureUnavailable
          feature="access"
          title={t("feature.serviceUnavailable")}
          detail={t("access.detail")}
        />
      </main>
    );
  }

  return (
    <main
      data-testid="production-access"
      className="mx-auto flex min-h-screen max-w-lg flex-col justify-center gap-4 p-8"
    >
      <h1 className="font-display text-3xl">{t("access.loginTitle")}</h1>
      <p className="text-muted-foreground">{t("access.loginDetail")}</p>
      {session.status === "expired" && (
        <p role="status" className="text-sm text-warning-foreground">
          {t("access.expired")}
        </p>
      )}
      <Button
        type="button"
        data-testid="production-login"
        onClick={() => beginLogin(returnUrl)}
      >
        {t("access.loginAction")}
      </Button>
    </main>
  );
}

const PRIMARY_NAV_ITEMS = [
  { to: "/app/hoje", key: "home", testId: "production-nav-home" },
  { to: "/app/despensa", key: "inventory", testId: "production-nav-despensa" },
  { to: "/app/receitas", key: "recipes", testId: "production-nav-receitas" },
  { to: "/app/perfil", key: "profile", testId: "production-nav-perfil" },
] as const;

function PrimaryNav() {
  const { t } = useProductionI18n();
  const location = useLocation();
  const labels: Record<(typeof PRIMARY_NAV_ITEMS)[number]["key"], string> = {
    home: t("nav.home"),
    inventory: t("inventory.title"),
    recipes: t("nav.recipes"),
    profile: t("nav.profile"),
  };
  return (
    <nav
      aria-label={t("nav.primary")}
      data-testid="production-primary-nav"
      className="flex flex-wrap gap-1"
    >
      {PRIMARY_NAV_ITEMS.map((item) => {
        const active = location.pathname.startsWith(item.to);
        return (
          <Link
            key={item.key}
            to={item.to}
            data-testid={item.testId}
            aria-current={active ? "page" : undefined}
            className={`rounded-md px-3 py-1.5 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
              active
                ? "bg-secondary font-medium"
                : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
            }`}
          >
            {labels[item.key] || item.key}
          </Link>
        );
      })}
    </nav>
  );
}

/**
 * Maps a production pathname to the catalog key of its page name, used for the
 * document title so browser tabs, history, and screen readers identify the page.
 */
export function pageTitleKeyFor(pathname: string): string {
  if (pathname === "/") return "page.title.entry";
  if (pathname === "/acesso") return "page.title.access";
  if (pathname.startsWith("/app/hoje")) return "page.title.home";
  if (pathname === "/app/despensa/novo") return "page.title.inventoryNew";
  if (/^\/app\/despensa\/[^/]+\/editar$/.test(pathname)) {
    return "page.title.inventoryEdit";
  }
  if (/^\/app\/despensa\/[^/]+$/.test(pathname)) {
    return "page.title.inventoryDetail";
  }
  if (pathname.startsWith("/app/despensa")) return "page.title.inventory";
  if (pathname === "/app/receitas/gerar") return "page.title.recipeGenerate";
  if (pathname.startsWith("/app/receitas/")) return "page.title.recipeDetail";
  if (pathname === "/app/receitas") return "page.title.recipes";
  if (pathname.startsWith("/app/perfil")) return "page.title.profile";
  return "page.title.unavailable";
}

/** Keeps `document.title` in sync with the current route and locale. */
function DocumentTitle() {
  const { t } = useProductionI18n();
  const { pathname } = useLocation();
  const page = t(pageTitleKeyFor(pathname));
  const brand = t("brand.name");
  useEffect(() => {
    document.title = pathname === "/" ? brand : `${page} · ${brand}`;
  }, [brand, page, pathname]);
  return null;
}

function ProductionAppShell({ children }: { children: ReactNode }) {
  const { t } = useProductionI18n();
  const { session, logout, isAuthenticated } = useSession();
  const coordinator = useOptionalUnsavedChangesCoordinator();
  const logoutInFlightRef = useRef(false);

  const performLogout = useCallback(() => {
    if (logoutInFlightRef.current) return;
    logoutInFlightRef.current = true;
    void Promise.resolve(logout()).finally(() => {
      logoutInFlightRef.current = false;
    });
  }, [logout]);

  const requestLogout = useCallback(() => {
    if (coordinator) {
      coordinator.requestNavigation(performLogout);
      return;
    }
    performLogout();
  }, [coordinator, performLogout]);

  return (
    <div className="min-h-screen bg-background text-foreground">
      <a
        href="#main-content"
        data-testid="production-skip-link"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-md focus:bg-background focus:px-4 focus:py-2 focus:ring-2 focus:ring-ring"
      >
        {t("nav.skipToContent")}
      </a>
      <header className="sticky top-0 z-40 border-b border-border bg-background/95 px-4 py-3 backdrop-blur sm:px-6 sm:py-4">
        {/*
          On narrow screens the brand and account controls share the first row and the
          primary navigation takes a full second row; from `sm` up everything is one row.
        */}
        <div className="mx-auto flex max-w-3xl flex-wrap items-center gap-x-4 gap-y-2">
          {/*
            Brand and primary nav use ordinary React Router Links. While a profile
            editor is dirty, UnsavedChangesCoordinatorProvider's useBlocker intercepts
            same-tab navigations (including these). Modified clicks open a new context
            and leave the draft in the original tab.
          */}
          <Link
            to="/app/hoje"
            data-testid="production-brand-link"
            className="order-1 font-display text-xl"
          >
            {t("brand.name")}
          </Link>
          {isAuthenticated && (
            <div className="order-3 w-full sm:order-2 sm:w-auto">
              <PrimaryNav />
            </div>
          )}
          <div className="order-2 ml-auto flex items-center gap-2 sm:order-3">
            <LocaleSwitcher />
            {isAuthenticated && (
              <>
                <span className="hidden text-sm text-muted-foreground md:inline">
                  {session.displayName || t("access.signedIn")}
                </span>
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  data-testid="production-logout"
                  onClick={requestLogout}
                >
                  {t("access.logout")}
                </Button>
              </>
            )}
          </div>
        </div>
      </header>
      <main
        id="main-content"
        tabIndex={-1}
        className="mx-auto max-w-3xl px-4 py-6 focus:outline-none sm:p-8"
      >
        {children}
      </main>
    </div>
  );
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { session } = useSession();
  const { t } = useProductionI18n();
  const location = useLocation();
  if (session.status === "loading") {
    return (
      <p
        role="status"
        data-testid="production-session-loading"
        className="p-8 text-sm text-muted-foreground"
      >
        {t("app.loading")}
      </p>
    );
  }
  if (session.status !== "authenticated") {
    return (
      <Navigate to="/acesso" replace state={{ from: location.pathname }} />
    );
  }
  return <>{children}</>;
}

function HomeRoute() {
  const runtime = useRuntime();
  // Production wires the unavailable adapter; prototype composition may inject mocks
  // via a separate app root — never through createProductionRuntime.
  return (
    <ContextualHomeProvider adapter={runtime.contextualHomeAdapter}>
      <ContextualHomePage />
    </ContextualHomeProvider>
  );
}

/**
 * Session and inventory providers mount only for access and authenticated
 * subtrees so the public `/` route never bootstraps `getSession()`.
 */
function SessionScopedRoutes({ children }: { children: ReactNode }) {
  const runtime = useRuntime();
  return (
    <SessionProvider adapter={runtime.sessionAdapter}>
      <InventoryProvider repository={runtime.inventoryRepository}>
        <RecipeProvider repository={runtime.recipeRepository}>
          {children}
        </RecipeProvider>
      </InventoryProvider>
    </SessionProvider>
  );
}

/**
 * Layout for every `/app/*` route. Session, inventory, the auth guard, the
 * unsaved-changes coordinator, and the shell mount once here, so moving between
 * destinations keeps the header in place and does not refetch the session or
 * reset inventory state. (Before PLAN-0035 each destination mounted its own
 * providers, which re-requested the session and blanked the shell on every
 * navigation.)
 */
function AuthenticatedAppLayout() {
  return (
    <SessionScopedRoutes>
      <RequireAuth>
        <UnsavedChangesCoordinatorProvider>
          <ProductionAppShell>
            <Outlet />
          </ProductionAppShell>
        </UnsavedChangesCoordinatorProvider>
      </RequireAuth>
    </SessionScopedRoutes>
  );
}

/**
 * Profile subtree. `ProfileProvider` fetches profile, preferences, equipment,
 * and completeness as soon as it mounts, so it is scoped to `/app/perfil*` and
 * `/app/hoje` and `/app/despensa` never call the profile endpoints.
 */
function ProfileLayout() {
  const runtime = useRuntime();
  return (
    <ProfileProvider
      repository={runtime.profileRepository}
      adultPolicy={runtime.adultDeclarationPolicy}
    >
      <ProfileWorkspaceStatusBanner />
      <Outlet />
    </ProfileProvider>
  );
}

function AppUnavailable() {
  const { t } = useProductionI18n();
  return (
    <div className="space-y-6">
      <h1 className="font-display text-3xl">{t("page.title.unavailable")}</h1>
      <FeatureUnavailable
        feature="app"
        title={t("feature.unavailable")}
        detail={t("app.unavailable.detail")}
      />
      <Link
        to="/app/hoje"
        data-testid="production-unavailable-home-link"
        className="inline-block text-sm underline underline-offset-4"
      >
        {t("app.unavailable.backHome")}
      </Link>
    </div>
  );
}

/** Renders the route element and keeps the document title in sync with it. */
function Titled({ children }: { children: ReactNode }) {
  return (
    <>
      <DocumentTitle />
      {children}
    </>
  );
}

/**
 * Data router so profile unsaved-change blocking can use `useBlocker` (not available
 * under legacy `BrowserRouter`). Public `/` stays outside SessionProvider.
 */
function createProductionRouter() {
  return createBrowserRouter([
    {
      path: "/",
      element: (
        <Titled>
          <SessionScopedRoutes>
            <EntryRoute />
          </SessionScopedRoutes>
        </Titled>
      ),
    },
    {
      path: "/acesso",
      element: (
        <Titled>
          <SessionScopedRoutes>
            <ProductionAccess />
          </SessionScopedRoutes>
        </Titled>
      ),
    },
    {
      path: "/app",
      element: (
        <Titled>
          <AuthenticatedAppLayout />
        </Titled>
      ),
      children: [
        { index: true, element: <Navigate to="/app/hoje" replace /> },
        { path: "hoje", element: <HomeRoute /> },
        { path: "despensa", element: <ProductionInventoryList /> },
        {
          path: "despensa/novo",
          element: <ProductionInventoryForm mode="create" />,
        },
        {
          path: "despensa/:lotId/editar",
          element: <ProductionInventoryForm mode="edit" />,
        },
        { path: "despensa/:lotId", element: <ProductionInventoryDetail /> },
        { path: "receitas", element: <RecipesListPage /> },
        { path: "receitas/gerar", element: <RecipeGeneratePage /> },
        { path: "receitas/:recipeId", element: <RecipeDetailPage /> },
        {
          path: "perfil",
          element: <ProfileLayout />,
          children: [
            { index: true, element: <ProfileOverviewPage /> },
            { path: "dados", element: <ProfileDataPage /> },
            { path: "preferencias", element: <ProfilePreferencesPage /> },
            { path: "equipamentos", element: <ProfileEquipmentPage /> },
          ],
        },
        { path: "*", element: <AppUnavailable /> },
      ],
    },
    { path: "*", element: <Navigate to="/" replace /> },
  ]);
}

/**
 * Production composition root.
 * Must not import StoreProvider, mock fixtures, scenario tooling, or synthetic seeds.
 * Public `/` stays outside SessionProvider so entry never fetches session.
 */
export default function ProductionApp() {
  // Compose once per mount so tests can stub globalThis.fetch before render.
  const runtime = useMemo(() => createProductionRuntime(), []);
  const router = useMemo(() => createProductionRouter(), []);
  return (
    <RuntimeProvider runtime={runtime}>
      <ProductionI18nProvider>
        <RouterProvider router={router} />
      </ProductionI18nProvider>
    </RuntimeProvider>
  );
}
