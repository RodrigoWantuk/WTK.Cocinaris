import { render, screen } from "@testing-library/react";
import {
  PRODUCTION_LOCALE_STORAGE_KEY,
  resolveProductionLocale,
  translateProduction,
} from "./productionCatalog";
import {
  ProductionI18nProvider,
  useProductionI18n,
} from "./ProductionI18nProvider";

function Probe() {
  const { t, locale } = useProductionI18n();
  return (
    <div>
      <span data-testid="locale">{locale}</span>
      <span data-testid="enter">{t("landing.enter")}</span>
      <span data-testid="tagline">{t("landing.tagline")}</span>
      <span data-testid="pending">{t("feature.integrationPending")}</span>
      <span data-testid="unavailable">{t("feature.unavailable")}</span>
      <span data-testid="service">{t("feature.serviceUnavailable")}</span>
      <span data-testid="login">{t("access.detail")}</span>
      <span data-testid="live">{t("app.unavailable.detail")}</span>
    </div>
  );
}

describe("production i18n", () => {
  beforeEach(() => {
    localStorage.removeItem(PRODUCTION_LOCALE_STORAGE_KEY);
  });

  it("defaults to pt-BR when no preference is stored", () => {
    expect(resolveProductionLocale(null, "fr-FR")).toBe("pt-BR");
    expect(translateProduction("pt-BR", "landing.enter")).toBe("Entrar");
  });

  it("translates required production strings in pt-BR, en, and es", () => {
    expect(translateProduction("pt-BR", "feature.integrationPending")).toBe(
      "Integração pendente",
    );
    expect(translateProduction("en", "landing.enter")).toBe("Enter");
    expect(translateProduction("en", "feature.serviceUnavailable")).toBe(
      "Service unavailable",
    );
    expect(translateProduction("en", "feature.unavailable")).toBe(
      "Feature unavailable",
    );
    expect(translateProduction("en", "access.detail")).toMatch(
      /could not check your session/i,
    );
    expect(translateProduction("en", "app.unavailable.detail")).toMatch(
      /still being built/i,
    );
    expect(translateProduction("en", "inventory.title")).toBe("Pantry");
    expect(translateProduction("pt-BR", "inventory.title")).toBe("Despensa");
    expect(translateProduction("es", "inventory.title")).toBe("Despensa");
    expect(translateProduction("es", "feature.serviceUnavailable")).toBe(
      "Servicio no disponible",
    );
  });

  it("sets document lang and renders locale-specific copy", () => {
    const { unmount } = render(
      <ProductionI18nProvider initialLocale="en">
        <Probe />
      </ProductionI18nProvider>,
    );
    expect(screen.getByTestId("locale")).toHaveTextContent("en");
    expect(screen.getByTestId("enter")).toHaveTextContent("Enter");
    expect(document.documentElement.lang).toBe("en");
    unmount();

    render(
      <ProductionI18nProvider initialLocale="pt-BR">
        <Probe />
      </ProductionI18nProvider>,
    );
    expect(screen.getByTestId("pending")).toHaveTextContent(
      "Integração pendente",
    );
    expect(document.documentElement.lang).toBe("pt-BR");
  });
});
