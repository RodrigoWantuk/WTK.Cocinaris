import { render, screen, waitFor } from "@testing-library/react";
import { ProfileOverviewPage } from "./ProfileOverviewPage";
import {
  createAbsentProfileSnapshot,
  createCompleteness,
  createEmptyEquipmentSnapshot,
  createEmptyPreferenceSnapshot,
  createConfirmedProfileSnapshot,
  createMockProfileRepo,
  renderProfileTree,
} from "./testUtils";

describe("ProfileOverviewPage", () => {
  it("renders completeness and section links, without a completed adult confirmation", async () => {
    const repo = createMockProfileRepo();
    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileOverviewPage />,
      }),
    );

    expect(await screen.findByTestId("profile-overview")).toBeInTheDocument();
    expect(
      screen.getByTestId("profile-overview-completeness"),
    ).toHaveTextContent("40%");
    // A completed adult confirmation is not shown again (owner decision 2026-10-28).
    expect(
      screen.queryByTestId("profile-overview-adult-declaration"),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("profile-overview-link-data")).toHaveAttribute(
      "href",
      "/app/perfil/dados",
    );
    expect(
      screen.getByTestId("profile-overview-link-preferences"),
    ).toHaveAttribute("href", "/app/perfil/preferencias");
    expect(
      screen.getByTestId("profile-overview-link-equipment"),
    ).toHaveAttribute("href", "/app/perfil/equipamentos");
    expect(
      screen.queryByTestId("profile-overview-not-started"),
    ).not.toBeInTheDocument();
  });

  it("asks for the adult confirmation only while it is pending", async () => {
    const pending = createConfirmedProfileSnapshot();
    pending.adultDeclaration = {
      adultDeclared: null,
      termsVersion: null,
      privacyVersion: null,
      acceptedAt: null,
      state: "NotDeclared",
    };
    render(
      renderProfileTree({
        repository: createMockProfileRepo({
          getProfile: vi.fn(async () => pending),
        }),
        children: <ProfileOverviewPage />,
      }),
    );

    expect(
      await screen.findByTestId("profile-overview-adult-declaration"),
    ).toHaveTextContent("Not declared yet");
  });

  it("shows the not-started state when profileExists is false", async () => {
    // A real backend never resolves a preferences/equipment version for an owner
    // with no persisted profile; the workspace consistency check enforces this.
    const repo = createMockProfileRepo({
      getProfile: vi.fn(async () => createAbsentProfileSnapshot()),
      getPreferences: vi.fn(async () =>
        createEmptyPreferenceSnapshot({ version: null, etag: null }),
      ),
      getEquipment: vi.fn(async () =>
        createEmptyEquipmentSnapshot({ version: null, etag: null }),
      ),
      getCompleteness: vi.fn(async () =>
        createCompleteness({ profileExists: false, percentComplete: 0 }),
      ),
    });
    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileOverviewPage />,
      }),
    );

    await waitFor(() =>
      expect(
        screen.getByTestId("profile-overview-not-started"),
      ).toBeInTheDocument(),
    );
  });

  it("shows a retry action when loading fails", async () => {
    const repo = createMockProfileRepo({
      getProfile: vi.fn(async () => {
        throw new Error("boom");
      }),
    });
    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileOverviewPage />,
      }),
    );

    expect(
      await screen.findByTestId("profile-overview-error"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("profile-overview-retry")).toBeInTheDocument();
  });
});
