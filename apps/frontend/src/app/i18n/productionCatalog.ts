/**
 * Production-safe translation catalog.
 * Independent of prototype StoreProvider / cocinaris_state_v1.
 */

import { inventoryCatalogs } from "./inventoryCatalog";
import { entryCatalogs } from "./entryCatalog";
import { homeCatalogs } from "./homeCatalog";
import { profileCatalogs } from "./profileUiCatalog";
import { recipesCatalogs } from "./recipesCatalog";
import { cookingCatalogs } from "./cookingCatalog";
import { libraryCatalogs } from "./libraryCatalog";
import { planningCatalogs } from "./planningCatalog";

export const PRODUCTION_LOCALES = ["pt-BR", "en", "es"] as const;
export type ProductionLocale = (typeof PRODUCTION_LOCALES)[number];

export const PRODUCTION_LOCALE_STORAGE_KEY = "cocinaris_production_locale";

type Catalog = Record<string, string>;

const sharedShell: Record<ProductionLocale, Catalog> = {
  "pt-BR": {
    "brand.name": "Cocinaris",
    "landing.enter": "Entrar",
    "landing.tagline":
      "O Cocinaris ajuda a transformar alimentos disponíveis em refeições úteis.",
    "landing.subtitle":
      "As superfícies autenticadas do produto exigem sessão e adapters gerenciados pelo backend.",
    "access.title.unavailable": "Serviço indisponível",
    "access.title.pending": "Integração pendente",
    "access.detail":
      "Não foi possível verificar sua sessão agora. Tente novamente em instantes.",
    "access.loginTitle": "Entrar",
    "access.loginDetail":
      "Você será levado à página de login do seu provedor de identidade. O Cocinaris nunca vê nem guarda sua senha.",
    "access.loginAction": "Continuar para o login",
    "access.expired": "Sua sessão expirou. Entre novamente para continuar.",
    "access.signedIn": "Sessão ativa",
    "access.logout": "Sair",
    "feature.unavailable": "Funcionalidade indisponível",
    "feature.integrationPending": "Integração pendente",
    "feature.serviceUnavailable": "Serviço indisponível",
    "app.unavailable.detail":
      "Receitas, planejamento, compras e cozinha guiada ainda estão em construção e chegam nas próximas versões.",
    "lang.label": "Idioma",
    "theme.label": "Tema",
    "theme.system": "Sistema",
    "theme.light": "Claro",
    "theme.dark": "Escuro",
    "install.title": "Instale o Cocinaris",
    "install.detail":
      "Abra direto da tela inicial do celular, em tela cheia, como um aplicativo.",
    "install.action": "Instalar",
    "install.later": "Agora não",
    "install.close": "Fechar",
    "install.iosBefore": "Toque em",
    "install.iosShare": "Compartilhar",
    "install.iosAfter": "e depois em “Adicionar à Tela de Início”.",
    "nav.home": "Início",
    "nav.primary": "Navegação principal",
    "nav.skipToContent": "Pular para o conteúdo",
    "app.loading": "Carregando sua sessão…",
    "app.unavailable.backHome": "Voltar ao início",
    "page.title.entry": "Cocinaris",
    "page.title.access": "Entrar",
    "page.title.home": "Início",
    "page.title.inventory": "Despensa",
    "page.title.inventoryNew": "Adicionar lote",
    "page.title.inventoryDetail": "Lote",
    "page.title.inventoryEdit": "Editar lote",
    "page.title.profile": "Perfil",
    "page.title.unavailable": "Em breve",
    "page.title.recipes": "Receitas",
    "page.title.recipeGenerate": "Gerar receita",
    "page.title.recipeDetail": "Receita",
  },
  en: {
    "brand.name": "Cocinaris",
    "landing.enter": "Enter",
    "landing.tagline":
      "Cocinaris helps transform available food into useful meals.",
    "landing.subtitle":
      "Authenticated product surfaces require backend-managed session and adapters.",
    "access.title.unavailable": "Service unavailable",
    "access.title.pending": "Integration pending",
    "access.detail":
      "We could not check your session right now. Please try again in a moment.",
    "access.loginTitle": "Sign in",
    "access.loginDetail":
      "You will continue to your identity provider's sign-in page. Cocinaris never sees or stores your password.",
    "access.loginAction": "Continue to sign in",
    "access.expired": "Your session expired. Sign in again to continue.",
    "access.signedIn": "Signed in",
    "access.logout": "Sign out",
    "feature.unavailable": "Feature unavailable",
    "feature.integrationPending": "Integration pending",
    "feature.serviceUnavailable": "Service unavailable",
    "app.unavailable.detail":
      "Recipes, planning, shopping, and guided cooking are still being built and arrive in upcoming releases.",
    "lang.label": "Language",
    "theme.label": "Theme",
    "theme.system": "System",
    "theme.light": "Light",
    "theme.dark": "Dark",
    "install.title": "Install Cocinaris",
    "install.detail":
      "Open it straight from your phone's home screen, full screen, like an app.",
    "install.action": "Install",
    "install.later": "Not now",
    "install.close": "Close",
    "install.iosBefore": "Tap",
    "install.iosShare": "Share",
    "install.iosAfter": "then “Add to Home Screen”.",
    "nav.home": "Home",
    "nav.primary": "Primary navigation",
    "nav.skipToContent": "Skip to content",
    "app.loading": "Loading your session…",
    "app.unavailable.backHome": "Back to home",
    "page.title.entry": "Cocinaris",
    "page.title.access": "Sign in",
    "page.title.home": "Home",
    "page.title.inventory": "Pantry",
    "page.title.inventoryNew": "Add lot",
    "page.title.inventoryDetail": "Lot",
    "page.title.inventoryEdit": "Edit lot",
    "page.title.profile": "Profile",
    "page.title.unavailable": "Coming soon",
    "page.title.recipes": "Recipes",
    "page.title.recipeGenerate": "Generate a recipe",
    "page.title.recipeDetail": "Recipe",
  },
  es: {
    "brand.name": "Cocinaris",
    "landing.enter": "Entrar",
    "landing.tagline":
      "Cocinaris ayuda a transformar alimentos disponibles en comidas útiles.",
    "landing.subtitle":
      "Las superficies autenticadas del producto requieren sesión y adapters gestionados por el backend.",
    "access.title.unavailable": "Servicio no disponible",
    "access.title.pending": "Integración pendiente",
    "access.detail":
      "No pudimos verificar tu sesión ahora. Inténtalo de nuevo en un momento.",
    "access.loginTitle": "Iniciar sesión",
    "access.loginDetail":
      "Continuarás a la página de inicio de sesión de tu proveedor de identidad. Cocinaris nunca ve ni guarda tu contraseña.",
    "access.loginAction": "Continuar al inicio de sesión",
    "access.expired":
      "Tu sesión expiró. Inicia sesión de nuevo para continuar.",
    "access.signedIn": "Sesión activa",
    "access.logout": "Salir",
    "feature.unavailable": "Función no disponible",
    "feature.integrationPending": "Integración pendiente",
    "feature.serviceUnavailable": "Servicio no disponible",
    "app.unavailable.detail":
      "Recetas, planificación, compras y cocina guiada aún están en construcción y llegan en próximas versiones.",
    "lang.label": "Idioma",
    "theme.label": "Tema",
    "theme.system": "Sistema",
    "theme.light": "Claro",
    "theme.dark": "Oscuro",
    "install.title": "Instala Cocinaris",
    "install.detail":
      "Ábrelo directo desde la pantalla de inicio del móvil, a pantalla completa, como una app.",
    "install.action": "Instalar",
    "install.later": "Ahora no",
    "install.close": "Cerrar",
    "install.iosBefore": "Toca",
    "install.iosShare": "Compartir",
    "install.iosAfter": "y luego “Añadir a pantalla de inicio”.",
    "nav.home": "Inicio",
    "nav.primary": "Navegación principal",
    "nav.skipToContent": "Saltar al contenido",
    "app.loading": "Cargando tu sesión…",
    "app.unavailable.backHome": "Volver al inicio",
    "page.title.entry": "Cocinaris",
    "page.title.access": "Iniciar sesión",
    "page.title.home": "Inicio",
    "page.title.inventory": "Despensa",
    "page.title.inventoryNew": "Agregar lote",
    "page.title.inventoryDetail": "Lote",
    "page.title.inventoryEdit": "Editar lote",
    "page.title.profile": "Perfil",
    "page.title.unavailable": "Próximamente",
    "page.title.recipes": "Recetas",
    "page.title.recipeGenerate": "Generar receta",
    "page.title.recipeDetail": "Receta",
  },
};

const catalogs: Record<ProductionLocale, Catalog> = {
  "pt-BR": {
    ...sharedShell["pt-BR"],
    ...entryCatalogs["pt-BR"],
    ...homeCatalogs["pt-BR"],
    ...inventoryCatalogs["pt-BR"],
    ...profileCatalogs["pt-BR"],
    ...recipesCatalogs["pt-BR"],
    ...cookingCatalogs["pt-BR"],
    ...libraryCatalogs["pt-BR"],
    ...planningCatalogs["pt-BR"],
  },
  en: {
    ...sharedShell.en,
    ...entryCatalogs.en,
    ...homeCatalogs.en,
    ...inventoryCatalogs.en,
    ...profileCatalogs.en,
    ...recipesCatalogs.en,
    ...cookingCatalogs.en,
    ...libraryCatalogs.en,
    ...planningCatalogs.en,
  },
  es: {
    ...sharedShell.es,
    ...entryCatalogs.es,
    ...homeCatalogs.es,
    ...inventoryCatalogs.es,
    ...profileCatalogs.es,
    ...recipesCatalogs.es,
    ...cookingCatalogs.es,
    ...libraryCatalogs.es,
    ...planningCatalogs.es,
  },
};

export function isProductionLocale(value: string): value is ProductionLocale {
  return (PRODUCTION_LOCALES as readonly string[]).includes(value);
}

/** Resolves initial locale: stored preference → navigator → pt-BR. */
export function resolveProductionLocale(
  stored: string | null | undefined,
  navigatorLanguage?: string | null,
): ProductionLocale {
  if (stored && isProductionLocale(stored)) return stored;
  const nav = String(navigatorLanguage || "").toLowerCase();
  if (nav.startsWith("pt")) return "pt-BR";
  if (nav.startsWith("es")) return "es";
  if (nav.startsWith("en")) return "en";
  return "pt-BR";
}

/**
 * Replaces `{{token}}` placeholders. Values must already be safe to display
 * (never inject untrusted HTML).
 */
export function interpolateProduction(
  template: string,
  vars?: Readonly<Record<string, string | number>>,
): string {
  if (!vars) return template;
  return template.replace(/\{\{(\w+)\}\}/g, (_, name: string) => {
    const value = vars[name];
    return value == null ? "" : String(value);
  });
}

/** Looks up a production catalog key with deterministic fallback to pt-BR then key. */
export function translateProduction(
  locale: ProductionLocale,
  key: string,
  vars?: Readonly<Record<string, string | number>>,
): string {
  const raw = catalogs[locale]?.[key] ?? catalogs["pt-BR"]?.[key] ?? key;
  return interpolateProduction(raw, vars);
}

export { catalogs as productionCatalogs };
