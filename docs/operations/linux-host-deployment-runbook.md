# Linux Host Deployment Runbook

- **Status:** Accepted
- **Last updated:** 2026-10-28
- **Plan:** [PLAN-0031](../plans/PLAN-0031-owner-host-development-and-deployment.md)
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
6. Pre-flight: the installed unit must start `Cocinaris.Api.dll`, and the environment file must not contain `KITCHENFLOW_*` keys.
7. Stop the service and take a backup.
8. Apply migrations as `cocinaris_owner`. A migration that creates a **new schema** also needs `GRANT USAGE ON SCHEMA <name> TO cocinaris_app;` and matching default privileges (see "First install").
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

## Rename migration (KitchenFlow → Cocinaris, PLAN-0030)

Do this once, on the first deploy of a release that contains PLAN-0030:

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

1. Install .NET 10.0.302 to `/opt/dotnet` (`dotnet-install.sh --version 10.0.302 --install-dir /opt/dotnet --no-path`), Node.js 24 to `/opt/node24` (checksum-verified tarball, then `corepack enable --install-directory /opt/node24/bin`), PostgreSQL, and Caddy.
2. `useradd --system --no-create-home --shell /usr/sbin/nologin wtk-cocinaris`.
3. Create `/etc/wtk-cocinaris/api.env` from `api.env.example` (`chown root:wtk-cocinaris`, `chmod 0640`).
4. Database: create role `cocinaris_owner` (NOLOGIN) and `cocinaris_app` (LOGIN, random password), and database `cocinaris` owned by `cocinaris_owner`. For each schema (`identity`, `inventory`, `platform`, `profiles`, `public`), grant USAGE plus DML on tables and sequences to `cocinaris_app`, and set `ALTER DEFAULT PRIVILEGES FOR ROLE cocinaris_owner` to match.
5. Register an OIDC client with redirect URI `https://<host>/signin-oidc` and sign-out callback `https://<host>/signout-callback-oidc`; put authority, client ID and secret in the environment file.
6. Add the `Caddyfile.cocinaris` block with the real host name, then `caddy validate` and `systemctl reload caddy`.
7. Point DNS at the host. With a proxying CDN, create the record **before** the proxy first requests a certificate. If the certificate attempt ran before DNS existed, `systemctl reload caddy` retries immediately (the CDN shows HTTP 525 until then).
8. `sudo scripts/deploy/deploy-linux-host.sh --install-units`.

## Operations

- Logs: `journalctl -u wtk-cocinaris`; deploy output is on the terminal.
- Health: `curl -s http://127.0.0.1:8004/health/ready` (database included; the OIDC provider is not a readiness dependency).
- End every session immediately: `rm -r /var/lib/wtk-cocinaris/session-keys && systemctl restart wtk-cocinaris`.
- Third-party content: `index.html` must not load external scripts, styles or fonts. A CDN in front of the host may inject its own analytics script (for example Cloudflare Web Analytics). Disable that in the CDN when user-session privacy requires it.
