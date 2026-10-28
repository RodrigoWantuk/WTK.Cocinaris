import type { Mock } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import {
  isWorkspaceConsistent,
  useProfileWorkspace,
  type ProfileWorkspaceContextValue,
} from "./ProfileProvider";
import {
  ProfileApiError,
  type ProfileMutationContext,
  type ProfilePatch,
  type ProfileSnapshot,
  type ProfileWorkspace,
} from "@/contracts/profile";
import {
  createAbsentProfileSnapshot,
  createConfirmedProfileSnapshot,
  createEmptyEquipmentSnapshot,
  createEmptyPreferenceSnapshot,
  createCompleteness,
  createMockProfileRepo,
  createSessionAdapter,
  renderProfileTree,
} from "./testUtils";

function buildAbsentWorkspace(
  overrides: Partial<ProfileWorkspace> = {},
): ProfileWorkspace {
  return {
    profile: createAbsentProfileSnapshot(),
    preferences: createEmptyPreferenceSnapshot({ version: null, etag: null }),
    equipment: createEmptyEquipmentSnapshot({ version: null, etag: null }),
    completeness: createCompleteness({
      profileExists: false,
      percentComplete: 0,
    }),
    version: null,
    etag: null,
    ...overrides,
  };
}

function buildConsistentWorkspace(
  overrides: Partial<ProfileWorkspace> = {},
): ProfileWorkspace {
  return {
    profile: createConfirmedProfileSnapshot(),
    preferences: createEmptyPreferenceSnapshot(),
    equipment: createEmptyEquipmentSnapshot(),
    completeness: createCompleteness({ profileExists: true }),
    version: "v1",
    etag: '"v1"',
    ...overrides,
  };
}

function StatusProbe() {
  const { status, workspace, isMutating, lastMutationError } =
    useProfileWorkspace();
  return (
    <div>
      <span data-testid="status">{status}</span>
      <span data-testid="profile-version">
        {workspace?.profile.version ?? ""}
      </span>
      <span data-testid="is-mutating">{String(isMutating)}</span>
      <span data-testid="last-error-code">{lastMutationError?.code ?? ""}</span>
    </div>
  );
}

describe("isWorkspaceConsistent", () => {
  it("treats an absent profile with every version/etag null and completeness.profileExists false as consistent", () => {
    expect(isWorkspaceConsistent(buildAbsentWorkspace())).toBe(true);
  });

  it("rejects an absent profile that still carries a preferences version", () => {
    const workspace = buildAbsentWorkspace({
      preferences: createEmptyPreferenceSnapshot({
        version: "v1",
        etag: '"v1"',
      }),
    });
    expect(isWorkspaceConsistent(workspace)).toBe(false);
  });

  it("rejects an absent profile whose completeness still reports profileExists true", () => {
    const workspace = buildAbsentWorkspace({
      completeness: createCompleteness({ profileExists: true }),
    });
    expect(isWorkspaceConsistent(workspace)).toBe(false);
  });

  it("treats a fully consistent existing profile as consistent", () => {
    expect(isWorkspaceConsistent(buildConsistentWorkspace())).toBe(true);
  });

  it("treats matching quoted and unquoted etags/versions as the same aggregate version", () => {
    const workspace = buildConsistentWorkspace({
      profile: createConfirmedProfileSnapshot({
        version: "v7",
        etag: '"v7"',
      }),
      preferences: createEmptyPreferenceSnapshot({
        version: "v7",
        etag: 'W/"v7"',
      }),
      equipment: createEmptyEquipmentSnapshot({ version: "v7", etag: "v7" }),
    });
    expect(isWorkspaceConsistent(workspace)).toBe(true);
  });

  it("rejects an existing profile whose preferences version diverges from the profile version", () => {
    const workspace = buildConsistentWorkspace({
      preferences: createEmptyPreferenceSnapshot({
        version: "v2",
        etag: '"v2"',
      }),
    });
    expect(isWorkspaceConsistent(workspace)).toBe(false);
  });

  it("rejects an existing profile whose header etag contradicts the body version, without preferring either", () => {
    const workspace = buildConsistentWorkspace({
      profile: createConfirmedProfileSnapshot({
        version: "v1",
        etag: '"v2"',
      }),
    });
    expect(isWorkspaceConsistent(workspace)).toBe(false);
  });

  it("rejects an existing profile missing the preferences etag even though the version matches", () => {
    const workspace = buildConsistentWorkspace({
      preferences: createEmptyPreferenceSnapshot({ version: "v1", etag: null }),
    });
    expect(isWorkspaceConsistent(workspace)).toBe(false);
  });

  it("rejects an existing profile missing the equipment version", () => {
    const workspace = buildConsistentWorkspace({
      equipment: createEmptyEquipmentSnapshot({ version: null, etag: null }),
    });
    expect(isWorkspaceConsistent(workspace)).toBe(false);
  });

  it("rejects an existing profile whose completeness still reports profileExists false", () => {
    const workspace = buildConsistentWorkspace({
      completeness: createCompleteness({ profileExists: false }),
    });
    expect(isWorkspaceConsistent(workspace)).toBe(false);
  });
});

describe("ProfileProvider workspace coordination", () => {
  it("loads profile, preferences, equipment, and completeness in parallel and reports ready", async () => {
    const repo = createMockProfileRepo();
    render(renderProfileTree({ repository: repo, children: <StatusProbe /> }));

    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("ready"),
    );
    expect(repo.getProfile).toHaveBeenCalledTimes(1);
    expect(repo.getPreferences).toHaveBeenCalledTimes(1);
    expect(repo.getEquipment).toHaveBeenCalledTimes(1);
    expect(repo.getCompleteness).toHaveBeenCalledTimes(1);
  });

  it("retries once on a version mismatch across collections and reports ready once consistent", async () => {
    const getPreferences = vi
      .fn()
      .mockResolvedValueOnce(
        createEmptyPreferenceSnapshot({ version: "stale", etag: '"stale"' }),
      )
      .mockResolvedValue(createEmptyPreferenceSnapshot());
    const repo = createMockProfileRepo({ getPreferences });

    render(renderProfileTree({ repository: repo, children: <StatusProbe /> }));

    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("ready"),
    );
    expect(getPreferences).toHaveBeenCalledTimes(2);
  });

  it("reports version_conflict and blocks reads from stale data when still inconsistent after one retry", async () => {
    const getPreferences = vi
      .fn()
      .mockResolvedValue(
        createEmptyPreferenceSnapshot({ version: "stale", etag: '"stale"' }),
      );
    const repo = createMockProfileRepo({ getPreferences });

    render(renderProfileTree({ repository: repo, children: <StatusProbe /> }));

    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent(
        "version_conflict",
      ),
    );
    // Exactly one retry: two attempts total, not unbounded retrying.
    expect(getPreferences).toHaveBeenCalledTimes(2);
    // The inconsistent snapshot the provider just fetched must never be exposed as
    // the current workspace, even though the two GETs raced independently.
    expect(screen.getByTestId("profile-version")).toHaveTextContent("");
  });

  it("treats an absent profile (all null versions) as consistent without retrying", async () => {
    const repo = createMockProfileRepo({
      getProfile: vi.fn(async () => ({
        ...createConfirmedProfileSnapshot(),
        profileExists: false,
        version: null,
        etag: null,
      })),
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

    render(renderProfileTree({ repository: repo, children: <StatusProbe /> }));

    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("ready"),
    );
    expect(repo.getPreferences).toHaveBeenCalledTimes(1);
  });

  it("sends the session CSRF token and current etag on patchProfile, then reloads and refreshes the session", async () => {
    const patchProfile = vi.fn<
      (
        ...args: [ProfilePatch, ProfileMutationContext]
      ) => Promise<ProfileSnapshot>
    >(async () => createConfirmedProfileSnapshot());
    const repo = createMockProfileRepo({ patchProfile });
    const sessionAdapter = createSessionAdapter();

    function Trigger() {
      const { patchProfile: patch, status } = useProfileWorkspace();
      return (
        <div>
          <span data-testid="status">{status}</span>
          <button
            type="button"
            data-testid="save"
            onClick={() =>
              void patch({ displayName: { action: "confirm", value: "Bea" } })
            }
          >
            save
          </button>
        </div>
      );
    }

    render(
      renderProfileTree({
        repository: repo,
        sessionAdapter,
        children: <Trigger />,
      }),
    );
    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("ready"),
    );

    const initialSessionCalls = (sessionAdapter.getSession as Mock).mock.calls
      .length;
    screen.getByTestId("save").click();

    await waitFor(() => expect(patchProfile).toHaveBeenCalledTimes(1));
    expect(patchProfile.mock.calls[0][1]).toMatchObject({
      csrfToken: "csrf-test",
      etag: '"v1"',
    });
    await waitFor(() =>
      expect(
        (sessionAdapter.getSession as Mock).mock.calls.length,
      ).toBeGreaterThan(initialSessionCalls),
    );
    // Reload after a successful mutation re-fetches all four resources again.
    expect(repo.getProfile).toHaveBeenCalledTimes(2);
  });

  it("serializes concurrent mutations so only one is in flight at a time", async () => {
    let active = 0;
    let maxActive = 0;
    const patchProfile = vi.fn<
      (
        ...args: [ProfilePatch, ProfileMutationContext]
      ) => Promise<ProfileSnapshot>
    >(async () => {
      active += 1;
      maxActive = Math.max(maxActive, active);
      await new Promise((resolve) => setTimeout(resolve, 10));
      active -= 1;
      return createConfirmedProfileSnapshot();
    });
    const repo = createMockProfileRepo({ patchProfile });

    function Trigger() {
      const { patchProfile: patch, status } = useProfileWorkspace();
      return (
        <div>
          <span data-testid="status">{status}</span>
          <button
            type="button"
            data-testid="save-a"
            onClick={() =>
              void patch({ displayName: { action: "confirm", value: "A" } })
            }
          >
            a
          </button>
          <button
            type="button"
            data-testid="save-b"
            onClick={() =>
              void patch({ displayName: { action: "confirm", value: "B" } })
            }
          >
            b
          </button>
        </div>
      );
    }

    render(renderProfileTree({ repository: repo, children: <Trigger /> }));
    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("ready"),
    );

    screen.getByTestId("save-a").click();
    screen.getByTestId("save-b").click();

    await waitFor(() => expect(patchProfile).toHaveBeenCalledTimes(2));
    expect(maxActive).toBe(1);
  });

  it("on precondition_failed (412), re-syncs the workspace and exposes the error code without retrying the mutation", async () => {
    const patchProfile = vi.fn<
      (
        ...args: [ProfilePatch, ProfileMutationContext]
      ) => Promise<ProfileSnapshot>
    >(async () => {
      throw new ProfileApiError("precondition_failed", "stale version", 412);
    });
    const repo = createMockProfileRepo({ patchProfile });

    function Trigger() {
      const {
        patchProfile: patch,
        status,
        lastMutationError,
      } = useProfileWorkspace();
      return (
        <div>
          <span data-testid="status">{status}</span>
          <span data-testid="last-error-code">
            {lastMutationError?.code ?? ""}
          </span>
          <button
            type="button"
            data-testid="save"
            onClick={() => {
              patch({
                displayName: { action: "confirm", value: "Bea" },
              }).catch(() => {
                // Surfaced via lastMutationError; the caller's draft is untouched here.
              });
            }}
          >
            save
          </button>
        </div>
      );
    }

    render(renderProfileTree({ repository: repo, children: <Trigger /> }));
    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("ready"),
    );

    screen.getByTestId("save").click();

    await waitFor(() => expect(patchProfile).toHaveBeenCalledTimes(1));
    // Exactly one attempt: 412 must never be auto-retried.
    expect(patchProfile).toHaveBeenCalledTimes(1);
    // Re-sync reload after the failed mutation.
    await waitFor(() => expect(repo.getProfile).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(screen.getByTestId("last-error-code")).toHaveTextContent(
        "precondition_failed",
      ),
    );
  });

  it("rejects a mutation attempted before the workspace has ever reached ready with workspace_not_ready, without calling the repository", async () => {
    const pendingProfile = new Promise<ProfileSnapshot>(() => {
      // Never resolves: keeps the provider stuck in `loading` for this test.
    });
    const patchProfile = vi.fn<
      (
        ...args: [ProfilePatch, ProfileMutationContext]
      ) => Promise<ProfileSnapshot>
    >(async () => createConfirmedProfileSnapshot());
    const repo = createMockProfileRepo({
      getProfile: vi.fn(() => pendingProfile),
      patchProfile,
    });

    let captured: ProfileWorkspaceContextValue | null = null;
    function Capture() {
      const value = useProfileWorkspace();
      captured = value;
      return <span data-testid="status">{value.status}</span>;
    }

    render(renderProfileTree({ repository: repo, children: <Capture /> }));
    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("loading"),
    );

    await act(async () => {
      await expect(
        captured!.patchProfile({
          displayName: { action: "confirm", value: "Bea" },
        }),
      ).rejects.toMatchObject({ code: "workspace_not_ready" });
    });
    expect(patchProfile).not.toHaveBeenCalled();
  });

  it("rejects a mutation while status is version_conflict, without calling the repository", async () => {
    const getPreferences = vi
      .fn()
      .mockResolvedValue(
        createEmptyPreferenceSnapshot({ version: "stale", etag: '"stale"' }),
      );
    const patchProfile = vi.fn<
      (
        ...args: [ProfilePatch, ProfileMutationContext]
      ) => Promise<ProfileSnapshot>
    >(async () => createConfirmedProfileSnapshot());
    const repo = createMockProfileRepo({ getPreferences, patchProfile });

    let captured: ProfileWorkspaceContextValue | null = null;
    function Capture() {
      const value = useProfileWorkspace();
      captured = value;
      return <span data-testid="status">{value.status}</span>;
    }

    render(renderProfileTree({ repository: repo, children: <Capture /> }));
    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent(
        "version_conflict",
      ),
    );
    expect(captured!.workspace).toBeNull();

    await act(async () => {
      await expect(
        captured!.patchProfile({
          displayName: { action: "confirm", value: "Bea" },
        }),
      ).rejects.toMatchObject({ code: "workspace_not_ready" });
    });
    expect(patchProfile).not.toHaveBeenCalled();
  });

  it(
    "reports a mutation as successful even when the mandatory post-mutation reload " +
      "fails, exposes saveRefreshFailed, blocks further mutations, and clears the " +
      "flag on an explicit reload",
    async () => {
      const getProfile = vi
        .fn()
        .mockResolvedValueOnce(createConfirmedProfileSnapshot()) // initial load
        .mockRejectedValueOnce(
          new ProfileApiError("unavailable", "backend hiccup", 503),
        ) // resync after the mutation
        .mockResolvedValue(createConfirmedProfileSnapshot()); // explicit reload
      const patchProfile = vi.fn<
        (
          ...args: [ProfilePatch, ProfileMutationContext]
        ) => Promise<ProfileSnapshot>
      >(async () => createConfirmedProfileSnapshot());
      const repo = createMockProfileRepo({ getProfile, patchProfile });

      let captured: ProfileWorkspaceContextValue | null = null;
      function Capture() {
        const value = useProfileWorkspace();
        captured = value;
        return (
          <div>
            <span data-testid="status">{value.status}</span>
            <span data-testid="save-refresh-failed">
              {String(value.saveRefreshFailed)}
            </span>
          </div>
        );
      }

      render(renderProfileTree({ repository: repo, children: <Capture /> }));
      await waitFor(() =>
        expect(screen.getByTestId("status")).toHaveTextContent("ready"),
      );

      // The mutation itself must resolve (report success) even though the reload
      // right after it will fail.
      await act(async () => {
        await expect(
          captured!.patchProfile({
            displayName: { action: "confirm", value: "Bea" },
          }),
        ).resolves.toBeDefined();
      });
      expect(patchProfile).toHaveBeenCalledTimes(1);

      await waitFor(() =>
        expect(screen.getByTestId("save-refresh-failed")).toHaveTextContent(
          "true",
        ),
      );

      // Further mutations are rejected until the flag is cleared.
      await act(async () => {
        await expect(
          captured!.patchProfile({
            displayName: { action: "confirm", value: "Ignored" },
          }),
        ).rejects.toMatchObject({ code: "workspace_not_ready" });
      });
      expect(patchProfile).toHaveBeenCalledTimes(1);

      await act(async () => {
        await captured!.reload();
      });

      await waitFor(() =>
        expect(screen.getByTestId("status")).toHaveTextContent("ready"),
      );
      expect(screen.getByTestId("save-refresh-failed")).toHaveTextContent(
        "false",
      );

      // Mutations work again now that the workspace is confirmed ready.
      await act(async () => {
        await expect(
          captured!.patchProfile({
            displayName: { action: "confirm", value: "Carla" },
          }),
        ).resolves.toBeDefined();
      });
      expect(patchProfile).toHaveBeenCalledTimes(2);
    },
  );
});
