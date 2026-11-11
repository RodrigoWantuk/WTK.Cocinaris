import type {
  ShoppingPlan,
  ShoppingPlanRepository,
} from "@/contracts/shoppingPlan";

/** Time to undo a "yes" before the server starts preparing the recipe, in milliseconds. */
export const UNDO_WINDOW_MS = 5000;
/** How often the plan is read while a dish is being prepared. */
export const POLL_MS = 4000;
/** Longer wait after a failed read, so a lost connection is not hammered. */
const POLL_AFTER_ERROR_MS = 15000;

const wait = (ms: number) =>
  new Promise((resolve) => globalThis.setTimeout(resolve, ms));

type PlanListener = (plan: ShoppingPlan) => void;

const hidden = () =>
  typeof document !== "undefined" && document.visibilityState === "hidden";

/**
 * Follows the preparation of chosen dishes (PLAN-0039 Slice 2). The server prepares each recipe in a
 * job, with the app open or not, so the app only reads the plan while a dish is pending and the page
 * is visible, and passes each plan to the open screens. Reading the plan also asks the server to queue
 * a job that was lost, so a dish never waits for nothing.
 *
 * There is one follower for the whole signed-in app, so moving between screens neither stops it nor
 * starts a second copy.
 */
export class PlanExpansionRunner {
  private readonly planListeners = new Set<PlanListener>();
  private latest: ShoppingPlan | null = null;
  private running = false;
  private disposed = false;

  constructor(private readonly repo: ShoppingPlanRepository) {}

  /** Listens for plans the server returns. Returns the way to stop listening. */
  subscribe(onPlan: PlanListener): () => void {
    this.planListeners.add(onPlan);
    return () => {
      this.planListeners.delete(onPlan);
    };
  }

  /** Stops for good when the signed-in app goes away. */
  dispose(): void {
    this.disposed = true;
  }

  /** Takes the newest plan, and follows it while a dish is pending. */
  track(plan: ShoppingPlan): void {
    this.latest = plan;
    if (this.running || this.disposed || !pending(plan)) return;
    this.running = true;
    void this.follow().finally(() => {
      this.running = false;
    });
  }

  private publish(plan: ShoppingPlan): void {
    this.latest = plan;
    for (const listener of this.planListeners) listener(plan);
  }

  private async follow(): Promise<void> {
    while (!this.disposed && this.latest && pending(this.latest)) {
      await wait(POLL_MS);
      const current = this.latest;
      if (this.disposed || !current || !pending(current) || hidden()) continue;
      try {
        this.publish(await this.repo.get(current.sessionId));
      } catch {
        await wait(POLL_AFTER_ERROR_MS);
      }
    }
  }
}

function pending(plan: ShoppingPlan): boolean {
  return plan.accepted.some((card) => card.expansionState === "pending");
}
