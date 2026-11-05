import { describe, expect, it } from "vitest";
import type { ShoppingItem, ShoppingList } from "@/contracts/planning";
import {
  applyPending,
  createMemoryBackend,
  createOfflineListStore,
} from "./offlineListStore";

const line = (itemKey: string, extra: Partial<ShoppingItem> = {}) =>
  ({
    itemKey,
    displayName: itemKey,
    checked: false,
    ...extra,
  }) as ShoppingItem;
const list = (menuId: string, items: ShoppingItem[] = [line("a")]) =>
  ({ menuId, from: "2026-11-05", mealCount: 1, items }) as ShoppingList;
const check = (itemKey: string, checked = true) => ({
  kind: "check" as const,
  menuId: "m1",
  itemKey,
  checked,
  displayName: itemKey,
});

describe("offlineListStore", () => {
  it("keeps each owner's data apart and erases it when another owner signs in", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    await store.rememberOwner("ana", "Ana");
    await store.saveList("ana", list("m1"), "2026-11-05T10:00:00Z");
    await store.enqueue("ana", check("a"));
    expect(await store.loadList("bia", "m1")).toBeNull();

    await store.rememberOwner("ana", "Ana");
    expect(await store.loadList("ana", "m1")).not.toBeNull();

    await store.rememberOwner("bia", "Bia");
    expect(await store.loadList("ana", "m1")).toBeNull();
    expect(await store.outbox("ana")).toEqual([]);
    expect((await store.offlineOwner())?.ownerId).toBe("bia");
  });

  it("closes the offline shell on a 401 but keeps the outbox for the same owner", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    await store.rememberOwner("ana", "Ana");
    await store.enqueue("ana", check("a"));
    await store.closeOfflineShell();
    expect(await store.offlineOwner()).toBeNull();
    await store.rememberOwner("ana", "Ana");
    expect(await store.outbox("ana")).toHaveLength(1);
    expect(await store.offlineOwner()).not.toBeNull();
  });

  it("does not recreate the owner after everything was erased", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    await store.rememberOwner("ana", "Ana");
    await store.clearAll();
    await store.closeOfflineShell();
    expect(await store.offlineOwner()).toBeNull();
  });

  it("keeps only the newest change per line, in order, per kind", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    await store.enqueue("ana", check("a", true));
    await store.enqueue("ana", check("b", true));
    await store.enqueue("ana", check("a", false));
    const outbox = await store.outbox("ana");
    expect(outbox.map((entry) => entry.itemKey)).toEqual(["b", "a"]);
    expect(outbox[1]).toMatchObject({ checked: false });
  });

  it("keeps at most five lists, dropping the oldest", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    for (let day = 1; day <= 6; day++) {
      await store.saveList("ana", list(`m${day}`), `2026-11-0${day}T10:00:00Z`);
    }
    expect(await store.loadList("ana", "m1")).toBeNull();
    expect(await store.loadList("ana", "m6")).not.toBeNull();
    expect(await store.lastMenuId("ana")).toBe("m6");
  });
});

describe("applyPending", () => {
  it("shows queued changes of this menu only, and marks follow the mark key", () => {
    const shown = applyPending(
      list("m1", [line("a"), line("sub", { markKey: "original" })]),
      [
        { ...check("a"), id: "1" },
        { ...check("b"), id: "2", menuId: "m2" },
        {
          id: "3",
          kind: "mark",
          menuId: "m1",
          itemKey: "original",
          displayName: "x",
          mark: {
            itemKey: "original",
            state: "later",
            substituteName: null,
            place: null,
          },
        },
      ],
    );
    expect(shown.items[0].checked).toBe(true);
    expect(shown.items[1].later).toBe(true);
  });
});
