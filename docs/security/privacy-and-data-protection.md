# Privacy and Data Protection Requirements

- **Status:** Accepted product and architecture requirements
- **Last updated:** 2026-07-28
- **Legal status:** Requires professional legal review before launch

## Scope

Cocinaris is designed for LGPD- and GDPR-oriented operation. This document defines engineering and product requirements, not legal advice or final lawful-basis determination.

Production is initially expected in the United States, so international data-transfer analysis is mandatory before users in Brazil, the European Economic Area, or other regulated jurisdictions are served.

## Adult-only service

Cocinaris is for users aged 18 or older who can legally accept the terms in their jurisdiction.

The initial product records an adult declaration and terms version. It does not collect a complete birth date without a separate documented purpose. Country availability and wording require legal review. The declaration is asked once and the interface does not repeat age notices (owner decision 2026-10-28, PLAN-0053).

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

## Ingredient identity

PLAN-0055 ([ADR-0010](../architecture/decisions/0010-ingredient-identity-catalog-embeddings-pgvector.md)) adds global tables in the `ingredients` schema. They are shared by all users and not part of any one user's data:

| Table | Holds | Personal data | Retention |
| --- | --- | --- | --- |
| `catalog_items`, `catalog_vectors` | Curated project catalog and its vectors | None | While the catalog exists |
| `aliases` | Normalized ingredient name → catalog ingredient | None: no owner; a name is a food word, not linked to who typed it | Indefinite as reference data; a wrong alias is deleted by the owner |
| `text_embeddings` | Vector by SHA-256 of space and exact text | None: no owner and no text | Indefinite as a cache; may be truncated at any time |
| `resolution_queue` | Normalized and exact name, owner while pending, best candidate | Owner id only while the name is pending; cleared when decided | Pending names: until decided (minutes). Unresolved names: exact text without owner, kept so a later catalog can re-check them |

Only single ingredient names are sent to the embedding provider (OpenRouter, `data_collection: "deny"`, provider order pinned), never notes, recipes, allergies, or profiles.

Account deletion does not need to remove alias or cache rows, because they hold no owner. A pending queue row with the deleted owner is cleared when the job decides it. Deletion should also clear it directly once the deletion workflow is implemented.

## Saved candidates

PLAN-0056 Phase 3 adds `recipes.saved_candidates`:
- **Owner.** Each row belongs to one owner and is only ever read through owner-scoped queries.
- **Contents.** A copy of a suggestion the owner kept for later, including the names of their own pantry items it used.
- **After cooking.** A cooked entry keeps the id of the recipe it became, so a retried request returns the same recipe. It no longer appears in the list.
- **Retention and deletion.** Rows live as long as the account. Export and deletion must include them, together with the owner's recipes and generation sessions.

## Suggestion hints, history, and revision requests

PLAN-0060 stores short free text the owner writes for the AI, which may be personal:
- **Suggestion hint** (`recipes.generation_sessions.UserHint`) and **revision request** (`recipes.revision_proposals.Instruction`, up to its first 200 characters also as the accepted revision's change reason).
- **Owner.** Rows belong to one owner and are only read through owner-scoped queries.
- **Purpose.** Sent to the AI provider once as untrusted data, and shown back to the owner. Never written to logs or the usage ledger.
- **Never shared.** Recipes shaped by this text never enter the shared idea library.
- **Suggestion history.** Past requests stay listed with their candidates. "Remover" hides the request and clears its hint at once; its candidates stay, because saved entries and recipes may come from them.
- **Proposals.** A proposal's content is cleared when it is accepted or discarded. An undecided proposal stays until the account is deleted, and it changes nothing.
- **Retention and deletion.** Rows live as long as the account. Export and deletion must include them, together with the owner's recipes and generation sessions.

## Shopping plans

PLAN-0062 stores, per owner, the setup answers of each plan, and dish cards that may hold pantry names.

- **Purpose.** Build the plan and reuse the last answers as defaults. Answers never update the profile.
- **Retention.** Unseen and rejected cards are deleted when the plan is accepted, and all cards when it is discarded. Abandoned plans older than 30 days are deleted when the owner starts a new plan; there is no background job yet.
- **Preparation session.** The plan also keeps which tasks were done or skipped and when its timers end. These are identifiers and times with no food text, and they are deleted with the plan.
- **Typed ingredient.** It is untrusted text, limited to 60 characters, kept in the plan's setup, and never logged. A plan with one never feeds the shared idea library.
- **Typed hint (PLAN-0064).** The hint under "Quero outras opções" is untrusted text, limited to 40 characters, and never logged. It is kept in `DeckPreferenceJson` on the plan and on the cards made under it, and it is deleted with them under the rules above. The dish it compares with may also be sent to the model by name. A dish made under a hint never feeds the shared idea library.
- **Metrics.** They hold counts and closed codes, never dish or ingredient names.
- **On the device (ADR-0012).** The IndexedDB database `cocinaris-offline` holds the owner id and display name, the last shopping lists (at most five menus, with ingredient and dish names), unsent marks, and marks that stopped applying, under keys prefixed by the owner id. It is erased at logout and before another owner's session starts. A person who leaves without logging out leaves their last list readable offline on that device. An ended session keeps the data for the same owner and stops opening it. The service worker caches application files only and never an API response. No token is stored.
- **Notifications (ADR-0013).** With opt-in, `planning.push_subscriptions` holds the browser's push address and keys, and when a push service last accepted a message. `planning.push_delivery_days` holds the last day the owner was notified. A subscription is deleted when the person turns notifications off or logs out, when the push service says it is gone, or when the owner exceeds 10. The message holds a closed type and the language, encrypted for the browser. It names no food, health data, or restriction, and the push service sees only that a message was sent and when. Addresses and keys are never logged.
- **Rights.** Export and deletion must include `planning.push_subscriptions`, `planning.push_delivery_days`, `planning.shopping_plan_sessions`, `planning.shopping_plan_cards`, and `planning.shopping_line_marks` when the privacy center (PLAN-0049) is delivered. A mark may hold a typed substitute name, which is untrusted, limited to 120 characters, and never logged.

## Shared recipe ideas

PLAN-0055 Phase 2 ([ADR-0011](../architecture/decisions/0011-shared-recipe-idea-library.md)) adds `recipes.recipe_ideas`: generic recipe ideas shared by everyone, with no owner column. An idea is stored only after an AI expansion, and only when it can be made generic:
- catalog ingredient names;
- no inventory or equipment ids;
- no private-context term;
- no name the owner typed outside the catalog.

It keeps the restriction codes it was generated under, without linking them to any person. It is reference data, retained indefinitely. Account deletion does not need to remove ideas, because they hold nothing linked to the account. The person's own saved recipe stays a private copy.

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

Receipt photos (PLAN-0046, off until `Ai__Receipts__Enabled=true`):
- **Why this matters.** A receipt can show a tax id, an address, the store visited, and card digits.
- **Never on disk.** The upload is a raw request body, not a form, because ASP.NET buffers large forms to a temporary file. The bytes stay in process memory for that one request and are cleared when it ends, on every path. No object storage, queue, log, or ledger entry holds them.
- **Re-encoded.** A new JPEG of at most 1600 pixels is what leaves the host, so metadata such as location does not.
- **Third party.** The image goes once through OpenRouter to the configured vision model, with `data_collection: deny`. The person is told before choosing a photo. That option depends on the providers honoring it; review OpenRouter's terms before enabling.
- **Output.** The model is told to leave out personal data, and code independently drops any item whose name has a run of 8 or more digits, a link, or markup. Items are proposals: nothing reaches the pantry until the person confirms them.

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
