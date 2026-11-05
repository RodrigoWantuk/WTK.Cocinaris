import {
  createContext,
  createElement,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import type { SessionAdapter, SessionState } from "./types";

interface SessionContextValue {
  session: SessionState;
  refresh: () => Promise<SessionState>;
  /**
   * Reloads the BFF session projection. When the previous session was authenticated
   * and the reload does not return authenticated, keeps the previous snapshot so the
   * user is not ejected from protected routes, and reports `ok: false` for callers
   * that need a soft warning (profile post-save sync).
   */
  refreshSoft: () => Promise<{
    ok: boolean;
    session: SessionState;
    /** What the server just answered, which `session` may not reflect. */
    latest: SessionState;
  }>;
  beginLogin: (returnUrl?: string) => void;
  logout: () => Promise<void>;
  /** Convenience: true only for authenticated status. */
  isAuthenticated: boolean;
}

const SessionContext = createContext<SessionContextValue | null>(null);

/**
 * Loads session state through the injected SessionAdapter.
 */
export function SessionProvider({
  adapter,
  children,
}: {
  adapter: SessionAdapter;
  children: ReactNode;
}) {
  const [session, setSession] = useState<SessionState>({ status: "loading" });
  const sessionRef = useRef(session);
  sessionRef.current = session;

  const refresh = useCallback(async () => {
    setSession({ status: "loading" });
    const next = await adapter.getSession();
    setSession(next);
    return next;
  }, [adapter]);

  const refreshSoft = useCallback(async () => {
    const previous = sessionRef.current;
    const next = await adapter.getSession();
    if (next.status === "authenticated") {
      setSession(next);
      return { ok: true, session: next, latest: next };
    }
    if (previous.status === "authenticated") {
      // Preserve the authenticated shell; callers surface a soft warning instead of
      // ejecting the user to /acesso mid-task.
      return { ok: false, session: previous, latest: next };
    }
    setSession(next);
    return { ok: false, session: next, latest: next };
  }, [adapter]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo<SessionContextValue>(
    () => ({
      session,
      refresh,
      refreshSoft,
      beginLogin: (returnUrl?: string) => {
        // Login uses a full-document BFF challenge navigation; do not race a refresh.
        adapter.beginLogin(returnUrl);
      },
      logout: async () => {
        await adapter.logout();
        await refresh();
      },
      isAuthenticated: session.status === "authenticated",
    }),
    [adapter, refresh, refreshSoft, session],
  );

  return createElement(SessionContext.Provider, { value }, children);
}

export function useSession(): SessionContextValue {
  const ctx = useContext(SessionContext);
  if (!ctx) {
    throw new Error("useSession must be used within SessionProvider");
  }
  return ctx;
}
