import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProfileDataPage } from "./ProfileDataPage";
import {
  ProfileApiError,
  type ProfileMutationContext,
  type ProfilePatch,
  type ProfileSnapshot,
} from "@/contracts/profile";
import {
  createConfirmedProfileSnapshot,
  createMockProfileRepo,
  renderProfileTree,
} from "./testUtils";

function mockPatchProfile() {
  return vi.fn<
    (
      ...args: [ProfilePatch, ProfileMutationContext]
    ) => Promise<ProfileSnapshot>
  >(async () => createConfirmedProfileSnapshot());
}

describe("ProfileDataPage", () => {
  it("does not send a default-presence field as confirmed when the user saves other unrelated edits", async () => {
    const patchProfile = mockPatchProfile();
    const repo = createMockProfileRepo({ patchProfile });
    const user = userEvent.setup();

    render(
      renderProfileTree({ repository: repo, children: <ProfileDataPage /> }),
    );
    expect(await screen.findByTestId("profile-data")).toBeInTheDocument();

    // defaultChildCount is presence "default" in the fixture; leave it untouched.
    const displayNameInput = screen.getByTestId(
      "profile-data-input-displayName",
    );
    await user.clear(displayNameInput);
    await user.type(displayNameInput, "Bea");

    await user.click(screen.getByTestId("profile-data-household-save"));

    await waitFor(() => expect(patchProfile).toHaveBeenCalledTimes(1));
    const [patch] = patchProfile.mock.calls[0];
    expect(patch.displayName).toEqual({ action: "confirm", value: "Bea" });
    expect(patch.defaultChildCount).toBeUndefined();
  });

  it("sends an explicit confirm mutation with the default value when the user clicks 'use this default'", async () => {
    const patchProfile = mockPatchProfile();
    const repo = createMockProfileRepo({ patchProfile });
    const user = userEvent.setup();

    render(
      renderProfileTree({ repository: repo, children: <ProfileDataPage /> }),
    );
    expect(await screen.findByTestId("profile-data")).toBeInTheDocument();

    expect(
      screen.getByTestId("profile-data-status-defaultChildCount"),
    ).toHaveTextContent("Suggested default");

    await user.click(
      screen.getByTestId("profile-data-use-default-defaultChildCount"),
    );
    expect(
      screen.getByTestId("profile-data-status-defaultChildCount"),
    ).toHaveTextContent("Will be confirmed on save");

    await user.click(screen.getByTestId("profile-data-household-save"));

    await waitFor(() => expect(patchProfile).toHaveBeenCalledTimes(1));
    const [patch] = patchProfile.mock.calls[0];
    expect(patch.defaultChildCount).toEqual({ action: "confirm", value: 0 });
    expect(patch.displayName).toBeUndefined();
  });

  it("PATCHes only the cooking section fields when saving the cooking form", async () => {
    const patchProfile = mockPatchProfile();
    const repo = createMockProfileRepo({ patchProfile });
    const user = userEvent.setup();

    render(
      renderProfileTree({ repository: repo, children: <ProfileDataPage /> }),
    );
    expect(await screen.findByTestId("profile-data")).toBeInTheDocument();

    const prepInput = screen.getByTestId(
      "profile-data-input-ordinaryPrepMinutes",
    );
    await user.clear(prepInput);
    await user.type(prepInput, "45");

    await user.click(screen.getByTestId("profile-data-cooking-save"));

    await waitFor(() => expect(patchProfile).toHaveBeenCalledTimes(1));
    const [patch] = patchProfile.mock.calls[0];
    expect(patch.ordinaryPrepMinutes).toEqual({ action: "confirm", value: 45 });
    expect(patch.displayName).toBeUndefined();
    expect(patch.language).toBeUndefined();
  });

  it("cancel restores the household section to the last-loaded snapshot without submitting", async () => {
    const patchProfile = mockPatchProfile();
    const repo = createMockProfileRepo({ patchProfile });
    const user = userEvent.setup();

    render(
      renderProfileTree({ repository: repo, children: <ProfileDataPage /> }),
    );
    const displayNameInput = await screen.findByTestId(
      "profile-data-input-displayName",
    );
    await user.clear(displayNameInput);
    await user.type(displayNameInput, "Temporary");

    await user.click(screen.getByTestId("profile-data-household-cancel"));

    expect(screen.getByTestId("profile-data-input-displayName")).toHaveValue(
      "Ada",
    );
    expect(patchProfile).not.toHaveBeenCalled();
  });

  it("renders controlled fields as a closed select and sends the chosen wire value", async () => {
    const patchProfile = mockPatchProfile();
    const repo = createMockProfileRepo({ patchProfile });
    const user = userEvent.setup();

    render(
      renderProfileTree({ repository: repo, children: <ProfileDataPage /> }),
    );
    expect(await screen.findByTestId("profile-data")).toBeInTheDocument();

    const regionSelect = screen.getByTestId("profile-data-input-region");
    expect(regionSelect.tagName).toBe("SELECT");
    await user.selectOptions(regionSelect, "BR");

    await user.click(screen.getByTestId("profile-data-household-save"));

    await waitFor(() => expect(patchProfile).toHaveBeenCalledTimes(1));
    const [patch] = patchProfile.mock.calls[0];
    expect(patch.region).toEqual({ action: "confirm", value: "BR" });
  });

  it("blocks submit with a local field error instead of coercing an empty numeric field to 0", async () => {
    const patchProfile = mockPatchProfile();
    const repo = createMockProfileRepo({ patchProfile });
    const user = userEvent.setup();

    render(
      renderProfileTree({ repository: repo, children: <ProfileDataPage /> }),
    );
    expect(await screen.findByTestId("profile-data")).toBeInTheDocument();

    const adultInput = screen.getByTestId(
      "profile-data-input-defaultAdultCount",
    );
    await user.clear(adultInput);
    await user.click(screen.getByTestId("profile-data-household-save"));

    expect(
      await screen.findByTestId("profile-data-error-defaultAdultCount"),
    ).toBeInTheDocument();
    expect(
      screen.getByTestId("profile-data-household-error-summary"),
    ).toBeInTheDocument();
    expect(patchProfile).not.toHaveBeenCalled();
  });

  it("blocks submit when a numeric field is outside the backend's authoritative range", async () => {
    const patchProfile = mockPatchProfile();
    const repo = createMockProfileRepo({ patchProfile });
    const user = userEvent.setup();

    render(
      renderProfileTree({ repository: repo, children: <ProfileDataPage /> }),
    );
    expect(await screen.findByTestId("profile-data")).toBeInTheDocument();

    const servingInput = screen.getByTestId(
      "profile-data-input-defaultServingCount",
    );
    await user.clear(servingInput);
    await user.type(servingInput, "99");
    await user.click(screen.getByTestId("profile-data-household-save"));

    expect(
      await screen.findByTestId("profile-data-error-defaultServingCount"),
    ).toHaveTextContent("between 1 and 30");
    expect(patchProfile).not.toHaveBeenCalled();
  });

  it("maps backend field errors onto the matching field and summarizes unmatched ones", async () => {
    const patchProfile = vi.fn<
      (
        ...args: [ProfilePatch, ProfileMutationContext]
      ) => Promise<ProfileSnapshot>
    >(async () => {
      throw new ProfileApiError(
        "validation_failed",
        "The server rejected this input.",
        400,
        {
          fieldErrors: {
            displayName: ["displayName is invalid."],
            somethingUnmapped: ["unrecognized path"],
          },
        },
      );
    });
    const repo = createMockProfileRepo({ patchProfile });
    const user = userEvent.setup();

    render(
      renderProfileTree({ repository: repo, children: <ProfileDataPage /> }),
    );
    expect(await screen.findByTestId("profile-data")).toBeInTheDocument();

    const displayNameInput = screen.getByTestId(
      "profile-data-input-displayName",
    );
    await user.clear(displayNameInput);
    await user.type(displayNameInput, "x");
    await user.click(screen.getByTestId("profile-data-household-save"));

    expect(
      await screen.findByTestId("profile-data-error-displayName"),
    ).toHaveTextContent("displayName is invalid.");
    expect(
      screen.getByTestId("profile-data-household-error-summary-unknown"),
    ).toBeInTheDocument();
    // The failed field's draft is preserved, not discarded.
    expect(screen.getByTestId("profile-data-input-displayName")).toHaveValue(
      "x",
    );
  });

  it("shows an accessible confirmation instead of navigating immediately when leaving with unsaved edits", async () => {
    const repo = createMockProfileRepo();
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        initialPath: "/app/perfil/dados",
        children: <ProfileDataPage />,
      }),
    );
    const displayNameInput = await screen.findByTestId(
      "profile-data-input-displayName",
    );
    await user.clear(displayNameInput);
    await user.type(displayNameInput, "Changed");

    await user.click(screen.getByTestId("profile-data-back"));

    expect(
      await screen.findByTestId("profile-unsaved-dialog"),
    ).toBeInTheDocument();
    // Still on the same page: navigation was not performed yet.
    expect(screen.getByTestId("profile-data")).toBeInTheDocument();

    await user.click(screen.getByTestId("profile-unsaved-stay"));
    expect(
      screen.queryByTestId("profile-unsaved-dialog"),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("profile-data-input-displayName")).toHaveValue(
      "Changed",
    );
  });
});
