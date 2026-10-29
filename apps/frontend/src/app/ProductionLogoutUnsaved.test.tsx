/**
 * PLAN-0020 remediation — dirty logout must use the unsaved-changes coordinator.
 */
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ProductionApp from "@/app/ProductionApp";
import { PRODUCTION_LOCALE_STORAGE_KEY } from "@/app/i18n/productionCatalog";

function absentField(defaultValue: unknown = null, presence = "absent") {
  return { value: null, presence, defaultValue, durability: "durable" };
}

const PROFILE_BODY = {
  ownerUserId: "11111111-1111-1111-1111-111111111111",
  displayName: {
    value: "Ada",
    presence: "confirmed",
    defaultValue: null,
    durability: "durable",
  },
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
  profileExists: true,
  version: "v1",
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: "2026-08-01T00:00:00Z",
};

const EQUIPMENT_BODY = {
  version: "v1",
  entries: [
    {
      entryId: "eq-oven",
      stableCode: "oven",
      customName: null,
      capacity: 30,
      capacityUnit: "L",
      constraintNote: null,
      sortOrder: 0,
    },
  ],
};

function mockAuthenticatedFetch(
  logoutCalls: string[],
  options?: { logoutStatus?: number },
) {
  const logoutStatus = options?.logoutStatus ?? 204;
  return vi
    .spyOn(globalThis, "fetch")
    .mockImplementation(async (input, init) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof Request
            ? input.url
            : String(input);
      const method =
        (init && "method" in init && init.method) ||
        (input instanceof Request ? input.method : "GET");
      if (url.includes("/api/v1/auth/logout")) {
        logoutCalls.push("logout");
        if (logoutStatus >= 400) {
          return new Response(JSON.stringify({ title: "logout failed" }), {
            status: logoutStatus,
            headers: { "content-type": "application/json" },
          });
        }
        return new Response(null, { status: 204 });
      }
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
            profilePercentComplete: 20,
            adultDeclarationState: "NotDeclared",
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        );
      }
      if (url.includes("/api/v1/profile/preferences")) {
        return new Response(JSON.stringify({ version: "v1", entries: [] }), {
          status: 200,
          headers: { "content-type": "application/json", ETag: '"v1"' },
        });
      }
      if (url.includes("/api/v1/profile/equipment")) {
        return new Response(JSON.stringify(EQUIPMENT_BODY), {
          status: 200,
          headers: { "content-type": "application/json", ETag: '"v1"' },
        });
      }
      if (url.includes("/api/v1/profile/completeness")) {
        return new Response(
          JSON.stringify({
            percentComplete: 20,
            completedSections: 1,
            totalSections: 5,
            sectionCounts: { household: 1 },
            adultDeclarationState: "NotDeclared",
            profileExists: true,
          }),
          { status: 200, headers: { "content-type": "application/json" } },
        );
      }
      if (url.includes("/api/v1/profile") && method === "GET") {
        return new Response(JSON.stringify(PROFILE_BODY), {
          status: 200,
          headers: { "content-type": "application/json", ETag: '"v1"' },
        });
      }
      return new Response("not found", { status: 404 });
    });
}

async function dirtyProfileData(user: ReturnType<typeof userEvent.setup>) {
  await waitFor(() =>
    expect(
      screen.getByTestId("profile-data-input-displayName"),
    ).toBeInTheDocument(),
  );
  await user.clear(screen.getByTestId("profile-data-input-displayName"));
  await user.type(
    screen.getByTestId("profile-data-input-displayName"),
    "Beatrice",
  );
}

// Sign out lives in the account menu since PLAN-0054 Phase 1.
async function clickLogout(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByTestId("production-account-menu"));
  await user.click(screen.getByTestId("production-logout"));
}

describe("ProductionApp dirty logout protection", () => {
  beforeEach(() => {
    window.localStorage.setItem(PRODUCTION_LOCALE_STORAGE_KEY, "en");
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("logs out immediately from a clean profile page", async () => {
    const logoutCalls: string[] = [];
    mockAuthenticatedFetch(logoutCalls);
    window.history.pushState({}, "", "/app/perfil/dados");
    const user = userEvent.setup();
    render(<ProductionApp />);
    await waitFor(() =>
      expect(
        screen.getByTestId("profile-data-input-displayName"),
      ).toBeInTheDocument(),
    );
    await clickLogout(user);
    await waitFor(() => expect(logoutCalls).toHaveLength(1));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("prompts before logout when Profile Data is dirty; Stay preserves draft", async () => {
    const logoutCalls: string[] = [];
    mockAuthenticatedFetch(logoutCalls);
    window.history.pushState({}, "", "/app/perfil/dados");
    const user = userEvent.setup();
    render(<ProductionApp />);
    await dirtyProfileData(user);
    await clickLogout(user);
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    expect(logoutCalls).toHaveLength(0);
    await user.click(screen.getByTestId("profile-unsaved-stay"));
    expect(logoutCalls).toHaveLength(0);
    expect(screen.getByTestId("profile-data-input-displayName")).toHaveValue(
      "Beatrice",
    );
  });

  it("prompts before logout when Preferences is dirty", async () => {
    const logoutCalls: string[] = [];
    mockAuthenticatedFetch(logoutCalls);
    window.history.pushState({}, "", "/app/perfil/preferencias");
    const user = userEvent.setup();
    render(<ProductionApp />);
    await waitFor(() =>
      expect(screen.getByTestId("profile-preferences")).toBeInTheDocument(),
    );
    await user.type(
      screen.getByTestId("profile-preferences-custom-label"),
      "Dirty preference draft",
    );
    await clickLogout(user);
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    expect(logoutCalls).toHaveLength(0);
    expect(screen.getByTestId("profile-preferences-custom-label")).toHaveValue(
      "Dirty preference draft",
    );
  });

  it("prompts before logout when Equipment is dirty", async () => {
    const logoutCalls: string[] = [];
    mockAuthenticatedFetch(logoutCalls);
    window.history.pushState({}, "", "/app/perfil/equipamentos");
    const user = userEvent.setup();
    render(<ProductionApp />);
    await waitFor(() =>
      expect(screen.getByTestId("profile-equipment")).toBeInTheDocument(),
    );
    await user.clear(screen.getByTestId("profile-equipment-capacity-eq-oven"));
    await user.type(
      screen.getByTestId("profile-equipment-capacity-eq-oven"),
      "40",
    );
    await clickLogout(user);
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    expect(logoutCalls).toHaveLength(0);
    expect(
      screen.getByTestId("profile-equipment-capacity-eq-oven"),
    ).toHaveValue(40);
  });

  it("Escape cancels dirty logout without calling logout", async () => {
    const logoutCalls: string[] = [];
    mockAuthenticatedFetch(logoutCalls);
    window.history.pushState({}, "", "/app/perfil/dados");
    const user = userEvent.setup();
    render(<ProductionApp />);
    await dirtyProfileData(user);
    await clickLogout(user);
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument(),
    );
    expect(logoutCalls).toHaveLength(0);
    expect(screen.getByTestId("profile-data-input-displayName")).toHaveValue(
      "Beatrice",
    );
  });

  it("discards dirty draft and logs out exactly once", async () => {
    const logoutCalls: string[] = [];
    mockAuthenticatedFetch(logoutCalls);
    window.history.pushState({}, "", "/app/perfil/dados");
    const user = userEvent.setup();
    render(<ProductionApp />);
    await dirtyProfileData(user);
    await clickLogout(user);
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    await user.click(screen.getByTestId("profile-unsaved-discard"));
    await waitFor(() => expect(logoutCalls).toHaveLength(1));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("repeated logout clicks while confirmation is open do not duplicate logout", async () => {
    const logoutCalls: string[] = [];
    mockAuthenticatedFetch(logoutCalls);
    window.history.pushState({}, "", "/app/perfil/dados");
    const user = userEvent.setup();
    render(<ProductionApp />);
    await dirtyProfileData(user);
    await user.click(screen.getByTestId("production-account-menu"));
    const logoutButton = screen.getByTestId("production-logout");
    await user.click(logoutButton);
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    // Modal sets pointer-events:none on the page; still ensure synthetic repeats
    // only arm one pending logout action.
    fireEvent.click(logoutButton);
    fireEvent.click(logoutButton);
    expect(logoutCalls).toHaveLength(0);
    await user.click(screen.getByTestId("profile-unsaved-discard"));
    await waitFor(() => expect(logoutCalls).toHaveLength(1));
  });

  it("logout API failure after confirmed discard does not reopen confirmation", async () => {
    const logoutCalls: string[] = [];
    mockAuthenticatedFetch(logoutCalls, { logoutStatus: 500 });
    window.history.pushState({}, "", "/app/perfil/dados");
    const user = userEvent.setup();
    render(<ProductionApp />);
    await dirtyProfileData(user);
    await clickLogout(user);
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    await user.click(screen.getByTestId("profile-unsaved-discard"));
    await waitFor(() => expect(logoutCalls).toHaveLength(1));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    // Session remains authenticated when logout fails; draft was discarded.
    expect(
      screen.getByTestId("profile-data-input-displayName"),
    ).toBeInTheDocument();
  });
});
