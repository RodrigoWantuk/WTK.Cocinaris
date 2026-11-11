import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RecipeApiError } from "@/contracts/recipes";
import type { PrivacyRepository } from "@/contracts/privacy";
import { ProfilePrivacyPage } from "./ProfilePrivacyPage";
import { createMockProfileRepo, renderProfileTree } from "./testUtils";

function privacy(
  overrides: Partial<PrivacyRepository> = {},
): PrivacyRepository {
  return {
    getCenter: vi.fn(async () => ({ contactEmail: null })),
    downloadExport: vi.fn(async () => ({
      blob: new Blob(["zip"], { type: "application/zip" }),
      fileName: "cocinaris-dados-2026-11-11.zip",
    })),
    deleteAccount: vi.fn(async () => undefined),
    ...overrides,
  };
}

function renderPage(repository: PrivacyRepository) {
  render(
    renderProfileTree({
      repository: createMockProfileRepo(),
      children: <ProfilePrivacyPage repository={repository} />,
    }),
  );
}

describe("ProfilePrivacyPage (PLAN-0049)", () => {
  afterEach(() => vi.restoreAllMocks());

  it("says what is kept, who receives a part, and where each part is corrected", async () => {
    renderPage(privacy());
    expect(await screen.findByRole("heading", { level: 1 })).toHaveTextContent(
      "Your data",
    );
    // Allergies and restrictions are named as sensitive.
    expect(screen.getByTestId("privacy-kept-restrictions")).toHaveTextContent(
      "sensitive data",
    );
    expect(
      screen.getByText(/DeepSeek, for recipes and menus/),
    ).toBeInTheDocument();
    expect(screen.getByTestId("privacy-correct-preferences")).toHaveAttribute(
      "href",
      "/app/perfil/preferencias",
    );
    expect(screen.getByTestId("privacy-correct-pantry")).toHaveAttribute(
      "href",
      "/app/despensa",
    );
    // Deleting needs the typed word first.
    expect(screen.getByTestId("privacy-delete")).toBeDisabled();
    expect(screen.queryByTestId("privacy-contact")).toBeNull();
  });

  it("downloads the ZIP in the page's language and saves it under the server's name", async () => {
    const repository = privacy({
      getCenter: vi.fn(async () => ({
        contactEmail: "privacidade@example.com",
      })),
    });
    const createObjectURL = vi.fn(() => "blob:export");
    Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() });
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(() => undefined);
    renderPage(repository);
    const user = userEvent.setup();
    expect(await screen.findByTestId("privacy-contact")).toHaveTextContent(
      "privacidade@example.com",
    );
    await user.click(screen.getByTestId("privacy-download"));

    expect(repository.downloadExport).toHaveBeenCalledWith(
      "en",
      expect.any(AbortSignal),
    );
    await waitFor(() => expect(click).toHaveBeenCalled());
    const anchor = click.mock.instances[0] as unknown as HTMLAnchorElement;
    expect(anchor.download).toBe("cocinaris-dados-2026-11-11.zip");
    expect(screen.getByTestId("privacy-download-status")).toHaveTextContent(
      "The file is being saved",
    );
  });

  it("says when the hourly limit is reached, and when the file could not be made", async () => {
    const downloadExport = vi
      .fn()
      .mockRejectedValueOnce(
        new RecipeApiError("rate_limit_exceeded" as never, "limited", 429),
      )
      .mockRejectedValueOnce(new Error("network"));
    renderPage(privacy({ downloadExport }));
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("privacy-download"));
    expect(
      await screen.findByText(
        "You already downloaded three times this hour. Try again later.",
      ),
    ).toBeInTheDocument();
    await user.click(screen.getByTestId("privacy-download"));
    expect(
      await screen.findByText("I could not prepare the file. Try again."),
    ).toBeInTheDocument();
  });
});
