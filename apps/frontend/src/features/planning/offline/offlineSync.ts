import type { PlanningRepository } from "@/contracts/planning";
import { RecipeApiError } from "@/contracts/recipes";
import { lineMatches, type OfflineListStore } from "./offlineListStore";

/** Whether a failure means "try again later": no connection, or the service is failing. */
export function isUnreachable(error: unknown): boolean {
  return error instanceof RecipeApiError && error.code === "unavailable";
}

/** Why a delivery stopped; `done` means the outbox was processed. */
export type FlushOutcome = "done" | "unreachable" | "signed_out";

/** Failures that keep the outbox. Anything else means the server refused that one change. */
function stopReason(error: unknown): FlushOutcome | null {
  if (isUnreachable(error)) return "unreachable";
  if (error instanceof RecipeApiError) {
    return error.status === 401 ? "signed_out" : null;
  }
  return "unreachable";
}

/**
 * Sends queued changes in order (ADR-0012). A line no longer on the list, a menu that is gone, or a
 * change the server refuses becomes a conflict to report. A 401 or an unreachable server stops here
 * and keeps the rest. Each entry leaves the outbox only after it is settled, so an interrupted
 * delivery repeats at most one idempotent `PUT`.
 */
export async function flushOutbox({
  store,
  owner,
  repo,
  csrfToken,
  today,
}: {
  store: OfflineListStore;
  owner: string;
  repo: PlanningRepository;
  csrfToken: string;
  today: string;
}): Promise<FlushOutcome> {
  const lists = new Map<
    string,
    Awaited<ReturnType<PlanningRepository["getShopping"]>> | null
  >();
  for (const entry of await store.outbox(owner)) {
    if (!lists.has(entry.menuId)) {
      try {
        lists.set(entry.menuId, await repo.getShopping(entry.menuId, today));
      } catch (error) {
        const stop = stopReason(error);
        if (stop) return stop;
        lists.set(entry.menuId, null);
      }
    }
    const applies = lists
      .get(entry.menuId)
      ?.items.some((item) => lineMatches(item, entry));
    let refused = !applies;
    if (applies) {
      try {
        if (entry.kind === "check") {
          await repo.setChecked(entry.menuId, entry.itemKey, entry.checked, {
            csrfToken,
          });
        } else if (repo.setLineMark) {
          await repo.setLineMark(entry.menuId, entry.mark, { csrfToken });
        }
      } catch (error) {
        const stop = stopReason(error);
        if (stop) return stop;
        refused = true;
      }
    }
    if (refused) await store.addConflict(owner, entry.displayName);
    await store.removeEntry(owner, entry.id);
  }
  return "done";
}
