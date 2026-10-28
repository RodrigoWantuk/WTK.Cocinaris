/**
 * Minimal RFC 7807 / Cocinaris problem-details shape from the OpenAPI contract.
 */
export interface CocinarisProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  errorCode?: string | null;
  traceId?: string | null;
  errors?: Record<string, string[] | undefined> | null;
}

/**
 * Parses a problem+json body when present; returns null for empty or non-object bodies.
 */
export async function readProblemDetails(
  response: Response,
): Promise<CocinarisProblemDetails | null> {
  const contentType = response.headers.get("content-type") ?? "";
  if (
    !contentType.includes("application/problem+json") &&
    !contentType.includes("application/json")
  ) {
    return null;
  }
  try {
    const body = (await response.clone().json()) as unknown;
    if (!body || typeof body !== "object") {
      return null;
    }
    return body as CocinarisProblemDetails;
  } catch {
    return null;
  }
}

/**
 * Returns the opaque ETag header value when present (including weak prefixes).
 */
export function readEtag(response: Response): string | null {
  return response.headers.get("ETag") ?? response.headers.get("etag");
}
