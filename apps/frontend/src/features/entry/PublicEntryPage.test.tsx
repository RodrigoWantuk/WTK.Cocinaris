import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { RuntimeProvider } from "@/app/runtime/RuntimeProvider";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { createUnavailableContextualHomeAdapter } from "@/adapters/live/unavailableContextualHomeAdapter";
import { UnavailablePreparationRouteRepository } from "@/adapters/live/unavailablePreparationRouteRepository";
import { createUnavailableAdultDeclarationPolicy } from "@/features/profile/adultDeclarationPolicy";
import { PublicEntryPage } from "./PublicEntryPage";
import type { FrontendRuntime } from "@/app/runtime/types";
import type { HomeTelemetryEvent } from "@/contracts/contextualHome";
import { PRODUCTION_LOCALE_STORAGE_KEY } from "@/app/i18n/productionCatalog";

function renderEntry() {
  const events: HomeTelemetryEvent[] = [];
  const runtime: FrontendRuntime = {
    mode: "test",
    sessionAdapter: {
      getSession: async () => {
        throw new Error("public entry must not call getSession");
      },
      beginLogin: () => {
        throw new Error("public entry must not begin login");
      },
      logout: async () => undefined,
    },
    inventoryRepository: {
      listLots: async () => ({ items: [], nextCursor: null }),
    } as unknown as FrontendRuntime["inventoryRepository"],
    recipeRepository: {
      listRecipes: async () => [],
    } as unknown as FrontendRuntime["recipeRepository"],
    preparationRouteRepository: new UnavailablePreparationRouteRepository(),
    contextualHomeAdapter: createUnavailableContextualHomeAdapter(),
    profileRepository: {
      getProfile: async () => {
        throw new Error("public entry must not call profile endpoints");
      },
    } as unknown as FrontendRuntime["profileRepository"],
    adultDeclarationPolicy: createUnavailableAdultDeclarationPolicy(),
    enableScenarioBar: false,
    enablePrototypeFixtures: false,
    persistPrototypeAuth: false,
    shoppingRequirementProjections: [],
  };

  render(
    <RuntimeProvider runtime={runtime}>
      <ProductionI18nProvider initialLocale="en">
        <MemoryRouter>
          <PublicEntryPage
            telemetry={{
              track: (event) => events.push(event),
            }}
          />
        </MemoryRouter>
      </ProductionI18nProvider>
    </RuntimeProvider>,
  );
  return { events };
}

describe("PublicEntryPage", () => {
  beforeEach(() => {
    localStorage.removeItem(PRODUCTION_LOCALE_STORAGE_KEY);
  });

  it("explains the product without SessionProvider or authenticated API calls", async () => {
    const fetchSpy = jest.spyOn(globalThis, "fetch");
    const { events } = renderEntry();
    expect(
      await screen.findByTestId("production-landing-title"),
    ).toHaveTextContent(/Turn available food/);
    expect(screen.getByTestId("production-landing-subtitle")).toHaveTextContent(
      /not just a recipe generator/i,
    );
    expect(screen.getByTestId("entry-media-placeholder")).toBeInTheDocument();
    expect(screen.getByTestId("entry-legal-terms").tagName.toLowerCase()).toBe(
      "a",
    );
    expect(
      screen.getByTestId("entry-legal-privacy").tagName.toLowerCase(),
    ).toBe("a");
    expect(screen.getByTestId("hero-enter")).toHaveAttribute("href", "/acesso");
    expect(events.some((e) => e.name === "public_entry_viewed")).toBe(true);
    expect(fetchSpy).not.toHaveBeenCalled();
    fetchSpy.mockRestore();
  });

  it("tracks login CTA without nested interactive controls", async () => {
    const user = userEvent.setup();
    const { events } = renderEntry();
    expect(document.querySelectorAll("a button").length).toBe(0);
    await user.click(screen.getByTestId("hero-enter"));
    expect(events.some((e) => e.name === "login_cta_selected")).toBe(true);
  });

  it("uses smooth scrolling by default and auto when reduced motion is preferred", async () => {
    const user = userEvent.setup();
    const scrollSpy = jest.fn();
    Element.prototype.scrollIntoView = scrollSpy;
    const originalMatchMedia = window.matchMedia;

    window.matchMedia = jest.fn().mockImplementation(() => ({
      matches: false,
      media: "(prefers-reduced-motion: reduce)",
      onchange: null,
      addListener: jest.fn(),
      removeListener: jest.fn(),
      addEventListener: jest.fn(),
      removeEventListener: jest.fn(),
      dispatchEvent: jest.fn(),
    })) as unknown as typeof window.matchMedia;

    renderEntry();
    await user.click(screen.getByTestId("entry-cta-demo"));
    expect(scrollSpy).toHaveBeenCalledWith({ behavior: "smooth" });

    scrollSpy.mockClear();
    window.matchMedia = jest.fn().mockImplementation((query: string) => ({
      matches: query.includes("prefers-reduced-motion: reduce"),
      media: query,
      onchange: null,
      addListener: jest.fn(),
      removeListener: jest.fn(),
      addEventListener: jest.fn(),
      removeEventListener: jest.fn(),
      dispatchEvent: jest.fn(),
    })) as unknown as typeof window.matchMedia;
    await user.click(screen.getByTestId("entry-cta-demo"));
    expect(scrollSpy).toHaveBeenCalledWith({ behavior: "auto" });
    window.matchMedia = originalMatchMedia;
  });

  it("falls back to auto scrolling when matchMedia is unavailable", async () => {
    const user = userEvent.setup();
    const scrollSpy = jest.fn();
    Element.prototype.scrollIntoView = scrollSpy;
    const original = window.matchMedia;
    // @ts-expect-error intentional unsupported environment
    window.matchMedia = undefined;
    renderEntry();
    await user.click(screen.getByTestId("entry-cta-demo"));
    expect(scrollSpy).toHaveBeenCalledWith({ behavior: "auto" });
    window.matchMedia = original;
  });

  it("falls back to auto scrolling when matchMedia throws", async () => {
    const user = userEvent.setup();
    const scrollSpy = jest.fn();
    Element.prototype.scrollIntoView = scrollSpy;
    const original = window.matchMedia;
    window.matchMedia = jest.fn(() => {
      throw new Error("matchMedia unavailable");
    }) as unknown as typeof window.matchMedia;
    renderEntry();
    await user.click(screen.getByTestId("entry-cta-demo"));
    expect(scrollSpy).toHaveBeenCalledWith({ behavior: "auto" });
    window.matchMedia = original;
  });
});
