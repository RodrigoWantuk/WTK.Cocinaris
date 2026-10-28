# Cocinaris Development Environment

- **Status:** Accepted
- **Last updated:** 2026-07-29
- **Applies to:** Windows and Linux development hosts, local AI coding agents, CI preparation, and the Texas VPS integration environment
- **Related plans:** PLAN-0002, PLAN-0003, PLAN-0004, PLAN-0005
- **Related ADRs:** ADR-0001 through ADR-0006

## Purpose

This document is the canonical list of software and resources required to develop Cocinaris. It separates tools installed on the host from services that run in containers so Windows and Linux agents use the same dependency topology.

A new agent must read this document before claiming an implementation or testing plan. Do not guess missing tools, install infrastructure services natively by habit, or replace version lines without updating the active plan.

## Supported host modes

Cocinaris supports three local host modes.

### Linux host

Recommended for autonomous agents, CI parity, and the Texas VPS.

- Use a Docker-supported 64-bit Linux distribution.
- Debian and Ubuntu are preferred because Docker and .NET publish direct installation guidance.
- Run repository commands from a normal user account.
- Docker Engine may run rootful or rootless according to host policy.

### Windows native tools with Docker Desktop

Recommended when the developer uses Windows-native IDEs and shells.

- Windows 11 64-bit is the preferred desktop baseline.
- Install .NET SDK, Node.js, Git, and optional IDE tools on Windows.
- Use Docker Desktop with the WSL2 backend and **Linux containers**.
- Run repository commands from PowerShell 7 or another documented Windows shell.
- Do not switch Docker to Windows-container mode for Cocinaris.

### Windows with WSL2 development

Recommended when the agent should behave like Linux while the host remains Windows.

- Install a supported Linux distribution under WSL2.
- Enable Docker Desktop integration for that distribution, or run a supported Docker Engine entirely inside WSL2.
- Install .NET SDK, Node.js, Git, and CLI utilities inside WSL2.
- Clone the repository under the Linux filesystem, for example `~/src/WTK.Cocinaris`, not under `/mnt/c`, to avoid filesystem performance and permission problems.
- Run all install, build, test, and package-manager commands inside WSL2.
- Never share `node_modules`, `.pnpm-store`, `bin`, `obj`, NuGet caches, Playwright browsers, or generated build output between Windows-native and WSL2 toolchains.

Choose one Windows mode per working copy. Mixing modes is unsupported.

## Hardware guidance

These are project recommendations, not vendor minimums.

| Resource | Minimum workable | Recommended for AI agents and full local stack |
|---|---:|---:|
| CPU | 4 logical cores | 8 or more logical cores |
| RAM | 16 GB | 32 GB |
| Free SSD space | 40 GB | 80 GB or more |
| Docker memory allocation | 8 GB | 12–16 GB |
| Docker CPU allocation | 4 cores | 6–8 cores |

Keycloak, PostgreSQL, browsers, frontend tooling, backend tests, and container image layers can run simultaneously. An 8 GB host may start partial services but is not a supported full-stack development baseline.

Set a development Keycloak container memory limit of approximately 1 GB unless measurement justifies a change. Keycloak's official container guidance notes that container memory limits matter because JVM heap sizing is relative to available memory.

## Required host-installed software

Install the following on the chosen host execution environment.

| Tool | Required version policy | Purpose |
|---|---|---|
| Git | Current supported stable release | Source control, branches, subtree synchronization |
| Docker Engine or Docker Desktop | Current stable release with the `docker compose` plugin | Linux-container runtime for infrastructure dependencies |
| .NET SDK | .NET 10 LTS, latest supported security patch | ASP.NET Core backend, tests, migrations, local tools |
| Node.js | Node.js 24 LTS, latest 24.x security patch | Lovable-generated React frontend and frontend tooling |
| Browser | Current stable Chromium-based browser; Firefox also recommended | Manual UI, OIDC, responsive, and accessibility validation |
| `curl` | Current distribution/vendor version | Health checks and HTTP diagnostics |
| `jq` | Current distribution/vendor version, or PowerShell JSON equivalent | JSON inspection in scripts and diagnostics |
| OpenSSL | Current supported distribution/vendor version | Certificate and TLS diagnostics |
| CA certificates | Current operating-system package | HTTPS, package, container, and OIDC trust |

### Version baseline verified on 2026-07-29

- .NET 10 is an LTS release supported through November 2028. Install the latest .NET 10 patch rather than pinning an old vulnerable patch globally.
- Node.js 24 is LTS. The official download page listed 24.18.0 as the latest 24.x LTS release when this document was written. The repository will later pin an exact version through `.nvmrc`, `.node-version`, Volta metadata, or an equivalent file created by PLAN-0004.
- PostgreSQL 18.4 is the current supported PostgreSQL 18 minor release at this document date.
- Keycloak 26.7.0 is the current documented release at this document date.
- RabbitMQ 4.3.4 is the current release at this document date, but RabbitMQ is not required by the first vertical slice.
- OpenTelemetry Collector 0.147.0 is the documented container example version at this document date.

Patch versions change. Exact container tags and project tool versions must be pinned in compose files, lockfiles, `global.json`, and tool manifests. Updating a pinned version requires validation and a normal pull request.

## Recommended host tools

These improve productivity but are not runtime dependencies.

| Tool | Purpose |
|---|---|
| Visual Studio Code | Cross-platform editor suitable for backend, frontend, containers, and Markdown |
| C# Dev Kit or Rider | .NET navigation, debugging, and test execution |
| Visual Studio with .NET 10 support | Optional Windows-native backend IDE |
| GitHub CLI (`gh`) | Pull-request, workflow, and repository diagnostics |
| PowerShell 7 | Cross-platform administrative scripting |
| a database GUI | Optional PostgreSQL inspection; never replace migration/tests with manual edits |
| an HTTP client | Optional API exploration; committed contract tests remain authoritative |

An AI agent must remain capable of running required tasks from the command line without depending on an IDE-only operation.

## Software that is not installed natively by default

The following run in containers or are not yet required.

| Software | Native installation required? | Project policy |
|---|---|---|
| PostgreSQL Server | No | Run the pinned PostgreSQL container |
| Keycloak Server | No | Run the official pinned Keycloak container |
| Java/OpenJDK | No | The Keycloak container includes its runtime; native Keycloak is unsupported for the default workflow |
| RabbitMQ Server | No | Optional compose profile when a later plan introduces messaging |
| Erlang/OTP | No | Included in RabbitMQ container |
| Redis Server | No | Optional, nonauthoritative component introduced only by a plan with measured need |
| S3-compatible server | No | Provider/emulator remains unselected until media work begins |
| SMTP test server | No | Add a container only when email flow enters scope |
| Python | No | .NET owns the backend; Python is allowed only for a future isolated specialized service with its own plan |
| Kubernetes | No | Not required for initial development or release |

The PostgreSQL command-line client is optional. Agents may use `docker exec` to run `psql` inside the PostgreSQL container. When installing `psql` on the host, prefer major version 18.

If someone intentionally runs Keycloak outside a container, current official Keycloak guidance supports OpenJDK 17, 21, and 25 and recommends the latest supported JDK. That is an exception path and must be documented; it is not part of the normal environment.

## Containerized dependency topology

### Required by the first authenticated inventory slice

| Service | Initial pinned target | Required by | Default host port | Notes |
|---|---|---|---:|---|
| PostgreSQL | `postgres:18.4` | PLAN-0003, PLAN-0005 | 5432 | One server may host separate `cocinaris` and `keycloak` development databases |
| Keycloak | `quay.io/keycloak/keycloak:26.7.0` | PLAN-0003, PLAN-0004, PLAN-0005 | 8080 | Development mode only locally; import versioned Cocinaris realm |

### Recommended development observability

| Service | Initial pinned target | Default ports | Notes |
|---|---|---|---|
| OpenTelemetry Collector | `otel/opentelemetry-collector:0.147.0` or repository-approved distribution | 4317 gRPC, 4318 HTTP | May export to console/debug backend initially; no production vendor implied |

### Architecture services not required by the first slice

| Service | Initial direction | Compose behavior |
|---|---|---|
| RabbitMQ | Pin current supported 4.x management image when first used; 4.3.4 was current at document date | Disabled optional profile; first slice readiness must not depend on it |
| Redis | Select and pin only when measured cache/rate need exists | Disabled optional profile; never source of authoritative inventory/quota data |
| S3-compatible storage | Vendor/emulator not yet selected | Omit until upload/media plan |
| SMTP catcher | Tool not yet selected | Omit until email verification/notification plan |

Do not add every future service to the default `docker compose up` path. Default startup must contain only dependencies required by the current executable slice.

## Standard local ports

Plans may change a port only when they update this document and `.env.example`.

| Component | Port | Exposure |
|---|---:|---|
| Frontend development server | 3000 | localhost |
| Backend HTTP development endpoint | 7080 | localhost; redirect or diagnostics only |
| Backend HTTPS development endpoint | 7443 | localhost |
| Keycloak HTTP development endpoint | 8080 | `127.0.0.1` only |
| PostgreSQL | 5432 | `127.0.0.1` only |
| OpenTelemetry OTLP gRPC | 4317 | container network and localhost when diagnostics require |
| OpenTelemetry OTLP HTTP | 4318 | container network and localhost when diagnostics require |
| RabbitMQ AMQP, future profile | 5672 | localhost only in development |
| RabbitMQ management, future profile | 15672 | localhost only in development |

Production does not inherit development port exposure or insecure Keycloak mode.

## Host installation guidance

### .NET SDK

Install .NET 10 SDK using Microsoft's official installer or supported operating-system package instructions.

Verify:

```text
dotnet --info
dotnet --list-sdks
```

Requirements:

- a `10.0.x` SDK is visible;
- architecture matches the host;
- no preview SDK is selected by default;
- once PLAN-0003 creates `apps/backend/global.json`, `dotnet --version` from that directory resolves the pinned SDK/roll-forward policy.

When the SDK is installed outside the default search path (for example a tarball install under `/opt/dotnet`, as on the owner's test host verified on 2026-10-28), add that directory to `PATH` and set `DOTNET_ROOT` to it before running backend commands. Without it, `dotnet` is not found even though the SDK is present.

Do not globally install `dotnet-ef`. PLAN-0003 must create a local .NET tool manifest, and agents run:

```text
dotnet tool restore
dotnet tool run dotnet-ef --version
```

The same rule applies to future repository-scoped .NET tools.

### Node.js

Install Node.js 24 LTS through the official installer or a version manager appropriate to the operating system.

Verify:

```text
node --version
npm --version
```

Requirements:

- Node reports `v24.x`;
- use the package manager selected by the Lovable-generated lockfile;
- do not create multiple lockfiles;
- do not install frontend build tools globally;
- use project scripts through the chosen package manager.

The frontend and `packages/api-client` pin Yarn Classic through `packageManager` (`yarn@1.22.22`). Enable it with Corepack (`corepack enable`) rather than a global npm install. Several gates, including `yarn audit:policy`, spawn `yarn` directly and fail with `spawnSync yarn ENOENT` when it is missing. Node.js 24 is the supported baseline, but as of 2026-10-28 `.github/workflows/frontend.yml` still runs Node.js 20 (the Backend and PLAN-0005 workflows use 24.18.0), and the owner's test host runs 20.19.2. Aligning the frontend workflow on Node.js 24 is tracked by PLAN-0030.

PLAN-0004 must inspect whether Lovable generated TanStack Start or React/Vite and preserve that supported runtime. New Lovable projects created after May 13, 2026 commonly use TanStack Start with SSR except where the Lovable plan/runtime says otherwise.

### Docker

Linux:

- install Docker Engine from Docker's official repository for the distribution;
- install the Compose plugin so the command is `docker compose`, not legacy `docker-compose`;
- verify daemon access according to host policy;
- understand that membership in the `docker` group is security-sensitive and effectively grants powerful host access.

Windows:

- install Docker Desktop;
- enable the WSL2 backend;
- use Linux containers;
- verify WSL integration when commands run inside WSL2;
- confirm Docker Desktop license eligibility for the organization using it.

Verify:

```text
docker version
docker info
docker compose version
docker run --rm hello-world
```

Do not use manually downloaded static Docker binaries as the normal managed development installation when package-managed Engine or Docker Desktop is available.

### Git and CLI utilities

Verify:

```text
git --version
curl --version
jq --version
openssl version
```

On Windows where `jq` is absent, PowerShell's `ConvertFrom-Json` may be used by manual commands, but repository automation must either provide a cross-platform script or declare `jq` explicitly.

## Repository tool and dependency policy

- Commit `global.json`, `Directory.Packages.props`, NuGet lock files where selected, frontend lockfile, and local tool manifests.
- Use exact package versions in project-controlled files.
- Restore tools from the repository; do not depend on undocumented global packages.
- Generated OpenAPI clients and contracts must have reproducible commands.
- Container images use explicit version tags and should be locked to digests for CI/integration once the initial compose implementation stabilizes.
- Secrets never appear in committed `.env`, compose files, realm exports, shell history examples, or test logs.
- Commit only `.env.example` with safe placeholders or clearly synthetic development-only fixture credentials.
- Dependency upgrades receive their own validation evidence and do not ride silently with unrelated feature work.

## Local HTTPS and trust

The backend-managed cookie session requires HTTPS behavior representative of production.

PLAN-0003 must document one cross-platform method, normally ASP.NET Core development certificates for local backend HTTPS plus trusted localhost routing.

Possible verification:

```text
dotnet dev-certs https --check --trust
```

On Linux, trust behavior differs by distribution/browser. If automatic trust is unavailable, document the local reverse-proxy or certificate-import method. Do not disable certificate validation in application code, OIDC handlers, generated API clients, or tests as a permanent workaround.

Keycloak may use HTTP `start-dev` on loopback for local development only. Production Keycloak must use production mode and TLS according to operational plans.

## Environment variables and secrets

The actual names are finalized by PLAN-0003, but local configuration will include categories such as:

```text
COCINARIS_DB_HOST
COCINARIS_DB_PORT
COCINARIS_DB_NAME
COCINARIS_DB_USER
COCINARIS_DB_PASSWORD
KEYCLOAK_DB_NAME
KEYCLOAK_DB_USER
KEYCLOAK_DB_PASSWORD
COCINARIS_OIDC_AUTHORITY
COCINARIS_OIDC_CLIENT_ID
COCINARIS_OIDC_CLIENT_SECRET
COCINARIS_SESSION_KEYRING_PATH
OTEL_EXPORTER_OTLP_ENDPOINT
ASPNETCORE_ENVIRONMENT
ASPNETCORE_URLS
```

Rules:

- local values live in ignored files or user secret stores;
- CI values live in GitHub/environment secret storage;
- development realm fixtures may use synthetic known passwords, clearly marked as development-only;
- production secrets are never derived from example values;
- frontend build variables may contain only public configuration and must never contain OIDC client secrets, database credentials, AI keys, or backend secrets.

## Verification checklist before an agent claims a plan

Run from the same execution environment that will build the repository:

```text
git --version
docker version
docker compose version
dotnet --info
node --version
npm --version
curl --version
jq --version
openssl version
```

Confirm:

- [ ] Git works and the repository has no unexplained local changes.
- [ ] Docker runs Linux containers.
- [ ] `docker compose` is available.
- [ ] .NET 10 SDK is installed.
- [ ] Node.js 24 LTS is installed.
- [ ] host has enough free disk and allocated Docker memory.
- [ ] required local ports are free or documented overrides are applied.
- [ ] system clock and timezone are correct; tests still use UTC/canonical dates.
- [ ] no production credential or personal data is present.
- [ ] branch was created from current `main` after prerequisite plan merge.
- [ ] active plan and registry were updated before the first commit.

Record exact command output versions in the plan's first progress entry. Do not merely write `environment ready`.

## Bootstrap sequence before implementation assets exist

Until PLAN-0003 creates compose and solution files, an agent can only validate host prerequisites and read plans. Do not fabricate commands against nonexistent files.

Expected sequence:

```text
git clone <repository-url>
cd WTK.Cocinaris
git fetch --all --prune
# create the plan-specific branch from current main
git status
# run the verification checklist above
```

## Expected bootstrap sequence after PLAN-0003 foundation exists

Exact script names created by PLAN-0003 become authoritative. The intended flow is:

```text
# copy only safe local placeholders
cp .env.example .env.local          # Linux/WSL example
# or use the documented PowerShell equivalent on Windows

docker compose -f infrastructure/compose/compose.dev.yml config
docker compose -f infrastructure/compose/compose.dev.yml up -d postgres keycloak otel-collector

dotnet tool restore
dotnet restore apps/backend/Cocinaris.slnx --locked-mode
<repository migration command>
<repository backend run command>

# after apps/frontend exists
<selected package manager> install --frozen-lockfile
<selected package manager> run api:check
<selected package manager> run dev
```

The actual compose service names and scripts must be documented when created. Agents must use repository scripts rather than preserving obsolete commands from this placeholder sequence.

## Browser and Playwright requirements

PLAN-0004 and PLAN-0005 use Playwright through the frontend project's pinned dependency.

- Do not install Playwright globally.
- Install project browser binaries through the package script or pinned Playwright CLI.
- Linux CI may require the project's documented dependency-install command.
- Windows and Linux browser caches are separate.
- Record browser versions in independent test evidence.
- Manual validation should include a current Chromium-based browser and Firefox; Playwright WebKit is required where the supported test host can run it.

## Lovable requirements

Lovable is a hosted service; no Lovable runtime is installed locally.

The owner must provide:

- a Lovable account/workspace with sufficient credits;
- two-factor authentication;
- a new Lovable project;
- a dedicated GitHub repository created through Lovable's connector;
- stable repository ownership/name;
- access for the frontend integration agent.

Lovable currently cannot import this existing monorepo. Follow PLAN-0004's dedicated repository and controlled subtree workflow.

## Continuous-integration parity

- CI should use Linux runners first because production and container workloads are Linux-oriented.
- Backend code must remain buildable on Windows paths and not rely on case-insensitive filenames.
- Frontend scripts must avoid shell-only syntax unless wrapped by a cross-platform tool.
- Path separators, line endings, executable bits, and case sensitivity require explicit review.
- Integration tests use ephemeral containers and synthetic data.
- CI must pin .NET, Node, package lockfiles, and container versions consistently with local files.

A future Windows CI lane may be added when implementation volume justifies it. Local Windows support is required even if initial CI is Linux-only.

## Port and resource troubleshooting

### Port already in use

- Identify the owning process before changing project defaults.
- Prefer documented `.env.local` overrides.
- Update OIDC redirect URIs and frontend proxy configuration together.
- Never expose PostgreSQL or Keycloak on all network interfaces merely to avoid a local conflict.

### Docker cannot start containers

- Verify Linux-container mode.
- Check virtualization/WSL2/KVM availability.
- Check allocated memory and free disk.
- Inspect `docker info` and container logs.
- Do not solve failures by deleting volumes before determining whether migration/data evidence is needed.

### OIDC redirect or cookie failure

- Verify hostnames, ports, HTTPS trust, Keycloak realm import, client redirect URI, cookie attributes, and browser time.
- Do not disable OIDC state/correlation, CSRF, Secure cookies, or TLS validation to make the flow pass.

### Reproducible real-Keycloak smoke

After starting the default Compose topology, applying the backend migrations, and starting the API at `https://localhost:7443`, run the headless two-user browser smoke from the repository root:

```text
COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
COCINARIS_SMOKE_PASSWORD_B=<development-fixture-password> \
node scripts/backend/smoke-keycloak.mjs
```

The passwords are deliberately not reproduced in this document or shell history. Obtain the nonproduction fixture values from the versioned local realm import only when performing this local test; they are never production credentials. The script opens isolated Chromium profiles and proves the standard OIDC Authorization Code plus PKCE redirect, backend-managed Secure/HttpOnly session, CSRF-protected create request, and `404` ownership isolation. It emits no cookies, tokens, headers, request bodies, credentials, or private lot values.

The local ASP.NET Core development certificate must be trusted by the browser. The diagnostic-only `COCINARIS_SMOKE_ALLOW_UNTRUSTED_LOCAL_CERTIFICATE=1` option is not accepted validation evidence, must not be used in CI, and must not be added to application configuration. Fix certificate trust before recording a passing smoke result.

### Cross-platform dependency corruption

Delete and restore generated outputs only after confirming they are not source files:

- frontend dependency directory and build output;
- backend `bin` and `obj`;
- local tool caches when documented.

Do not commit regenerated outputs unless the repository explicitly tracks them.

## Agent completion evidence

An implementation or testing agent's first environment progress entry must include:

- host mode and OS/version;
- CPU architecture;
- RAM and available disk;
- Git, Docker, Compose, .NET, Node, npm/package-manager versions;
- selected shell;
- exact branch and baseline commit;
- required service container versions after compose exists;
- ports or overrides;
- environment limitations;
- confirmation that no production data/secrets are used.

An agent that cannot verify a required tool must mark the plan `Blocked` or document a justified nonblocking limitation. It must not claim successful build/test execution by inference.

## Official references verified for this baseline

- [.NET releases and support](https://learn.microsoft.com/dotnet/core/releases-and-support)
- [Docker Engine installation](https://docs.docker.com/engine/install/)
- [Docker Desktop](https://docs.docker.com/desktop/)
- [Node.js releases](https://nodejs.org/en/about/previous-releases)
- [PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/)
- [Keycloak Docker getting started](https://www.keycloak.org/getting-started/getting-started-docker)
- [Keycloak container guide](https://www.keycloak.org/server/containers)
- [Keycloak supported configurations](https://www.keycloak.org/server/supported-configurations)
- [RabbitMQ installation](https://www.rabbitmq.com/docs/download)
- [OpenTelemetry Collector installation](https://opentelemetry.io/docs/collector/install/)
- [Lovable GitHub integration](https://docs.lovable.dev/integrations/github)
- [Lovable FAQ and generated stacks](https://docs.lovable.dev/introduction/faq)
