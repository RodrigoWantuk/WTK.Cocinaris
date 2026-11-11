import { createCocinarisClient } from "@cocinaris/api-client";
import type { PrivacyRepository } from "@/contracts/privacy";
import { throwForFailure, wrapAbort } from "./liveRecipeRepository";

const FALLBACK = "Unable to prepare your data.";

/** The name in `Content-Disposition`, or a dated default when the header is missing or unreadable. */
function fileNameOf(response: Response): string {
  const header = response.headers.get("Content-Disposition") ?? "";
  const star = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (star) {
    try {
      return decodeURIComponent(star[1]);
    } catch {
      // Falls through to the plain name.
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain?.[1] ?? "cocinaris-dados.zip";
}

/** Creates the privacy repository (PLAN-0049); requests carry the session cookie. */
export function createLivePrivacyRepository(options?: {
  baseUrl?: string;
  fetchImpl?: typeof fetch;
}): PrivacyRepository {
  const fetchImpl =
    options?.fetchImpl ??
    ((input: RequestInfo | URL, init?: RequestInit) =>
      globalThis.fetch(input, init));
  const client = createCocinarisClient({
    baseUrl: options?.baseUrl ?? "",
    fetch: (request) => fetchImpl(request),
  });
  const include = { credentials: "include" as const };

  return {
    async getCenter(signal) {
      try {
        const { data, error, response } = await client.GET("/api/v1/privacy", {
          signal,
          ...include,
        });
        if (!response.ok || !data)
          await throwForFailure(response, FALLBACK, error);
        return {
          contactEmail:
            typeof data?.contactEmail === "string" && data.contactEmail
              ? data.contactEmail
              : null,
        };
      } catch (err) {
        wrapAbort(err);
      }
    },
    async downloadExport(language, signal) {
      try {
        const { data, error, response } = await client.GET(
          "/api/v1/privacy/export",
          {
            params: { query: { language } },
            parseAs: "blob",
            signal,
            ...include,
          },
        );
        if (!response.ok || !data)
          await throwForFailure(response, FALLBACK, error);
        return { blob: data as Blob, fileName: fileNameOf(response) };
      } catch (err) {
        wrapAbort(err);
      }
    },
    async deleteAccount(confirmation, csrfToken) {
      try {
        const { error, response } = await client.POST(
          "/api/v1/privacy/account-deletion",
          {
            body: { confirmation },
            params: { header: { "X-CSRF-TOKEN": csrfToken } },
            ...include,
          },
        );
        if (response.status !== 202)
          await throwForFailure(response, FALLBACK, error);
      } catch (err) {
        wrapAbort(err);
      }
    },
  };
}
