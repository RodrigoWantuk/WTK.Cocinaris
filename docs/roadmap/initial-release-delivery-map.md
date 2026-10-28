# Initial Release Delivery Map

- **Status:** Accepted operational map
- **Created:** 2026-08-05
- **Last updated:** 2026-10-28 (PLAN-0031 coverage review integrated with PR #44)
- **Baseline:** `main` at `e8c2cfa` (PLAN-0028 merged) plus the PLAN-0031–PLAN-0036 foundation stack
- **Ordered roadmap and dependencies:** [PLAN-0031 coverage review](../plans/PLAN-0031-amendment-2026-10-28-coverage-review.md)

This map accounts for major initial-release capabilities. States are exclusive per row.

## Implemented

| Capability | Evidence |
|---|---|
| Authentication and backend-managed OIDC session | PLAN-0003 |
| Inventory backend and production frontend | PLAN-0003, PLAN-0016 |
| Profile, preferences, household context, and equipment | PLAN-0012, PLAN-0020 |
| Public entry and mock-backed contextual home | PLAN-0010, PLAN-0011 |
| Prepared components and derived lots (backend) | PLAN-0023 |
| Recipe AI protocol and deterministic contracts | PLAN-0017, PLAN-0022 (`0.3` Revised) |
| AI Gateway and cook-now recipe generation (suggest → select → expand → save) | PLAN-0028 |
| Product and technical name Cocinaris | PLAN-0033 |
| Owner Linux host as development and deployment environment, deploy script, runbook, backup restore rehearsal | PLAN-0034 |
| Production shell and navigation review | PLAN-0035 |
| Vite and Vitest frontend toolchain | PLAN-0036 |

## In Progress

_None at this checkpoint._

## Ready

| Capability | Plan | Notes |
|---|---|---|
| Launch / AI-economics Phases 1–2 documentation execution | PLAN-0008 | Recipe evaluation consumed from PLAN-0022; spend/billing still gated |
| Contextual-home deterministic inventory/profile tiers | PLAN-0021 | Menu tier waits for PLAN-0029 |

## Planned

| Capability | Plan |
|---|---|
| Inventory lifecycle transitions, product identity, shelf-life evidence and attention | PLAN-0037 |
| Recipe library: manual create/edit, revisions vs derived recipes, favorites | PLAN-0038 |
| Asynchronous platform (worker, outbox, RabbitMQ, persistent jobs) | PLAN-0039 |
| AI Gateway hardening: model ID, monthly cost cap, provider ADR, latency | PLAN-0040 |
| Sequential planning, accepted menus, shopping projection | PLAN-0029 (Draft) |
| Shopping execution, package sizes, purchase reconciliation | PLAN-0041 |
| Guided cooking and execution reconciliation | PLAN-0042 |
| Localized plan recovery | PLAN-0043 |
| Multi-day preparation route | PLAN-0044 |
| Troubleshooting and controlled learning | PLAN-0045 |
| Object storage, AI list parsing and receipt-photo inventory import | PLAN-0046 |
| Recipe import, private sharing, permanent photos | PLAN-0047 |
| Recipe thumbnail generation | PLAN-0030 (Draft) |
| Attention dashboard and notifications | PLAN-0048 |
| Privacy export, deletion, consent, and retention | PLAN-0049 |
| Usage entitlements and billing | PLAN-0050 |
| Abuse controls | PLAN-0051 |
| Release readiness and launch operations | PLAN-0052, PLAN-0008 |

## Deferred beyond first release

| Capability | Notes |
|---|---|
| Native Android / iOS applications | Explicitly out of initial release |
| Permanent exclusive AI provider selection | Providers remain replaceable |

Do not treat Planned or Deferred rows as implemented.
