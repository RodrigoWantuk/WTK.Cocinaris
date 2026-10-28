# PLAN-0031: Establish the Owner Linux Host as the Development and Deployment Environment

- **Status:** In Progress
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
6. **Agent workflow:** propose an `AGENTS.md` §13 change that authorizes this host for remote Git/GitHub operations with owner-configured credentials. **The owner applies it**: agents may not change the rules that govern their own remote authority (an agent attempt on 2026-10-28 was blocked by the agent permission classifier). Until then, no push or PR is made from this host.
7. **Reconcile ADR-0006:** the owner host is the current development and deployment environment, and the Virginia production target stays as the accepted future production region.
8. **Deploy the renamed build** once it is merged, following the runbook. Merge itself remains the owner's action.

### Excluded

- Production in Virginia, CI/CD auto-deploy, secret manager adoption (recorded as follow-ups for PLAN-0051).
- Changing the deployment's identity provider or its registration.

## Acceptance criteria

- [x] Backend unit, architecture and **integration** tests pass on the host, including the OIDC-fixture-dependent tests.
- [x] Frontend gates pass on the host with Node.js 24 and Yarn via Corepack. The frontend workflow uses Node.js 24.
- [x] The deployment script and runbook can rebuild the running deployment from a ref without touching the development checkout. Verified with a dry run (`--dry-run`) on the host.
- [x] A backup restore rehearsal into a scratch database succeeds and is documented.
- [x] No identity-provider product name appears in deployment documentation.
- [ ] `AGENTS.md` §13 reflects the host decision (owner action; proposed text below).

## Proposed `AGENTS.md` §13 change (owner to apply)

Insert before "### Quando o hostname for diferente" and adapt the publish check to accept both hostnames:

```markdown
### Quando o hostname for `rwantuk` (host de desenvolvimento e deploy)

Desde 2026-10-28 o host Linux `rwantuk` é o ambiente canônico de desenvolvimento, teste, build e deploy
do Cocinaris (docs/development/environment.md#owner-linux-host). Neste host:

* operações remotas usam `git` e `gh` diretamente, com as credenciais que o owner configurou no host;
  o GitHub App e seus scripts não existem aqui e não devem ser instalados ou recriados;
* se a autenticação remota não estiver configurada, o agente para e informa o owner, sem procurar,
  ler ou criar credenciais;
* branches de trabalho usam o prefixo `agent/`; o agente abre ou atualiza um pull request draft e
  solicita revisão de `RodrigoWantuk`;
* o agente nunca executa `gh pr merge`, nunca faz push direto para `main`, nunca executa
  `gh auth login`/`gh auth logout` e nunca exibe tokens ou segredos;
* o deploy segue docs/operations/linux-host-deployment-runbook.md; deploy de `main` após merge feito
  pelo owner é permitido; deploy de código não mergeado exige instrução explícita do owner.
```

## Execution state

- **Current run delivery target:** All included items except the `AGENTS.md` change (owner) and the post-merge deploy.
- **Current checkpoint:** Host tooling, development fixtures, documentation, deployment assets and runbook delivered.
  - Docker 26.1.5 / Compose 2.26.1 with `iptables: false`; Node.js 24.22.1 at `/opt/node24` (checksum verified) with Yarn 1.22.22 via Corepack; `jq`; Playwright Chromium and system libraries.
  - Compose PostgreSQL port configurable (`COCINARIS_DEV_POSTGRES_PORT`).
  - Frontend workflow on Node.js 24.18.0.
  - ADR-0004 and ADR-0006 amendments, environment guide "Owner Linux host" section, deployment runbook, `infrastructure/deploy/linux-host/`, `scripts/deploy/deploy-linux-host.sh`.
- **Last completed step:** A deploy dry run of `8f74062` (PLAN-0030 head) built, published and generated the migration script, then **refused to publish** because the entry page loads third-party scripts. That is the intended guard behavior; the scripts are removed by PLAN-0032. The failed partial release was left behind, so the script now deletes any release that does not finish building.
- **Exact next action:** Owner applies the proposed `AGENTS.md` §13 change; agent pushes the stacked branches and opens draft PRs (PLAN-0028 → 0029 → 0030 → 0031 → 0032). After owner merge: rename migration steps from the runbook and a real deploy with `--install-units`.
- **Blockers:** Owner must apply the `AGENTS.md` §13 change before pushes and PRs from this host. Owner merge before deploying.
- **Validation performed:**
  - On the host: backend unit 52/52, architecture 14/14 and integration 175/175 with the OIDC fixture; format; migrations; OpenAPI (recorded under PLAN-0030).
  - Frontend full gate set on Node.js 24.
  - Backup restore rehearsal: 15 tables and 9 migration rows matched.
  - Deploy dry run as described above.
  - Existing sites returned HTTP 200 after the Docker install.
- **Known failures or limitations:** None for the dry run (passed on `4c1cc72`, see progress log). CSP is documented in the reverse-proxy template but not enabled live, because sign-in cannot be verified end to end by an agent.
- **Working tree state:** Committed on `agent/plan-0031-owner-host-environment`.

## Progress log

### 2026-10-28T14:45:00Z — Claude Code

- Plan created from owner instruction. Docker Engine 26.1.5 and Compose 2.26.1 installed with `iptables`/`ip6tables` disabled; existing sites verified up afterwards (HTTP 200).

### 2026-10-28T18:10:00Z — Claude Code

- **Checkpoint:** Host environment, deployment assets and documentation delivered (see execution state).
- **Incidents handled:** A session restart interrupted the first dry run mid-build; the partial release was removed and the dry run repeated. The development compose stack was stopped to free memory.
- **Privacy incident (handled under PLAN-0032):** The deployed `index.html` loaded a third-party script and a session-recording analytics snippet inherited from the Emergent import. At 15:29Z the deployed file was hot-fixed (scripts removed, title corrected; backup at `/var/backups/wtk-cocinaris/web-index-20261028T152920Z.html`). The deploy script now refuses entry pages with external script or style origins.
- **Next action:** See execution state.

### 2026-10-28T18:15:00Z — Claude Code

- Deploy dry run on `4c1cc72` (PLAN-0032 head) **passed** in about 95 seconds:
  - worktree build, API publish, migration script and production frontend completed;
  - the third-party check passed;
  - the pre-flight listed the unit and environment-key rename steps.
- The unactivated release was removed. Remaining acceptance: `AGENTS.md` §13 (owner) and the post-merge deploy.

