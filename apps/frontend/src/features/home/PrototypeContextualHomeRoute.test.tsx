import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { RuntimeProvider } from "@/app/runtime/RuntimeProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { createMockSessionAdapter } from "@/app/session/mockSessionAdapter";
import { createMockContextualHomeAdapter } from "@/adapters/mock/contextual-home/mockContextualHomeAdapter";
import { PrototypeContextualHomeRoute } from "./PrototypeContextualHomeRoute";
import type { FrontendRuntime } from "@/app/runtime/types";
import { UnavailablePreparationRouteRepository } from "@/adapters/live/unavailablePreparationRouteRepository";
import { createUnavailableAdultDeclarationPolicy } from "@/features/profile/adultDeclarationPolicy";

function renderPrototypeHome() {
  const sessionAdapter = createMockSessionAdapter({
    initiallyAuthenticated: true,
    displayName: "Ana",
    timeZone: "UTC",
  });
  sessionAdapter.beginLogin();
  const runtime: FrontendRuntime = {
    mode: "prototype",
    sessionAdapter,
    inventoryRepository: {
      listLots: async () => ({ items: [], nextCursor: null }),
    } as unknown as FrontendRuntime["inventoryRepository"],
    recipeRepository: {
      listRecipes: async () => [],
    } as unknown as FrontendRuntime["recipeRepository"],
    preparationRouteRepository: new UnavailablePreparationRouteRepository(),
    contextualHomeAdapter: createMockContextualHomeAdapter({
      scenario: "default",
    }),
    profileRepository: {} as unknown as FrontendRuntime["profileRepository"],
    adultDeclarationPolicy: createUnavailableAdultDeclarationPolicy(),
    enableScenarioBar: false,
    enablePrototypeFixtures: true,
    persistPrototypeAuth: false,
    shoppingRequirementProjections: [],
  };

  return render(
    <RuntimeProvider runtime={runtime}>
      <SessionProvider adapter={sessionAdapter}>
        <ProductionI18nProvider initialLocale="en">
          <MemoryRouter>
            <PrototypeContextualHomeRoute />
          </MemoryRouter>
        </ProductionI18nProvider>
      </SessionProvider>
    </RuntimeProvider>,
  );
}

describe("PrototypeContextualHomeRoute", () => {
  it("switches scenarios deterministically without effect-order races", async () => {
    const user = userEvent.setup();
    renderPrototypeHome();
    expect(await screen.findByTestId("home-source-menu")).toHaveAttribute(
      "data-status",
      "ready",
    );

    await user.selectOptions(
      screen.getByTestId("home-scenario-select"),
      "noMenu",
    );
    await waitFor(() =>
      expect(screen.queryByTestId("home-source-menu")).not.toBeInTheDocument(),
    );
    expect(screen.getByTestId("home-source-inventory")).toHaveAttribute(
      "data-status",
      "ready",
    );

    await user.selectOptions(
      screen.getByTestId("home-scenario-select"),
      "inventoryFailed",
    );
    await waitFor(() =>
      expect(screen.getByTestId("home-source-inventory")).toHaveAttribute(
        "data-status",
        "failed",
      ),
    );

    await user.selectOptions(
      screen.getByTestId("home-scenario-select"),
      "allSourcesReady",
    );
    await waitFor(() => {
      expect(screen.getByTestId("home-source-menu")).toHaveAttribute(
        "data-status",
        "ready",
      );
      expect(screen.getByTestId("home-source-inventory")).toHaveAttribute(
        "data-status",
        "ready",
      );
      expect(screen.getByTestId("home-source-profile")).toHaveAttribute(
        "data-status",
        "ready",
      );
    });
  });
});
