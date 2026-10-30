import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { AccountMenu } from "@/app/shell/AccountMenu";
import { applyTheme, readThemePreference } from "./theme";

function mockSystemDark(dark: boolean) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: dark && query.includes("dark"),
    media: query,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })) as unknown as typeof window.matchMedia;
}

describe("theme (PLAN-0057)", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove("dark");
  });

  it("follows the system until the person chooses, and keeps the choice per device", async () => {
    mockSystemDark(true);
    expect(readThemePreference()).toBe("system");
    applyTheme(readThemePreference());
    expect(document.documentElement).toHaveClass("dark");

    const user = userEvent.setup();
    render(
      <ProductionI18nProvider initialLocale="en">
        <MemoryRouter>
          <AccountMenu displayName={null} onLogout={() => undefined} />
        </MemoryRouter>
      </ProductionI18nProvider>,
    );
    // Without a name the avatar is an icon, not "?".
    const avatar = screen.getByTestId("production-account-menu");
    expect(avatar).not.toHaveTextContent("?");
    await user.click(avatar);
    await user.click(screen.getByRole("button", { name: "Light" }));
    expect(document.documentElement).not.toHaveClass("dark");
    expect(localStorage.getItem("cocinaris.theme")).toBe("light");
    expect(screen.getByRole("button", { name: "Light" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );

    await user.click(screen.getByRole("button", { name: "System" }));
    expect(localStorage.getItem("cocinaris.theme")).toBeNull();
    expect(document.documentElement).toHaveClass("dark");
  });
});
