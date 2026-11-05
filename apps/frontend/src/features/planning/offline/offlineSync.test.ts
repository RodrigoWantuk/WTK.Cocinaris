import { describe, expect, it, vi } from "vitest";
import type { PlanningRepository, ShoppingItem } from "@/contracts/planning";
import { RecipeApiError } from "@/contracts/recipes";
import {
  createMemoryBackend,
  createOfflineListStore,
} from "./offlineListStore";
import { flushOutbox } from "./offlineSync";

const unavailable = () => new RecipeApiError("unavailable", "x", 503);

function setup(keys: string[] = ["a", "b"]) {
  const store = createOfflineListStore(createMemoryBackend());
  const repo = {
    getShopping: vi.fn(async (menuId: string) => ({
      menuId,
      from: "2026-11-05",
      mealCount: 1,
      items: keys.map((itemKey) => ({ itemKey }) as ShoppingItem),
    })),
    setChecked: vi.fn(async () => undefined),
    setLineMark: vi.fn(async () => undefined),
  } as unknown as PlanningRepository;
  const flush = () =>
    flushOutbox({
      store,
      owner: "ana",
      repo,
      csrfToken: "fresh",
      today: "2026-11-05",
    });
  const queue = (itemKey: string, menuId = "m1") =>
    store.enqueue("ana", {
      kind: "check",
      menuId,
      itemKey,
      checked: true,
      displayName: `name-${itemKey}`,
    });
  return { store, repo, flush, queue };
}

describe("flushOutbox", () => {
  it("sends in order with the fresh token and empties the outbox", async () => {
    const { store, repo, flush, queue } = setup();
    await queue("a");
    await queue("b");
    await store.enqueue("ana", {
      kind: "mark",
      menuId: "m1",
      itemKey: "a",
      displayName: "x",
      mark: { itemKey: "a", state: "later", substituteName: null, place: null },
    });
    expect(await flush()).toBe("done");
    expect(
      vi.mocked(repo.setChecked).mock.calls.map((call) => call[1]),
    ).toEqual(["a", "b"]);
    expect(vi.mocked(repo.setChecked).mock.calls[0][3]).toEqual({
      csrfToken: "fresh",
    });
    expect(repo.setLineMark).toHaveBeenCalledOnce();
    expect(repo.getShopping).toHaveBeenCalledOnce();
    expect(await store.outbox("ana")).toEqual([]);
    expect(await store.conflicts("ana")).toEqual([]);
  });

  it("reports a line no longer on the list instead of sending it", async () => {
    const { store, repo, flush, queue } = setup(["b"]);
    await queue("a");
    await queue("b");
    expect(await flush()).toBe("done");
    expect(
      vi.mocked(repo.setChecked).mock.calls.map((call) => call[1]),
    ).toEqual(["b"]);
    expect(await store.conflicts("ana")).toEqual(["name-a"]);
    expect(await store.outbox("ana")).toEqual([]);
  });

  it("reports every mark of a menu that no longer exists", async () => {
    const { store, repo, flush, queue } = setup();
    vi.mocked(repo.getShopping).mockRejectedValue(
      new RecipeApiError("not_found", "x", 404),
    );
    await queue("a");
    expect(await flush()).toBe("done");
    expect(await store.conflicts("ana")).toEqual(["name-a"]);
  });

  it("reports a change the server refuses and continues", async () => {
    const { store, repo, flush, queue } = setup();
    vi.mocked(repo.setChecked).mockRejectedValueOnce(
      new RecipeApiError("validation_failed", "x", 400),
    );
    await queue("a");
    await queue("b");
    expect(await flush()).toBe("done");
    expect(await store.conflicts("ana")).toEqual(["name-a"]);
    expect(repo.setChecked).toHaveBeenCalledTimes(2);
  });

  it("keeps the outbox on a 401, for the same owner after sign-in", async () => {
    const { store, repo, flush, queue } = setup();
    vi.mocked(repo.setChecked).mockRejectedValue(
      new RecipeApiError("authentication_required", "x", 401),
    );
    await queue("a");
    expect(await flush()).toBe("signed_out");
    expect(await store.outbox("ana")).toHaveLength(1);
    expect(await store.conflicts("ana")).toEqual([]);
  });

  it("stops without losing anything when the server cannot be reached", async () => {
    const { store, repo, flush, queue } = setup();
    vi.mocked(repo.setChecked)
      .mockResolvedValueOnce(undefined)
      .mockRejectedValue(unavailable());
    await queue("a");
    await queue("b");
    expect(await flush()).toBe("unreachable");
    const left = await store.outbox("ana");
    expect(left.map((entry) => entry.itemKey)).toEqual(["b"]);

    vi.mocked(repo.getShopping).mockRejectedValue(unavailable());
    expect(await flush()).toBe("unreachable");
    expect(await store.outbox("ana")).toHaveLength(1);
  });
});
