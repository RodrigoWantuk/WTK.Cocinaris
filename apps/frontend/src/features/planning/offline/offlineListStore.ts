import { clear, createStore, del, get, set, update } from "idb-keyval";
import type { ShoppingItem, ShoppingList } from "@/contracts/planning";

/** Most menus whose list stays on the device. */
const MAX_LISTS = 5;

/** Minimal key-value storage behind {@link OfflineListStore}, so tests need no IndexedDB. */
export interface KeyValueBackend {
  get(key: string): Promise<unknown>;
  set(key: string, value: unknown): Promise<void>;
  del(key: string): Promise<void>;
  clear(): Promise<void>;
  /** Atomically replaces a value with what `change` returns. */
  update(key: string, change: (value: unknown) => unknown): Promise<void>;
}

/** Storage that lasts as long as the page. */
export function createMemoryBackend(): KeyValueBackend {
  const values = new Map<string, unknown>();
  return {
    get: async (key) => values.get(key),
    set: async (key, value) => void values.set(key, value),
    del: async (key) => void values.delete(key),
    clear: async () => values.clear(),
    update: async (key, change) =>
      void values.set(key, change(values.get(key))),
  };
}

/**
 * IndexedDB storage (ADR-0012). A private window or a blocked site can refuse IndexedDB, or lose it
 * mid-session; the page then keeps working in memory instead of failing the shopping list.
 */
export function createIdbBackend(): KeyValueBackend {
  const memory = createMemoryBackend();
  let idb: ReturnType<typeof createStore> | null = null;
  let broken = typeof indexedDB === "undefined";
  const attempt = async <T>(
    run: (store: ReturnType<typeof createStore>) => Promise<T>,
    fallback: () => Promise<T>,
  ): Promise<T> => {
    if (broken) return fallback();
    try {
      idb ??= createStore("cocinaris-offline", "kv");
      return await run(idb);
    } catch {
      broken = true;
      return fallback();
    }
  };
  return {
    get: (key) =>
      attempt(
        (store) => get(key, store),
        () => memory.get(key),
      ),
    set: (key, value) =>
      attempt(
        (store) => set(key, value, store),
        () => memory.set(key, value),
      ),
    del: (key) =>
      attempt(
        (store) => del(key, store),
        () => memory.del(key),
      ),
    clear: () =>
      attempt(
        (store) => clear(store),
        () => memory.clear(),
      ),
    update: (key, change) =>
      attempt(
        (store) => update(key, change, store),
        () => memory.update(key, change),
      ),
  };
}

/** A list as last received from the server. */
export interface SavedList {
  list: ShoppingList;
  /** ISO instant of the server response. */
  savedAt: string;
}

/** The body of a line mark, as the server takes it. */
export interface LineMarkBody {
  itemKey: string;
  state: "later" | null;
  substituteName: string | null;
  place: string | null;
}

/** A change waiting to reach the server. `displayName` only names it if it stops applying. */
export type OutboxEntry =
  | {
      id: string;
      kind: "check";
      menuId: string;
      itemKey: string;
      checked: boolean;
      displayName: string;
    }
  | {
      id: string;
      kind: "mark";
      menuId: string;
      itemKey: string;
      mark: LineMarkBody;
      displayName: string;
    };

/** Distributes `Omit` over the union, so each kind keeps its own fields. */
export type NewOutboxEntry = OutboxEntry extends infer E
  ? E extends OutboxEntry
    ? Omit<E, "id">
    : never
  : never;

interface OwnerRecord {
  ownerId: string;
  displayName: string | null;
  /** False after the server said the session ended: nothing shows offline until a new sign-in. */
  shellAllowed: boolean;
}

/** Whether a line is the target of an outbox entry; marks follow the key the server marks by. */
export function lineMatches(item: ShoppingItem, entry: OutboxEntry): boolean {
  return entry.kind === "check"
    ? item.itemKey === entry.itemKey
    : (item.markKey ?? item.itemKey) === entry.itemKey;
}

/** Shows changes the server has not received yet on a list it sent or the device saved. */
export function applyPending(
  list: ShoppingList,
  entries: OutboxEntry[],
): ShoppingList {
  const mine = entries.filter((entry) => entry.menuId === list.menuId);
  if (mine.length === 0) return list;
  return {
    ...list,
    items: list.items.map((item) =>
      mine.reduce<ShoppingItem>(
        (line, entry) =>
          !lineMatches(item, entry)
            ? line
            : entry.kind === "check"
              ? { ...line, checked: entry.checked }
              : { ...line, later: entry.mark.state === "later" },
        item,
      ),
    ),
  };
}

/**
 * What the shopping list keeps on the device (ADR-0012). Every key is prefixed by the owner, and a
 * different owner signing in erases everything first. The remembered owner selects local data and
 * authorizes nothing: the server still decides on every request.
 */
export function createOfflineListStore(backend: KeyValueBackend) {
  const listsKey = (owner: string) => `${owner}:lists`;
  const lastMenuKey = (owner: string) => `${owner}:last-menu`;
  const outboxKey = (owner: string) => `${owner}:outbox`;
  const conflictsKey = (owner: string) => `${owner}:conflicts`;
  const OWNER_KEY = "owner";

  const readArray = async <T>(key: string) =>
    ((await backend.get(key)) as T[] | undefined) ?? [];

  return {
    /** Records who signed in. A different owner erases the previous owner's data first. */
    async rememberOwner(ownerId: string, displayName: string | null) {
      const current = (await backend.get(OWNER_KEY)) as OwnerRecord | undefined;
      if (current && current.ownerId !== ownerId) await backend.clear();
      await backend.set(OWNER_KEY, {
        ownerId,
        displayName,
        shellAllowed: true,
      } satisfies OwnerRecord);
    },

    /** The owner whose list may open without a connection, if any. */
    async offlineOwner(): Promise<{
      ownerId: string;
      displayName: string | null;
    } | null> {
      const current = (await backend.get(OWNER_KEY)) as OwnerRecord | undefined;
      return current?.shellAllowed
        ? { ownerId: current.ownerId, displayName: current.displayName }
        : null;
    },

    /** The session ended on the server: keep the outbox for the same owner, close the offline shell. */
    async closeOfflineShell() {
      // Signing out also lands here, right after the erase: it must not recreate the record.
      if (!(await backend.get(OWNER_KEY))) return;
      await backend.update(OWNER_KEY, (value) => ({
        ...(value as OwnerRecord),
        shellAllowed: false,
      }));
    },

    /** Erases everything on the device, for every owner. */
    async clearAll() {
      await backend.clear();
    },

    async saveList(owner: string, list: ShoppingList, savedAt: string) {
      await backend.update(listsKey(owner), (value) => {
        const lists = {
          ...((value as Record<string, SavedList> | undefined) ?? {}),
          [list.menuId]: { list, savedAt },
        };
        const newest = Object.entries(lists)
          .sort(([, a], [, b]) => b.savedAt.localeCompare(a.savedAt))
          .slice(0, MAX_LISTS);
        return Object.fromEntries(newest);
      });
      await backend.set(lastMenuKey(owner), list.menuId);
    },

    async loadList(owner: string, menuId: string): Promise<SavedList | null> {
      const lists = (await backend.get(listsKey(owner))) as
        | Record<string, SavedList>
        | undefined;
      return lists?.[menuId] ?? null;
    },

    async lastMenuId(owner: string) {
      return (
        ((await backend.get(lastMenuKey(owner))) as string | undefined) ?? null
      );
    },

    /** Queues a change. A newer change to the same line replaces the older one (last write wins). */
    async enqueue(owner: string, entry: NewOutboxEntry) {
      const queued = { ...entry, id: crypto.randomUUID() } as OutboxEntry;
      await backend.update(outboxKey(owner), (value) => [
        ...((value as OutboxEntry[] | undefined) ?? []).filter(
          (other) =>
            !(
              other.kind === queued.kind &&
              other.menuId === queued.menuId &&
              other.itemKey === queued.itemKey
            ),
        ),
        queued,
      ]);
    },

    outbox: (owner: string) => readArray<OutboxEntry>(outboxKey(owner)),

    /** Removes one entry by id, so a change queued during delivery is never lost. */
    async removeEntry(owner: string, id: string) {
      await backend.update(outboxKey(owner), (value) =>
        ((value as OutboxEntry[] | undefined) ?? []).filter(
          (entry) => entry.id !== id,
        ),
      );
    },

    conflicts: (owner: string) => readArray<string>(conflictsKey(owner)),

    async addConflict(owner: string, displayName: string) {
      await backend.update(conflictsKey(owner), (value) => [
        ...((value as string[] | undefined) ?? []),
        displayName,
      ]);
    },

    async clearConflicts(owner: string) {
      await backend.del(conflictsKey(owner));
    },
  };
}

export type OfflineListStore = ReturnType<typeof createOfflineListStore>;
