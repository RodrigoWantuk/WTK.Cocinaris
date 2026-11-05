import { createCocinarisClient } from "@cocinaris/api-client";
import type { PushRepository } from "@/contracts/pushNotifications";
import { throwForFailure, wrapAbort } from "./liveRecipeRepository";

const FALLBACK = "Unable to update notifications.";

/** Creates the notification repository (ADR-0013). */
export function createLivePushRepository(options?: {
  baseUrl?: string;
  fetchImpl?: typeof fetch;
}): PushRepository {
  const fetchImpl =
    options?.fetchImpl ??
    ((input: RequestInfo | URL, init?: RequestInit) =>
      globalThis.fetch(input, init));
  const client = createCocinarisClient({
    baseUrl: options?.baseUrl ?? "",
    fetch: (request) => fetchImpl(request),
  });
  const include = { credentials: "include" as const };

  async function mutate(
    call: () => Promise<{ error?: unknown; response: Response }>,
  ): Promise<void> {
    try {
      const { error, response } = await call();
      if (!response.ok) await throwForFailure(response, FALLBACK, error);
    } catch (err) {
      wrapAbort(err);
    }
  }

  return {
    async getStatus(signal) {
      try {
        const { data, error, response } = await client.GET(
          "/api/v1/notifications/push",
          { signal, ...include },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, FALLBACK, error);
        }
        return {
          available: data?.available === true,
          publicKey:
            typeof data?.publicKey === "string" ? data.publicKey : null,
        };
      } catch (err) {
        wrapAbort(err);
      }
    },
    subscribe: (keys, csrfToken) =>
      mutate(() =>
        client.PUT("/api/v1/notifications/push/subscription", {
          body: keys,
          params: { header: { "X-CSRF-TOKEN": csrfToken } },
          ...include,
        }),
      ),
    unsubscribe: (endpoint, csrfToken) =>
      mutate(() =>
        client.POST("/api/v1/notifications/push/subscription/removal", {
          body: { endpoint },
          params: { header: { "X-CSRF-TOKEN": csrfToken } },
          ...include,
        }),
      ),
  };
}
