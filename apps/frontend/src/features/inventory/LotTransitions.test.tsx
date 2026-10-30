import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  InventoryApiError,
  type InventoryLotView,
} from "@/adapters/live/inventoryTypes";
import { ProductionInventoryDetail } from "./ProductionInventoryDetail";
import {
  allowedTransitions,
  supportsPartial,
  targetOptions,
} from "./lotTransitions";
import {
  createMockInventoryRepo,
  renderInventoryTree,
  sampleLot,
} from "./testUtils";

function lot(overrides: Partial<InventoryLotView> = {}): InventoryLotView {
  return { ...sampleLot, ...overrides };
}

describe("lifecycle transition rules (client mirror)", () => {
  it("offers open, move and freeze for a sealed pantry lot", () => {
    expect(allowedTransitions(lot())).toEqual(["Open", "Move", "Freeze"]);
  });

  it("offers only thawing for an opened lot in the freezer", () => {
    expect(
      allowedTransitions(
        lot({ storageLocation: "Freezer", packageState: "Opened" }),
      ),
    ).toEqual(["StartThaw"]);
  });

  it("offers finishing the thaw while thawing", () => {
    const thawing = lot({
      storageLocation: "Refrigerator",
      packageState: "Opened",
      lifecycle: { ...sampleLot.lifecycle, state: "Thawing" },
    });
    expect(allowedTransitions(thawing)).toEqual([
      "Move",
      "Freeze",
      "CompleteThaw",
    ]);
  });

  it("allows partial transitions only for measured lots and splittable kinds", () => {
    expect(supportsPartial("Freeze", lot())).toBe(true);
    expect(supportsPartial("Open", lot())).toBe(false);
    expect(
      supportsPartial(
        "Freeze",
        lot({ quantity: { kind: "qualitative", availability: "Available" } }),
      ),
    ).toBe(false);
  });

  it("never offers the freezer as a move or thaw destination", () => {
    expect(targetOptions("Move", lot())).toEqual(["Refrigerator"]);
    expect(
      targetOptions("StartThaw", lot({ storageLocation: "Freezer" })),
    ).toEqual(["Refrigerator", "Pantry"]);
    expect(targetOptions("Open", lot({ storageLocation: "Freezer" }))).toEqual([
      null,
    ]);
  });
});

describe("lot detail transitions", () => {
  function renderWith(repo = createMockInventoryRepo()) {
    return render(
      renderInventoryTree({
        repository: repo,
        initialPath: `/app/despensa/${sampleLot.lotId}`,
        children: <ProductionInventoryDetail />,
      }),
    );
  }

  it("freezes part of the lot and reports the new lot", async () => {
    const transitionLot = vi.fn(async () =>
      lot({
        quantity: { kind: "measured", value: 300, unit: "Gram" },
        etag: '"v2"',
      }),
    );
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => lot()),
      transitionLot,
    });
    renderWith(repo);

    const panel = await screen.findByTestId("inventory-transition");
    await userEvent.click(
      within(panel).getByTestId("inventory-transition-kind-Freeze"),
    );
    await userEvent.type(
      within(panel).getByTestId("inventory-transition-partial"),
      "200",
    );
    await userEvent.click(
      within(panel).getByTestId("inventory-transition-submit"),
    );

    await waitFor(() => expect(transitionLot).toHaveBeenCalledTimes(1));
    expect(transitionLot).toHaveBeenCalledWith(
      sampleLot.lotId,
      {
        kind: "Freeze",
        partialValue: 200,
        unit: "Gram",
        targetStorageLocation: null,
      },
      expect.objectContaining({ csrfToken: "csrf-test", etag: '"v1"' }),
    );
    expect(
      await screen.findByTestId("inventory-transition-notice"),
    ).toHaveTextContent("The part you separated is a new lot");
  });

  it("opens the whole package and moves it in one step", async () => {
    const transitionLot = vi.fn(async () => lot({ packageState: "Opened" }));
    renderWith(
      createMockInventoryRepo({
        getLot: vi.fn(async () => lot()),
        transitionLot,
      }),
    );

    const panel = await screen.findByTestId("inventory-transition");
    await userEvent.click(
      within(panel).getByTestId("inventory-transition-target-Refrigerator"),
    );
    await userEvent.click(
      within(panel).getByTestId("inventory-transition-submit"),
    );

    await waitFor(() =>
      expect(transitionLot).toHaveBeenCalledWith(
        sampleLot.lotId,
        {
          kind: "Open",
          partialValue: null,
          unit: null,
          targetStorageLocation: "Refrigerator",
        },
        expect.anything(),
      ),
    );
    expect(screen.queryByTestId("inventory-transition-notice")).toBeNull();
  });

  it("shows a localized error when the backend rejects the transition", async () => {
    const transitionLot = vi.fn(async () => {
      throw new InventoryApiError(
        "conflict",
        "Only a thawing lot can finish thawing.",
        422,
      );
    });
    renderWith(
      createMockInventoryRepo({
        getLot: vi.fn(async () => lot()),
        transitionLot,
      }),
    );

    await userEvent.click(
      await screen.findByTestId("inventory-transition-submit"),
    );

    expect(
      await screen.findByTestId("inventory-transition-error"),
    ).toHaveTextContent("Could not change the lot's state.");
  });

  it("shows the lifecycle state, timestamps and source lot of a derived lot", async () => {
    const derived = lot({
      storageLocation: "Freezer",
      lifecycle: {
        state: "Frozen",
        openedAt: null,
        frozenAt: "2026-10-28T10:00:00Z",
        thawStartedAt: null,
        thawedAt: null,
        parentLotId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
      },
    });
    renderWith(createMockInventoryRepo({ getLot: vi.fn(async () => derived) }));

    expect(
      await screen.findByTestId("inventory-lifecycle-state"),
    ).toHaveTextContent("Frozen");
    expect(
      screen.getByTestId("inventory-lifecycle-frozenAt"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("inventory-parent-lot")).toHaveAttribute(
      "href",
      "/app/despensa/cccccccc-cccc-cccc-cccc-cccccccccccc",
    );
  });

  it("labels transition history entries and links the related lot", async () => {
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => lot()),
      getHistory: vi.fn(async () => [
        {
          entryId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
          kind: "Transaction",
          type: "TransitionSplit",
          reasonCode: null,
          changedFields: null,
          occurredAt: "2026-10-28T10:00:00Z",
          transition: {
            kind: "Freeze",
            previousState: "AsAcquired",
            resultingState: "AsAcquired",
            previousStorageLocation: "Pantry",
            resultingStorageLocation: "Pantry",
            relatedLotId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
          },
        },
      ]),
    });
    renderWith(repo);

    const history = await screen.findByTestId("inventory-history");
    await waitFor(() => expect(history).toHaveTextContent("Part split off"));
    expect(history).toHaveTextContent("Freeze");
    expect(
      within(history).getByTestId("inventory-history-related-lot"),
    ).toHaveAttribute(
      "href",
      "/app/despensa/ffffffff-ffff-ffff-ffff-ffffffffffff",
    );
  });
});
