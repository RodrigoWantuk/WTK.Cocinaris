import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProfilePreferencesPage } from "./ProfilePreferencesPage";
import {
  ProfileApiError,
  type PreferenceCommand,
  type PreferenceSnapshot,
  type ProfileMutationContext,
} from "@/contracts/profile";
import {
  createEmptyPreferenceSnapshot,
  createMockProfileRepo,
  renderProfileTree,
} from "./testUtils";

function mockMutatePreferences() {
  return vi.fn<
    (
      ...args: [PreferenceCommand[], ProfileMutationContext]
    ) => Promise<PreferenceSnapshot>
  >(async () => createEmptyPreferenceSnapshot());
}

describe("ProfilePreferencesPage", () => {
  it("adds a non-sensitive catalog preference immediately, without a confirmation step", async () => {
    const mutatePreferences = vi.fn<
      (
        ...args: [PreferenceCommand[], ProfileMutationContext]
      ) => Promise<PreferenceSnapshot>
    >(async () =>
      createEmptyPreferenceSnapshot({
        entries: [
          {
            entryId: "e1",
            category: "Preference",
            stableCode: "spicy_food",
            note: null,
            presence: "confirmed",
            sortOrder: 0,
          },
        ],
      }),
    );
    const repo = createMockProfileRepo({ mutatePreferences });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfilePreferencesPage />,
      }),
    );
    expect(
      await screen.findByTestId("profile-preferences"),
    ).toBeInTheDocument();

    await user.selectOptions(
      screen.getByTestId("profile-preferences-catalog-select"),
      "spicy_food",
    );
    await user.click(screen.getByTestId("profile-preferences-catalog-submit"));

    await waitFor(() => expect(mutatePreferences).toHaveBeenCalledTimes(1));
    expect(mutatePreferences.mock.calls[0][0]).toEqual([
      {
        action: "add",
        category: "Preference",
        stableCode: "spicy_food",
        note: null,
      },
    ]);
    expect(
      screen.queryByTestId("profile-preferences-sensitive-confirm"),
    ).not.toBeInTheDocument();
  });

  it("requires explicit sensitive confirmation before adding an Allergy entry, and does not mutate until confirmed", async () => {
    const mutatePreferences = mockMutatePreferences();
    const repo = createMockProfileRepo({ mutatePreferences });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfilePreferencesPage />,
      }),
    );
    expect(
      await screen.findByTestId("profile-preferences"),
    ).toBeInTheDocument();

    await user.click(screen.getByTestId("profile-preferences-tab-Allergy"));
    await user.selectOptions(
      screen.getByTestId("profile-preferences-catalog-select"),
      "peanut_allergy",
    );
    await user.click(screen.getByTestId("profile-preferences-catalog-submit"));

    const confirmDialog = await screen.findByTestId(
      "profile-preferences-sensitive-confirm",
    );
    expect(confirmDialog).toHaveTextContent(/not.*medical advice/i);
    // No mutation is sent while the confirmation is pending.
    expect(mutatePreferences).not.toHaveBeenCalled();

    await user.click(
      within(confirmDialog).getByTestId(
        "profile-preferences-sensitive-confirm-add",
      ),
    );

    await waitFor(() => expect(mutatePreferences).toHaveBeenCalledTimes(1));
    expect(mutatePreferences.mock.calls[0][0]).toEqual([
      {
        action: "add",
        category: "Allergy",
        stableCode: "peanut_allergy",
        note: null,
      },
    ]);
    expect(
      screen.queryByTestId("profile-preferences-sensitive-confirm"),
    ).not.toBeInTheDocument();
  });

  it("cancelling the sensitive confirmation discards the pending add without mutating", async () => {
    const mutatePreferences = mockMutatePreferences();
    const repo = createMockProfileRepo({ mutatePreferences });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfilePreferencesPage />,
      }),
    );
    expect(
      await screen.findByTestId("profile-preferences"),
    ).toBeInTheDocument();

    await user.click(
      screen.getByTestId("profile-preferences-tab-MedicalRestriction"),
    );
    await user.selectOptions(
      screen.getByTestId("profile-preferences-catalog-select"),
      "low_sodium_diet",
    );
    await user.click(screen.getByTestId("profile-preferences-catalog-submit"));

    await screen.findByTestId("profile-preferences-sensitive-confirm");
    await user.click(
      screen.getByTestId("profile-preferences-sensitive-cancel"),
    );

    expect(
      screen.queryByTestId("profile-preferences-sensitive-confirm"),
    ).not.toBeInTheDocument();
    expect(mutatePreferences).not.toHaveBeenCalled();
  });

  it("mints an opaque custom stable code and stores the typed label as the note", async () => {
    const mutatePreferences = mockMutatePreferences();
    const repo = createMockProfileRepo({ mutatePreferences });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfilePreferencesPage />,
      }),
    );
    expect(
      await screen.findByTestId("profile-preferences"),
    ).toBeInTheDocument();

    await user.type(
      screen.getByTestId("profile-preferences-custom-label"),
      "Grandma's stew",
    );
    await user.click(screen.getByTestId("profile-preferences-custom-submit"));

    await waitFor(() => expect(mutatePreferences).toHaveBeenCalledTimes(1));
    const [commands] = mutatePreferences.mock.calls[0];
    const [command] = commands;
    expect(command.action).toBe("add");
    expect(command.note).toBe("Grandma's stew");
    expect(command.stableCode).not.toContain("Grandma");
    expect(command.stableCode).toMatch(/^custom_/);
  });

  it("has no separate note field for a custom entry — a single input doubles as its label", () => {
    render(
      renderProfileTree({
        repository: createMockProfileRepo(),
        children: <ProfilePreferencesPage />,
      }),
    );
    expect(
      screen.queryByTestId("profile-preferences-custom-note"),
    ).not.toBeInTheDocument();
  });

  it("uses a distinct action label for adding a custom entry than for adding a catalog entry", async () => {
    render(
      renderProfileTree({
        repository: createMockProfileRepo(),
        children: <ProfilePreferencesPage />,
      }),
    );
    await screen.findByTestId("profile-preferences");
    const catalogSubmit = screen.getByTestId(
      "profile-preferences-catalog-submit",
    );
    const customSubmit = screen.getByTestId(
      "profile-preferences-custom-submit",
    );
    expect(catalogSubmit.textContent).not.toEqual(customSubmit.textContent);
  });

  it("preserves the typed custom label after a failed add instead of discarding it", async () => {
    const mutatePreferences = vi.fn<
      (
        ...args: [PreferenceCommand[], ProfileMutationContext]
      ) => Promise<PreferenceSnapshot>
    >(async () => {
      throw new ProfileApiError("validation_failed", "rejected", 400);
    });
    const repo = createMockProfileRepo({ mutatePreferences });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfilePreferencesPage />,
      }),
    );
    await screen.findByTestId("profile-preferences");

    await user.type(
      screen.getByTestId("profile-preferences-custom-label"),
      "Keeps this text",
    );
    await user.click(screen.getByTestId("profile-preferences-custom-submit"));

    await waitFor(() =>
      expect(
        screen.getByTestId("profile-preferences-error-message"),
      ).toBeInTheDocument(),
    );
    expect(screen.getByTestId("profile-preferences-custom-label")).toHaveValue(
      "Keeps this text",
    );
  });

  it("preserves the pending sensitive add's typed note when the confirmation is cancelled", async () => {
    const mutatePreferences = vi.fn<
      (
        ...args: [PreferenceCommand[], ProfileMutationContext]
      ) => Promise<PreferenceSnapshot>
    >(async () => createEmptyPreferenceSnapshot());
    const repo = createMockProfileRepo({ mutatePreferences });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfilePreferencesPage />,
      }),
    );
    await screen.findByTestId("profile-preferences");

    await user.click(screen.getByTestId("profile-preferences-tab-Allergy"));
    await user.selectOptions(
      screen.getByTestId("profile-preferences-catalog-select"),
      "peanut_allergy",
    );
    await user.type(
      screen.getByTestId("profile-preferences-catalog-note"),
      "kept note",
    );
    await user.click(screen.getByTestId("profile-preferences-catalog-submit"));
    await screen.findByTestId("profile-preferences-sensitive-confirm");

    await user.click(
      screen.getByTestId("profile-preferences-sensitive-cancel"),
    );

    expect(screen.getByTestId("profile-preferences-catalog-note")).toHaveValue(
      "kept note",
    );
    expect(mutatePreferences).not.toHaveBeenCalled();
  });

  it("uses a Radix alert dialog for the sensitive confirmation (aria-modal, Escape closes)", async () => {
    const repo = createMockProfileRepo();
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfilePreferencesPage />,
      }),
    );
    await screen.findByTestId("profile-preferences");

    await user.click(screen.getByTestId("profile-preferences-tab-Allergy"));
    await user.selectOptions(
      screen.getByTestId("profile-preferences-catalog-select"),
      "peanut_allergy",
    );
    await user.click(screen.getByTestId("profile-preferences-catalog-submit"));

    const dialog = await screen.findByTestId(
      "profile-preferences-sensitive-confirm",
    );
    expect(dialog).toHaveAttribute("role", "alertdialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");

    await user.keyboard("{Escape}");
    expect(
      screen.queryByTestId("profile-preferences-sensitive-confirm"),
    ).not.toBeInTheDocument();
  });

  it("provides visible, contextual names for preference controls", async () => {
    const repo = createMockProfileRepo({
      getPreferences: vi.fn(async () =>
        createEmptyPreferenceSnapshot({
          entries: [
            {
              entryId: "entry-spicy",
              category: "Preference",
              stableCode: "spicy_food",
              note: "Keep it mild",
              presence: "confirmed",
              sortOrder: 0,
            },
          ],
        }),
      ),
    });

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfilePreferencesPage />,
      }),
    );
    await screen.findByTestId("profile-preferences");

    expect(
      screen.getByRole("combobox", { name: /add from catalog/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("textbox", {
        name: /optional note for catalog entry/i,
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("textbox", { name: /custom entry label/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("textbox", { name: /^note — /i }),
    ).toBeInTheDocument();
  });
});
