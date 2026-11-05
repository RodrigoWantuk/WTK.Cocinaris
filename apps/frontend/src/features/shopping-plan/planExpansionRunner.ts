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

type PlanListener = (plan: ShoppingPlan) => void;
type StopListener = (code: string) => void;

/**
 * Expands accepted cards from the browser, one at a time (PLAN-0062, PLAN-0065). There is no server
 * queue yet (PLAN-0039), and expansion needs the signed-in request. One at a time leaves a slot of the
 * two AI requests a person may have running for the next batch of cards.
 *
 * There is one runner for the whole signed-in app, so moving between screens neither stops the work
 * nor starts a second copy racing the first. A card accepted while running waits {@link UNDO_WINDOW_MS},
 * so a quick undo costs nothing; a card already pending when its plan is first seen skips the wait.
 * Every request is idempotent on the server, so leaving and coming back simply continues.
 */
export class PlanExpansionRunner {
  private readonly seen = new Map<string, Map<string, number>>();
  private readonly planListeners = new Set<PlanListener>();
  private readonly stopListeners = new Set<StopListener>();
  private latest: ShoppingPlan | null = null;
  private options = { csrfToken: "", language: "" };
  private running = false;
  private disposed = false;
  private stopped = false;

  constructor(private readonly repo: ShoppingPlanRepository) {}

  /** Listens for plans the server returns and for the code of a stop. Returns the way to stop listening. */
  subscribe(onPlan: PlanListener, onStop: StopListener): () => void {
    this.planListeners.add(onPlan);
    this.stopListeners.add(onStop);
    return () => {
      this.planListeners.delete(onPlan);
      this.stopListeners.delete(onStop);
    };
  }

  /** Allows work again after a stop: a screen opening is the person coming back to try. */
  resume(): void {
    this.stopped = false;
  }

  /** Stops for good when the signed-in app goes away. */
  dispose(): void {
    this.disposed = true;
  }

  /** Takes the newest plan, and starts on any pending card. */
  track(
    plan: ShoppingPlan,
    options: { csrfToken: string; language: string },
  ): void {
    this.latest = plan;
    this.options = options;
    const now = Date.now();
    let times = this.seen.get(plan.sessionId);
    if (!times) {
      // Cards pending when the plan is first seen already had their chance to be undone.
      times = new Map(
        plan.accepted.map((card) => [card.cardId, now - UNDO_WINDOW_MS]),
      );
      this.seen.set(plan.sessionId, times);
    }
    for (const card of plan.accepted) {
      if (card.expansionState === "pending" && !times.has(card.cardId)) {
        times.set(card.cardId, now);
      }
    }
    if (this.running || this.stopped || this.disposed) return;
    if (!plan.accepted.some((card) => card.expansionState === "pending")) {
      return;
    }
    this.running = true;
    void this.work().finally(() => {
      this.running = false;
    });
  }

  private publish(plan: ShoppingPlan): void {
    this.latest = plan;
    for (const listener of this.planListeners) listener(plan);
  }

  private stop(code: string): void {
    this.stopped = true;
    for (const listener of this.stopListeners) listener(code);
  }

  private async work(): Promise<void> {
    for (;;) {
      const current = this.latest;
      const next = current?.accepted.find(
        (card) => card.expansionState === "pending",
      );
      if (!current || !next || this.disposed || this.stopped) return;
      const since =
        this.seen.get(current.sessionId)?.get(next.cardId) ?? Date.now();
      const remaining = UNDO_WINDOW_MS - (Date.now() - since);
      if (remaining > 0) {
        await wait(remaining);
        continue;
      }
      for (let attempt = 0; ; attempt += 1) {
        try {
          const { csrfToken, language } = this.options;
          this.publish(
            await this.repo.expand(current.sessionId, next.cardId, {
              csrfToken,
              language,
            }),
          );
          break;
        } catch (err) {
          const code = err instanceof RecipeApiError ? err.code : "unexpected";
          if (
            (code === "ai_operation_conflict" || code === "conflict") &&
            attempt < CONFLICT_RETRIES_MS.length
          ) {
            await wait(CONFLICT_RETRIES_MS[attempt]);
            continue;
          }
          if (this.disposed) return;
          // The server recorded the failure on the card, except for a conflict or a lost connection.
          try {
            const reloaded = await this.repo.get(current.sessionId);
            this.publish(reloaded);
            if (
              reloaded.accepted.some(
                (card) =>
                  card.cardId === next.cardId &&
                  card.expansionState === "pending",
              )
            ) {
              this.stop(code);
              return;
            }
          } catch {
            this.stop(code);
            return;
          }
          if (
            code === "ai_budget_exhausted" ||
            code === "ai_budget_unavailable" ||
            code === "ai_capability_unavailable"
          ) {
            this.stop(code);
            return;
          }
          break;
        }
      }
    }
  }
}
