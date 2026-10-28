import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ProductionApp from "./ProductionApp";
import { PRODUCTION_LOCALE_STORAGE_KEY } from "./i18n/productionCatalog";

describe("ProductionApp landing controls", () => {
  beforeEach(() => {
    localStorage.removeItem(PRODUCTION_LOCALE_STORAGE_KEY);
    window.history.pushState({}, "", "/");
    jest.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(
        JSON.stringify({
          errorCode: "authentication_required",
          detail: "Authentication is required.",
        }),
        {
          status: 401,
          headers: { "content-type": "application/problem+json" },
        },
      ),
    );
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it("renders public `/` without calling session or fetch", async () => {
    const fetchSpy = jest.spyOn(globalThis, "fetch");
    render(<ProductionApp />);
    expect(await screen.findByTestId("production-landing")).toBeInTheDocument();
    expect(fetchSpy).not.toHaveBeenCalled();
    expect(
      fetchSpy.mock.calls.some(([input]) =>
        String(input).includes("/api/v1/session"),
      ),
    ).toBe(false);
  });

  it("uses asChild link CTA without nested button inside anchor", async () => {
    const user = userEvent.setup();
    render(<ProductionApp />);

    expect(document.querySelectorAll("a button").length).toBe(0);

    const cta = screen.getByTestId("landing-enter");
    expect(cta.tagName.toLowerCase()).toBe("a");
    expect(cta).toHaveAttribute("href", "/acesso");

    cta.focus();
    await user.keyboard("{Enter}");
    expect(await screen.findByTestId("production-access")).toBeInTheDocument();
    expect(
      screen.getByText(
        /login gerenciado|Backend-managed|sesión del navegador/i,
      ),
    ).toBeInTheDocument();
  });

  it("exposes a compact mobile locale select that persists production locale only", async () => {
    const user = userEvent.setup();
    render(<ProductionApp />);

    const select = screen.getByTestId("production-lang-select");
    expect(select).toHaveAccessibleName(/Idioma|Language/i);

    await user.selectOptions(select, "en");
    expect(localStorage.getItem(PRODUCTION_LOCALE_STORAGE_KEY)).toBe("en");
    expect(document.documentElement.lang).toBe("en");
    expect(screen.getByTestId("production-landing-tagline")).toHaveTextContent(
      /Cocinaris helps you decide|Turn available food|Cocinaris helps transform/,
    );

    await user.selectOptions(select, "es");
    expect(localStorage.getItem(PRODUCTION_LOCALE_STORAGE_KEY)).toBe("es");
    expect(document.documentElement.lang).toBe("es");

    expect(localStorage.getItem("cocinaris_state_v1")).toBeNull();

    const header = screen
      .getByTestId("production-landing")
      .querySelector("header");
    expect(header).toBeTruthy();
    expect(
      within(header as HTMLElement).getByTestId("production-lang-select"),
    ).toBeInTheDocument();
  });
});
