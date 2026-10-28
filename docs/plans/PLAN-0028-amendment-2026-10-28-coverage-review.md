# PLAN-0028 Amendment — Plan Coverage Review and Revised Roadmap (2026-10-28)

- **Status:** Accepted
- **Type:** Documentation amendment
- **Parent plan:** [`PLAN-0028`](PLAN-0028-resume-project-and-define-evolution-roadmap.md)
- **Created:** 2026-10-28
- **Stakeholder decisions recorded (owner instruction, 2026-10-28):**
  - **D-1 resolved:** the product name is **Cocinaris** everywhere, including technical identifiers (namespaces, assemblies, packages, configuration keys, cookies, metrics).
  - **Host decision:** the owner's Linux host that serves `cocinaris.rwantuk.com.br` is the development and deployment environment. Every development, test, build, and deploy activity must be executable there.
  - **Identity documentation:** documentation treats the identity provider as a generic standards-compliant OIDC provider and does not name the provider used by a deployment.
- **D-2 resolved (owner, 2026-10-28, later the same day):** migrate the frontend from CRA/CRACO to Vite (PLAN-0033).
- **D-3 resolved (owner, 2026-10-28):** the recipe AI provider is the one the owner evaluated satisfactorily in PLAN-0017/PLAN-0022: **DeepSeek `deepseek-v4-flash` through the DeepSeek API** (`api.deepseek.com`, server-side `DEEPSEEK_API_KEY`), routed as in `docs/ai/decisions/model-routing-cook-now-menu-planning-2026-08-05.md`. The owner set no spend cap. PLAN-0037 starts with a configurable development cap of **US$ 5 per calendar month**, plus per-operation ceilings from the routing decision. That default is conservative relative to PLAN-0022's ≈ US$ 0.0016 per call, and the owner may change it. PLAN-0037 records the provider ADR.

## Purpose

Before defining the next steps, the owner asked for a review of every plan, past and present, to find work that was deferred, left as follow-up, or never assigned. This amendment records that review and replaces the PLAN-0028 roadmap table. Reserved plan IDs from PLAN-0028 had no files, so they are renumbered here.

## Method

- Read every unchecked acceptance item (`- [ ]`) in `docs/plans/PLAN-*.md`. Superseded PLAN-0004 was excluded because PLAN-0014/0015/0016 replaced it.
- Searched every plan for deferred, follow-up, later-plan, residual-risk, and known-limitation statements.
- Compared the accepted first-release capability list (`docs/product/initial-release.md`), the release quality gates, and the ADR future-decision backlog against implemented code and the PLAN-0028 roadmap.
- Inspected the inventory domain model to verify which lifecycle and shelf-life capabilities actually exist.

## Coverage gaps found

| ID | Gap | Source | Previously owned by | New owner |
|---|---|---|---|---|
| G-01 | **Inventory lifecycle transitions** (open, refrigerate, freeze, thaw, move storage, partial transition into a derived lot) are not implemented. The transaction types are only `Initial`, `Consume`, `Discard`, `Correct`, `AvailabilityChanged`, `Deleted` and the preparation pair. | `initial-release.md` Inventory, `inventory-lifecycle.md` | Nobody | PLAN-0034 |
| G-02 | **Shelf-life evidence and attention engine.** No curated product/food-safety rules, regional references, attention states (`use soon`, `check condition`, `uncertain`, `likely discard`), or reminder actions. `ExpirationProvenance` supports only `UserEntered`. The deterministic home inventory tier (PLAN-0021) depends on this. | `inventory-lifecycle.md`, foundation principle "shelf-life advisory and explainable", product-foundation gates | Nobody | PLAN-0034 |
| G-03 | **Product identity and catalog.** Distinct products (fresh vs canned tomato), culinary family/role classification, and package-size knowledge are needed by planning and shopping. | Discovery "Products remain distinct", closed-loop "Package sizes" | Implicit only | PLAN-0034 (identity), PLAN-0039 (package sizes) |
| G-04 | **Recipe thumbnail generation.** The PLAN-0008 thumbnail amendment lists ten follow-up implementation plans; none were in the queue. | `PLAN-0008-amendment-recipe-thumbnail-policy.md` | Nobody | PLAN-0046 |
| G-05 | **Manual accessibility and visual audits** (NVDA, VoiceOver, human screenshot review, exploratory charters) were deferred as "non-blocking" by PLAN-0005, PLAN-0015 and PLAN-0016. The release gates require accessible critical interfaces. | PLAN-0005 §Future, PLAN-0015 | "Candidate for later pre-release plan" | PLAN-0051 (pre-release); continuous automated checks in PLAN-0032 |
| G-06 | **Deferred observability and resilience tests:** TEST-0005-092 (incompatible schema state), -128 (trace correlation UI → API → DB), -130 (bounded metric labels), -131 (cancellation leaves no partial mutation). | PLAN-0005 | "Optional later automation" | PLAN-0051 |
| G-07 | **AI latency characterization** (statistical p50/p95, TTFT) was deferred. | PLAN-0022 | Deferred | PLAN-0037 |
| G-08 | **Usage allowance, subscription, and billing.** Visible usage and reset timing, plan limits, billing provider, and advertising policy need ADRs and implementation. | `initial-release.md` Commercial, `usage-and-cost-governance.md` | PLAN-0008 (strategy only) | PLAN-0049 |
| G-09 | **Abuse controls:** automated sign-up, multi-account farming, disposable email, upload and prompt abuse, and global limits. Only API rate limiting exists. | `security-and-abuse.md`, discovery | Nobody | PLAN-0050 |
| G-10 | **Release readiness operations:** backup **restore rehearsal** (backups exist on the host; no restore was tested), load and cost tests, telemetry-redaction validation, alerting and an OTLP collector on the host, queue-recovery tests, legal-review checklist. | `initial-release.md` Release gates | Nobody | PLAN-0051 |
| G-11 | **ADR backlog without owners:** migration tooling conventions, secret management, deployment pipeline/release gates, retention schedule, notification provider, object-storage provider, billing/ads. | `architecture/decisions/README.md` | Nobody | Assigned per plan below; deployment pipeline and secrets → PLAN-0031 |
| G-12 | **Development and deployment on the owner host.** Deployment assets live outside the repository, and the deploy procedure builds from the same checkout agents work in. Docker, Yarn and Node.js 24 were missing, and `AGENTS.md` §13 forbids remote operations from this host. | Owner instruction 2026-10-28 | Nobody | PLAN-0031 |
| G-13 | **Navigation and interface consistency.** There was no review of the production shell, navigation, and information architecture after PLAN-0011/0016/0020 were delivered independently. | Owner instruction 2026-10-28 | Nobody | PLAN-0032 |
| G-14 | **Production prepared-component UI.** The PLAN-0023 endpoint exists, but no production surface creates or inspects prepared components. | PLAN-0023 scope ("minimal frontend only if required") | Folded into cooking | PLAN-0040 (cooking) and PLAN-0034 (lot detail display) |
| G-15 | **Open GitHub issues** referenced by completed plans (#37, #41) may still be open after their retests passed. | PLAN-0020, PLAN-0023, PLAN-0025 | Retest plans | PLAN-0031 (verify and close from the host once GitHub access works) |

Items checked and found covered: PLAN-0005 blocked items `#20`–`#26` passed under PLAN-0016; PLAN-0011 Phases 3–4 → PLAN-0021; PLAN-0017 research → PLAN-0022 (completed); PLAN-0014 remediation → PLAN-0015.

## Revised roadmap

This table replaces the PLAN-0028 roadmap. Dependencies use plan IDs.

### Stage A — Trustworthy baseline, identity, environment, and experience

| Plan | Title | Type | Depends on |
|---|---|---|---|
| PLAN-0029 | Restore green baseline and reconcile drift | Operations | — |
| **PLAN-0030** | Rename the product and technical identifiers to Cocinaris | Operations | PLAN-0029 |
| **PLAN-0031** | Establish the owner Linux host as the development and deployment environment | Operations | PLAN-0030 |
| **PLAN-0032** | Review and improve production interface and navigation | Implementation | PLAN-0030 |
| PLAN-0033 | Frontend toolchain migration (ADR-0008, CRA → Vite) | Implementation | PLAN-0032, D-2 |

### Stage B — Domain foundations and AI

| Plan | Title | Depends on |
|---|---|---|
| PLAN-0034 | Inventory lifecycle transitions, product identity, shelf-life evidence and attention engine (G-01, G-02, G-03) | PLAN-0030 |
| PLAN-0035 | Recipes module and library | PLAN-0030 |
| PLAN-0036 | Asynchronous platform foundation (worker, outbox, RabbitMQ, persistent jobs) | PLAN-0031 |
| PLAN-0037 | AI Gateway, quota/cost ledger, recipe suggest/expand, latency characterization (G-07) | PLAN-0035, D-3 |

### Stage C — Planning loop

| Plan | Title | Depends on |
|---|---|---|
| PLAN-0038 | Sequential planning simulation, accepted menu, reservations and locks | PLAN-0034, PLAN-0035 |
| PLAN-0021 | Contextual home live sources (existing; deterministic tiers after PLAN-0034) | PLAN-0034, PLAN-0038, PLAN-0037 |
| PLAN-0039 | Shopping requirements, package sizes, planned vs actual purchase | PLAN-0038 |

### Stage D — Cook and reconcile

| Plan | Title | Depends on |
|---|---|---|
| PLAN-0040 | Guided cooking, execution, atomic reconciliation, quick completion, prepared-component UI (G-14) | PLAN-0035, PLAN-0038 |
| PLAN-0041 | Localized plan recovery | PLAN-0040 |
| PLAN-0042 | Multi-day preparation dependency route (live) | PLAN-0040 |
| PLAN-0043 | Text troubleshooting and controlled learning | PLAN-0037, PLAN-0040 |

### Stage E — Inputs, media, notifications, rights, commercial, launch

| Plan | Title | Depends on |
|---|---|---|
| PLAN-0044 | Object storage and AI inventory input (list, receipt photo) | PLAN-0036, PLAN-0037 |
| PLAN-0045 | Recipe import (URL/image), private sharing, permanent photos | PLAN-0035, PLAN-0044 |
| PLAN-0046 | Recipe thumbnail generation (G-04) | PLAN-0037, PLAN-0044 |
| PLAN-0047 | Attention dashboard and notifications (web push, email) | PLAN-0034, PLAN-0036 |
| PLAN-0048 | Privacy center (export, correction, deletion, retention jobs) | PLAN-0036 |
| PLAN-0049 | Usage allowance, plans and billing (G-08) | PLAN-0037 |
| PLAN-0050 | Abuse controls (G-09) | PLAN-0031 |
| PLAN-0051 | Release readiness: restore rehearsal, load/cost tests, observability and alerts, deferred PLAN-0005 tests, manual accessibility audits, legal checklist (G-05, G-06, G-10) | Stage D |
| PLAN-0008 | Launch, acquisition, AI economics (existing) | PLAN-0037 |

The technical rename (former PLAN-0043 placeholder) is now PLAN-0030 and is executed immediately because D-1 is resolved.

## Parallelism

- PLAN-0030 goes first because every later plan would otherwise touch renamed files twice.
- PLAN-0031 and PLAN-0032 follow in the same run. They touch disjoint areas (operations/docs vs frontend).
- PLAN-0033 (bundler) must not overlap frontend feature work.
- PLAN-0034 and PLAN-0035 can run in parallel. Both regenerate the OpenAPI client, so merge them sequentially.
