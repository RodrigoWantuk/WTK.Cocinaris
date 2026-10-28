import type { MockInstance } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ProductionApp, { pageTitleKeyFor } from "./ProductionApp";
import { PRODUCTION_LOCALE_STORAGE_KEY } from "./i18n/productionCatalog";

const SESSION = {
  userId: "11111111-1111-1111-1111-111111111111",
  csrfToken: "csrf-shell",
  supportedLocales: ["en", "pt-BR", "es"],
  displayName: "Ada",
  language: "en",
  timeZone: "UTC",
  measurementSystem: "Metric",
  profileExists: true,
  profilePercentComplete: 50,
  adultDeclarationState: "Declared",
};

function json(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "content-type": "application/json" },
  });
}

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === "string") return input;
  if (input instanceof Request) return input.url;
  return String(input);
}

describe("Production shell and navigation (PLAN-0035)", () => {
  let fetchSpy: MockInstance;

  beforeEach(() => {
    localStorage.setItem(PRODUCTION_LOCALE_STORAGE_KEY, "en");
    fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async (input) => {
        const url = requestUrl(input);
        if (url.includes("/api/v1/session")) return json(SESSION);
        if (url.includes("/api/v1/inventory/lots")) {
          return json({ items: [], nextCursor: null });
        }
        return new Response(null, { status: 404 });
      });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.removeItem(PRODUCTION_LOCALE_STORAGE_KEY);
  });

  const sessionFetches = () =>
    fetchSpy.mock.calls.filter(([input]) =>
      requestUrl(input).includes("/api/v1/session"),
    ).length;

  it("keeps one session and the shell mounted while moving between destinations", async () => {
    window.history.pushState({}, "", "/app/hoje");
    render(<ProductionApp />);
    const header = await screen.findByTestId("production-primary-nav");
    await waitFor(() => expect(sessionFetches()).toBe(1));

    await userEvent.click(screen.getByTestId("production-nav-despensa"));
    expect(
      await screen.findByTestId("production-inventory-list"),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByTestId("production-nav-home"));
    await screen.findByTestId("home-sources");

    // Same header element: the layout did not unmount, and no session refetch happened.
    expect(screen.getByTestId("production-primary-nav")).toBe(header);
    expect(sessionFetches()).toBe(1);
    expect(screen.queryByTestId("production-session-loading")).toBeNull();
  });

  it("titles each page and updates the title on navigation", async () => {
    window.history.pushState({}, "", "/app/hoje");
    render(<ProductionApp />);
    await waitFor(() => expect(document.title).toBe("Home · Cocinaris"));
    await userEvent.click(await screen.findByTestId("production-nav-despensa"));
    await waitFor(() => expect(document.title).toBe("Pantry · Cocinaris"));
  });

  it("offers a skip link that targets the main landmark", async () => {
    window.history.pushState({}, "", "/app/despensa");
    render(<ProductionApp />);
    const skip = await screen.findByTestId("production-skip-link");
    expect(skip).toHaveAttribute("href", "#main-content");
    expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
  });

  it("gives not-yet-available areas a heading and a way back home", async () => {
    window.history.pushState({}, "", "/app/compras");
    render(<ProductionApp />);
    expect(
      await screen.findByRole("heading", { level: 1, name: "Coming soon" }),
    ).toBeInTheDocument();
    expect(
      screen.getByTestId("production-unavailable-home-link"),
    ).toHaveAttribute("href", "/app/hoje");
    expect(screen.getByTestId("feature-unavailable-app")).toBeInTheDocument();
  });

  it("sends a visitor who already has a session from the entry page to the home", async () => {
    window.history.pushState({}, "", "/");
    render(<ProductionApp />);
    await screen.findByTestId("production-primary-nav");
    await waitFor(() => expect(window.location.pathname).toBe("/app/hoje"));
  });

  it("keeps signed-out visitors on the public entry page", async () => {
    fetchSpy.mockImplementation(
      async () => new Response(null, { status: 401 }),
    );
    window.history.pushState({}, "", "/");
    render(<ProductionApp />);
    await waitFor(() => expect(sessionFetches()).toBe(1));
    expect(window.location.pathname).toBe("/");
    expect(screen.queryByTestId("production-primary-nav")).toBeNull();
  });

  it.each([
    ["/", "page.title.entry"],
    ["/acesso", "page.title.access"],
    ["/app/hoje", "page.title.home"],
    ["/app/despensa", "page.title.inventory"],
    ["/app/despensa/novo", "page.title.inventoryNew"],
    ["/app/despensa/abc", "page.title.inventoryDetail"],
    ["/app/despensa/abc/editar", "page.title.inventoryEdit"],
    ["/app/perfil/preferencias", "page.title.profile"],
    ["/app/receitas", "page.title.recipes"],
    ["/app/receitas/gerar", "page.title.recipeGenerate"],
    ["/app/receitas/abc", "page.title.recipeDetail"],
    ["/app/compras", "page.title.unavailable"],
  ])("maps %s to %s", (pathname, key) => {
    expect(pageTitleKeyFor(pathname)).toBe(key);
  });
});
