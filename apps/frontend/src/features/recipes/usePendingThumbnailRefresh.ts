import { useEffect, useRef } from "react";

/** Waits between refreshes while an image is pending: about 1.5 minutes in all, then it stops. */
const DELAYS_MS = [3000, 4000, 5000, 8000, 10000, 15000, 20000, 30000];

/**
 * Refreshes recipe data while a dish image is still being generated (PLAN-0030), so the image
 * appears without a manual reload. Generation takes about 5 seconds after the job picks it up.
 *
 * Each refresh must produce new data (a new `token`) to schedule the next one, so a failed
 * refresh ends the polling instead of retrying forever. The schedule resets once nothing is
 * pending, and the pending timer is cleared on unmount.
 *
 * @param pending Whether any shown image is `pending`.
 * @param token The loaded data; changes each time a refresh delivers new data.
 * @param refresh Reloads the data without showing a loading state.
 */
export function usePendingThumbnailRefresh(
  pending: boolean,
  token: unknown,
  refresh: () => void,
): void {
  const attemptRef = useRef(0);
  const refreshRef = useRef(refresh);
  useEffect(() => {
    refreshRef.current = refresh;
  }, [refresh]);

  useEffect(() => {
    if (!pending) {
      attemptRef.current = 0;
      return;
    }
    const delay = DELAYS_MS[attemptRef.current];
    if (delay === undefined) return;
    const handle = window.setTimeout(() => {
      attemptRef.current += 1;
      refreshRef.current();
    }, delay);
    return () => window.clearTimeout(handle);
  }, [pending, token]);
}
