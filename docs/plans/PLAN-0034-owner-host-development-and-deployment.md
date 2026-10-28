# PLAN-0034: Establish the Owner Linux Host as the Development and Deployment Environment

- **Status:** Validating
- **Type:** Operations
- **Priority:** Critical
- **Owner:** Claude Code
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** `agent/plan-0031-0036-foundation-stack` (integrates the former stacked branches with `main` after PR #44)
- **Pull request:** Draft PR from `agent/plan-0031-0036-foundation-stack` (one consolidated PR for PLAN-0031–PLAN-0036)
- **Numbering:** Created as PLAN-0031. Renumbered to PLAN-0034 on 2026-10-28 because merged PR #44 had already published PLAN-0028–PLAN-0030 ([mapping](PLAN-0031-amendment-2026-10-28-coverage-review.md#renumbering-after-pr-44)).
- **Related ADRs:** ADR-0004, ADR-0006
- **Dependencies:** PLAN-0033 (names used in scripts and runbooks)

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
   - an operations runbook covering first install, update, rollback/forward repair, restore rehearsal, the rename migration from PLAN-0033, and log locations.
5. **Identity documentation:** deployments document a generic OIDC provider (authority, client ID/secret, redirect and sign-out callback paths). ADR-0004 gets an amendment note stating that Keycloak is the development/test fixture and any standards-compliant OIDC provider may back a deployment.
6. **Agent workflow:** propose an `AGENTS.md` §13 change that authorizes this host for remote Git/GitHub operations with owner-configured credentials. **The owner applies it**: agents may not change the rules that govern their own remote authority (an agent attempt on 2026-10-28 was blocked by the agent permission classifier). Until then, no push or PR is made from this host.
7. **Reconcile ADR-0006:** the owner host is the current development and deployment environment, and the Virginia production target stays as the accepted future production region.
8. **Deploy the renamed build** once it is merged, following the runbook. Merge itself remains the owner's action.

### Excluded

- Production in Virginia, CI/CD auto-deploy, secret manager adoption (recorded as follow-ups for PLAN-0052).
- Changing the deployment's identity provider or its registration.

## Acceptance criteria

- [x] Backend unit, architecture and **integration** tests pass on the host, including the OIDC-fixture-dependent tests.
- [x] Frontend gates pass on the host with Node.js 24 and Yarn via Corepack. The frontend workflow uses Node.js 24.
- [x] The deployment script and runbook can rebuild the running deployment from a ref without touching the development checkout. Verified with a dry run (`--dry-run`) on the host.
- [x] A backup restore rehearsal into a scratch database succeeds and is documented.
- [x] No identity-provider product name appears in deployment documentation.
- [x] `AGENTS.md` §13 reflects the host decision (applied 2026-10-28 on explicit owner instruction).

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
- **Last completed step:** A deploy dry run of `8f74062` (PLAN-0033 head) built, published and generated the migration script, then **refused to publish** because the entry page loads third-party scripts. That is the intended guard behavior; the scripts are removed by PLAN-0035. The failed partial release was left behind, so the script now deletes any release that does not finish building.
- **Exact next action:** Final CI on the draft PR head; owner merge. Owner may add the AI Gateway key to `api.env` (runbook, first install step 5) and restart the service.
- **Blockers:** None. Owner merge determines delivery state only.
- **Validation performed:**
  - On the host: backend unit 52/52, architecture 14/14 and integration 175/175 with the OIDC fixture; format; migrations; OpenAPI (recorded under PLAN-0033).
  - Frontend full gate set on Node.js 24.
  - Backup restore rehearsal: 15 tables and 9 migration rows matched.
  - Deploy dry run as described above.
  - Existing sites returned HTTP 200 after the Docker install.
- **Known failures or limitations:** None for the dry run (passed on `4c1cc72`, see progress log). CSP is documented in the reverse-proxy template but not enabled live, because sign-in cannot be verified end to end by an agent.
- **Working tree state:** Committed on `agent/plan-0031-0036-foundation-stack`.

## Progress log

### 2026-10-28T14:45:00Z — Claude Code

- Plan created from owner instruction. Docker Engine 26.1.5 and Compose 2.26.1 installed with `iptables`/`ip6tables` disabled; existing sites verified up afterwards (HTTP 200).

### 2026-10-28T18:10:00Z — Claude Code

- **Checkpoint:** Host environment, deployment assets and documentation delivered (see execution state).
- **Incidents handled:** A session restart interrupted the first dry run mid-build; the partial release was removed and the dry run repeated. The development compose stack was stopped to free memory.
- **Privacy incident (handled under PLAN-0035):** The deployed `index.html` loaded a third-party script and a session-recording analytics snippet inherited from the Emergent import. At 15:29Z the deployed file was hot-fixed (scripts removed, title corrected; backup at `/var/backups/wtk-cocinaris/web-index-20261028T152920Z.html`). The deploy script now refuses entry pages with external script or style origins.
- **Next action:** See execution state.

### 2026-10-28T18:15:00Z — Claude Code

- Deploy dry run on `4c1cc72` (PLAN-0035 head) **passed** in about 95 seconds:
  - worktree build, API publish, migration script and production frontend completed;
  - the third-party check passed;
  - the pre-flight listed the unit and environment-key rename steps.
- The unactivated release was removed. Remaining acceptance: `AGENTS.md` §13 (owner) and the post-merge deploy.

### 2026-10-28T20:15:00Z — Claude Code

- The owner confirmed the GitHub CLI login on this host and explicitly instructed: "pode aplicar a mudança do AGENTS.md proposta no PLAN-0034". Applied, with one adjustment: the review request applies when the author identity differs from `RodrigoWantuk`, because pushes from this host use the owner's own account and GitHub does not allow requesting a review from the author. The section intro now states that remote operations are allowed only on the listed hosts.
- Remote access verified (`gh auth status`, `git ls-remote`). The token has very broad scopes; recommended to the owner to replace it with a token restricted to this repository.
- The first fetch revealed open PR #44 (another agent, 2026-08-06): it implements the recipe AI gateway and cook-now generation as PLAN-0028, and drafts PLAN-0029/PLAN-0030, which collide with this stack's plan IDs. Nothing was pushed; owner decision requested on integration order.

### 2026-10-28T22:20:00Z — Claude Code

- The owner merged PR #44 and gave two standing instructions: "Pode tratar suas alterações como aprovadas" and "Sempre faça deploy das alterações quando fechar um ciclo, o ambiente não está em prod, ainda somos somente eu e você testando". Deploying a closed cycle from its branch before merge is therefore authorized on this host while it is not production. Merge itself remains the owner's action (`AGENTS.md` §11).
- Integrated `main` (PR #44) into the stack on `agent/plan-0031-0036-foundation-stack` and renumbered this stack's plans (this plan was PLAN-0031).
- Found and fixed a deployment gap: the PR #44 migration creates the `recipes` and `ai` schemas, and the deploy script did not grant them to `cocinaris_app`, so the first deploy would have failed with permission errors. The script now grants USAGE and DML on every schema owned by `cocinaris_owner` after migrations, plus default privileges. Verified twice on a scratch database (idempotent).
- `api.env.example` and the runbook document the optional AI Gateway keys (`Ai__Providers__DeepSeek__Enabled`, `Ai__Providers__DeepSeek__ApiKey`).
- **Next action:** Deploy this branch with the rename migration (`api.env` keys, `--install-units`), verify health and the public site, then push and open the draft PR.

### 2026-10-28T22:12:00Z — Claude Code (deploy)

- Deployed `bf2f258` from `agent/plan-0031-0036-foundation-stack` under the owner's standing instruction, with the one-time rename migration: `api.env` keys renamed `KITCHENFLOW_*` → `COCINARIS_*` (copy kept at `api.env.pre-rename`), `--install-units`.
- The script took a backup (`cocinaris-20261028T220912Z.dump`), applied the PR #44 migration (10 rows in `__EFMigrationsHistory`), granted `cocinaris_app` on the new `recipes` and `ai` schemas, adopted the old plain directories as release `20261028T220913Z-legacy` and switched to `20261028T220813Z-bf2f2583ce27`.
- Verified: the unit starts `Cocinaris.Api.dll`; `/health/ready` 200; public `/` and `/app/receitas` 200; `/api/v1/session` and `/api/v1/recipes` 401 without a session; entry page title "Cocinaris" with no external origins; no warnings in the service journal. Existing sessions ended, as documented for the rename.
- The AI Gateway is not configured (`Ai__Providers__DeepSeek__*` absent), so recipe generation shows its unavailable state until the owner adds the key.
- Browser smoke repeated at normal host load: 28/28 passed. The two earlier timeouts were caused by load from another project on the host.
