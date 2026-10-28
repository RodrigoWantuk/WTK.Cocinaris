import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProductionInventoryForm } from "./ProductionInventoryForm";
import { InventoryApiError } from "@/adapters/live/inventoryTypes";
import {
  createMockInventoryRepo,
  createSessionAdapter,
  sampleLot,
} from "./testUtils";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { InventoryProvider } from "./InventoryProvider";
import { MemoryRouter, Route, Routes } from "react-router-dom";

function renderForm(
  mode: "create" | "edit",
  repo = createMockInventoryRepo(),
  locale: "en" | "pt-BR" | "es" = "en",
) {
  return render(
    <ProductionI18nProvider initialLocale={locale}>
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider repository={repo}>
          <MemoryRouter
            initialEntries={[
              mode === "create"
                ? "/app/despensa/novo"
                : `/app/despensa/${sampleLot.lotId}/editar`,
            ]}
          >
            <Routes>
              <Route
                path="/app/despensa/novo"
                element={<ProductionInventoryForm mode="create" />}
              />
              <Route
                path="/app/despensa/:lotId/editar"
                element={<ProductionInventoryForm mode="edit" />}
              />
              <Route path="/app/despensa/:lotId" element={<div>detail</div>} />
            </Routes>
          </MemoryRouter>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("ProductionInventoryForm", () => {
  it("creates measured lots with locale decimals and idempotency key", async () => {
    const user = userEvent.setup();
    const createLot = vi.fn(async (_input, options) => {
      expect(options.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/i);
      return sampleLot;
    });
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo, "pt-BR");

    await user.type(screen.getByTestId("inventory-product-name"), "Arroz");
    await user.type(screen.getByTestId("inventory-amount"), "1.250,5");
    await user.click(screen.getByTestId("inventory-save"));

    await waitFor(() => expect(createLot).toHaveBeenCalled());
    expect(createLot).toHaveBeenCalledWith(
      expect.objectContaining({
        productName: "Arroz",
        quantity: { kind: "measured", value: 1250.5, unit: "Gram" },
        customLocation: null,
      }),
      expect.objectContaining({ csrfToken: "csrf-test" }),
    );
  });

  it("requires custom location for Other and sends it on create", async () => {
    const user = userEvent.setup();
    const createLot = vi.fn(async () => sampleLot);
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo);

    await user.type(screen.getByTestId("inventory-product-name"), "Herbs");
    await user.type(screen.getByTestId("inventory-amount"), "1");
    await user.selectOptions(screen.getByTestId("inventory-location"), "Other");
    await user.click(screen.getByTestId("inventory-save"));
    expect(await screen.findByTestId("inventory-form-error")).toHaveTextContent(
      /custom location/i,
    );

    await user.type(
      screen.getByTestId("inventory-custom-location"),
      "Garage shelf",
    );
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() =>
      expect(createLot).toHaveBeenCalledWith(
        expect.objectContaining({
          storageLocation: "Other",
          customLocation: "Garage shelf",
        }),
        expect.any(Object),
      ),
    );
  });

  it("edits metadata with ETag and surfaces 412 without silent retry", async () => {
    const user = userEvent.setup();
    const updateLot = vi
      .fn()
      .mockRejectedValueOnce(
        new InventoryApiError("precondition_failed", "stale", 412),
      );
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => sampleLot),
      updateLot,
    });
    renderForm("edit", repo);
    expect(await screen.findByDisplayValue("Rice")).toBeInTheDocument();
    await user.clear(screen.getByTestId("inventory-product-name"));
    await user.type(screen.getByTestId("inventory-product-name"), "Brown rice");
    await user.click(screen.getByTestId("inventory-save"));
    expect(
      await screen.findByTestId("inventory-form-conflict"),
    ).toBeInTheDocument();
    expect(updateLot).toHaveBeenCalledTimes(1);
    expect(updateLot).toHaveBeenCalledWith(
      sampleLot.lotId,
      expect.any(Object),
      expect.objectContaining({ etag: '"v1"' }),
    );
  });

  it("maps customLocation field errors from the backend", async () => {
    const user = userEvent.setup();
    const createLot = vi.fn(async () => {
      throw new InventoryApiError("validation_failed", "invalid", 400, {
        customLocation: ["Custom location is invalid."],
      });
    });
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo);
    await user.type(screen.getByTestId("inventory-product-name"), "X");
    await user.type(screen.getByTestId("inventory-amount"), "1");
    await user.selectOptions(screen.getByTestId("inventory-location"), "Other");
    await user.type(screen.getByTestId("inventory-custom-location"), "bad");
    await user.click(screen.getByTestId("inventory-save"));
    expect(await screen.findByTestId("inventory-form-error")).toHaveTextContent(
      /Custom location is invalid/,
    );
    expect(screen.getByTestId("inventory-custom-location")).toHaveAttribute(
      "aria-invalid",
      "true",
    );
  });

  it("reuses the same create idempotency key after transport failure", async () => {
    const user = userEvent.setup();
    const createLot = vi
      .fn()
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockResolvedValueOnce(sampleLot);
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo);

    await user.type(screen.getByTestId("inventory-product-name"), "Arroz");
    await user.type(screen.getByTestId("inventory-amount"), "1");
    await user.click(screen.getByTestId("inventory-save"));
    expect(
      await screen.findByTestId("inventory-form-error"),
    ).toBeInTheDocument();

    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(2));
    const firstKey = createLot.mock.calls[0][1].idempotencyKey;
    const secondKey = createLot.mock.calls[1][1].idempotencyKey;
    expect(firstKey).toMatch(/^[0-9a-f-]{36}$/i);
    expect(secondKey).toBe(firstKey);
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });

  it("reuses create key for equivalent pt-BR decimal text after ambiguous failure", async () => {
    const user = userEvent.setup();
    const createLot = vi
      .fn()
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo, "pt-BR");

    await user.type(screen.getByTestId("inventory-product-name"), "Arroz");
    await user.type(screen.getByTestId("inventory-amount"), "1,0");
    await user.click(screen.getByTestId("inventory-save"));
    await screen.findByTestId("inventory-form-error");

    await user.clear(screen.getByTestId("inventory-amount"));
    await user.type(screen.getByTestId("inventory-amount"), "1,00");
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(2));
    expect(createLot.mock.calls[0][1].idempotencyKey).toBe(
      createLot.mock.calls[1][1].idempotencyKey,
    );
    expect(createLot.mock.calls[0][0].quantity).toEqual({
      kind: "measured",
      value: 1,
      unit: "Gram",
    });
    expect(createLot.mock.calls[1][0].quantity).toEqual({
      kind: "measured",
      value: 1,
      unit: "Gram",
    });
  });

  it("reuses create key for equivalent en decimal text after ambiguous failure", async () => {
    const user = userEvent.setup();
    const createLot = vi
      .fn()
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo, "en");

    await user.type(screen.getByTestId("inventory-product-name"), "Rice");
    await user.type(screen.getByTestId("inventory-amount"), "1.0");
    await user.click(screen.getByTestId("inventory-save"));
    await screen.findByTestId("inventory-form-error");

    await user.clear(screen.getByTestId("inventory-amount"));
    await user.type(screen.getByTestId("inventory-amount"), "1.00");
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(2));
    expect(createLot.mock.calls[0][1].idempotencyKey).toBe(
      createLot.mock.calls[1][1].idempotencyKey,
    );
  });

  it("ignores inactive measured fields while qualitative create attempt is open", async () => {
    const user = userEvent.setup();
    const createLot = vi
      .fn()
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo);

    await user.type(screen.getByTestId("inventory-product-name"), "Herbs");
    await user.type(screen.getByTestId("inventory-amount"), "5");
    await user.click(
      screen.getByRole("radio", { name: /qualitative|Qualitative/i }),
    );
    await user.click(screen.getByTestId("inventory-save"));
    await screen.findByTestId("inventory-form-error");
    const firstKey = createLot.mock.calls[0][1].idempotencyKey;

    // Change measured fields while inactive, then return to qualitative.
    await user.click(screen.getByRole("radio", { name: /measured|Measured/i }));
    await user.clear(screen.getByTestId("inventory-amount"));
    await user.type(screen.getByTestId("inventory-amount"), "999");
    await user.click(
      screen.getByRole("radio", { name: /qualitative|Qualitative/i }),
    );
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(2));
    expect(createLot.mock.calls[1][1].idempotencyKey).toBe(firstKey);
    expect(createLot.mock.calls[1][0].quantity).toEqual({
      kind: "qualitative",
      availability: "Available",
    });
  });

  it("ignores inactive availability while measured create attempt is open", async () => {
    const user = userEvent.setup();
    const createLot = vi
      .fn()
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo);

    await user.click(
      screen.getByRole("radio", { name: /qualitative|Qualitative/i }),
    );
    await user.selectOptions(
      screen.getByLabelText(/Availability|Disponibilidad/i),
      "Low",
    );
    await user.click(screen.getByRole("radio", { name: /measured|Measured/i }));
    await user.type(screen.getByTestId("inventory-product-name"), "Rice");
    await user.type(screen.getByTestId("inventory-amount"), "1");
    await user.click(screen.getByTestId("inventory-save"));
    await screen.findByTestId("inventory-form-error");
    const firstKey = createLot.mock.calls[0][1].idempotencyKey;

    await user.click(
      screen.getByRole("radio", { name: /qualitative|Qualitative/i }),
    );
    await user.selectOptions(
      screen.getByLabelText(/Availability|Disponibilidad/i),
      "Unavailable",
    );
    await user.click(screen.getByRole("radio", { name: /measured|Measured/i }));
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(2));
    expect(createLot.mock.calls[1][1].idempotencyKey).toBe(firstKey);
    expect(createLot.mock.calls[1][0].quantity).toEqual({
      kind: "measured",
      value: 1,
      unit: "Gram",
    });
  });

  it("issues a new create idempotency key after material form changes", async () => {
    const user = userEvent.setup();
    const createLot = vi
      .fn()
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo);

    await user.type(screen.getByTestId("inventory-product-name"), "Arroz");
    await user.type(screen.getByTestId("inventory-amount"), "1");
    await user.click(screen.getByTestId("inventory-save"));
    await screen.findByTestId("inventory-form-error");

    await user.clear(screen.getByTestId("inventory-amount"));
    await user.type(screen.getByTestId("inventory-amount"), "2");
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(2));
    expect(createLot.mock.calls[0][1].idempotencyKey).not.toBe(
      createLot.mock.calls[1][1].idempotencyKey,
    );
  });

  it("does not rotate create key for trim-equivalent optional fields", async () => {
    const user = userEvent.setup();
    const createLot = vi
      .fn()
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const repo = createMockInventoryRepo({ createLot });
    renderForm("create", repo);

    await user.type(screen.getByTestId("inventory-product-name"), "  Rice  ");
    await user.type(screen.getByTestId("inventory-amount"), "1");
    await user.click(screen.getByTestId("inventory-save"));
    await screen.findByTestId("inventory-form-error");
    const firstKey = createLot.mock.calls[0][1].idempotencyKey;
    expect(createLot.mock.calls[0][0].productName).toBe("Rice");

    await user.clear(screen.getByTestId("inventory-product-name"));
    await user.type(screen.getByTestId("inventory-product-name"), "Rice");
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(2));
    expect(createLot.mock.calls[1][1].idempotencyKey).toBe(firstKey);
  });

  it("does not reuse create idempotency key after confirmed success", async () => {
    const user = userEvent.setup();
    const createLot = vi
      .fn()
      .mockResolvedValueOnce({ ...sampleLot, lotId: "lot-a" })
      .mockResolvedValueOnce({ ...sampleLot, lotId: "lot-b" });
    const repo = createMockInventoryRepo({ createLot });
    const { unmount } = renderForm("create", repo);

    await user.type(screen.getByTestId("inventory-product-name"), "Arroz");
    await user.type(screen.getByTestId("inventory-amount"), "1");
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(1));
    const firstKey = createLot.mock.calls[0][1].idempotencyKey;
    unmount();

    renderForm("create", repo);
    await user.type(screen.getByTestId("inventory-product-name"), "Arroz");
    await user.type(screen.getByTestId("inventory-amount"), "1");
    await user.click(screen.getByTestId("inventory-save"));
    await waitFor(() => expect(createLot).toHaveBeenCalledTimes(2));
    expect(createLot.mock.calls[1][1].idempotencyKey).not.toBe(firstKey);
  });

  it("loads edit mode successfully and blocks submit before ready", async () => {
    let resolveLot!: (lot: typeof sampleLot) => void;
    const getLot = vi.fn(
      () =>
        new Promise<typeof sampleLot>((resolve) => {
          resolveLot = resolve;
        }),
    );
    const updateLot = vi.fn(async () => sampleLot);
    const repo = createMockInventoryRepo({ getLot, updateLot });
    renderForm("edit", repo);

    expect(screen.getByTestId("inventory-edit-loading")).toBeInTheDocument();
    expect(screen.queryByTestId("inventory-save")).not.toBeInTheDocument();

    resolveLot(sampleLot);
    expect(await screen.findByDisplayValue("Rice")).toBeInTheDocument();
    expect(screen.getByTestId("inventory-save")).not.toBeDisabled();
  });

  it("shows not-found state for edit 404", async () => {
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => {
        throw new InventoryApiError("not_found", "missing", 404);
      }),
    });
    renderForm("edit", repo);
    expect(
      await screen.findByTestId("inventory-edit-not-found"),
    ).toBeInTheDocument();
    expect(
      screen.queryByTestId("production-inventory-form"),
    ).not.toBeInTheDocument();
  });

  it("shows unavailable edit error and retries successfully", async () => {
    const user = userEvent.setup();
    const getLot = vi
      .fn()
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockResolvedValueOnce(sampleLot);
    const repo = createMockInventoryRepo({ getLot });
    renderForm("edit", repo);
    expect(
      await screen.findByTestId("inventory-edit-error"),
    ).toBeInTheDocument();
    await user.click(screen.getByTestId("inventory-edit-retry"));
    expect(await screen.findByDisplayValue("Rice")).toBeInTheDocument();
    expect(getLot).toHaveBeenCalledTimes(2);
  });
});
