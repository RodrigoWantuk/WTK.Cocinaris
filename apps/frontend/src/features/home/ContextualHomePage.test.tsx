import { render, screen, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { RuntimeProvider } from "@/app/runtime/RuntimeProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { createMockSessionAdapter } from "@/app/session/mockSessionAdapter";
import {
  createMockContextualHomeAdapter,
  type MockHomeScenarioId,
} from "@/adapters/mock/contextual-home/mockContextualHomeAdapter";
import { createUnavailableContextualHomeAdapter } from "@/adapters/live/unavailableContextualHomeAdapter";
import { ContextualHomeProvider } from "./ContextualHomeProvider";
import { ContextualHomePage } from "./ContextualHomePage";
import type { FrontendRuntime } from "@/app/runtime/types";
import type {
  ContextualHomeAdapter,
  HomeQuickChooserDefinition,
  HomeSourceResult,
} from "@/contracts/contextualHome";
import { homeCatalogText } from "@/contracts/contextualHome";
import { UnavailablePreparationRouteRepository } from "@/adapters/live/unavailablePreparationRouteRepository";
import { createUnavailableAdultDeclarationPolicy } from "@/features/profile/adultDeclarationPolicy";

function renderHome(options?: {
  displayName?: string | null;
  timeZone?: string | null;
  scenario?: { scenario: MockHomeScenarioId; menuFailTimes?: number };
  unavailable?: boolean;
  browserTimeZone?: string | null;
  now?: Date;
  adapter?: ContextualHomeAdapter;
}) {
  const homeAdapter =
    options?.adapter ??
    (options?.unavailable
      ? createUnavailableContextualHomeAdapter()
      : createMockContextualHomeAdapter(options?.scenario));
  const sessionAdapter = createMockSessionAdapter({
    initiallyAuthenticated: true,
    displayName: options?.displayName ?? null,
    timeZone: options?.timeZone ?? null,
  });
  sessionAdapter.beginLogin();

  const runtime: FrontendRuntime = {
    mode: "test",
    sessionAdapter,
    inventoryRepository: {
      listLots: async () => ({ items: [], nextCursor: null }),
    } as unknown as FrontendRuntime["inventoryRepository"],
    recipeRepository: {
      listRecipes: async () => [],
    } as unknown as FrontendRuntime["recipeRepository"],
    preparationRouteRepository: new UnavailablePreparationRouteRepository(),
    contextualHomeAdapter: homeAdapter,
    profileRepository: {} as unknown as FrontendRuntime["profileRepository"],
    adultDeclarationPolicy: createUnavailableAdultDeclarationPolicy(),
    enableScenarioBar: false,
    enablePrototypeFixtures: !options?.unavailable,
    persistPrototypeAuth: false,
    shoppingRequirementProjections: [],
  };

  return render(
    <RuntimeProvider runtime={runtime}>
      <SessionProvider adapter={sessionAdapter}>
        <ProductionI18nProvider initialLocale="en">
          <MemoryRouter>
            <ContextualHomeProvider adapter={homeAdapter}>
              <ContextualHomePage
                now={options?.now ?? new Date("2026-06-15T15:00:00.000Z")}
                browserTimeZone={
                  options?.browserTimeZone === undefined
                    ? "UTC"
                    : options.browserTimeZone
                }
              />
            </ContextualHomeProvider>
          </MemoryRouter>
        </ProductionI18nProvider>
      </SessionProvider>
    </RuntimeProvider>,
  );
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe("ContextualHomePage", () => {
  it("greets by name and keeps primary question", async () => {
    renderHome({
      displayName: "Ana",
      timeZone: "America/Sao_Paulo",
      now: new Date("2026-06-15T11:00:00.000-03:00"),
    });
    expect(await screen.findByTestId("home-greeting")).toHaveTextContent(
      /Good morning, Ana/,
    );
    expect(screen.getByTestId("home-primary-question")).toHaveTextContent(
      /What shall we cook today/,
    );
  });

  it("renders expanded suggestion fields for menu ready candidate", async () => {
    renderHome({ scenario: { scenario: "default" } });
    const card = await screen.findByTestId(
      "home-candidate-mock-menu-lentil-stew",
    );
    expect(card).toHaveAttribute("data-readiness", "ready_now");
    expect(
      screen.getByTestId("home-active-min-mock-menu-lentil-stew"),
    ).toBeInTheDocument();
    expect(
      screen.getByTestId("home-total-min-mock-menu-lentil-stew"),
    ).toBeInTheDocument();
    expect(
      screen.getByTestId("home-available-mock-menu-lentil-stew"),
    ).toBeInTheDocument();
  });

  it("shows missing required items and shopping required", async () => {
    renderHome({ scenario: { scenario: "menuMissingRequired" } });
    expect(
      await screen.findByTestId("home-missing-mock-menu-shopping-required"),
    ).toBeInTheDocument();
    expect(
      screen.getByTestId("home-candidate-mock-menu-shopping-required"),
    ).toHaveAttribute("data-shopping", "required");
  });

  it("shows thaw preparation requirements", async () => {
    renderHome({ scenario: { scenario: "menuNeedsThaw" } });
    expect(
      await screen.findByTestId("home-prep-mock-menu-needs-thaw"),
    ).toBeInTheDocument();
    expect(
      screen.getByTestId("home-candidate-mock-menu-needs-thaw"),
    ).toHaveAttribute("data-readiness", "needs_thaw");
  });

  it("shows uncertainty notices", async () => {
    renderHome({ scenario: { scenario: "withUncertainty" } });
    expect(
      await screen.findByTestId("home-uncertainty-mock-inv-uncertain-stew"),
    ).toBeInTheDocument();
  });

  it("omits empty menu tier without blanking inventory", async () => {
    renderHome({ scenario: { scenario: "noMenu" } });
    expect(
      await screen.findByTestId("home-source-inventory"),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("home-source-menu")).not.toBeInTheDocument();
  });

  it("keeps inventory when menu fails and recovers on retry", async () => {
    const user = userEvent.setup();
    renderHome({
      scenario: { scenario: "transientMenuFailThenRecover", menuFailTimes: 1 },
    });
    expect(await screen.findByTestId("home-source-menu")).toHaveAttribute(
      "data-status",
      "failed",
    );
    expect(screen.getByTestId("home-source-menu")).toHaveAttribute(
      "data-retryable",
      "true",
    );
    expect(screen.getByTestId("home-source-inventory")).toHaveAttribute(
      "data-status",
      "ready",
    );
    await user.click(screen.getByTestId("home-source-retry-menu"));
    await waitFor(() =>
      expect(screen.getByTestId("home-source-menu")).toHaveAttribute(
        "data-status",
        "ready",
      ),
    );
  });

  it("does not offer retry for permanent unavailable production sources", async () => {
    renderHome({ unavailable: true });
    expect(
      await screen.findByTestId("home-live-unavailable"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("home-source-menu")).toHaveAttribute(
      "data-retryable",
      "false",
    );
    expect(
      screen.queryByTestId("home-source-retry-menu"),
    ).not.toBeInTheDocument();
  });

  it("ignores stale menu responses after context change", async () => {
    const first = deferred<HomeSourceResult>();
    const second = deferred<HomeSourceResult>();
    let call = 0;
    const adapter: ContextualHomeAdapter = {
      async loadMenuSource() {
        call += 1;
        return call === 1 ? first.promise : second.promise;
      },
      async loadInventorySource() {
        return {
          tier: "inventory",
          status: "empty",
          retryable: false,
          statusReasonKey: "home.source.empty.inventory",
          items: [],
        };
      },
      async loadProfileSource() {
        return {
          tier: "profile",
          status: "empty",
          retryable: false,
          statusReasonKey: "home.source.incomplete.profile",
          items: [],
        };
      },
      async getQuickChooserDefinition() {
        return {
          capabilityStatus: "available",
          questions: [
            {
              id: "time",
              promptKey: "home.chooser.q.time",
              options: [
                { id: "under_20", labelKey: "home.chooser.a.under20" },
                { id: "about_40", labelKey: "home.chooser.a.about40" },
              ],
            },
          ],
        };
      },
      async loadQuickChooserSuggestions() {
        return {
          tier: "quickChooser",
          status: "empty",
          retryable: false,
          items: [],
        };
      },
    };

    const view = renderHome({
      adapter,
      timeZone: "America/Sao_Paulo",
      browserTimeZone: "Europe/Lisbon",
    });
    expect(
      await screen.findByTestId("home-source-loading-menu"),
    ).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByTestId("home-timezone-use-browser"));

    await act(async () => {
      second.resolve({
        tier: "menu",
        status: "ready",
        retryable: false,
        items: [
          {
            id: "newer",
            title: homeCatalogText("home.fixture.menu.lentilStew"),
            sourceTier: "menu",
            sourceLabelKey: "home.source.label.menu",
            reasonCodes: ["planned_for_daypart"],
          },
        ],
      });
      await second.promise;
    });
    expect(
      await screen.findByTestId("home-candidate-newer"),
    ).toBeInTheDocument();

    await act(async () => {
      first.resolve({
        tier: "menu",
        status: "ready",
        retryable: false,
        items: [
          {
            id: "stale-old",
            title: homeCatalogText("home.fixture.menu.lentilStew"),
            sourceTier: "menu",
            sourceLabelKey: "home.source.label.menu",
            reasonCodes: ["planned_for_daypart"],
          },
        ],
      });
      await first.promise;
    });
    expect(
      screen.queryByTestId("home-candidate-stale-old"),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("home-candidate-newer")).toBeInTheDocument();
    view.unmount();
  });

  it("runs one-question chooser without persistence", async () => {
    const user = userEvent.setup();
    renderHome({ scenario: { scenario: "oneQuestion" } });
    await screen.findByTestId("home-open-chooser");
    await user.click(screen.getByTestId("home-open-chooser"));
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));
    expect(await screen.findByTestId("chooser-results")).toBeInTheDocument();
    expect(localStorage.getItem("cocinaris_state_v1")).toBeNull();
  });

  it("cancels chooser during deferred load and ignores late result", async () => {
    const user = userEvent.setup();
    const pending = deferred<HomeSourceResult>();
    const base = createMockContextualHomeAdapter({ scenario: "default" });
    const adapter: ContextualHomeAdapter = {
      loadMenuSource: (q) => base.loadMenuSource(q),
      loadInventorySource: (q) => base.loadInventorySource(q),
      loadProfileSource: (q) => base.loadProfileSource(q),
      getQuickChooserDefinition: (q) => base.getQuickChooserDefinition(q),
      loadQuickChooserSuggestions: () => pending.promise,
    };
    renderHome({ adapter });
    await screen.findByTestId("home-open-chooser");
    await user.click(screen.getByTestId("home-open-chooser"));
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));
    await user.click(screen.getByTestId("chooser-option-use_what_i_have"));
    await user.click(screen.getByTestId("chooser-next"));
    await user.click(screen.getByTestId("chooser-cancel"));
    expect(screen.queryByTestId("quick-chooser")).not.toBeInTheDocument();
    await act(async () => {
      pending.resolve({
        tier: "quickChooser",
        status: "ready",
        retryable: false,
        items: [
          {
            id: "late",
            title: homeCatalogText("home.fixture.chooser.simpleSoup"),
            sourceTier: "quickChooser",
            sourceLabelKey: "home.source.label.quickChooser",
            reasonCodes: ["matches_request_answers"],
          },
        ],
      });
      await pending.promise;
    });
    expect(screen.queryByTestId("chooser-results")).not.toBeInTheDocument();
    expect(screen.queryByTestId("home-candidate-late")).not.toBeInTheDocument();
  });

  it("shows AI unavailable without retry when permanent", async () => {
    const user = userEvent.setup();
    renderHome({ scenario: { scenario: "aiUnavailable" } });
    await screen.findByTestId("home-open-chooser");
    await user.click(screen.getByTestId("home-open-chooser"));
    expect(screen.getByTestId("quick-chooser")).toBeInTheDocument();
    expect(screen.queryByTestId("chooser-retry")).not.toBeInTheDocument();
    await user.click(screen.getByTestId("chooser-cancel"));
  });

  it("moves focus into the chooser dialog when opened", async () => {
    const user = userEvent.setup();
    renderHome({ scenario: { scenario: "default" } });
    const open = await screen.findByTestId("home-open-chooser");
    await user.click(open);
    const dialog = await screen.findByTestId("quick-chooser");
    await waitFor(() => {
      expect(dialog.contains(document.activeElement)).toBe(true);
    });
  });

  it("protects double submit with a single suggestion request", async () => {
    const user = userEvent.setup();
    const pending = deferred<HomeSourceResult>();
    let calls = 0;
    const base = createMockContextualHomeAdapter({ scenario: "oneQuestion" });
    const adapter: ContextualHomeAdapter = {
      loadMenuSource: (q) => base.loadMenuSource(q),
      loadInventorySource: (q) => base.loadInventorySource(q),
      loadProfileSource: (q) => base.loadProfileSource(q),
      getQuickChooserDefinition: (q) => base.getQuickChooserDefinition(q),
      loadQuickChooserSuggestions: async () => {
        calls += 1;
        return pending.promise;
      },
    };
    renderHome({ adapter });
    await screen.findByTestId("home-open-chooser");
    await user.click(screen.getByTestId("home-open-chooser"));
    await user.click(screen.getByTestId("chooser-option-under_20"));
    const next = screen.getByTestId("chooser-next");
    await user.click(next);
    await user.click(next);
    expect(calls).toBe(1);
    await act(async () => {
      pending.resolve({
        tier: "quickChooser",
        status: "ready",
        retryable: false,
        items: [
          {
            id: "once",
            title: homeCatalogText("home.fixture.chooser.simpleSoup"),
            sourceTier: "quickChooser",
            sourceLabelKey: "home.source.label.quickChooser",
            reasonCodes: ["matches_request_answers"],
          },
        ],
      });
      await pending.promise;
    });
    expect(
      await screen.findByTestId("home-candidate-once"),
    ).toBeInTheDocument();
  });

  it("ignores older chooser suggestion when a newer attempt finishes first", async () => {
    const user = userEvent.setup();
    const first = deferred<HomeSourceResult>();
    const second = deferred<HomeSourceResult>();
    let call = 0;
    const base = createMockContextualHomeAdapter({ scenario: "oneQuestion" });
    const adapter: ContextualHomeAdapter = {
      loadMenuSource: (q) => base.loadMenuSource(q),
      loadInventorySource: (q) => base.loadInventorySource(q),
      loadProfileSource: (q) => base.loadProfileSource(q),
      getQuickChooserDefinition: (q) => base.getQuickChooserDefinition(q),
      loadQuickChooserSuggestions: async () => {
        call += 1;
        return call === 1 ? first.promise : second.promise;
      },
    };
    renderHome({ adapter });
    await screen.findByTestId("home-open-chooser");
    await user.click(screen.getByTestId("home-open-chooser"));
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));
    await user.click(screen.getByTestId("chooser-cancel"));

    await user.click(await screen.findByTestId("home-open-chooser"));
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));

    await act(async () => {
      second.resolve({
        tier: "quickChooser",
        status: "ready",
        retryable: false,
        items: [
          {
            id: "newer-choice",
            title: homeCatalogText("home.fixture.chooser.simpleSoup"),
            sourceTier: "quickChooser",
            sourceLabelKey: "home.source.label.quickChooser",
            reasonCodes: ["matches_request_answers"],
          },
        ],
      });
      await second.promise;
    });
    expect(
      await screen.findByTestId("home-candidate-newer-choice"),
    ).toBeInTheDocument();

    await act(async () => {
      first.resolve({
        tier: "quickChooser",
        status: "ready",
        retryable: false,
        items: [
          {
            id: "older-choice",
            title: homeCatalogText("home.fixture.chooser.simpleSoup"),
            sourceTier: "quickChooser",
            sourceLabelKey: "home.source.label.quickChooser",
            reasonCodes: ["matches_request_answers"],
          },
        ],
      });
      await first.promise;
    });
    expect(
      screen.queryByTestId("home-candidate-older-choice"),
    ).not.toBeInTheDocument();
    expect(
      screen.getByTestId("home-candidate-newer-choice"),
    ).toBeInTheDocument();
  });

  it("ignores stale inventory responses after context change", async () => {
    const first = deferred<HomeSourceResult>();
    const second = deferred<HomeSourceResult>();
    let call = 0;
    const adapter: ContextualHomeAdapter = {
      async loadMenuSource() {
        return {
          tier: "menu",
          status: "empty",
          retryable: false,
          statusReasonKey: "home.source.empty.menu",
          items: [],
        };
      },
      async loadInventorySource() {
        call += 1;
        return call === 1 ? first.promise : second.promise;
      },
      async loadProfileSource() {
        return {
          tier: "profile",
          status: "empty",
          retryable: false,
          statusReasonKey: "home.source.incomplete.profile",
          items: [],
        };
      },
      async getQuickChooserDefinition() {
        return {
          capabilityStatus: "available",
          questions: [
            {
              id: "time",
              promptKey: "home.chooser.q.time",
              options: [
                { id: "under_20", labelKey: "home.chooser.a.under20" },
                { id: "about_40", labelKey: "home.chooser.a.about40" },
              ],
            },
          ],
        };
      },
      async loadQuickChooserSuggestions() {
        return {
          tier: "quickChooser",
          status: "empty",
          retryable: false,
          items: [],
        };
      },
    };
    renderHome({
      adapter,
      timeZone: "America/Sao_Paulo",
      browserTimeZone: "Europe/Lisbon",
    });
    expect(
      await screen.findByTestId("home-source-loading-inventory"),
    ).toBeInTheDocument();
    const user = userEvent.setup();
    await user.click(screen.getByTestId("home-timezone-use-browser"));
    await act(async () => {
      second.resolve({
        tier: "inventory",
        status: "ready",
        retryable: false,
        items: [
          {
            id: "inv-new",
            title: homeCatalogText("home.fixture.inventory.attentionStew"),
            sourceTier: "inventory",
            sourceLabelKey: "home.source.label.inventory",
            reasonCodes: ["attention_window"],
            attentionInfluenced: true,
          },
        ],
      });
      await second.promise;
    });
    expect(
      await screen.findByTestId("home-candidate-inv-new"),
    ).toBeInTheDocument();
    await act(async () => {
      first.resolve({
        tier: "inventory",
        status: "ready",
        retryable: false,
        items: [
          {
            id: "inv-old",
            title: homeCatalogText("home.fixture.inventory.attentionStew"),
            sourceTier: "inventory",
            sourceLabelKey: "home.source.label.inventory",
            reasonCodes: ["attention_window"],
          },
        ],
      });
      await first.promise;
    });
    expect(
      screen.queryByTestId("home-candidate-inv-old"),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("home-candidate-inv-new")).toBeInTheDocument();
  });

  it("keeps Tab focus inside the open chooser", async () => {
    const user = userEvent.setup();
    renderHome({ scenario: { scenario: "default" } });
    await user.click(await screen.findByTestId("home-open-chooser"));
    const dialog = await screen.findByTestId("quick-chooser");
    await waitFor(() =>
      expect(dialog.contains(document.activeElement)).toBe(true),
    );
    for (let i = 0; i < 12; i += 1) {
      await user.tab();
      expect(dialog.contains(document.activeElement)).toBe(true);
    }
    for (let i = 0; i < 12; i += 1) {
      await user.tab({ shift: true });
      expect(dialog.contains(document.activeElement)).toBe(true);
    }
  });

  it("restores focus to opener after Escape", async () => {
    const user = userEvent.setup();
    renderHome({ scenario: { scenario: "default" } });
    const open = await screen.findByTestId("home-open-chooser");
    open.focus();
    await user.click(open);
    expect(screen.getByTestId("quick-chooser")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    await waitFor(() =>
      expect(screen.queryByTestId("quick-chooser")).not.toBeInTheDocument(),
    );
    expect(document.activeElement).toBe(open);
  });

  it("retries transient chooser-definition failures", async () => {
    const user = userEvent.setup();
    let calls = 0;
    const base = createMockContextualHomeAdapter({ scenario: "default" });
    const adapter: ContextualHomeAdapter = {
      loadMenuSource: (q) => base.loadMenuSource(q),
      loadInventorySource: (q) => base.loadInventorySource(q),
      loadProfileSource: (q) => base.loadProfileSource(q),
      getQuickChooserDefinition: async () => {
        calls += 1;
        if (calls === 1) {
          throw new Error("transient definition");
        }
        return base.getQuickChooserDefinition({
          locale: "en",
          timeZone: "UTC",
          now: new Date(),
        });
      },
      loadQuickChooserSuggestions: (q) => base.loadQuickChooserSuggestions(q),
    };
    renderHome({ adapter });
    const open = await screen.findByTestId("home-open-chooser");
    await user.click(open);
    expect(screen.getByTestId("quick-chooser")).toHaveAttribute(
      "data-capability",
      "temporarily_unavailable",
    );
    expect(screen.getByTestId("chooser-retry")).toBeInTheDocument();
    const callsBeforeRetry = calls;
    await user.click(screen.getByTestId("chooser-retry"));
    await waitFor(() =>
      expect(screen.getByTestId("quick-chooser")).toHaveAttribute(
        "data-capability",
        "available",
      ),
    );
    expect(calls).toBe(callsBeforeRetry + 1);
  });

  it("ignores stale deferred chooser definitions after a newer load", async () => {
    const first = deferred<HomeQuickChooserDefinition>();
    const second = deferred<HomeQuickChooserDefinition>();
    let call = 0;
    const base = createMockContextualHomeAdapter({ scenario: "default" });
    const adapter: ContextualHomeAdapter = {
      loadMenuSource: (q) => base.loadMenuSource(q),
      loadInventorySource: (q) => base.loadInventorySource(q),
      loadProfileSource: (q) => base.loadProfileSource(q),
      getQuickChooserDefinition: async () => {
        call += 1;
        return call === 1 ? first.promise : second.promise;
      },
      loadQuickChooserSuggestions: (q) => base.loadQuickChooserSuggestions(q),
    };
    renderHome({
      adapter,
      timeZone: "America/Sao_Paulo",
      browserTimeZone: "Europe/Lisbon",
    });
    await screen.findByTestId("home-source-loading-menu");
    const user = userEvent.setup();
    await user.click(screen.getByTestId("home-timezone-use-browser"));
    await act(async () => {
      second.resolve({
        capabilityStatus: "available",
        questions: [
          {
            id: "only",
            promptKey: "home.chooser.q.time",
            options: [
              { id: "under_20", labelKey: "home.chooser.a.under20" },
              { id: "about_40", labelKey: "home.chooser.a.about40" },
            ],
          },
        ],
      });
      await second.promise;
    });
    await user.click(await screen.findByTestId("home-open-chooser"));
    expect(screen.getByTestId("chooser-option-under_20")).toBeInTheDocument();
    expect(
      screen.queryByTestId("chooser-option-use_what_i_have"),
    ).not.toBeInTheDocument();
    await act(async () => {
      first.resolve({
        capabilityStatus: "available",
        questions: [
          {
            id: "stale",
            promptKey: "home.chooser.q.shopping",
            options: [
              {
                id: "use_what_i_have",
                labelKey: "home.chooser.a.useWhatIHave",
              },
              { id: "ok_to_buy", labelKey: "home.chooser.a.okToBuy" },
            ],
          },
        ],
      });
      await first.promise;
    });
    expect(screen.getByTestId("chooser-option-under_20")).toBeInTheDocument();
    expect(
      screen.queryByTestId("chooser-option-use_what_i_have"),
    ).not.toBeInTheDocument();
  });

  it("renders literal recipe and product names without localization lookup", async () => {
    const adapter: ContextualHomeAdapter = {
      async loadMenuSource() {
        return {
          tier: "menu",
          status: "ready",
          retryable: false,
          items: [
            {
              id: "live-recipe",
              title: { kind: "literal", value: "Live Garlic Pasta" },
              sourceTier: "menu",
              sourceLabelKey: "home.source.label.menu",
              reasonCodes: ["planned_for_daypart"],
              missingRequirements: [
                {
                  code: "garlic",
                  kind: "required",
                  label: { kind: "literal", value: "Fresh garlic bulb" },
                },
              ],
            },
          ],
        };
      },
      async loadInventorySource() {
        return {
          tier: "inventory",
          status: "empty",
          retryable: false,
          items: [],
        };
      },
      async loadProfileSource() {
        return {
          tier: "profile",
          status: "empty",
          retryable: false,
          items: [],
        };
      },
      async getQuickChooserDefinition() {
        return {
          capabilityStatus: "available",
          questions: [
            {
              id: "time",
              promptKey: "home.chooser.q.time",
              options: [
                { id: "under_20", labelKey: "home.chooser.a.under20" },
                { id: "about_40", labelKey: "home.chooser.a.about40" },
              ],
            },
          ],
        };
      },
      async loadQuickChooserSuggestions() {
        return {
          tier: "quickChooser",
          status: "empty",
          retryable: false,
          items: [],
        };
      },
    };
    renderHome({ adapter });
    expect(await screen.findByText("Live Garlic Pasta")).toBeInTheDocument();
    expect(screen.getByText(/Fresh garlic bulb/)).toBeInTheDocument();
  });

  it("keeps production chooser definition permanent and non-retryable", async () => {
    const user = userEvent.setup();
    renderHome({ unavailable: true });
    await screen.findByTestId("home-open-chooser");
    await user.click(screen.getByTestId("home-open-chooser"));
    expect(screen.getByTestId("quick-chooser")).toHaveAttribute(
      "data-capability",
      "not_implemented",
    );
    expect(screen.queryByTestId("chooser-retry")).not.toBeInTheDocument();
  });

  it("fails closed for invalid runtime definitions without blanking the home", async () => {
    const user = userEvent.setup();
    const base = createMockContextualHomeAdapter({ scenario: "default" });
    const adapter: ContextualHomeAdapter = {
      loadMenuSource: (q) => base.loadMenuSource(q),
      loadInventorySource: (q) => base.loadInventorySource(q),
      loadProfileSource: (q) => base.loadProfileSource(q),
      getQuickChooserDefinition: async () =>
        ({
          capabilityStatus: "available",
          questions: [],
        }) as unknown as HomeQuickChooserDefinition,
      loadQuickChooserSuggestions: (q) => base.loadQuickChooserSuggestions(q),
    };
    renderHome({ adapter });
    expect(
      await screen.findByTestId("home-candidate-mock-menu-lentil-stew"),
    ).toBeInTheDocument();
    await user.click(await screen.findByTestId("home-open-chooser"));
    expect(screen.getByTestId("quick-chooser")).toHaveAttribute(
      "data-capability",
      "temporarily_unavailable",
    );
    expect(
      screen.getByText(/Could not open the quick chooser/i),
    ).toBeInTheDocument();
    expect(screen.getByTestId("chooser-cancel")).toBeInTheDocument();
    expect(screen.getByTestId("chooser-retry")).toBeInTheDocument();
    expect(
      screen.getByTestId("home-candidate-mock-menu-lentil-stew"),
    ).toBeInTheDocument();
  });
});
