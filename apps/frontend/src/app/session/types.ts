/**
 * Backend-managed session states exposed to the UI.
 * Tokens and credentials must never be stored in the browser by these adapters.
 */
export type SessionStatus =
  | "loading"
  | "authenticated"
  | "signedOut"
  | "expired"
  | "unavailable";

/**
 * Safe session projection for production UI. Never includes OIDC tokens.
 */
export interface SessionState {
  status: SessionStatus;
  /** Internal Cocinaris user id when authenticated; never an OIDC token. */
  internalUserId?: string | null;
  /** CSRF token from GET /api/v1/session when available. */
  csrfToken?: string | null;
  displayName?: string | null;
  language?: string | null;
  timeZone?: string | null;
  measurementSystem?: string | null;
  profileExists?: boolean;
  profilePercentComplete?: number | null;
  adultDeclarationState?: string | null;
  supportedLocales?: string[];
}

/**
 * Session boundary compatible with the backend-managed browser session.
 */
export interface SessionAdapter {
  getSession(): Promise<SessionState>;
  beginLogin(returnUrl?: string): void;
  logout(): Promise<void>;
}
