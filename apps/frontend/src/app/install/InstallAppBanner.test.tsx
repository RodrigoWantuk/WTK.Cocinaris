import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { InstallAppBanner } from "./InstallAppBanner";
import {
  isIosSafari,
  resetInstallPromptForTests,
  startInstallPromptCapture,
} from "./installPrompt";

function media(matches: Record<string, boolean>) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: matches[query] ?? false,
    media: query,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })) as unknown as typeof window.matchMedia;
}

function renderBanner() {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <InstallAppBanner />
    </ProductionI18nProvider>,
  );
}

describe("install the app (PLAN-0059)", () => {
  beforeEach(() => {
    localStorage.clear();
    resetInstallPromptForTests();
    startInstallPromptCapture();
  });

  it("offers the browser's install dialog on a phone and hides after a choice", async () => {
    media({ "(pointer: coarse)": true });
    const prompt = vi.fn(async () => undefined);
    renderBanner();
    expect(screen.queryByTestId("install-app-banner")).toBeNull();

    const offer = Object.assign(
      new Event("beforeinstallprompt", { cancelable: true }),
      {
        prompt,
        userChoice: Promise.resolve({ outcome: "accepted" as const }),
      },
    );
    act(() => {
      window.dispatchEvent(offer);
    });
    expect(offer.defaultPrevented).toBe(true);
    await userEvent
      .setup()
      .click(await screen.findByTestId("install-app-action"));
    expect(prompt).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId("install-app-banner")).toBeNull();
  });

  it("remembers 'not now' on this device and stays away on desktops and when installed", async () => {
    media({ "(pointer: coarse)": true });
    renderBanner();
    act(() => {
      window.dispatchEvent(
        Object.assign(new Event("beforeinstallprompt", { cancelable: true }), {
          prompt: vi.fn(),
          userChoice: Promise.resolve({ outcome: "dismissed" as const }),
        }),
      );
    });
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "Not now" }));
    expect(screen.queryByTestId("install-app-banner")).toBeNull();
    expect(
      Number(localStorage.getItem("cocinaris.install.dismissedAt")),
    ).toBeGreaterThan(0);

    localStorage.clear();
    media({ "(pointer: coarse)": true, "(display-mode: standalone)": true });
    const installed = renderBanner();
    expect(installed.container).toBeEmptyDOMElement();
  });

  it("recognizes Safari on iPhone and leaves other iOS browsers alone", () => {
    const iphone =
      "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";
    expect(isIosSafari(iphone)).toBe(true);
    expect(isIosSafari(iphone.replace("Version/18.0", "CriOS/130.0"))).toBe(
      false,
    );
    expect(
      isIosSafari(
        "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36",
      ),
    ).toBe(false);
  });
});
