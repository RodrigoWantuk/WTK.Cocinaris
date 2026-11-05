# Product Foundation Test Gates

- **Status:** Accepted
- **Last updated:** 2026-08-05
- **Lean validation amendment:** [`../plans/PLAN-0007-amendment-2026-08-05-lean-validation.md`](../plans/PLAN-0007-amendment-2026-08-05-lean-validation.md)

## Purpose

These gates translate the accepted discovery into behaviors that implementation work and, when justified by elevated risk, independent testing plans must verify. They supplement [`strategy.md`](strategy.md). Ordinary changes use lean targeted validation; independent validation plans are reserved for concrete high-impact risk.

A feature is not correct merely because its happy-path UI works or its AI output looks plausible.

## Cross-user isolation

Verify that one user cannot read, infer, mutate, share, delete, export, or receive notifications for another user's:

- profile and restrictions;
- inventory and lot history;
- plan and shopping state;
- private recipes and revisions;
- executions, troubleshooting, notes, and photos;
- usage, subscription, and privacy requests.

Shared recipes create independent copies and never shared mutable ownership.

## Inventory invariants

Test:

- quantity or explicit availability is required;
- canonical unit conversion and rounding;
- distinct products are not collapsed incorrectly;
- lots preserve state and provenance;
- partial transitions create correctly linked derived lots;
- shelf-life evidence priority and confidence;
- explicit information is not overwritten by a weaker estimate;
- reservation does not reduce physical quantity;
- over-reservation and locks are explained;
- duplicate or concurrent transitions do not double-consume;
- attention is advisory and actionable;
- corrections remain auditable.

## Planning and shopping invariants

Test:

- recipe selection works without an active plan;
- plans support simulation, draft, and accepted state;
- simulation has no authoritative side effects;
- fixed and flexible entries coexist;
- changing one recipe does not silently regenerate accepted unaffected entries;
- actual purchases do not silently rewrite the plan;
- required, recommended, optional, available, and operational shopping items remain distinguishable;
- package remainder calculations identify source and uncertainty;
- cancelled meals release or preserve reservations according to explicit policy.

Shopping plans (PLAN-0062) also require:

- a restricted dish never becomes a card, and a blocked expanded recipe creates no meal;
- no goal, chip, or anchor can relax a restriction;
- the same input always gives the same card order;
- no reservation or home-page meal before the plan is accepted;
- retrying a decision, batch, or expansion creates no duplicates;
- the flow finishes with saved recipes when AI is off;
- offline, the saved list opens and only marks work, and marks reach the server once, in order, when the connection returns;
- no API response reaches cache storage, logout erases the device, and a second owner sees nothing of the first (`scripts/e2e/offline-list-journey.mjs`);
- planning screens pass `scripts/e2e/mobile-audit.mjs` at 320, 360, and 390 px, and dishes offered fit the meal they are for (`MealFit`);
- notifications are opt-in, name no food or restriction, reach an owner at most once a day, call only known push services, and stay with their owner (ADR-0013);
- the preparation route is deterministic, hands-on parts never overlap, it opens only after acceptance, it resumes after a reload, and it changes the pantry only through the dishes' own finish screens (`scripts/e2e/session-route-journey.mjs`).

## Recipe and cooking invariants

Test:

- imports store normalized content but not source URL or source image;
- parsing warnings identify ambiguity and inconsistency;
- viewing a recipe does not start an execution;
- revisions preserve identity and history;
- material derivations create a new UUID and parent reference;
- favorites follow current revision;
- scheduled entries can pin a revision;
- troubleshooting changes execution-local state first;
- permanent change requires final user choice;
- finalization allows edits to expected consumption;
- execution completion and inventory reconciliation commit atomically or remain explicitly pending;
- photos are permanent only after explicit action and can be deleted.

## AI gates

For every operation verify:

- all calls pass through the gateway;
- authorization and entitlement precede provider use;
- structured input and output limits;
- minimal context and redaction;
- prompt-injected source content cannot gain authority;
- schema, domain, restriction, safety, and state validation;
- timeout, cancellation, repair, fallback, and safe failure;
- quota reservation, settlement, refund, and idempotency;
- provider/model/workflow/usage/cost telemetry without private-content leakage;
- evaluated quality across Portuguese, English, and Spanish;
- non-AI degraded behavior.

## Identity and session gates

Test:

- OIDC login, verification, recovery, external login, linking policy, and global sign-out;
- internal user UUID independence from provider subject;
- secure cookie flags and expiration;
- CSRF protection;
- no long-lived tokens in frontend storage;
- privilege separation and MFA for privileged accounts;
- session revocation after deletion or incident.

## Privacy gates

Test:

- minimum-account collection;
- permission and consent separation;
- access and correction;
- complete readable export;
- item deletion and photo deletion;
- durable, resumable, and idempotent account deletion;
- processor deletion propagation where applicable;
- retention and backup expiration behavior;
- temporary image deletion after success and failure;
- no default telemetry leakage of profiles, restrictions, prompts, responses, or private recipes.

## Messaging and job gates

Test:

- jobs survive API and worker restart;
- user can navigate away and return;
- outbox recovers from broker outage;
- duplicate delivery does not duplicate consumption, quota, notification, export, or deletion;
- bounded retry and dead-letter behavior;
- cancellation and timeout;
- poison-message isolation;
- replay procedures and authorization;
- progress and failure category visibility.

## Localization and accessibility gates

Test critical flows in Portuguese, English, and Spanish with metric and imperial display, including:

- translated and regional culinary terms;
- unit, date, time, currency, and number formatting;
- layout expansion;
- keyboard and screen-reader operation;
- focus and live updates during cooking;
- non-color status communication;
- responsive device sizes;
- accessible alternatives to push and timed interaction.

## Reliability and recovery gates

Before public release verify:

- launch-envelope load model;
- AI cost-rate limits and emergency disablement;
- rolling-compatible migrations;
- application rollback;
- backup restore and point-in-time recovery;
- queue recovery;
- Keycloak recovery;
- object deletion and storage lifecycle;
- AI and notification degraded modes;
- observability coverage and alert routing.

## Independent test plans

Substantial implementation plans require an independent testing plan when they affect inventory reconciliation, identity, cross-user access, AI safety, privacy deletion, billing or quota, messaging, migrations, or production recovery.
