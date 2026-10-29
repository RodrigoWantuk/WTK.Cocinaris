import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import ProductionApp from "./ProductionApp";
import { PRODUCTION_LOCALE_STORAGE_KEY } from "./i18n/productionCatalog";

function absentField(defaultValue: unknown = null, presence = "absent") {
  return { value: null, presence, defaultValue, durability: "durable" };
}

const ABSENT_PROFILE_BODY = {
  ownerUserId: "11111111-1111-1111-1111-111111111111",
  displayName: absentField(),
  household: {
    defaultAdultCount: absentField(1, "default"),
    defaultChildCount: absentField(0, "default"),
    defaultServingCount: absentField(1, "default"),
    language: absentField("en", "default"),
    region: absentField(),
    currency: absentField(),
    measurementSystem: absentField("Metric", "default"),
    timeZone: absentField(),
    planningCadence: absentField(),
    shoppingCadence: absentField(),
  },
  cookingContext: {
    overallSkill: absentField(),
    confidence: absentField(),
    preferredInstructionDetail: absentField(),
    ordinaryPrepMinutes: absentField(),
    exceptionalPrepMinutes: absentField(),
    effortTolerance: absentField(),
    cleanupTolerance: absentField(),
    repeatMealPreference: absentField(),
    reheatingPreference: absentField(),
    leftoverPreference: absentField(),
    freezingPreference: absentField(),
  },
  adultDeclaration: {
    adultDeclared: null,
    termsVersion: null,
    privacyVersion: null,
    acceptedAt: null,
    state: "NotDeclared",
  },
  knownTechniques: [],
  techniquesToLearn: [],
  goals: [],
  abandonmentReasons: [],
  profileExists: false,
  version: null,
  createdAt: null,
  updatedAt: null,
};

function mockProductionFetch() {
  return vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url =
      typeof input === "string"
        ? input
        : input instanceof Request
          ? input.url
          : String(input);
    if (url.includes("/api/v1/session")) {
      return new Response(
        JSON.stringify({
          userId: "11111111-1111-1111-1111-111111111111",
          csrfToken: "csrf-route",
          supportedLocales: ["en", "pt-BR", "es"],
          displayName: "Ada",
          language: "en",
          timeZone: "UTC",
          measurementSystem: "Metric",
          profileExists: false,
          profilePercentComplete: 0,
          adultDeclarationState: "NotDeclared",
        }),
        { status: 200, headers: { "content-type": "application/json" } },
      );
    }
    if (url.includes("/api/v1/profile/preferences")) {
      return new Response(JSON.stringify({ version: null, entries: [] }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    }
    if (url.includes("/api/v1/profile/equipment")) {
      return new Response(JSON.stringify({ version: null, entries: [] }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    }
    if (url.includes("/api/v1/profile/completeness")) {
      return new Response(
        JSON.stringify({
          percentComplete: 0,
          completedSections: 0,
          totalSections: 5,
          sectionCounts: {},
          adultDeclarationState: "NotDeclared",
          profileExists: false,
        }),
        { status: 200, headers: { "content-type": "application/json" } },
      );
    }
    if (url.includes("/api/v1/profile")) {
      return new Response(JSON.stringify(ABSENT_PROFILE_BODY), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    }
    return new Response(null, { status: 404 });
  });
}

describe("Production profile routes", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the profile overview instead of FeatureUnavailable and exposes primary nav links", async () => {
    localStorage.removeItem(PRODUCTION_LOCALE_STORAGE_KEY);
    window.history.pushState({}, "", "/app/perfil");
    mockProductionFetch();

    render(<ProductionApp />);

    expect(await screen.findByTestId("profile-overview")).toBeInTheDocument();
    expect(
      screen.queryByTestId("feature-unavailable-app"),
    ).not.toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.getByTestId("profile-overview-not-started"),
      ).toBeInTheDocument(),
    );

    expect(screen.getByTestId("production-nav-home")).toHaveAttribute(
      "href",
      "/app/hoje",
    );
    expect(screen.getByTestId("production-nav-despensa")).toHaveAttribute(
      "href",
      "/app/despensa",
    );
    // Profile lives in the account menu (PLAN-0054 Phase 1).
    fireEvent.click(screen.getByTestId("production-account-menu"));
    expect(screen.getByTestId("production-nav-perfil")).toHaveAttribute(
      "href",
      "/app/perfil",
    );
    expect(screen.getByTestId("production-account-menu")).toHaveAttribute(
      "aria-expanded",
      "true",
    );
  });

  it("renders the data, preferences, and equipment sub-routes", async () => {
    localStorage.removeItem(PRODUCTION_LOCALE_STORAGE_KEY);
    mockProductionFetch();

    window.history.pushState({}, "", "/app/perfil/dados");
    const { unmount: unmountData } = render(<ProductionApp />);
    expect(await screen.findByTestId("profile-data")).toBeInTheDocument();
    unmountData();

    window.history.pushState({}, "", "/app/perfil/preferencias");
    const { unmount: unmountPreferences } = render(<ProductionApp />);
    expect(
      await screen.findByTestId("profile-preferences"),
    ).toBeInTheDocument();
    unmountPreferences();

    window.history.pushState({}, "", "/app/perfil/equipamentos");
    render(<ProductionApp />);
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();
  });
});
