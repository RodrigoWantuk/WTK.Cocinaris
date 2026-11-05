import { describe, expect, it, vi } from "vitest";
import {
  createMemoryBackend,
  createOfflineListStore,
} from "@/features/planning/offline/offlineListStore";
import { createBffSessionAdapter } from "./bffSessionAdapter";
import { withOfflineSession } from "./offlineSessionAdapter";
import type { SessionAdapter, SessionState } from "./types";

const adapterFor = (...answers: SessionState[]) => {
  const getSession = vi.fn<() => Promise<SessionState>>();
  answers.forEach((answer) => getSession.mockResolvedValueOnce(answer));
  return {
    getSession,
    beginLogin: vi.fn(),
    logout: vi.fn(async () => undefined),
  } satisfies SessionAdapter;
};
const signedIn = (id: string): SessionState => ({
  status: "authenticated",
  internalUserId: id,
  csrfToken: "csrf",
  displayName: "Ana",
});
const noNetwork: SessionState = { status: "unavailable", networkFailure: true };

describe("withOfflineSession", () => {
  it("opens offline only after a real sign-in, without a CSRF token", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    const adapter = withOfflineSession(
      adapterFor(noNetwork, signedIn("ana"), noNetwork),
      store,
    );
    expect((await adapter.getSession()).status).toBe("unavailable");
    expect((await adapter.getSession()).status).toBe("authenticated");
    expect(await adapter.getSession()).toEqual({
      status: "offline",
      internalUserId: "ana",
      displayName: "Ana",
      csrfToken: null,
    });
  });

  it("does not treat a failing server as offline", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    const adapter = withOfflineSession(
      adapterFor(signedIn("ana"), { status: "unavailable" }),
      store,
    );
    await adapter.getSession();
    expect((await adapter.getSession()).status).toBe("unavailable");
  });

  it("a 401 goes to sign-in and closes the offline shell", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    const adapter = withOfflineSession(
      adapterFor(signedIn("ana"), { status: "signedOut" }, noNetwork),
      store,
    );
    await adapter.getSession();
    expect((await adapter.getSession()).status).toBe("signedOut");
    expect((await adapter.getSession()).status).toBe("unavailable");
  });

  it("erases the previous owner's data before the new session resolves", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    const adapter = withOfflineSession(
      adapterFor(signedIn("ana"), signedIn("bia")),
      store,
    );
    await adapter.getSession();
    await store.enqueue("ana", {
      kind: "check",
      menuId: "m",
      itemKey: "a",
      checked: true,
      displayName: "a",
    });
    await adapter.getSession();
    expect(await store.outbox("ana")).toEqual([]);
  });
});

describe("createBffSessionAdapter", () => {
  it("marks only a rejected fetch as a network failure", async () => {
    const offline = createBffSessionAdapter({
      fetchImpl: async () => {
        throw new TypeError("Failed to fetch");
      },
    });
    expect((await offline.getSession()).networkFailure).toBe(true);
    const failing = createBffSessionAdapter({
      fetchImpl: async () => new Response(null, { status: 502 }),
    });
    expect((await failing.getSession()).networkFailure).toBeUndefined();
  });

  it("erases local data after the server ends the session and before leaving", async () => {
    const order: string[] = [];
    const adapter = createBffSessionAdapter({
      fetchImpl: async (input) => {
        const url = input instanceof Request ? input.url : String(input);
        order.push(url.includes("logout") ? "logout" : "session");
        return url.includes("logout")
          ? new Response(null, { status: 204 })
          : Response.json({
              userId: "ana",
              csrfToken: "csrf",
              profileExists: true,
            });
      },
      navigate: () => order.push("navigate"),
      onSignedOut: async () => void order.push("erase"),
    });
    await adapter.getSession();
    await adapter.logout();
    expect(order).toEqual(["session", "logout", "erase", "navigate"]);
  });

  it("keeps local data when the server refuses the logout", async () => {
    const onSignedOut = vi.fn(async () => undefined);
    const adapter = createBffSessionAdapter({
      fetchImpl: async (input) => {
        const url = input instanceof Request ? input.url : String(input);
        return url.includes("logout")
          ? new Response(null, { status: 400 })
          : Response.json({
              userId: "ana",
              csrfToken: "csrf",
              profileExists: true,
            });
      },
      navigate: vi.fn(),
      onSignedOut,
    });
    await adapter.getSession();
    await expect(adapter.logout()).rejects.toThrow();
    expect(onSignedOut).not.toHaveBeenCalled();
  });
});
