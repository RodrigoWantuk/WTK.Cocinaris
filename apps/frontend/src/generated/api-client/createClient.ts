import createClient from "openapi-fetch";
import type { paths } from "./generated/schema";

/**
 * Creates a typed OpenAPI fetch client for Cocinaris v1.
 * Callers must supply same-origin credentials and never store OIDC tokens.
 */
export function createCocinarisClient(options?: {
  /** API origin; empty string means same-origin relative `/api/v1` paths. */
  baseUrl?: string;
  /** Optional AbortSignal applied as a default fetch init merge. */
  signal?: AbortSignal;
  fetch?: (input: Request) => Promise<Response>;
}) {
  return createClient<paths>({
    baseUrl: options?.baseUrl ?? "",
    fetch: options?.fetch,
    credentials: "include",
  });
}

export type CocinarisClient = ReturnType<typeof createCocinarisClient>;
