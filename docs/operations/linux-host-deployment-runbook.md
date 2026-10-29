# Linux Host Deployment Runbook

- **Status:** Accepted
- **Last updated:** 2026-10-28
- **Plan:** [PLAN-0034](../plans/PLAN-0034-owner-host-development-and-deployment.md)
- **Assets:** [`infrastructure/deploy/linux-host/`](../../infrastructure/deploy/linux-host/), [`scripts/deploy/deploy-linux-host.sh`](../../scripts/deploy/deploy-linux-host.sh)

This runbook deploys Cocinaris on one Linux host: a systemd-managed API, static frontend files behind a TLS-terminating reverse proxy, and PostgreSQL on the same host. It is the procedure for the owner's Debian 13 host, which is both the development environment and the current deployment (see [`environment.md`](../development/environment.md#owner-linux-host)). The Virginia production target in ADR-0006 remains the future production region.

The identity provider is any standards-compliant OIDC provider. This repository does not document which provider a deployment uses.

## Layout

| Item | Location |
|---|---|
| Development checkout (agents and developers work here) | `/svr/www/wtk-cocinaris` |
| Releases (immutable, newest five kept) | `/srv/wtk-cocinaris/releases/<UTC stamp>-<sha>/{api,web,migrations.sql,REVISION}` |
| Active release | symlinks `/srv/wtk-cocinaris/api` and `/srv/wtk-cocinaris/web` |
| API service | `wtk-cocinaris.service`, user `wtk-cocinaris`, `127.0.0.1:8004` |
| Configuration and secrets | `/etc/wtk-cocinaris/api.env` (`root:wtk-cocinaris`, `0640`); template `api.env.example` |
| Session key ring | `/var/lib/wtk-cocinaris/session-keys` (systemd `StateDirectory`) |
| Database | `cocinaris`. Role `cocinaris_owner` (no login) owns the schemas and runs migrations; the API connects as `cocinaris_app` (DML only) |
| Backups | `wtk-cocinaris-backup.timer` daily, `/var/backups/wtk-cocinaris/cocinaris-<stamp>.dump`, 14 days |
| .NET SDK / Node.js | `/opt/dotnet` (10.0.302), `/opt/node24` (24.x, Yarn through Corepack) |
| Reverse proxy | Caddy site block, template `Caddyfile.cocinaris` |

## Deploy

The script must run as root. It builds in a temporary worktree of the requested ref, so it never touches the development checkout or its branch.

```sh
cd /svr/www/wtk-cocinaris
sudo scripts/deploy/deploy-linux-host.sh --dry-run          # build origin/main into a release, change nothing else
sudo scripts/deploy/deploy-linux-host.sh                    # build and activate origin/main
sudo scripts/deploy/deploy-linux-host.sh --ref <sha|branch> # deploy another ref (for example a reviewed tag)
```

Steps performed:

1. Fetch, then create a detached worktree of the ref.
2. Restore (locked mode), build and publish the API.
3. Generate EF Core's idempotent migration script.
4. Install the frontend with the frozen lockfile, build in production mode, and run the build-mode guard.
5. Refuse to publish if `index.html` loads any script or stylesheet from another origin.
6. Pre-flight: the installed unit must start `Cocinaris.Api.dll`, the environment file must not contain `KITCHENFLOW_*` keys, and PostgreSQL must offer the `vector` extension ([pgvector](#pgvector)).
7. Stop the service and take a backup.
8. Create the `vector` and `pg_trgm` extensions as the superuser (idempotent), then apply migrations as `cocinaris_owner`, then grant `cocinaris_app` USAGE and DML on every schema the owner role owns, including schemas a migration has just created (`recipes` and `ai` arrive this way), and set matching default privileges. The grant step is idempotent.
9. Switch the `api` and `web` symlinks, start the service, and wait for `/health/ready`.
10. Prune old releases.

Merging to `main` is the owner's decision. Deploy `main` only after the PR's CI passed.

## Rollback

```sh
sudo scripts/deploy/deploy-linux-host.sh --rollback
```

This repoints the symlinks to the previous release and restarts. Migrations are **forward-only**. Roll back only when the previous release works with the current schema. Additive migrations are compatible; a migration that drops or renames something the old code uses is not. Otherwise, restore the pre-deploy backup (next section) and then roll back.

## Restore and restore rehearsal

The deploy and the daily timer both write custom-format dumps. To restore, stop the service, then:

```sh
sudo systemctl stop wtk-cocinaris
sudo -u postgres dropdb cocinaris && sudo -u postgres createdb -O cocinaris_owner cocinaris
sudo -u postgres pg_restore --exit-on-error -d cocinaris /var/backups/wtk-cocinaris/cocinaris-<stamp>.dump
sudo systemctl start wtk-cocinaris
```

Rehearse a restore without touching the live database at least once per release cycle:

```sh
D=$(ls -t /var/backups/wtk-cocinaris/cocinaris-*.dump | head -1)
sudo -u postgres createdb cocinaris_restore_rehearsal
sudo -u postgres pg_restore --no-owner --no-privileges --exit-on-error -d cocinaris_restore_rehearsal "$D"
# Compare row counts of every table and "__EFMigrationsHistory" against the live database, then:
sudo -u postgres dropdb cocinaris_restore_rehearsal
```

**Rehearsal record:**
- 2026-10-28: a fresh dump (129 KB) restored without errors.
- All 15 application tables and the migration history (9 rows) matched the live database.

## Rename migration (KitchenFlow → Cocinaris, PLAN-0033)

Do this once, on the first deploy of a release that contains PLAN-0033:

```sh
sudo cp -p /etc/wtk-cocinaris/api.env /etc/wtk-cocinaris/api.env.pre-rename
sudo sed -i 's/^KITCHENFLOW_/COCINARIS_/' /etc/wtk-cocinaris/api.env
sudo scripts/deploy/deploy-linux-host.sh --install-units
```

`--install-units` installs the repository's unit files, which start `Cocinaris.Api.dll`, and the backup script at `/usr/local/sbin/cocinaris-db-backup`. Effects users notice once:
- every open session ends, because the cookie names and the Data Protection application name changed;
- the interface language falls back to the default until chosen again;
- clients holding old ETags or pagination cursors refetch.

Rename any dashboard or alert that uses `kitchenflow.*` metric names.

## First install on a new host

1. Install .NET 10.0.302 to `/opt/dotnet` (`dotnet-install.sh --version 10.0.302 --install-dir /opt/dotnet --no-path`), Node.js 24 to `/opt/node24` (checksum-verified tarball, then `corepack enable --install-directory /opt/node24/bin`), PostgreSQL with pgvector (`postgresql-<major>-pgvector`, see [pgvector](#pgvector)), and Caddy.
2. `useradd --system --no-create-home --shell /usr/sbin/nologin wtk-cocinaris`.
3. Create `/etc/wtk-cocinaris/api.env` from `api.env.example` (`chown root:wtk-cocinaris`, `chmod 0640`).
4. Database: create role `cocinaris_owner` (NOLOGIN) and `cocinaris_app` (LOGIN, random password), and database `cocinaris` owned by `cocinaris_owner`. The deploy script grants `cocinaris_app` USAGE plus DML on every schema owned by `cocinaris_owner` after each migration run; `COCINARIS_APP_ROLE` overrides the role name.
5. Optional AI Gateway: set `Ai__Providers__DeepSeek__Enabled=true` and `Ai__Providers__DeepSeek__ApiKey` in the environment file. Without them, recipe generation shows its unavailable state and the rest of the product works. Optional overrides (ADR-0009):
   - the model and its fallback, `Ai__Providers__DeepSeek__Model` (default `deepseek-v4-flash`) and `Ai__Providers__DeepSeek__FallbackModel` (default `deepseek-chat`; empty disables it);
   - the monthly spend ceiling, `Ai__Usage__MonthlyCostCeilingUsd` (default 5);
   - the rolling 24-hour token limits, `Ai__Usage__GlobalTokensPer24Hours` (default 600000) and `Ai__Usage__UserTokensPer24Hours` (default 150000);
   - list prices, `Ai__Pricing__Models__<model>__InputUsdPerMillion` and `__OutputUsdPerMillion`.

   Check spend, latency, and quality flags with `sudo scripts/ops/ai-usage-report.sh [ceiling-usd]`.

   Optional recipe dish images (PLAN-0030):
   - set `Ai__Images__Enabled=true` and `Ai__Providers__OpenRouter__ApiKey`;
   - the images use OpenRouter FLUX 3 Image Klein at 16:9, tier `768` (1024 × 576, US$ 0.008 each), and count toward the same monthly ceiling;
   - the daily limits are `Ai__Images__UserDailyImages` (default 10) and `Ai__Images__GlobalDailyImages` (default 100);
   - files are kept in `/var/lib/wtk-cocinaris/media/thumbnails` (the service state directory), or in `Ai__Images__StoragePath`. The deploy database backup does not include them; a lost file shows the placeholder until the asset row is reset to `Pending`, which costs one new image;
   - while disabled, recipes show a placeholder and existing images are still served.

   Optional ingredient embeddings (PLAN-0055, [ADR-0010](../architecture/decisions/0010-ingredient-identity-catalog-embeddings-pgvector.md)):
   - set `Ai__Embeddings__Enabled=true` together with the same `Ai__Providers__OpenRouter__ApiKey`;
   - names in recipes and the pantry are linked to catalog ingredients with OpenRouter `qwen/qwen3-embedding-8b` (about US$ 0.0000001 per new name), and the calls count toward the same monthly ceiling;
   - curated names and near-identical spellings work without it.

   Review learned links and unresolved names with `sudo scripts/ops/ingredient-identity-report.sh`. The guide is in [`docs/ai/ingredient-identity.md`](../ai/ingredient-identity.md#operations).
6. Register an OIDC client with redirect URI `https://<host>/signin-oidc` and sign-out callback `https://<host>/signout-callback-oidc`; put authority, client ID and secret in the environment file.
7. Add the `Caddyfile.cocinaris` block with the real host name, then `caddy validate` and `systemctl reload caddy`.
8. Point DNS at the host. With a proxying CDN, create the record **before** the proxy first requests a certificate. If the certificate attempt ran before DNS existed, `systemctl reload caddy` retries immediately (the CDN shows HTTP 525 until then).
9. `sudo scripts/deploy/deploy-linux-host.sh --install-units`.

## pgvector

Since PLAN-0055 the migrations need two PostgreSQL extensions:

- `vector` (pgvector), a separate package;
- `pg_trgm`, part of PostgreSQL contrib.

On Debian install the package that matches the server's major version, for example `apt-get install postgresql-17-pgvector` (installed on the owner host on 2026-10-30, pgvector 0.8.0). No restart is needed.

The deploy script runs `CREATE EXTENSION IF NOT EXISTS` for both as the `postgres` superuser before migrations, because `cocinaris_owner` may lack `CREATE` on the database. It refuses to deploy while `vector` is not available. Development and tests use the image `pgvector/pgvector:0.8.7-pg18-trixie`.

Restoring a backup on another server needs the same package; otherwise the restore fails at `CREATE EXTENSION vector`.

## Operations

- Logs: `journalctl -u wtk-cocinaris`; deploy output is on the terminal.
- Health: `curl -s http://127.0.0.1:8004/health/ready` (database included; the OIDC provider is not a readiness dependency).
- End every session immediately: `rm -r /var/lib/wtk-cocinaris/session-keys && systemctl restart wtk-cocinaris`.
- Third-party content: `index.html` must not load external scripts, styles or fonts. A CDN in front of the host may inject its own analytics script (for example Cloudflare Web Analytics). Disable that in the CDN when user-session privacy requires it.
