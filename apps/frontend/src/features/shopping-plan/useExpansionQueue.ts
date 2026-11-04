import { useEffect, useRef, useState } from "react";
import { RecipeApiError } from "@/contracts/recipes";
import type {
  ShoppingPlan,
  ShoppingPlanRepository,
} from "@/contracts/shoppingPlan";

/** Time to undo a "yes" before any AI work starts, in milliseconds. */
export const UNDO_WINDOW_MS = 5000;
/** Waits before retrying when the person has other AI work running. */
const CONFLICT_RETRIES_MS = [3000, 6000, 12000];

const wait = (ms: number) =>
  new Promise((resolve) => globalThis.setTimeout(resolve, ms));

/**
 * Expands accepted cards from the browser, one at a time (PLAN-0062). There is no server queue yet
 * (PLAN-0039), and expansion needs the signed-in request. One at a time leaves a slot of the two AI
 * requests a person may have running for the next batch of cards.
 *
 * A card accepted on this screen waits {@link UNDO_WINDOW_MS}, so a quick undo costs nothing; a card
 * already pending when the plan opens skips the wait. Every request is idempotent on the server, so
 * leaving and coming back simply continues.
 *
 * @param plan The current plan; expansion runs while it has pending cards.
 * @param repo Repository.
 * @param options CSRF token and interface language for requests.
 * @param onPlan Receives each plan the server returns.
 * @returns A stable error code when expansion stopped because AI is unavailable or out of budget.
 */
export function useExpansionQueue(
  plan: ShoppingPlan | null,
  repo: ShoppingPlanRepository,
  options: { csrfToken: string | null | undefined; language: string },
  onPlan: (plan: ShoppingPlan) => void,
): string | null {
  const [stopped, setStopped] = useState<string | null>(null);
  const running = useRef(false);
  const unmounted = useRef(false);
  const seen = useRef<Map<string, number> | null>(null);
  const latestPlan = useRef(plan);
  latestPlan.current = plan;
  const onPlanRef = useRef(onPlan);
  onPlanRef.current = onPlan;

  useEffect(() => {
    unmounted.current = false;
    return () => {
      unmounted.current = true;
    };
  }, []);

  const pendingIds = (plan?.accepted ?? [])
    .filter((card) => card.expansionState === "pending")
    .map((card) => card.cardId)
    .join(",");

  useEffect(() => {
    if (!plan || !options.csrfToken || stopped) return;
    const now = Date.now();
    if (seen.current === null) {
      // Cards pending when the plan opens already had their chance to be undone.
      seen.current = new Map(
        plan.accepted.map((card) => [card.cardId, now - UNDO_WINDOW_MS]),
      );
    }
    const pending = pendingIds ? pendingIds.split(",") : [];
    for (const id of pending) {
      if (!seen.current.has(id)) seen.current.set(id, now);
    }
    if (pending.length === 0 || running.current) return;

    running.current = true;
    const csrfToken = options.csrfToken;
    const language = options.language;
    const work = async () => {
      for (;;) {
        const current = latestPlan.current;
        const next = current?.accepted.find(
          (card) => card.expansionState === "pending",
        );
        if (!current || !next || unmounted.current) return;
        const since = seen.current?.get(next.cardId) ?? Date.now();
        const remaining = UNDO_WINDOW_MS - (Date.now() - since);
        if (remaining > 0) {
          await wait(remaining);
          continue;
        }
        for (let attempt = 0; ; attempt += 1) {
          try {
            const updated = await repo.expand(current.sessionId, next.cardId, {
              csrfToken,
              language,
            });
            if (!unmounted.current) {
              latestPlan.current = updated;
              onPlanRef.current(updated);
            }
            break;
          } catch (err) {
            const code =
              err instanceof RecipeApiError ? err.code : "unexpected";
            if (
              (code === "ai_operation_conflict" || code === "conflict") &&
              attempt < CONFLICT_RETRIES_MS.length
            ) {
              await wait(CONFLICT_RETRIES_MS[attempt]);
              continue;
            }
            if (unmounted.current) return;
            // The server recorded the failure on the card, except for a conflict or a lost connection.
            try {
              const reloaded = await repo.get(current.sessionId);
              latestPlan.current = reloaded;
              onPlanRef.current(reloaded);
              const still = reloaded.accepted.some(
                (card) =>
                  card.cardId === next.cardId &&
                  card.expansionState === "pending",
              );
              if (still) {
                setStopped(code);
                return;
              }
            } catch {
              setStopped(code);
              return;
            }
            if (
              code === "ai_budget_exhausted" ||
              code === "ai_budget_unavailable" ||
              code === "ai_capability_unavailable"
            ) {
              setStopped(code);
              return;
            }
            break;
          }
        }
      }
    };
    void work().finally(() => {
      running.current = false;
    });
    // Runs again when the set of pending cards changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingIds, plan?.sessionId, options.csrfToken, stopped, repo]);

  return stopped;
}
