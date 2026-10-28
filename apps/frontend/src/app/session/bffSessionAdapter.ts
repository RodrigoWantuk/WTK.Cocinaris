import {
  createCocinarisClient,
  readProblemDetails,
} from "@cocinaris/api-client";
import { normalizeSafeReturnUrl } from "@/lib/safeReturnUrl";
import type { SessionAdapter, SessionState } from "./types";

/**
 * Live BFF session adapter using same-origin cookies and CSRF.
 * Never reads or writes OIDC tokens in browser storage.
 */
export function createBffSessionAdapter(options?: {
  fetchImpl?: typeof fetch;
  /** Optional navigation hook for tests; defaults to form submit / location.assign. */
  navigate?: (url: string) => void;
}): SessionAdapter {
  const fetchImpl =
    options?.fetchImpl ??
    ((input: RequestInfo | URL, init?: RequestInit) =>
      globalThis.fetch(input, init));
  const client = createCocinarisClient({
    fetch: (request) => fetchImpl(request),
  });
  let lastCsrf: string | null = null;

  return {
    async getSession(): Promise<SessionState> {
      try {
        const { data, response, error } = await client.GET("/api/v1/session", {
          credentials: "include",
        });
        if (response.status === 401) {
          // Contract currently emits authentication_required for absent/invalid sessions.
          // Do not invent an "expired" distinction without a stable backend errorCode.
          void error;
          return { status: "signedOut", internalUserId: null, csrfToken: null };
        }
        if (!response.ok || !data) {
          if (response.status >= 500 || response.status === 0) {
            return {
              status: "unavailable",
              internalUserId: null,
              csrfToken: null,
            };
          }
          return { status: "signedOut", internalUserId: null, csrfToken: null };
        }
        lastCsrf = data.csrfToken ?? null;
        return {
          status: "authenticated",
          internalUserId: data.userId,
          csrfToken: data.csrfToken,
          displayName: data.displayName ?? null,
          language: data.language ?? null,
          timeZone: data.timeZone ?? null,
          measurementSystem: data.measurementSystem ?? null,
          profileExists: data.profileExists,
          profilePercentComplete:
            data.profilePercentComplete == null
              ? null
              : Number(data.profilePercentComplete),
          adultDeclarationState: data.adultDeclarationState ?? null,
          supportedLocales: data.supportedLocales ?? [],
        };
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") {
          throw err;
        }
        return { status: "unavailable", internalUserId: null, csrfToken: null };
      }
    },

    beginLogin(returnUrl?: string): void {
      const safe = normalizeSafeReturnUrl(returnUrl);
      const action = `/api/v1/auth/login?returnUrl=${encodeURIComponent(safe)}`;
      if (options?.navigate) {
        // Tests may observe the challenge URL without a real navigation.
        options.navigate(action);
        return;
      }
      const form = document.createElement("form");
      form.method = "POST";
      form.action = action;
      form.style.display = "none";
      document.body.appendChild(form);
      form.submit();
    },

    async logout(): Promise<void> {
      const csrf = lastCsrf;
      if (!csrf) {
        // Refresh once to obtain a request-specific CSRF before logout.
        const session = await this.getSession();
        if (session.status !== "authenticated" || !session.csrfToken) {
          if (options?.navigate) {
            options.navigate("/");
          } else {
            window.location.assign("/");
          }
          return;
        }
        lastCsrf = session.csrfToken;
      }
      const token = lastCsrf!;
      const response = await fetchImpl("/api/v1/auth/logout", {
        method: "POST",
        credentials: "include",
        headers: { "X-CSRF-TOKEN": token },
        redirect: "manual",
      });
      if (response.status === 400) {
        await readProblemDetails(response);
        throw new Error("logout_csrf_failed");
      }
      lastCsrf = null;
      if (options?.navigate) {
        options.navigate("/");
      } else {
        window.location.assign("/");
      }
    },
  };
}
