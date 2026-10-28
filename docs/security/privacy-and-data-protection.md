# Privacy and Data Protection Requirements

- **Status:** Accepted product and architecture requirements
- **Last updated:** 2026-07-28
- **Legal status:** Requires professional legal review before launch

## Scope

Cocinaris is designed for LGPD- and GDPR-oriented operation. This document defines engineering and product requirements, not legal advice or final lawful-basis determination.

Production is initially expected in the United States, so international data-transfer analysis is mandatory before users in Brazil, the European Economic Area, or other regulated jurisdictions are served.

## Adult-only service

Cocinaris is for users aged 18 or older who can legally accept the terms in their jurisdiction.

The initial product records an adult declaration and terms version. It does not collect a complete birth date without a separate documented purpose. Country availability and wording require legal review.

The service, onboarding, advertising, and community content must not target children.

## Data categories

Potential personal data includes:

- identity, email, authentication subjects, and session data;
- language, region, timezone, and notification addresses;
- household size and routines;
- food preferences and dislikes;
- allergies, intolerances, medical, religious, or ethical restrictions;
- kitchen equipment and skill;
- inventory, shopping, planning, and meal history;
- recipes, notes, ratings, photos, and troubleshooting;
- learned preference hypotheses;
- usage, quota, billing, security, audit, and support records;
- AI request metadata and derived data.

Health-related restrictions and allergies require heightened treatment and access control.

## Privacy principles

- collect only data required for a documented purpose;
- separate required processing from optional product improvement, marketing, advertising, and publication;
- use privacy-preserving defaults;
- expose source, purpose, sharing, and retention information;
- keep raw private content out of default telemetry;
- enforce household isolation and least privilege;
- make correction and deletion operational capabilities;
- avoid retaining transient source files;
- review new providers and data flows before integration.

## Account minimum

A minimal account may require:

- email or external identity;
- internal Cocinaris user UUID;
- language, region, timezone, and measurement preference;
- adult and terms declaration;
- privacy-notice acknowledgement;
- essential operational communication address.

Do not require legal name, CPF, home address, full birth date, gender, or other identity data without a documented product or legal purpose.

## Consent and permissions

Do not use one generic checkbox to cover unrelated processing.

Separate, where legally and technically appropriate:

- acceptance of terms;
- acknowledgement of the privacy notice;
- processing of allergies or other sensitive data;
- web push permission;
- optional email communication;
- permanent user-photo storage;
- AI processing required for an explicitly requested operation;
- marketing and advertising tracking;
- private sharing and public publication.

Consent is not assumed to be the lawful basis for every operation. Final bases require legal analysis.

## User rights and privacy center

The first release provides:

- confirmation and understandable information about processing;
- access to account and domain data;
- correction of incomplete or inaccurate data;
- readable export;
- deletion of private recipes, executions, photos, inventory, and other eligible records;
- notification and permission management;
- consent withdrawal where applicable;
- information about processors and sharing;
- account deletion and progress tracking;
- a support path for rights that cannot be fully self-served.

Some deletion requests may be limited by security, financial, legal, or dispute-retention obligations. Such exceptions must be documented and minimized.

## Account deletion workflow

```text
Authenticate and reconfirm destructive intent
→ disable or schedule login termination
→ revoke sessions and notification tokens
→ freeze conflicting writes
→ delete or anonymize eligible domain data
→ delete permanent photos
→ process identity-provider deletion or unlinking
→ propagate processor requests where required
→ retain only justified minimal records
→ expire backup copies under policy
→ complete and audit the request
```

The workflow is durable, resumable, observable, and idempotent.

## Retention

Define retention separately for:

- active account data;
- inventory and correction history;
- recipe revisions and executions;
- user photos;
- AI operation metadata;
- prompts and responses, when any retention is justified;
- security and access logs;
- audit events;
- support data;
- quota and billing ledgers;
- notification delivery logs;
- deleted-account tombstones;
- backups.

`Indefinite` is not an acceptable undocumented default.

## AI processing

- send only context required for the requested operation;
- remove direct identifiers unless needed;
- do not send complete household history by default;
- record provider, model, purpose, and data category;
- prohibit training or secondary use unless contract and user policy explicitly permit it;
- evaluate provider retention, subprocessors, region, and transfer mechanisms;
- provide useful non-AI operation where defined;
- do not log complete prompts and responses by default.

## Images

### Temporary import images

Receipt and recipe-import images are deleted immediately after successful parsing or immediately after a reported failure. No automatic retry retains the file.

### Permanent user photos

A recipe or execution photo is stored only through explicit user action. The user can view and delete it. Publication is a separate explicit action.

## International transfers

Before production launch:

- map controller, processor, subprocessor, and transfer roles;
- identify storage and processing countries;
- review applicable LGPD international-transfer mechanisms;
- review GDPR transfer mechanisms where relevant;
- maintain a processor and subprocessor inventory;
- ensure contractual and technical safeguards;
- reflect transfers in the privacy notice;
- provide a vendor replacement and deletion path.

## Automated decisions and explanations

Cocinaris recommendations remain advisory. Users can inspect relevant factors, correct data, reject recommendations, and request an alternative.

The system does not expose private chain-of-thought or provider reasoning. It provides product-level explanations such as urgency, inventory use, preference fit, effort, and purchase impact.

## Official references

- LGPD data-subject information: https://www.gov.br/anpd/pt-br/assuntos/titular-de-dados-1/direito-dos-titulares
- ANPD international data transfers: https://www.gov.br/anpd/pt-br/assuntos/assuntos-internacionais/transferencia-internacional-de-dados
- GDPR text: https://eur-lex.europa.eu/eli/reg/2016/679/oj
