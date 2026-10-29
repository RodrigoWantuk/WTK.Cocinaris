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
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import { ProductionInventoryList } from "@/features/inventory/ProductionInventoryList";
import { ProductionInventoryDetail } from "@/features/inventory/ProductionInventoryDetail";
import { ProductionInventoryForm } from "@/features/inventory/ProductionInventoryForm";
import { RecipeProvider } from "@/features/recipes/RecipeProvider";
import { PlanningProvider } from "@/features/planning/PlanningProvider";
import { MenusPage } from "@/features/planning/MenusPage";
import { ShoppingHubPage } from "@/features/planning/ShoppingHubPage";
import { AccountMenu, LanguageChoice } from "@/app/shell/AccountMenu";
import { AppNavigation } from "@/app/shell/AppNavigation";
import { MenuPage } from "@/features/planning/MenuPage";
import { ShoppingPage } from "@/features/planning/ShoppingPage";
import { RecipesListPage } from "@/features/recipes/RecipesListPage";
import { RecipeGeneratePage } from "@/features/recipes/RecipeGeneratePage";
import { TodayPage } from "@/features/home/TodayPage";
import { CookingPage } from "@/features/recipes/CookingPage";
import { RecipeEditorPage } from "@/features/recipes/RecipeEditorPage";
import { RecipeDetailPage } from "@/features/recipes/RecipeDetailPage";
import { PublicEntryPage } from "@/features/entry/PublicEntryPage";
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
  if (pathname === "/app/receitas/nova") return "page.title.recipeNew";
  if (/^\/app\/receitas\/[^/]+\/editar$/.test(pathname))
    return "page.title.recipeEdit";
  if (/^\/app\/receitas\/[^/]+\/variacao$/.test(pathname))
    return "page.title.recipeVariation";
  if (/^\/app\/receitas\/[^/]+\/versoes\//.test(pathname))
    return "page.title.recipeRevision";
  if (pathname === "/app/compras") return "page.title.shopping";
  if (pathname === "/app/cardapio") return "page.title.planning";
  if (/^\/app\/cardapio\/[^/]+\/compras$/.test(pathname))
    return "page.title.shopping";
  if (pathname.startsWith("/app/cardapio/")) return "page.title.menu";
  if (pathname.startsWith("/app/receitas/cozinhar/"))
    return "page.title.cooking";
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
      <header className="sticky top-0 z-40 border-b border-border bg-background px-4 py-3 sm:px-6">
        {/*
          No backdrop filter here: it would make the header the containing block of the fixed
          bottom navigation on phones. Brand and navigation use React Router links, so the
          unsaved-changes coordinator still intercepts same-tab navigation from profile editors.
        */}
        <div className="mx-auto flex max-w-5xl items-center gap-4">
          <Link
            to="/app/hoje"
            data-testid="production-brand-link"
            className="text-lg font-bold tracking-tight"
          >
            {t("brand.name")}
          </Link>
          {isAuthenticated && (
            <div className="md:flex-1">
              <AppNavigation />
            </div>
          )}
          <div className="ml-auto flex items-center gap-2">
            {isAuthenticated ? (
              <AccountMenu
                displayName={session.displayName ?? null}
                onLogout={requestLogout}
              />
            ) : (
              <LanguageChoice />
            )}
          </div>
        </div>
      </header>
      <main
        id="main-content"
        tabIndex={-1}
        className="mx-auto max-w-3xl px-4 pb-28 pt-6 focus:outline-none sm:px-8 md:pb-10 md:pt-8"
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
  // Live deterministic sources (PLAN-0021, PLAN-0042). The prototype composition keeps the
  // contextual-home presentation with mock adapters in its own app root.
  return <TodayPage />;
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
          <PlanningProvider repository={runtime.planningRepository}>
            {children}
          </PlanningProvider>
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
        { path: "compras", element: <ShoppingHubPage /> },
        { path: "cardapio", element: <MenusPage /> },
        { path: "cardapio/:menuId", element: <MenuPage /> },
        { path: "cardapio/:menuId/compras", element: <ShoppingPage /> },
        { path: "receitas/nova", element: <RecipeEditorPage mode="new" /> },
        { path: "receitas/:recipeId", element: <RecipeDetailPage /> },
        {
          path: "receitas/:recipeId/versoes/:revisionNumber",
          element: <RecipeDetailPage />,
        },
        {
          path: "receitas/:recipeId/editar",
          element: <RecipeEditorPage mode="edit" />,
        },
        {
          path: "receitas/:recipeId/variacao",
          element: <RecipeEditorPage mode="variation" />,
        },
        { path: "receitas/cozinhar/:executionId", element: <CookingPage /> },
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
