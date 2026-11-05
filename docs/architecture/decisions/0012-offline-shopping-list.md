# ADR-0012 — Offline shopping list: a precache-only service worker and owner-scoped IndexedDB

- **Status:** Accepted
- **Date:** 2026-11-05
- **Owners:** Repository owner; Claude Code (implementation)
- **Decision scope:** Frontend delivery, browser storage, session bootstrap, deployment rollback
- **Related:** [PLAN-0062](../../plans/PLAN-0062-guided-shopping-planning.md) Slice 3, [ADR-0004](0004-identity-and-browser-session.md), [ADR-0008](0008-frontend-toolchain-vite.md)
- **Supersedes:** None
- **Superseded by:** None

## Context

People lose signal inside markets, which is exactly when they read the shopping list and check lines off. The owner chose an offline list in the PLAN-0062 discovery. The application had no service worker and kept no personal data in browser storage, and sessions live only in an HttpOnly cookie managed by the backend.

## Decision

1. **Service worker for assets only.** `vite-plugin-pwa` in `generateSW` mode precaches the built files, and navigations fall back to the cached `index.html`. `/api/`, `/signin-oidc`, `/signout-callback-oidc`, and `/health/` are denied from that fallback, and no runtime caching route exists. **The worker never sees API responses, so it cannot store personal data.** It exists only in the production build.
2. **List data lives in IndexedDB, through application code.** `idb-keyval` stores the last lists (at most five menus), the outbox, and unapplied marks, with every key prefixed by the owner id. If IndexedDB is unavailable, the same store runs in memory for that page.
3. **Only check marks and "buy later" work offline.** They are queued in an outbox. Anything that changes the pantry or needs a server decision ("Comprei", "Já tenho", arrival, substitutes, places, package sizes) is disabled with "Precisa de internet", because it needs validation that cannot run in the browser.
4. **Delivery.** The outbox is sent in order on the `online` event, on app open, when the page becomes visible, and every 30 seconds while something is pending. Marks are last write wins, with no prompt. The 30-second retry exists because `navigator.onLine` stays true on the weak signal typical of a market.
5. **Conflicts.** Before sending, the outbox is compared with the current list. A line no longer there, a menu that no longer exists, or a mark the server refuses (400, 409, 422) is dropped and named in "N marcações não se aplicam mais". An expired session (401) or an unreachable or failing server (5xx) keeps the outbox for later.
6. **Offline session state.** A **network** failure on session load, with a remembered owner, produces the session state `offline`. It has no CSRF token, `isAuthenticated` stays false, and it allows only `/app/compras` and `/app/cardapio/:id/compras`. The remembered id only selects local data: it authorizes nothing on the server, and the server still answers 401 to anything sent without a session.
7. **Clearing.** A logout clears all offline data after the server confirms it. Another owner signing in clears the previous owner's data, including unsent marks, before the session becomes available. A 401 keeps the data so the same owner can resume, but disables the offline shell until they sign in again.

## Alternatives considered

- **Caching API responses in the worker:** rejected. Cached responses would survive logout unless each path were tracked, and a shared device could show another owner's list.
- **`localStorage`:** rejected. It is synchronous and small, and its origin-wide keys would make accidental cross-owner reads easier.
- **Reloading the page when a new worker activates:** rejected. It could discard a form being filled in.

## Consequences

- An installed or returning visitor gets the last deployed assets on the first load after a deploy; the new worker activates at once and the following load is current. API changes must therefore stay additive across one deploy, which the forward-only migrations already require.
- **Rollback hazard.** A release without `sw.js` would not remove the worker: Caddy answers the update check with `index.html`, the check fails, and the old worker keeps serving the old application. `deploy-linux-host.sh --rollback` therefore installs `infrastructure/deploy/linux-host/sw-unregister.js` as `sw.js` when the target release has none. That script clears the caches, unregisters the worker, and reloads open pages.
- Ingredient and dish names are now on the device until logout or until another owner signs in. A person who leaves without logging out leaves their last list readable offline on that device, as a downloaded file would be. The privacy document records this.
- `workbox-build` brought vulnerable versions of `glob`; `package.json` pins patched versions through `resolutions`, and the same for the `minimatch` used by `typescript-eslint`. Both are build-time only.
- A Content-Security-Policy, if enabled later, is covered by the template's `script-src 'self'`, which `worker-src` falls back to.
