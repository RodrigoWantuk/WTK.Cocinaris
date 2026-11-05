import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useSession } from "@/app/session/SessionProvider";
import type { ShoppingItem, ShoppingList } from "@/contracts/planning";
import { localIsoDate } from "../planningDates";
import { usePlanningRepository } from "../PlanningProvider";
import {
  applyPending,
  type LineMarkBody,
  type NewOutboxEntry,
  type OfflineListStore,
  type OutboxEntry,
  type SavedList,
} from "./offlineListStore";
import { flushOutbox, isUnreachable } from "./offlineSync";

/** `navigator.onLine` stays true on a weak signal, so pending work also retries on this interval. */
const RETRY_MS = 30_000;
/** Longest a logout waits for pending marks; leaving must not depend on the network. */
const LOGOUT_FLUSH_MS = 5_000;

/** Offline behavior of the shopping list (ADR-0012). */
export interface OfflineList {
  /** Whether the server cannot be reached now, as far as the device can tell. */
  offline: boolean;
  /** Line names whose queued change no longer applied, until dismissed. */
  conflicts: string[];
  /** Grows after a delivery, so an open list reloads. */
  syncVersion: number;
  dismissConflicts(): void;
  /** Keeps a list on the device. */
  saveList(list: ShoppingList, savedAt: string): Promise<void>;
  /** The saved list of a menu, with queued changes shown. Null when none was saved. */
  loadList(menuId: string): Promise<SavedList | null>;
  /** A list from the server, with queued changes shown. */
  withPending(list: ShoppingList): Promise<ShoppingList>;
  /** The menu whose list was opened last. */
  lastMenuId(): Promise<string | null>;
  /** Records that a request could not reach the server. */
  noteUnreachable(): void;
  /** Sends a check mark, or queues it. Returns whether it was queued. Throws if refused. */
  sendCheck(
    menuId: string,
    item: ShoppingItem,
    checked: boolean,
  ): Promise<"sent" | "queued">;
  /** Sends a "buy later" mark, or queues it. Returns whether it was queued. Throws if refused. */
  sendLater(
    menuId: string,
    item: ShoppingItem,
    mark: LineMarkBody,
  ): Promise<"sent" | "queued">;
  /** Tries once to deliver pending marks before a logout erases them. */
  flushBeforeLogout(): Promise<void>;
}

const OfflineListContext = createContext<OfflineList | null>(null);

/**
 * Offline state of the shopping list, or null where it does not exist: the prototype, and tests
 * that render a page alone. Callers then behave as they do online.
 */
export function useOfflineList(): OfflineList | null {
  return useContext(OfflineListContext);
}

/**
 * Keeps the list usable without a connection and delivers queued marks (ADR-0012): on the `online`
 * event, on app open, when the page becomes visible, and on an interval while anything is pending.
 * Only one delivery runs at a time; a change queued during one starts another right after.
 */
export function OfflineListProvider({
  store,
  children,
}: {
  store: OfflineListStore;
  children: ReactNode;
}) {
  const { session, refreshSoft, refresh } = useSession();
  const repo = usePlanningRepository();
  const owner =
    session.status === "authenticated" || session.status === "offline"
      ? (session.internalUserId ?? null)
      : null;
  const [deviceOnline, setDeviceOnline] = useState(
    () => typeof navigator === "undefined" || navigator.onLine !== false,
  );
  const [unreachable, setUnreachable] = useState(false);
  const [pending, setPending] = useState(0);
  const [conflicts, setConflicts] = useState<string[]>([]);
  const [syncVersion, setSyncVersion] = useState(0);
  const offline = !deviceOnline || unreachable || session.status === "offline";

  const syncing = useRef<Promise<void> | null>(null);
  const again = useRef(false);
  const ownerRef = useRef(owner);
  ownerRef.current = owner;

  const reread = useCallback(
    async (id: string) => {
      const [queued, unapplied] = await Promise.all([
        store.outbox(id),
        store.conflicts(id),
      ]);
      setPending(queued.length);
      setConflicts(unapplied);
      return queued.length;
    },
    [store],
  );

  const sync = useCallback((): Promise<void> => {
    if (syncing.current) {
      again.current = true;
      return syncing.current;
    }
    const run = async () => {
      // Asks for the session first: it proves the connection and brings a fresh CSRF token.
      const { ok, session: current, latest } = await refreshSoft();
      if (!ok) {
        if (latest.status === "signedOut") {
          // A 401 goes to sign-in; the outbox stays for this owner (decided in ADR-0012).
          await refresh();
          return;
        }
        setUnreachable(true);
        return;
      }
      const id = current.internalUserId;
      if (!id || !current.csrfToken) return;
      const wasOffline = unreachableRef.current;
      setUnreachable(false);
      const delivered = await store.outbox(id);
      const outcome = await flushOutbox({
        store,
        owner: id,
        repo,
        csrfToken: current.csrfToken,
        today: localIsoDate(),
      });
      await reread(id);
      if (outcome === "unreachable") setUnreachable(true);
      if (outcome === "signed_out") await refresh();
      if (wasOffline || delivered.length > 0) {
        setSyncVersion((value) => value + 1);
      }
    };
    const started = run()
      .catch(() => setUnreachable(true))
      .finally(() => {
        syncing.current = null;
        if (again.current) {
          again.current = false;
          void sync();
        }
      });
    syncing.current = started;
    return started;
  }, [refresh, refreshSoft, repo, reread, store]);

  const unreachableRef = useRef(unreachable);
  unreachableRef.current = unreachable;

  useEffect(() => {
    const goOnline = () => {
      setDeviceOnline(true);
      void sync();
    };
    const goOffline = () => setDeviceOnline(false);
    window.addEventListener("online", goOnline);
    window.addEventListener("offline", goOffline);
    return () => {
      window.removeEventListener("online", goOnline);
      window.removeEventListener("offline", goOffline);
    };
  }, [sync]);

  // App open: anything left by an earlier visit goes out as soon as the session is known.
  useEffect(() => {
    if (!owner) return;
    let current = true;
    void reread(owner).then((queued) => {
      if (current && queued > 0 && session.status === "authenticated") {
        void sync();
      }
    });
    return () => {
      current = false;
    };
    // Runs per owner and session state; `sync` and `reread` follow from stable inputs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [owner, session.status]);

  const needsRetry = Boolean(owner) && (offline || pending > 0);
  useEffect(() => {
    if (!needsRetry || !deviceOnline) return;
    const timer = globalThis.setInterval(() => void sync(), RETRY_MS);
    const onVisible = () => {
      if (document.visibilityState === "visible") void sync();
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      globalThis.clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [deviceOnline, needsRetry, sync]);

  const queue = useCallback(
    async (entry: NewOutboxEntry) => {
      const id = ownerRef.current;
      if (!id) return;
      await store.enqueue(id, entry);
      const queued = await reread(id);
      if (queued > 0 && deviceOnline) void sync();
    },
    [deviceOnline, reread, store, sync],
  );

  /** Direct while everything is clear; a queued older change must not overwrite a newer one. */
  const deliver = useCallback(
    async (
      entry: NewOutboxEntry,
      send: (csrfToken: string) => Promise<void>,
    ): Promise<"sent" | "queued"> => {
      if (offline || pending > 0 || !session.csrfToken) {
        await queue(entry);
        return "queued";
      }
      try {
        await send(session.csrfToken);
        return "sent";
      } catch (error) {
        if (!isUnreachable(error)) throw error;
        setUnreachable(true);
        await queue(entry);
        return "queued";
      }
    },
    [offline, pending, queue, session.csrfToken],
  );

  const value = useMemo<OfflineList>(() => {
    const pendingEntries = async (): Promise<OutboxEntry[]> =>
      owner ? store.outbox(owner) : [];
    return {
      offline,
      conflicts,
      syncVersion,
      dismissConflicts: () => {
        setConflicts([]);
        if (owner) void store.clearConflicts(owner);
      },
      saveList: async (list, savedAt) => {
        if (owner) await store.saveList(owner, list, savedAt);
      },
      loadList: async (menuId) => {
        if (!owner) return null;
        const saved = await store.loadList(owner, menuId);
        if (!saved) return null;
        return {
          ...saved,
          list: applyPending(saved.list, await pendingEntries()),
        };
      },
      withPending: async (list) => applyPending(list, await pendingEntries()),
      lastMenuId: async () => (owner ? store.lastMenuId(owner) : null),
      noteUnreachable: () => setUnreachable(true),
      sendCheck: (menuId, item, checked) =>
        deliver(
          {
            kind: "check",
            menuId,
            itemKey: item.itemKey,
            checked,
            displayName: item.displayName,
          },
          (csrfToken) =>
            repo.setChecked(menuId, item.itemKey, checked, { csrfToken }),
        ),
      sendLater: (menuId, item, mark) =>
        deliver(
          {
            kind: "mark",
            menuId,
            itemKey: mark.itemKey,
            mark,
            displayName: item.displayName,
          },
          async (csrfToken) => {
            await repo.setLineMark?.(menuId, mark, { csrfToken });
          },
        ),
      flushBeforeLogout: async () => {
        if (pending === 0 || offline) return;
        await Promise.race([
          sync(),
          new Promise((resolve) =>
            globalThis.setTimeout(resolve, LOGOUT_FLUSH_MS),
          ),
        ]);
      },
    };
  }, [
    conflicts,
    deliver,
    offline,
    owner,
    pending,
    repo,
    store,
    sync,
    syncVersion,
  ]);

  return (
    <OfflineListContext.Provider value={value}>
      {children}
    </OfflineListContext.Provider>
  );
}
