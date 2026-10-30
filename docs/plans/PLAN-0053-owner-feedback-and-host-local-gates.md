# PLAN-0053: Owner Feedback Fixes and Host-Local Merge Gates

- **Status:** Blocked
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0053-owner-feedback-local-gates`
- **Pull request:** Not opened
- **Related documents:** `docs/product/entry-and-contextual-home.md`, `docs/product/audience-and-profile.md`, `docs/security/privacy-and-data-protection.md`, `docs/development/environment.md`, `AGENTS.md` §9 and §11

## Owner decisions recorded (2026-10-28)

- **D-4, no GitHub Actions:** "teremos que viver sem as pipelines do github nesse projeto". The GitHub account is locked for billing and Actions jobs never start. The merge gates move to a versioned script that runs on the owner host.
- **D-5, entry redirect:** "A home page do cocinaris não identifica que já tem uma sessão iniciada, deveria identificar e levar pra home". A visitor with a valid session who opens `/` goes to `/app/hoje`.
- **D-6, age requirement presentation:** "vamos parar de esfregar na cara do usuário toda hora que o aplicativo é pra maiores de idade. Colocamos isso nos termos de uso, no máximo pedimos uma confirmação formal do usuário ser maior de idade durante o cadastro". The 18+ policy and the recorded declaration remain; the interface asks once and does not repeat age notices.

- **Owner report (2026-10-28):** "A página de preencher perfil do usuário está toda quebrada ... depois de salvar, Suas alterações foram salvas, mas não foi possível carregar os dados mais recentes do perfil." Root cause below (item 5).

## Scope

### Included

1. D-5: the `/` route checks the session and redirects signed-in visitors. The public page renders immediately for everyone else and still never reads the session itself.
2. D-6:
   - remove the age notice from the public footer;
   - reword the entry hero line so it no longer addresses "adults";
   - show the profile adult-confirmation card only while the confirmation is pending;
   - remove the strings used only by the completed-confirmation details;
   - update the product, journey and privacy documents.
3. D-4:
   - `scripts/ci/run-local-gates.sh` runs every step of `backend.yml` and `frontend.yml` (including the browser smoke and the gitleaks secret scan) on a clean worktree of a committed ref, and writes a summary per commit under `artifacts/local-gates/<sha>/`;
   - environment guide section;
   - proposed `AGENTS.md` change below.
4. The secret scan flagged a browser-storage key name (`LS_PREFS_KEY`) in historical commit `e248126`. That is a false positive: it is not a credential. A narrow line allowlist was added to `.gitleaks.toml`.

5. **Proxy-altered ETags (production defect).** At the origin, Caddy's `encode` returned `etag: "<token>-gzip"` for compressed responses, and Cloudflare removes strong ETags from responses it re-encodes. The profile workspace verifies that its four parallel reads (profile, preferences, equipment, completeness) carry the same version and ETag. Behind the proxy they did not, so every resync after a save failed, and the workspace then blocked further edits until a reload. Tests and local runs do not pass through the proxy. Fix:
   - API responses under `/api` now carry `Cache-Control: no-store, no-transform`; private data must not be cached anyway;
   - the Caddy site block compresses only static files (repository template and live host; previous live file backed up under `/var/backups/wtk-cocinaris/`);
   - integration test `ApiResponsesForbidCachingAndTransformationSoProxiesKeepETags`.

6. **Profile saving made predictable.** The data page's per-field button was labelled "Usar e salvar este padrão" but only filled the field; the section's Salvar button saved it. It is now "Usar este padrão". Each page states its rule in the subtitle:
   - data: each section saves with its own Salvar button, nothing before;
   - preferences: each item saves on add or remove;
   - equipment: add, remove and reorder, then Salvar.
7. **No permanent age card.** Without published terms the adult confirmation cannot be completed, and the overview showed "ainda não está disponível nesta versão" on every visit. The card now appears only when a confirmation is possible and pending.
8. **Proxy replica check.** `scripts/e2e/proxy-replica.sh` runs the API behind a separate Caddy that uses the repository template, on `https://localhost:7443` with the OIDC fixture. `scripts/e2e/profile-journey.mjs` saves on every profile page through it. With the previous release's API and the previous template, the replica reproduced the defect: `/api/v1/profile` was compressed with ETag `"<token>-zstd"`, while the smaller preferences and equipment responses kept `"<token>"`, and the page reported "Não foi possível carregar seu perfil". With this branch, every step passes.

### Excluded

- The PLAN-0005 headed-browser validation workflow (it needs Xvfb and a headed Firefox; it is run on demand with `scripts/plan-0005/`).
- A first-access onboarding screen for the adult confirmation (today the pending confirmation is in the profile overview; a dedicated first-access step can follow the account onboarding work).

## Proposed `AGENTS.md` change (owner to authorize)

The owner applies it, or explicitly instructs the agent to apply it, because agents do not change the rules that govern their own validation.

In §9 "Lean risk-based validation", replace "and **one final CI execution on the PR head**" with:

> and one final run of `scripts/ci/run-local-gates.sh --ref <PR head>` on the owner host. GitHub Actions does not run for this repository (owner decision 2026-10-28); the summary under `artifacts/local-gates/<sha>/summary.md` replaces the CI run, and its result is recorded in the PR description.

In §9, replace "After CI passes on the final head, record workflow run IDs" with "After the local gates pass on the final head, record the commit and summary".

In §11, read "Do not merge failing checks" as "do not merge a head whose local gates failed".

## Acceptance criteria

- [x] Signed-in visitors at `/` reach `/app/hoje`; signed-out visitors see the public page without delay; tests cover both.
- [x] No age notice on the public page; the profile shows the adult confirmation only while pending; product and privacy documents updated.
- [ ] `scripts/ci/run-local-gates.sh` passes on this branch's head, and the result is recorded.
- [ ] Deployed to the owner host (owner standing instruction).
- [ ] `AGENTS.md` updated by or on explicit instruction of the owner.

## Execution state

- **Current run delivery target:** All included items.
- **Current checkpoint:** D-5 and D-6 implemented, and the frontend validated (Vitest 51 files / 423 tests, typecheck, lint, format, guards). The local gate script is written; the gitleaks false positive is allowlisted, and the full history scan is clean.
- **Exact next action:** The owner updates `AGENTS.md` §9/§11 with the wording in this plan (local gates instead of CI); then mark the plan completed.
- **Blockers:** `AGENTS.md` change awaits the owner.
- **Working tree state:** Clean; merged via #48.

## Progress log

### 2026-10-28T23:30:00Z — Claude Code

- Plan created from three owner messages (D-4, D-5, D-6). Implemented D-5 and D-6 with tests. The old entry test that asserted "no session request on `/`" encoded the previous engineering rule from PLAN-0011; it now asserts that the session check is the only request.
- Wrote the local gate script. Verified the pinned gitleaks image over full history: one false positive (storage key name), allowlisted narrowly, then no leaks.

### 2026-10-28T23:50:00Z — Claude Code

- Diagnosed the owner's profile report: proxy compression altered or removed API ETags (verified at the origin: `"dmgu8eyhbvuouf"` uncompressed vs `"dmgu8eyhbvuouf-gzip"` compressed). Added the API cache header and limited Caddy compression to static files; the live Caddy reload was validated and other sites on the host still answer. Integration test class passes (5/5).
- Mixed save behavior across profile pages (immediate list actions, per-form save buttons, a deferred equipment save) is reviewed next under this plan.

### 2026-10-29T00:30:00Z — Claude Code

- Resumed after a dropped connection.
  - The interrupted deploy of `f9508da` had already switched the release and started the service; only old-release pruning did not run.
  - Verified through the CDN: `/api` responses carry `Cache-Control: no-store, no-transform` and are not compressed.
  - Removed the leftover gate worktree.
- Reproduced the owner's profile failure in the proxy replica and verified the fix (details in item 8). The fix's header alone already prevents the defect behind the old template, because Caddy does not transform `no-transform` responses.
- Implemented items 6 and 7 with tests. Frontend checks: Vitest 51 files / 424 tests, typecheck, lint, format.

### 2026-10-30T18:00:00Z — Claude Code (registry reconciliation, PLAN-0057 Phase 0)

- The work merged via #48 and is deployed. GitHub Actions does not run for this repository, so `scripts/ci/run-local-gates.sh` is the gate. The only open item is the owner's `AGENTS.md` change, so the plan is Blocked on it.
