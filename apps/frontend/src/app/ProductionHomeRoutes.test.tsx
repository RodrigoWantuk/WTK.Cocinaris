import { render, screen, waitFor } from "@testing-library/react";
import ProductionApp from "./ProductionApp";
import { PRODUCTION_LOCALE_STORAGE_KEY } from "./i18n/productionCatalog";

describe("Production home route profile isolation", () => {
  beforeEach(() => {
    localStorage.removeItem(PRODUCTION_LOCALE_STORAGE_KEY);
    window.history.pushState({}, "", "/app/hoje");
    vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
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
            profileExists: true,
            profilePercentComplete: 50,
            adultDeclarationState: "Declared",
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        );
      }
      return new Response(null, { status: 404 });
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders /app/hoje without ever calling the profile endpoints", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    render(<ProductionApp />);
    await waitFor(() =>
      expect(screen.getByTestId("contextual-home")).toBeInTheDocument(),
    );
    const profileCalls = fetchSpy.mock.calls.filter(([input]) =>
      String(input).includes("/api/v1/profile"),
    );
    expect(profileCalls).toHaveLength(0);
  });
});
