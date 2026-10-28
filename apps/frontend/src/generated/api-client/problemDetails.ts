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
 * Returns problem details for a failed response, or null for empty or non-object bodies.
 *
 * Pass `parsedBody` (the `error` value returned by the generated client) whenever it is
 * available: openapi-fetch has already consumed the response body to produce it, and
 * browsers reject `clone()` on a consumed body, so re-reading would silently lose the
 * error code and field errors. Without `parsedBody` the response is read via a clone.
 */
export async function readProblemDetails(
  response: Response,
  parsedBody?: unknown,
): Promise<CocinarisProblemDetails | null> {
  if (parsedBody !== undefined) {
    return parsedBody && typeof parsedBody === "object"
      ? (parsedBody as CocinarisProblemDetails)
      : null;
  }
  if (response.bodyUsed) {
    return null;
  }
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
