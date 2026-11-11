import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { RecipeApiError } from "@/contracts/recipes";
import type { PrivacyRepository } from "@/contracts/privacy";
import {
  ACCOUNT_DELETED_PATH,
  AccountDeletedPage,
  AccountDeletionSection,
} from "./AccountDeletionSection";
import { createMockProfileRepo, renderProfileTree } from "./testUtils";

vi.mock("@/features/notifications/pushBrowser", () => ({
  unsubscribeFromPush: vi.fn(async () => null),
}));

function repository(
  deleteAccount: PrivacyRepository["deleteAccount"],
): PrivacyRepository {
  return {
    getCenter: vi.fn(async () => ({ contactEmail: null })),
    downloadExport: vi.fn(),
    deleteAccount,
  };
}

function renderSection(
  deleteAccount: PrivacyRepository["deleteAccount"],
  leave = vi.fn(),
) {
  render(
    renderProfileTree({
      repository: createMockProfileRepo(),
      children: (
        <AccountDeletionSection
          repository={repository(deleteAccount)}
          leave={leave}
        />
      ),
    }),
  );
  return leave;
}

describe("AccountDeletionSection (PLAN-0049 Slice 2)", () => {
  it("says what goes and what stays, and deletes only after the word is typed", async () => {
    const deleteAccount = vi.fn(async () => undefined);
    const leave = renderSection(deleteAccount);
    const user = userEvent.setup();
    expect(
      await screen.findByText(/Stays: your login at the login provider/),
    ).toBeInTheDocument();
    expect(screen.getByText(/for 13 months/)).toBeInTheDocument();
    const button = screen.getByTestId("privacy-delete");
    expect(button).toBeDisabled();
    await user.type(screen.getByLabelText("To confirm, type DELETE"), "delet");
    expect(button).toBeDisabled();
    await user.type(screen.getByLabelText("To confirm, type DELETE"), "e");
    await user.click(button);

    expect(deleteAccount).toHaveBeenCalledWith("delete", "csrf-test");
    await vi.waitFor(() =>
      expect(leave).toHaveBeenCalledWith(ACCOUNT_DELETED_PATH),
    );
  });

  it("asks for a new sign-in when the last one is not recent, and stays on the page", async () => {
    const deleteAccount = vi.fn(async () => {
      throw new RecipeApiError("unexpected", "Sign in again.", 403);
    });
    const leave = renderSection(deleteAccount);
    const user = userEvent.setup();
    await user.type(await screen.findByTestId("privacy-delete-word"), "DELETE");
    await user.click(screen.getByTestId("privacy-delete"));
    expect(
      await screen.findByTestId("privacy-delete-reauth"),
    ).toHaveTextContent("For your safety, sign in again");
    expect(
      screen.getByRole("button", { name: "Sign in again" }),
    ).toBeInTheDocument();
    expect(leave).not.toHaveBeenCalled();
  });

  it("says when the deletion failed", async () => {
    renderSection(
      vi.fn(async () => {
        throw new Error("network");
      }),
    );
    const user = userEvent.setup();
    await user.type(await screen.findByTestId("privacy-delete-word"), "DELETE");
    await user.click(screen.getByTestId("privacy-delete"));
    expect(
      await screen.findByText("I could not delete the account. Try again."),
    ).toBeInTheDocument();
  });

  it("tells a deleted person that the login stays and a new sign-in starts empty", () => {
    render(
      renderProfileTree({
        repository: createMockProfileRepo(),
        children: <AccountDeletedPage />,
      }),
    );
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(
      "Your account was deleted",
    );
    expect(screen.getByText(/an empty account starts/)).toBeInTheDocument();
  });
});
