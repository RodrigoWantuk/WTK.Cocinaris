import type { OfflineListStore } from "@/features/planning/offline/offlineListStore";
import type { SessionAdapter } from "./types";

/**
 * Adds the offline state to a session adapter (ADR-0012). Only a network failure, with an owner
 * remembered from a real sign-in, becomes `offline`. A 401 stays signed out and closes the offline
 * shell. The session resolves only after the store handled a change of owner, so a new owner can
 * never see the previous owner's data, not even briefly.
 *
 * @param beforeLogout Runs before a logout with the last CSRF token, while the session still exists. It must not throw.
 */
export function withOfflineSession(
  inner: SessionAdapter,
  store: OfflineListStore,
  beforeLogout?: (csrfToken: string | null) => Promise<void>,
): SessionAdapter {
  let csrfToken: string | null = null;
  return {
    ...inner,
    async getSession() {
      const next = await inner.getSession();
      csrfToken = next.csrfToken ?? null;
      if (next.status === "authenticated" && next.internalUserId) {
        await store.rememberOwner(
          next.internalUserId,
          next.displayName ?? null,
        );
      } else if (next.status === "signedOut") {
        await store.closeOfflineShell();
      } else if (next.networkFailure) {
        const owner = await store.offlineOwner();
        if (owner) {
          return {
            status: "offline",
            internalUserId: owner.ownerId,
            displayName: owner.displayName,
            csrfToken: null,
          };
        }
      }
      return next;
    },
    // The inner adapter's own `this.getSession` must keep calling itself, not this wrapper.
    beginLogin: (returnUrl) => inner.beginLogin(returnUrl),
    logout: async () => {
      await beforeLogout?.(csrfToken);
      await inner.logout();
    },
  };
}
