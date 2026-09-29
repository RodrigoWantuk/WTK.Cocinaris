/**
 * Production-safe translation catalog.
 * Independent of prototype StoreProvider / cocinaris_state_v1.
 */

import { inventoryCatalogs } from "./inventoryCatalog";
import { entryCatalogs } from "./entryCatalog";
import { homeCatalogs } from "./homeCatalog";
import { profileCatalogs } from "./profileUiCatalog";
import { recipesCatalogs } from "./recipesCatalog";

export const PRODUCTION_LOCALES = ["pt-BR", "en", "es"] as const;
export type ProductionLocale = (typeof PRODUCTION_LOCALES)[number];

export const PRODUCTION_LOCALE_STORAGE_KEY = "kitchenflow_production_locale";

type Catalog = Record<string, string>;

const sharedShell: Record<ProductionLocale, Catalog> = {
  "pt-BR": {
    "brand.name": "Cocinaris",
    "landing.enter": "Entrar",
    "landing.tagline":
      "O KitchenFlow ajuda a transformar alimentos disponíveis em refeições úteis.",
    "landing.subtitle":
      "As superfícies autenticadas do produto exigem sessão e adapters gerenciados pelo backend.",
    "access.title.unavailable": "Serviço indisponível",
    "access.title.pending": "Integração pendente",
    "access.detail":
      "Builds de produção não aceitam credenciais locais. É necessário login gerenciado pelo backend.",
    "access.loginTitle": "Entrar",
    "access.loginDetail":
      "O login usa o desafio gerenciado pelo backend no navegador. Credenciais do provedor de identidade não são coletadas nesta página.",
    "access.loginAction": "Continuar para o login",
    "access.expired": "Sua sessão expirou. Entre novamente para continuar.",
    "access.signedIn": "Sessão ativa",
    "access.logout": "Sair",
    "feature.unavailable": "Funcionalidade indisponível",
    "feature.integrationPending": "Integração pendente",
    "feature.serviceUnavailable": "Serviço indisponível",
    "app.unavailable.detail":
      "Esta área ainda não está conectada. A produção não faz fallback para dados mock de receitas, planejamento, compras ou cozinha.",
    "lang.label": "Idioma",
    "nav.home": "Início",
    "nav.primary": "Navegação principal",
  },
  en: {
    "brand.name": "Cocinaris",
    "landing.enter": "Enter",
    "landing.tagline":
      "KitchenFlow helps transform available food into useful meals.",
    "landing.subtitle":
      "Authenticated product surfaces require backend-managed session and adapters.",
    "access.title.unavailable": "Service unavailable",
    "access.title.pending": "Integration pending",
    "access.detail":
      "Production builds do not accept local credentials. Backend-managed login is required.",
    "access.loginTitle": "Sign in",
    "access.loginDetail":
      "Sign-in uses the backend-managed browser challenge. Identity-provider credentials are not collected on this page.",
    "access.loginAction": "Continue to sign in",
    "access.expired": "Your session expired. Sign in again to continue.",
    "access.signedIn": "Signed in",
    "access.logout": "Sign out",
    "feature.unavailable": "Feature unavailable",
    "feature.integrationPending": "Integration pending",
    "feature.serviceUnavailable": "Service unavailable",
    "app.unavailable.detail":
      "This area is not wired yet. Production does not fall back to mock recipes, planning, shopping, or cook data.",
    "lang.label": "Language",
    "nav.home": "Home",
    "nav.primary": "Primary navigation",
  },
  es: {
    "brand.name": "Cocinaris",
    "landing.enter": "Entrar",
    "landing.tagline":
      "KitchenFlow ayuda a transformar alimentos disponibles en comidas útiles.",
    "landing.subtitle":
      "Las superficies autenticadas del producto requieren sesión y adapters gestionados por el backend.",
    "access.title.unavailable": "Servicio no disponible",
    "access.title.pending": "Integración pendiente",
    "access.detail":
      "Las builds de producción no aceptan credenciales locales. Se requiere inicio de sesión gestionado por el backend.",
    "access.loginTitle": "Iniciar sesión",
    "access.loginDetail":
      "El inicio de sesión usa el desafío gestionado por el backend en el navegador. Las credenciales del proveedor de identidad no se recopilan en esta página.",
    "access.loginAction": "Continuar al inicio de sesión",
    "access.expired":
      "Tu sesión expiró. Inicia sesión de nuevo para continuar.",
    "access.signedIn": "Sesión activa",
    "access.logout": "Salir",
    "feature.unavailable": "Función no disponible",
    "feature.integrationPending": "Integración pendiente",
    "feature.serviceUnavailable": "Servicio no disponible",
    "app.unavailable.detail":
      "Esta área aún no está conectada. La producción no usa datos mock de recetas, planificación, compras o cocina.",
    "lang.label": "Idioma",
    "nav.home": "Inicio",
    "nav.primary": "Navegación principal",
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
  },
  en: {
    ...sharedShell.en,
    ...entryCatalogs.en,
    ...homeCatalogs.en,
    ...inventoryCatalogs.en,
    ...profileCatalogs.en,
    ...recipesCatalogs.en,
  },
  es: {
    ...sharedShell.es,
    ...entryCatalogs.es,
    ...homeCatalogs.es,
    ...inventoryCatalogs.es,
    ...profileCatalogs.es,
    ...recipesCatalogs.es,
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
