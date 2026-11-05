import { useCallback, useEffect, useRef, useState } from "react";
import { RecipeApiError } from "@/contracts/recipes";
import type { ShoppingPlan } from "@/contracts/shoppingPlan";
import { useShoppingPlanRepository } from "./ShoppingPlanProvider";

/** Why dishes were asked for; each kind shapes the request and what a failure means. */
export type MealBatchKind = "auto" | "more" | "request" | "like" | "switch";

/** One request for dishes of one meal type. */
export interface MealBatchJob {
  mealType: string;
  kind: MealBatchKind;
  /** A specific dish in the person's words, for `request`. */
  request?: string;
  /** The dish to stay close to, for `like`. */
  likeCardId?: string;
}

/** Why the last request for a meal type gave nothing, as a closed code. */
export interface MealBatchFailure {
  code: string;
  /** For `request_blocked`: the restriction's code. */
  restriction?: string;
  /** The words of a failed specific request, so they can be shown back. */
  request?: string;
}

/**
 * Asks the server for dishes, one request at a time (PLAN-0065). A person may run two AI requests at
 * once and recipe preparation needs one, so requests queue here instead of racing. A result belongs to
 * its meal type, not to whatever sheet is open when it comes back: the server's whole menu is
 * published, and each meal type reads its own options from it. A later switch request for a meal type
 * replaces a queued one, so only the last state is sent.
 *
 * @param sessionId The menu being built.
 * @param options CSRF token and interface language.
 * @param onPlan Receives each menu the server returns.
 */
export function useMealBatches(
  sessionId: string,
  options: { csrfToken: string | null | undefined; language: string },
  onPlan: (plan: ShoppingPlan) => void,
) {
  const repo = useShoppingPlanRepository();
  const [running, setRunning] = useState<MealBatchJob | null>(null);
  const [failures, setFailures] = useState<Record<string, MealBatchFailure>>(
    {},
  );
  const [exhausted, setExhausted] = useState<Set<string>>(new Set());
  const queue = useRef<MealBatchJob[]>([]);
  const busy = useRef(false);
  const asked = useRef<Set<string>>(new Set());
  const mounted = useRef(true);
  const onPlanRef = useRef(onPlan);
  onPlanRef.current = onPlan;
  const optionsRef = useRef(options);
  optionsRef.current = options;

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const drain = useCallback(async () => {
    if (busy.current) return;
    busy.current = true;
    for (;;) {
      const job = queue.current.shift();
      const { csrfToken, language } = optionsRef.current;
      if (!job || !csrfToken || !mounted.current) break;
      asked.current.add(job.mealType);
      setRunning(job);
      try {
        const plan = await repo.addBatch(
          sessionId,
          {
            mealType: job.mealType,
            request: job.kind === "request" ? (job.request ?? null) : null,
            likeCardId: job.kind === "like" ? (job.likeCardId ?? null) : null,
            replaceOffers: job.kind === "switch",
          },
          { csrfToken, idempotencyKey: crypto.randomUUID(), language },
        );
        if (!mounted.current) break;
        onPlanRef.current(plan);
        setFailures((current) => {
          const next = { ...current };
          delete next[job.mealType];
          return next;
        });
      } catch (err) {
        if (!mounted.current) break;
        const code = err instanceof RecipeApiError ? err.code : "unexpected";
        if (code === "no_more_options") {
          setExhausted((current) => new Set(current).add(job.mealType));
        }
        setFailures((current) => ({
          ...current,
          [job.mealType]: {
            code,
            restriction:
              err instanceof RecipeApiError
                ? err.fieldErrors.restriction?.[0]
                : undefined,
            request: job.kind === "request" ? job.request : undefined,
          },
        }));
      }
    }
    busy.current = false;
    if (mounted.current) setRunning(null);
  }, [repo, sessionId]);

  const enqueue = useCallback(
    (job: MealBatchJob) => {
      if (job.kind === "switch") {
        queue.current = queue.current.filter(
          (queued) =>
            !(queued.kind === "switch" && queued.mealType === job.mealType),
        );
      }
      if (job.kind === "auto" && queue.current.length > 0) return;
      queue.current.push(job);
      void drain();
    },
    [drain],
  );

  const clearFailure = useCallback((mealType: string) => {
    setFailures((current) => {
      if (!current[mealType]) return current;
      const next = { ...current };
      delete next[mealType];
      return next;
    });
  }, []);

  return {
    /** The request being answered, if any. */
    running,
    /** Why the last request for each meal type gave nothing. */
    failures,
    /** Meal types the server has no new dishes for. */
    exhausted,
    enqueue,
    clearFailure,
    /** Whether a meal type was asked for during this visit. */
    hasAsked: (mealType: string) => asked.current.has(mealType),
  };
}
