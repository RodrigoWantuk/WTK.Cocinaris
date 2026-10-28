import { render, screen, waitFor } from "@testing-library/react";
import { ProfileOverviewPage } from "./ProfileOverviewPage";
import {
  createAbsentProfileSnapshot,
  createCompleteness,
  createEmptyEquipmentSnapshot,
  createEmptyPreferenceSnapshot,
  createMockProfileRepo,
  renderProfileTree,
} from "./testUtils";

describe("ProfileOverviewPage", () => {
  it("renders completeness, adult declaration status, and section links for an existing profile", async () => {
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
    expect(
      screen.getByTestId("profile-overview-adult-declaration"),
    ).toHaveTextContent("Declared");
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
