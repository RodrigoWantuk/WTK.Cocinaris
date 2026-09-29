# Initial Release Delivery Map

- **Status:** Accepted operational map
- **Created:** 2026-08-05
- **Baseline:** `main` at `cfec79546fcd5e476f7198cdc788a6ce251d35c5` (PLAN-0022 merged)
- **Active implementation:** PLAN-0028

This map accounts for major initial-release capabilities. States are exclusive per row.

## Implemented

| Capability | Evidence |
|---|---|
| Authentication and backend-managed session | PLAN-0003 / Keycloak OIDC BFF |
| Inventory backend and production frontend | PLAN-0003, PLAN-0016 |
| Profile, preferences, household context, and equipment | PLAN-0012, PLAN-0020 |
| Public entry and mock-backed contextual home | PLAN-0010, PLAN-0011 |
| Prepared components and derived lots | PLAN-0023 |
| Recipe AI protocol and deterministic contracts | PLAN-0017, PLAN-0022 (`0.3` Revised) |
| AI Gateway and cook-now recipe generation (suggest → select → expand → save) | PLAN-0028 |

## In Progress

_None at this checkpoint._

## Ready

| Capability | Plan | Notes |
|---|---|---|
| Launch / AI-economics Phases 1–2 documentation execution | PLAN-0008 | Recipe evaluation consumed from PLAN-0022; spend/billing still gated |

## Blocked

| Capability | Plan | Blocker |
|---|---|---|
| Contextual-home live sources | PLAN-0021 | Accepted menu/planning read contract from sequential-planning slice (PLAN-0029) |

## Planned

| Capability | Plan / owner |
|---|---|
| Sequential planning, accepted menus, shopping projection | PLAN-0029 (Draft) |
| Recipe thumbnail generation | PLAN-0030 (Draft) |
| Recipe lifecycle, import, editing, revisions, favorites, private sharing | Future plans after PLAN-0028 |
| Guided cooking and execution reconciliation | Future plans |
| Localized plan recovery | Future plans |
| Multi-day preparation route | Future plans |
| Troubleshooting and controlled learning | Future plans |
| AI list parsing and receipt-photo inventory import | Future plans |
| Notifications | Future plans |
| Privacy export, deletion, consent, and retention | Future plans |
| Usage entitlements and billing | Future plans (PLAN-0008 related) |
| Production deployment and release operations | Future plans |
| Repository/product naming reconciliation | Future documentation |

## Deferred beyond first release

| Capability | Notes |
|---|---|
| Native Android / iOS applications | Explicitly out of initial release |
| Permanent exclusive AI provider selection | Providers remain replaceable |

Do not treat Planned or Deferred rows as implemented.
