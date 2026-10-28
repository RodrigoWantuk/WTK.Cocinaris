import { useState } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  createMemoryRouter,
  Link,
  Outlet,
  RouterProvider,
} from "react-router-dom";
import {
  UnsavedChangesCoordinatorProvider,
  useRegisterUnsavedChanges,
} from "./UnsavedChangesCoordinator";
import { useProfileWorkspace } from "./ProfileProvider";
import {
  createMockProfileRepo,
  createConfirmedProfileSnapshot,
  createSessionAdapter,
  renderProfileTree,
} from "./testUtils";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { ProfileApiError } from "@/contracts/profile";
import { describeProfileMutationError } from "./describeProfileMutationError";

function DirtyEditor() {
  const [dirty, setDirty] = useState(false);
  useRegisterUnsavedChanges(dirty, () => setDirty(false));
  return (
    <div>
      <button
        type="button"
        data-testid="make-dirty"
        onClick={() => setDirty(true)}
      >
        dirty
      </button>
      <button
        type="button"
        data-testid="make-clean"
        onClick={() => setDirty(false)}
      >
        clean
      </button>
      <span data-testid="dirty-flag">{String(dirty)}</span>
      <Link to="/app/hoje" data-testid="link-home">
        Home
      </Link>
      <Link to="/app/despensa" data-testid="link-inventory">
        Inventory
      </Link>
      <Link to="/app/hoje" data-testid="brand-link">
        Brand
      </Link>
    </div>
  );
}

function renderCoordinatorTree(initialPath = "/app/perfil/dados") {
  const router = createMemoryRouter(
    [
      {
        path: "/app/perfil",
        element: (
          <UnsavedChangesCoordinatorProvider>
            <Outlet />
          </UnsavedChangesCoordinatorProvider>
        ),
        children: [{ path: "dados", element: <DirtyEditor /> }],
      },
      {
        path: "/app/hoje",
        element: <div data-testid="arrived-home">home</div>,
      },
      {
        path: "/app/despensa",
        element: <div data-testid="arrived-inventory">inventory</div>,
      },
    ],
    { initialEntries: [initialPath] },
  );
  return render(
    <ProductionI18nProvider initialLocale="en">
      <RouterProvider router={router} />
    </ProductionI18nProvider>,
  );
}

describe("UnsavedChangesCoordinator shell navigation", () => {
  it("opens confirmation for Home when dirty and Stay keeps the draft", async () => {
    const user = userEvent.setup();
    renderCoordinatorTree();
    await user.click(screen.getByTestId("make-dirty"));
    await user.click(screen.getByTestId("link-home"));
    expect(await screen.findByTestId("profile-unsaved-dialog")).toBeVisible();
    await user.click(screen.getByTestId("profile-unsaved-stay"));
    expect(screen.queryByTestId("arrived-home")).not.toBeInTheDocument();
    expect(screen.getByTestId("dirty-flag")).toHaveTextContent("true");
  });

  it("discards and navigates to Inventory when confirmed", async () => {
    const user = userEvent.setup();
    renderCoordinatorTree();
    await user.click(screen.getByTestId("make-dirty"));
    await user.click(screen.getByTestId("link-inventory"));
    await user.click(await screen.findByTestId("profile-unsaved-discard"));
    expect(await screen.findByTestId("arrived-inventory")).toBeInTheDocument();
  });

  it("opens confirmation for brand navigation when dirty", async () => {
    const user = userEvent.setup();
    renderCoordinatorTree();
    await user.click(screen.getByTestId("make-dirty"));
    await user.click(screen.getByTestId("brand-link"));
    expect(await screen.findByTestId("profile-unsaved-dialog")).toBeVisible();
  });

  it("navigates immediately when clean", async () => {
    const user = userEvent.setup();
    renderCoordinatorTree();
    await user.click(screen.getByTestId("link-home"));
    expect(await screen.findByTestId("arrived-home")).toBeInTheDocument();
    expect(
      screen.queryByTestId("profile-unsaved-dialog"),
    ).not.toBeInTheDocument();
  });
});

describe("UnsavedChangesCoordinator history", () => {
  it("Back while dirty opens one confirmation; Stay keeps route; Discard leaves once", async () => {
    const user = userEvent.setup();
    const router = createMemoryRouter(
      [
        {
          path: "/app/perfil",
          element: (
            <UnsavedChangesCoordinatorProvider>
              <Outlet />
            </UnsavedChangesCoordinatorProvider>
          ),
          children: [{ path: "dados", element: <DirtyEditor /> }],
        },
        {
          path: "/app/hoje",
          element: <div data-testid="arrived-home">home</div>,
        },
      ],
      { initialEntries: ["/app/hoje", "/app/perfil/dados"] },
    );
    render(
      <ProductionI18nProvider initialLocale="en">
        <RouterProvider router={router} />
      </ProductionI18nProvider>,
    );

    await user.click(screen.getByTestId("make-dirty"));
    await act(async () => {
      router.navigate(-1);
    });
    expect(await screen.findByTestId("profile-unsaved-dialog")).toBeVisible();
    await user.click(screen.getByTestId("profile-unsaved-stay"));
    await waitFor(() =>
      expect(
        screen.queryByTestId("profile-unsaved-dialog"),
      ).not.toBeInTheDocument(),
    );
    expect(screen.queryByTestId("arrived-home")).not.toBeInTheDocument();
    expect(screen.getByTestId("dirty-flag")).toHaveTextContent("true");

    await act(async () => {
      router.navigate(-1);
    });
    expect(await screen.findByTestId("profile-unsaved-dialog")).toBeVisible();
    await user.click(screen.getByTestId("profile-unsaved-discard"));
    expect(await screen.findByTestId("arrived-home")).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.queryByTestId("profile-unsaved-dialog"),
      ).not.toBeInTheDocument(),
    );
  });

  it("saving (clean) removes the guard so Back does not prompt", async () => {
    const user = userEvent.setup();
    const router = createMemoryRouter(
      [
        {
          path: "/app/perfil",
          element: (
            <UnsavedChangesCoordinatorProvider>
              <Outlet />
            </UnsavedChangesCoordinatorProvider>
          ),
          children: [{ path: "dados", element: <DirtyEditor /> }],
        },
        {
          path: "/app/hoje",
          element: <div data-testid="arrived-home">home</div>,
        },
      ],
      { initialEntries: ["/app/hoje", "/app/perfil/dados"] },
    );
    render(
      <ProductionI18nProvider initialLocale="en">
        <RouterProvider router={router} />
      </ProductionI18nProvider>,
    );
    await user.click(screen.getByTestId("make-dirty"));
    await user.click(screen.getByTestId("make-clean"));
    await act(async () => {
      router.navigate(-1);
    });
    expect(await screen.findByTestId("arrived-home")).toBeInTheDocument();
    expect(
      screen.queryByTestId("profile-unsaved-dialog"),
    ).not.toBeInTheDocument();
  });
});

describe("describeProfileMutationError", () => {
  it("maps workspace_not_ready to actionable copy, not the generic save error", () => {
    const t = (key: string) => {
      const map: Record<string, string> = {
        "profile.error.workspaceNotReady":
          "The profile is not ready for changes. Reload before making another change.",
        "profile.error.save": "Could not save this change.",
      };
      return map[key] ?? key;
    };
    const message = describeProfileMutationError(
      new ProfileApiError("workspace_not_ready", "raw", 409),
      t as never,
    );
    expect(message).toContain("Reload before making another change");
    expect(message).not.toBe("Could not save this change.");
  });
});

describe("ProfileProvider post-save and session refresh UX", () => {
  it("keeps saveRefreshFailed visible across a failed reload retry", async () => {
    const getProfile = vi
      .fn()
      .mockResolvedValueOnce(createConfirmedProfileSnapshot())
      .mockRejectedValueOnce(
        new ProfileApiError("unavailable", "backend hiccup", 503),
      )
      .mockRejectedValueOnce(
        new ProfileApiError("unavailable", "still down", 503),
      )
      .mockResolvedValue(createConfirmedProfileSnapshot());
    const patchProfile = vi.fn(async () => createConfirmedProfileSnapshot());
    const repo = createMockProfileRepo({ getProfile, patchProfile });

    let captured: ReturnType<typeof useProfileWorkspace> | null = null;
    function Capture() {
      captured = useProfileWorkspace();
      return (
        <div>
          <span data-testid="save-refresh-failed">
            {String(captured.saveRefreshFailed)}
          </span>
          <span data-testid="can-mutate">{String(captured.canMutate)}</span>
        </div>
      );
    }

    render(renderProfileTree({ repository: repo, children: <Capture /> }));
    await waitFor(() =>
      expect(screen.getByTestId("can-mutate")).toHaveTextContent("true"),
    );

    await act(async () => {
      await captured!.patchProfile({
        displayName: { action: "confirm", value: "Bea" },
      });
    });
    expect(screen.getByTestId("save-refresh-failed")).toHaveTextContent("true");
    expect(screen.getByTestId("can-mutate")).toHaveTextContent("false");

    await act(async () => {
      await captured!.reload();
    });
    expect(screen.getByTestId("save-refresh-failed")).toHaveTextContent("true");

    await act(async () => {
      await captured!.reload();
    });
    await waitFor(() =>
      expect(screen.getByTestId("save-refresh-failed")).toHaveTextContent(
        "false",
      ),
    );
    expect(screen.getByTestId("can-mutate")).toHaveTextContent("true");
  });

  it("surfaces sessionRefreshWarning when session refresh fails after a successful save", async () => {
    const getSession = vi
      .fn()
      .mockResolvedValueOnce({
        status: "authenticated",
        internalUserId: "11111111-1111-1111-1111-111111111111",
        csrfToken: "csrf-test",
        displayName: "Ada",
        timeZone: "UTC",
        supportedLocales: ["en", "pt-BR", "es"],
      })
      .mockResolvedValueOnce({
        status: "unavailable",
        internalUserId: null,
        csrfToken: null,
      })
      .mockResolvedValue({
        status: "authenticated",
        internalUserId: "11111111-1111-1111-1111-111111111111",
        csrfToken: "csrf-test",
        displayName: "Ada",
        timeZone: "UTC",
        supportedLocales: ["en", "pt-BR", "es"],
      });
    const sessionAdapter = createSessionAdapter();
    sessionAdapter.getSession = getSession;

    const repo = createMockProfileRepo();
    let captured: ReturnType<typeof useProfileWorkspace> | null = null;
    function Capture() {
      captured = useProfileWorkspace();
      return (
        <span data-testid="session-refresh-warning">
          {String(captured.sessionRefreshWarning)}
        </span>
      );
    }

    render(
      renderProfileTree({
        repository: repo,
        sessionAdapter,
        children: <Capture />,
      }),
    );
    await waitFor(() => expect(captured?.status).toBe("ready"));

    await act(async () => {
      await expect(
        captured!.patchProfile({
          displayName: { action: "confirm", value: "Bea" },
        }),
      ).resolves.toBeDefined();
    });
    await waitFor(() =>
      expect(screen.getByTestId("session-refresh-warning")).toHaveTextContent(
        "true",
      ),
    );

    await act(async () => {
      await captured!.retrySessionRefresh();
    });
    await waitFor(() =>
      expect(screen.getByTestId("session-refresh-warning")).toHaveTextContent(
        "false",
      ),
    );
  });
});
