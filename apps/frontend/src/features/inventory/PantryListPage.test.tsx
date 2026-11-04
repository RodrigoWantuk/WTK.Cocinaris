import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { InventoryProvider } from "./InventoryProvider";
import { PantryListPage } from "./PantryListPage";
import {
  createMockInventoryRepo,
  createSessionAdapter,
  sampleLot,
} from "./testUtils";

function renderPage(repo: ReturnType<typeof createMockInventoryRepo>) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider repository={repo}>
          <MemoryRouter initialEntries={["/app/despensa/lista"]}>
            <Routes>
              <Route path="/app/despensa/lista" element={<PantryListPage />} />
              <Route path="/app/despensa" element={<p>pantry</p>} />
            </Routes>
          </MemoryRouter>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

const aiLine = {
  lineNumber: 1,
  original: "ARROZ TP1 5KG",
  name: "Arroz",
  quantity: 5000,
  unit: "Gram" as const,
  storageLocation: "Pantry" as const,
  recognized: true,
  uncertain: true,
};

describe("pantry AI readings (PLAN-0046)", () => {
  it("hides the photo when the deployment does not read receipts", async () => {
    const repo = createMockInventoryRepo();
    repo.parseList = vi.fn(async () => []);
    repo.isReceiptReadingAvailable = vi.fn(async () => false);
    renderPage(repo);
    await waitFor(() =>
      expect(repo.isReceiptReadingAvailable).toHaveBeenCalled(),
    );
    expect(screen.queryByTestId("pantry-list-photo")).toBeNull();
  });

  it("reads a photo into the same review, marks doubts, and saves only on confirmation", async () => {
    const user = userEvent.setup();
    const repo = createMockInventoryRepo();
    repo.parseList = vi.fn(async () => []);
    repo.isReceiptReadingAvailable = vi.fn(async () => true);
    repo.parseReceipt = vi.fn(async () => [aiLine]);
    repo.createLot = vi.fn(async () => sampleLot);
    renderPage(repo);
    await screen.findByTestId("pantry-list-photo");
    expect(screen.getByText(/read once by an AI service/i)).toBeInTheDocument();

    const photo = new File(["x"], "nota.jpg", { type: "image/jpeg" });
    await user.upload(screen.getByTestId("pantry-list-photo-input"), photo);
    expect(await screen.findByText("Check this one")).toBeInTheDocument();
    expect(screen.getByTestId("pantry-list-name")).toHaveValue("Arroz");
    expect(repo.createLot).not.toHaveBeenCalled();

    await user.click(screen.getByTestId("pantry-list-save"));
    await screen.findByText("pantry");
    expect(repo.createLot).toHaveBeenCalledTimes(1);
  });

  it("explains a failed photo and refuses one that is too large without sending it", async () => {
    const user = userEvent.setup();
    const repo = createMockInventoryRepo();
    repo.parseList = vi.fn(async () => []);
    repo.isReceiptReadingAvailable = vi.fn(async () => true);
    repo.parseReceipt = vi.fn(async () => {
      throw new Error("down");
    });
    renderPage(repo);
    await screen.findByTestId("pantry-list-photo");
    const input = screen.getByTestId("pantry-list-photo-input");
    await user.upload(
      input,
      new File(["x"], "nota.jpg", { type: "image/jpeg" }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(/discarded/i);

    const large = new File(["x"], "grande.jpg", { type: "image/jpeg" });
    Object.defineProperty(large, "size", { value: 13 * 1024 * 1024 });
    await user.upload(input, large);
    expect(await screen.findByRole("alert")).toHaveTextContent(/12 MB/);
    expect(repo.parseReceipt).toHaveBeenCalledTimes(1);
  });

  it("organizes a list with AI on request and keeps the text when it fails", async () => {
    const user = userEvent.setup();
    const repo = createMockInventoryRepo();
    repo.parseList = vi.fn(async () => []);
    repo.parseListWithAi = vi
      .fn()
      .mockRejectedValueOnce(new Error("down"))
      .mockResolvedValueOnce([aiLine]);
    renderPage(repo);
    await user.type(screen.getByTestId("pantry-list-text"), "umas batatas");
    await user.click(screen.getByTestId("pantry-list-read-ai"));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByTestId("pantry-list-text")).toHaveValue("umas batatas");
    expect(repo.parseList).not.toHaveBeenCalled();

    await user.click(screen.getByTestId("pantry-list-read-ai"));
    expect(await screen.findByTestId("pantry-list-row")).toBeInTheDocument();
  });
});

describe("pantry list entry (PLAN-0046)", () => {
  it("reads the list, lets the person fix each line, and saves one item per row", async () => {
    const user = userEvent.setup();
    const repo = createMockInventoryRepo();
    repo.parseList = vi.fn(async () => [
      {
        lineNumber: 1,
        original: "2 kg de arroz",
        name: "Arroz",
        quantity: 2000,
        unit: "Gram" as const,
        storageLocation: "Pantry" as const,
        recognized: true,
      },
      {
        lineNumber: 2,
        original: "manteiga",
        name: "Manteiga",
        quantity: null,
        unit: null,
        storageLocation: "Refrigerator" as const,
        recognized: true,
      },
      {
        lineNumber: 3,
        original: "xyz",
        name: "Xyz",
        quantity: null,
        unit: null,
        storageLocation: "Pantry" as const,
        recognized: false,
      },
    ]);
    repo.createLot = vi.fn(async () => sampleLot);
    renderPage(repo);

    await user.type(screen.getByTestId("pantry-list-text"), "2 kg de arroz");
    await user.click(screen.getByTestId("pantry-list-read"));
    const rows = await screen.findAllByTestId("pantry-list-row");
    expect(rows).toHaveLength(3);
    expect(within(rows[0]!).getByTestId("pantry-list-amount")).toHaveValue(
      "2,000",
    );

    // The person drops the line that is not food and moves the butter to the freezer.
    await user.click(
      within(rows[2]!).getByRole("button", { name: "Remove Xyz" }),
    );
    await user.click(within(rows[1]!).getByRole("button", { name: "Freezer" }));
    await user.click(screen.getByTestId("pantry-list-save"));

    await waitFor(() => expect(repo.createLot).toHaveBeenCalledTimes(2));
    expect(repo.createLot).toHaveBeenNthCalledWith(
      1,
      {
        productName: "Arroz",
        quantity: { kind: "measured", value: 2000, unit: "Gram" },
        storageLocation: "Pantry",
      },
      expect.objectContaining({ idempotencyKey: expect.any(String) }),
    );
    expect(repo.createLot).toHaveBeenNthCalledWith(
      2,
      {
        productName: "Manteiga",
        quantity: { kind: "qualitative", availability: "Available" },
        storageLocation: "Freezer",
      },
      expect.anything(),
    );
    expect(await screen.findByText("pantry")).toBeInTheDocument();
  });

  it("keeps failed rows to retry and never saves the ones already added again", async () => {
    const user = userEvent.setup();
    const repo = createMockInventoryRepo();
    repo.parseList = vi.fn(async () => [
      {
        lineNumber: 1,
        original: "arroz",
        name: "Arroz",
        quantity: null,
        unit: null,
        storageLocation: "Pantry" as const,
        recognized: true,
      },
      {
        lineNumber: 2,
        original: "feijão",
        name: "Feijão",
        quantity: null,
        unit: null,
        storageLocation: "Pantry" as const,
        recognized: true,
      },
    ]);
    repo.createLot = vi
      .fn()
      .mockResolvedValueOnce(sampleLot)
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValue(sampleLot);
    renderPage(repo);

    await user.type(screen.getByTestId("pantry-list-text"), "arroz");
    await user.click(screen.getByTestId("pantry-list-read"));
    await screen.findAllByTestId("pantry-list-row");
    await user.click(screen.getByTestId("pantry-list-save"));
    expect(
      await screen.findByText(/Some items were not added/),
    ).toBeInTheDocument();

    await user.click(screen.getByTestId("pantry-list-save"));
    await waitFor(() => expect(repo.createLot).toHaveBeenCalledTimes(3));
    expect(repo.createLot).toHaveBeenLastCalledWith(
      expect.objectContaining({ productName: "Feijão" }),
      expect.anything(),
    );
  });
});
