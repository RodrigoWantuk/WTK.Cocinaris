# PLAN-0031: Establish the Owner Linux Host as the Development and Deployment Environment

- **Status:** Ready
- **Type:** Operations
- **Priority:** Critical
- **Owner:** Claude Code
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** `agent/plan-0031-owner-host-environment` (stacked on PLAN-0030)
- **Pull request:** Not opened
- **Related ADRs:** ADR-0004, ADR-0006
- **Dependencies:** PLAN-0030 (names used in scripts and runbooks)

## Objective

Make the owner's Debian 13 host, which serves `cocinaris.rwantuk.com.br`, a complete development, test, build and deployment environment for Cocinaris, with the procedures versioned in this repository and the identity provider described only as a generic OIDC provider.

## Context

On 2026-10-28 the host had .NET 10.0.302 at `/opt/dotnet` (not on `PATH`), Node.js 20.19.2, no Yarn, and no Docker. A Cocinaris deployment existed with these parts:

- a systemd service running as an unprivileged user on `127.0.0.1:8004`;
- static frontend files served by the host reverse proxy;
- PostgreSQL 17 with an owner role for migrations and an application role for data;
- a daily `pg_dump` timer.

Its procedure lived outside this repository and built from the same checkout that agents develop in. `AGENTS.md` §13 allowed remote Git/GitHub operations only from a different machine.

## Scope

### Included

1. **Tooling:** Docker Engine (daemon configured with `iptables: false` so host firewall and forwarding stay untouched; only localhost port publishing), Docker Compose, Node.js 24 isolated under `/opt` (system Node untouched), Yarn through Corepack, `dotnet` on `PATH` for interactive shells.
2. **Development fixtures:** compose port bindings configurable, so the development PostgreSQL does not collide with a host PostgreSQL on 5432.
3. **Environment guide:** `docs/development/environment.md` gets an "Owner Linux host" section with exact verification commands.
4. **Deployment in the repository:**
   - `infrastructure/deploy/linux-host/` with a systemd unit template, reverse-proxy snippet, backup unit and timer, and an environment-file example with placeholders only;
   - `scripts/deploy/deploy-linux-host.sh`, which builds from a clean worktree of a chosen ref (default `origin/main`) instead of the development checkout, backs up, applies idempotent migrations as the schema owner, publishes, and health-checks;
   - an operations runbook covering first install, update, rollback/forward repair, restore rehearsal, the rename migration from PLAN-0030, and log locations.
5. **Identity documentation:** deployments document a generic OIDC provider (authority, client ID/secret, redirect and sign-out callback paths). ADR-0004 gets an amendment note stating that Keycloak is the development/test fixture and any standards-compliant OIDC provider may back a deployment.
6. **Agent workflow:** `AGENTS.md` §13 authorizes this host for remote Git/GitHub operations using the owner-configured credentials, with the same safety rules (no merge, no pushes to `main`, no auth login/logout, never print tokens).
7. **Reconcile ADR-0006:** the owner host is the current development and deployment environment, and the Virginia production target stays as the accepted future production region.
8. **Deploy the renamed build** once it is merged, following the runbook. Merge itself remains the owner's action.

### Excluded

- Production in Virginia, CI/CD auto-deploy, secret manager adoption (recorded as follow-ups for PLAN-0051).
- Changing the deployment's identity provider or its registration.

## Acceptance criteria

- [ ] Backend unit, architecture and **integration** tests pass on the host, including the OIDC-fixture-dependent tests.
- [ ] Frontend gates pass on the host with Node.js 24 and Yarn via Corepack. The frontend workflow uses Node.js 24.
- [ ] The deployment script and runbook can rebuild the running deployment from a ref without touching the development checkout. Verified with a dry run (`--dry-run`) on the host.
- [ ] A backup restore rehearsal into a scratch database succeeds and is documented.
- [ ] No identity-provider product name appears in deployment documentation.
- [ ] `AGENTS.md` §13 reflects the host decision.

## Execution state

- **Current run delivery target:** All included items except item 8, which needs the owner merge.
- **Current checkpoint:** Docker installed with `iptables: false`; backend integration tests ran (164/175 passed; the 11 failures need the OIDC development fixture running, as in CI).
- **Exact next action:** After PLAN-0030 lands on the stack, install Node.js 24, adapt the compose ports, and run the full gate set.
- **Blockers:** None.

## Progress log

### 2026-10-28T14:45:00Z — Claude Code

- Plan created from owner instruction. Docker Engine 26.1.5 and Compose 2.26.1 installed with `iptables`/`ip6tables` disabled; existing sites verified up afterwards (HTTP 200).
