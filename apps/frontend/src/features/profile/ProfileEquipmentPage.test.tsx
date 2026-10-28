import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProfileEquipmentPage } from "./ProfileEquipmentPage";
import type {
  EquipmentInput,
  EquipmentSnapshot,
  ProfileMutationContext,
} from "@/contracts/profile";
import { ProfileApiError } from "@/contracts/profile";
import {
  createEmptyEquipmentSnapshot,
  createMockProfileRepo,
  renderProfileTree,
} from "./testUtils";

function mockReplaceEquipment() {
  return vi.fn<
    (
      ...args: [EquipmentInput[], ProfileMutationContext]
    ) => Promise<EquipmentSnapshot>
  >(async () => createEmptyEquipmentSnapshot());
}

function seededEquipment() {
  return createEmptyEquipmentSnapshot({
    entries: [
      {
        entryId: "eq-oven",
        stableCode: "oven",
        customName: null,
        capacity: null,
        capacityUnit: null,
        constraintNote: null,
        isActive: true,
        sortOrder: 0,
      },
      {
        entryId: "eq-blender",
        stableCode: "blender",
        customName: null,
        capacity: null,
        capacityUnit: null,
        constraintNote: null,
        isActive: true,
        sortOrder: 1,
      },
    ],
  });
}

describe("ProfileEquipmentPage", () => {
  it("moves an entry up and submits the whole reordered collection with the workspace etag", async () => {
    const replaceEquipment = mockReplaceEquipment();
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => seededEquipment()),
      replaceEquipment,
    });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();

    const entries = screen.getByTestId("profile-equipment-entries");
    const beforeOrder = Array.from(entries.querySelectorAll("li")).map((li) =>
      li.getAttribute("data-testid"),
    );
    expect(beforeOrder).toEqual([
      "profile-equipment-entry-eq-oven",
      "profile-equipment-entry-eq-blender",
    ]);

    await user.click(
      screen.getByTestId("profile-equipment-move-up-eq-blender"),
    );

    const afterOrder = Array.from(entries.querySelectorAll("li")).map((li) =>
      li.getAttribute("data-testid"),
    );
    expect(afterOrder).toEqual([
      "profile-equipment-entry-eq-blender",
      "profile-equipment-entry-eq-oven",
    ]);

    await user.click(screen.getByTestId("profile-equipment-save"));

    await waitFor(() => expect(replaceEquipment).toHaveBeenCalledTimes(1));
    const [entriesSubmitted, context] = replaceEquipment.mock.calls[0];
    expect(entriesSubmitted.map((entry) => entry.stableCode)).toEqual([
      "blender",
      "oven",
    ]);
    expect(context).toMatchObject({ csrfToken: "csrf-test", etag: '"v1"' });
  });

  it("adds a custom equipment entry with an opaque stable code and the typed name", async () => {
    const replaceEquipment = mockReplaceEquipment();
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => createEmptyEquipmentSnapshot()),
      replaceEquipment,
    });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();

    await user.type(
      screen.getByTestId("profile-equipment-custom-input"),
      "Grandma's mixer",
    );
    await user.click(screen.getByTestId("profile-equipment-custom-submit"));
    await user.click(screen.getByTestId("profile-equipment-save"));

    await waitFor(() => expect(replaceEquipment).toHaveBeenCalledTimes(1));
    const [entriesSubmitted] = replaceEquipment.mock.calls[0];
    expect(entriesSubmitted).toHaveLength(1);
    expect(entriesSubmitted[0].customName).toBe("Grandma's mixer");
    expect(entriesSubmitted[0].stableCode).toMatch(/^custom_/);
  });

  it("save is disabled until the draft is dirty, and cancel restores the last-loaded snapshot", async () => {
    const replaceEquipment = mockReplaceEquipment();
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => seededEquipment()),
      replaceEquipment,
    });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();
    expect(screen.getByTestId("profile-equipment-save")).toBeDisabled();

    await user.click(screen.getByTestId("profile-equipment-remove-eq-blender"));
    expect(screen.getByTestId("profile-equipment-save")).not.toBeDisabled();

    await user.click(screen.getByTestId("profile-equipment-cancel"));
    expect(
      screen.getByTestId("profile-equipment-entry-eq-blender"),
    ).toBeInTheDocument();
    expect(replaceEquipment).not.toHaveBeenCalled();
  });

  it("requires a name before a custom entry can be added", async () => {
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => createEmptyEquipmentSnapshot()),
    });
    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();
    expect(
      screen.getByTestId("profile-equipment-custom-submit"),
    ).toBeDisabled();
  });

  it("rejects a negative capacity locally and never submits it to the backend", async () => {
    const replaceEquipment = mockReplaceEquipment();
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => seededEquipment()),
      replaceEquipment,
    });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();

    const capacityInput = screen.getByTestId(
      "profile-equipment-capacity-eq-oven",
    );
    await user.type(capacityInput, "-5");
    await user.type(
      screen.getByTestId("profile-equipment-capacity-unit-eq-oven"),
      "L",
    );
    await user.click(screen.getByTestId("profile-equipment-save"));

    await waitFor(() =>
      expect(
        document.getElementById("profile-equipment-capacity-error-eq-oven"),
      ).not.toBeNull(),
    );
    expect(replaceEquipment).not.toHaveBeenCalled();
  });

  it("rejects a capacity value with no unit as an incoherent pair", async () => {
    const replaceEquipment = mockReplaceEquipment();
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => seededEquipment()),
      replaceEquipment,
    });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();

    await user.type(
      screen.getByTestId("profile-equipment-capacity-eq-oven"),
      "5",
    );
    await user.click(screen.getByTestId("profile-equipment-save"));

    await waitFor(() =>
      expect(
        document.getElementById("profile-equipment-capacityUnit-error-eq-oven"),
      ).not.toBeNull(),
    );
    expect(replaceEquipment).not.toHaveBeenCalled();
  });

  it("announces the new position of a moved entry through a live region", async () => {
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => seededEquipment()),
    });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();
    expect(
      screen.getByTestId("profile-equipment-live-region"),
    ).toHaveTextContent("");

    await user.click(
      screen.getByTestId("profile-equipment-move-up-eq-blender"),
    );

    expect(
      screen.getByTestId("profile-equipment-live-region"),
    ).not.toHaveTextContent("");
  });

  it("moves focus to the add-from-catalog control after removing an entry", async () => {
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => seededEquipment()),
    });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    expect(await screen.findByTestId("profile-equipment")).toBeInTheDocument();

    await user.click(screen.getByTestId("profile-equipment-remove-eq-blender"));

    await waitFor(() =>
      expect(
        screen.getByTestId("profile-equipment-catalog-select"),
      ).toHaveFocus(),
    );
  });

  it("maps stable-code validation errors to the equipment entry and retains the draft without retrying", async () => {
    const replaceEquipment = vi.fn<
      (
        ...args: [EquipmentInput[], ProfileMutationContext]
      ) => Promise<EquipmentSnapshot>
    >(async () => {
      throw new ProfileApiError(
        "validation_failed",
        "The server rejected this input.",
        400,
        {
          fieldErrors: {
            "entries[0].stableCode": ["Equipment identifier is invalid."],
          },
        },
      );
    });
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => seededEquipment()),
      replaceEquipment,
    });
    const user = userEvent.setup();

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    await screen.findByTestId("profile-equipment");

    await user.click(screen.getByTestId("profile-equipment-remove-eq-blender"));
    await user.click(screen.getByTestId("profile-equipment-save"));

    await waitFor(() => expect(replaceEquipment).toHaveBeenCalledTimes(1));
    expect(
      screen.getByTestId("profile-equipment-error-summary"),
    ).toHaveTextContent("Equipment identifier is invalid.");
    expect(
      document.getElementById("profile-equipment-entry-eq-oven"),
    ).toBeInTheDocument();
    expect(
      screen.getByTestId("profile-equipment-entry-eq-oven"),
    ).toHaveTextContent("Equipment identifier is invalid.");
    await user.click(
      screen.getByTestId(
        "profile-equipment-error-summary-jump-profile-equipment-entry-eq-oven",
      ),
    );
    expect(screen.getByTestId("profile-equipment-entry-eq-oven")).toHaveFocus();
    expect(screen.getByTestId("profile-equipment-save")).not.toBeDisabled();
    expect(replaceEquipment).toHaveBeenCalledTimes(1);
  });

  it("provides visible, contextual names for equipment controls", async () => {
    const repo = createMockProfileRepo({
      getEquipment: vi.fn(async () => seededEquipment()),
    });

    render(
      renderProfileTree({
        repository: repo,
        children: <ProfileEquipmentPage />,
      }),
    );
    await screen.findByTestId("profile-equipment");

    expect(
      screen.getByRole("combobox", { name: /add equipment from catalog/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("textbox", { name: /capacity unit — oven/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("textbox", { name: /constraint note — oven/i }),
    ).toBeInTheDocument();
  });
});
