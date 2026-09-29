import { useCallback, useMemo, useRef, type ReactNode } from "react";
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

function ProductionAccess() {
  const { session, beginLogin } = useSession();
  const { t } = useProductionI18n();
  const location = useLocation();
  const returnUrl =
    (location.state as { from?: string } | null)?.from ?? "/app/hoje";

  if (session.status === "loading") {
    return (
      <div className="mx-auto flex min-h-screen max-w-lg items-center p-8">
        <p role="status">{t("inventory.loading")}</p>
      </div>
    );
  }

  if (session.status === "authenticated") {
    return <Navigate to="/app/hoje" replace />;
  }

  if (session.status === "unavailable") {
    return (
      <div className="mx-auto flex min-h-screen max-w-lg items-center p-8">
        <FeatureUnavailable
          feature="access"
          title={t("feature.serviceUnavailable")}
          detail={t("access.detail")}
        />
      </div>
    );
  }

  return (
    <div
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
    </div>
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
      <header className="sticky top-0 z-40 border-b border-border bg-background/95 px-6 py-4 backdrop-blur">
        <div className="mx-auto flex max-w-3xl flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-4">
            {/*
              Brand and primary nav use ordinary React Router Links. While a profile
              editor is dirty, UnsavedChangesCoordinatorProvider's useBlocker intercepts
              same-tab navigations (including these). Modified clicks open a new context
              and leave the draft in the original tab.
            */}
            <Link
              to="/app/hoje"
              data-testid="production-brand-link"
              className="font-display text-xl"
            >
              {t("brand.name")}
            </Link>
            {isAuthenticated && <PrimaryNav />}
          </div>
          <div className="flex items-center gap-2">
            <LocaleSwitcher />
            {isAuthenticated && (
              <>
                <span className="hidden text-sm text-muted-foreground sm:inline">
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
      <main className="mx-auto max-w-3xl p-8">{children}</main>
    </div>
  );
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { session } = useSession();
  const location = useLocation();
  if (session.status === "loading") {
    return <p role="status">…</p>;
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
 *
 * `ProfileProvider` is intentionally NOT mounted here: it fetches profile,
 * preferences, equipment, and completeness as soon as the session becomes
 * authenticated, so mounting it for every session-scoped route would make
 * `/app/hoje` and `/app/despensa` call the profile endpoints even though
 * they never read profile data. See `ProfileScopedRoutes` below, which wraps
 * only the `/app/perfil*` subtree.
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
 * Adds `ProfileProvider` on top of `SessionScopedRoutes`, scoped to the
 * `/app/perfil*` subtree only. Keeping this separate from
 * `SessionScopedRoutes` is what keeps `/app/hoje` and `/app/despensa` from
 * ever calling `getProfile`/`getPreferences`/`getEquipment`/`getCompleteness`.
 */
function ProfileScopedRoutes({ children }: { children: ReactNode }) {
  const runtime = useRuntime();
  return (
    <SessionScopedRoutes>
      <ProfileProvider
        repository={runtime.profileRepository}
        adultPolicy={runtime.adultDeclarationPolicy}
      >
        {children}
      </ProfileProvider>
    </SessionScopedRoutes>
  );
}

function ProfileLayout() {
  return (
    <ProfileScopedRoutes>
      <RequireAuth>
        <UnsavedChangesCoordinatorProvider>
          <ProductionAppShell>
            <ProfileWorkspaceStatusBanner />
            <Outlet />
          </ProductionAppShell>
        </UnsavedChangesCoordinatorProvider>
      </RequireAuth>
    </ProfileScopedRoutes>
  );
}

function AppUnavailable() {
  const { t } = useProductionI18n();
  return (
    <SessionScopedRoutes>
      <RequireAuth>
        <ProductionAppShell>
          <FeatureUnavailable
            feature="app"
            title={t("feature.unavailable")}
            detail={t("app.unavailable.detail")}
          />
        </ProductionAppShell>
      </RequireAuth>
    </SessionScopedRoutes>
  );
}

/**
 * Data router so profile unsaved-change blocking can use `useBlocker` (not available
 * under legacy `BrowserRouter`). Public `/` stays outside SessionProvider.
 */
function createProductionRouter() {
  return createBrowserRouter([
    { path: "/", element: <PublicEntryPage /> },
    {
      path: "/acesso",
      element: (
        <SessionScopedRoutes>
          <ProductionAccess />
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/hoje",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <ProductionAppShell>
              <HomeRoute />
            </ProductionAppShell>
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <Navigate to="/app/hoje" replace />
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/despensa",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <ProductionAppShell>
              <ProductionInventoryList />
            </ProductionAppShell>
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/despensa/novo",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <ProductionAppShell>
              <ProductionInventoryForm mode="create" />
            </ProductionAppShell>
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/despensa/:lotId/editar",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <ProductionAppShell>
              <ProductionInventoryForm mode="edit" />
            </ProductionAppShell>
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/despensa/:lotId",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <ProductionAppShell>
              <ProductionInventoryDetail />
            </ProductionAppShell>
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/receitas",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <ProductionAppShell>
              <RecipesListPage />
            </ProductionAppShell>
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/receitas/gerar",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <ProductionAppShell>
              <RecipeGeneratePage />
            </ProductionAppShell>
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/receitas/:recipeId",
      element: (
        <SessionScopedRoutes>
          <RequireAuth>
            <ProductionAppShell>
              <RecipeDetailPage />
            </ProductionAppShell>
          </RequireAuth>
        </SessionScopedRoutes>
      ),
    },
    {
      path: "/app/perfil",
      element: <ProfileLayout />,
      children: [
        { index: true, element: <ProfileOverviewPage /> },
        { path: "dados", element: <ProfileDataPage /> },
        { path: "preferencias", element: <ProfilePreferencesPage /> },
        { path: "equipamentos", element: <ProfileEquipmentPage /> },
      ],
    },
    { path: "/app/*", element: <AppUnavailable /> },
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
