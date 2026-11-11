# PLAN-0049: Privacy Center ("Seus dados")

- **Status:** In Progress; decisions D1 to D4 answered by the owner on 2026-11-11; Slice 1 in a draft PR
- **Type:** Implementation
- **Priority:** High: the next step is the external test with three people (PLAN-0065 Gate S5), who must be able to take and delete their own data
- **Owner:** Claude Code (claimed 2026-11-11)
- **Created:** 2026-11-11
- **Last updated:** 2026-11-11
- **Branch:** Plan and Slice 1 `agent/plan-0049-privacy-center`; each later slice uses the branch named in its section
- **Pull request:** Slice 1 draft PR, opened with this file
- **Related issues:** None
- **Related ADRs:** ADR-0013 (web push); none new is needed, see "Background work"
- **Dependencies:** None open. The roadmap listed PLAN-0039 (queue, outbox, workers) first; this plan uses the hosted-service pattern already in production instead (see "Background work").

## How to use this plan

Execute one slice at a time, in order. A slice is one pull request from a freshly fetched `origin/main`, and the next starts only after the owner merges the previous one. Agents never merge, approve, or enable auto-merge.

- **Deviation rule:** if the code contradicts a detail here, keep the outcome and the invariants, adapt the mechanics, and record the deviation in the progress log.
- **Stop conditions:** an invariant cannot be met, a decision below turns out to be needed again, or an unrelated failure blocks the gates.
- Before every commit, update `Execution state`, `Progress log`, and the registry row.

## Objective

A person can, from one page, see what Cocinaris keeps about them and who processes it, download all of it, reach where each part is corrected, and delete their account. Data that has no purpose any more is deleted by a daily job instead of living forever. This delivers the "User rights and privacy center", "Account deletion workflow", and "Retention" sections of [`privacy-and-data-protection.md`](../security/privacy-and-data-protection.md).

## Decisions

### Answered by the owner (2026-11-11)

| ID | Question | Answer |
|---|---|---|
| D1 | When does an account deletion happen? | **At once**, after a typed confirmation, with no grace period and no way back. The work is still durable and resumes if interrupted. |
| D2 | What happens to the login at the identity provider? | **Unlink only.** Every Cocinaris record is deleted or anonymized; the login stays at the provider. Signing in again later starts an empty account. The owner deletes a login by hand when someone asks; the support path says so. |
| D3 | Export format? | **A ZIP with `cocinaris-dados.json` (complete) and `cocinaris-dados.html` (readable).** |
| D4 | AI usage records of a deleted account? | **Anonymized and kept for 13 months**, then deleted by the retention job. The owner id is replaced by one random id per deletion, so monthly spend stays correct. |

### Decided by this plan (engineering defaults the owner can change in review)

| ID | Decision | Why |
|---|---|---|
| E1 | Deletion needs a **recent sign-in**: at most 15 minutes old, or the page sends the person through sign-in again before confirming. | "Authenticate and reconfirm destructive intent" in the privacy document; a stolen or forgotten open session must not delete an account. |
| E2 | Sessions get an **absolute lifetime of 30 days** on top of the 8-hour sliding idle timeout, through a sign-in time claim. | Sliding sessions can live forever today, so a deletion could not reliably end every session on every device. With a 30-day cap, a deletion tombstone needs to live only 31 days. |
| E3 | A **deletion tombstone** keeps the SHA-256 of issuer and subject, the deletion time, and per-table counts (no content) for 31 days. | It rejects sessions from before the deletion, and it is the minimal record that the request was completed. |
| E4 | **Idempotency records** are deleted after 7 days. | They replay responses for retries, which happen within minutes; they duplicate personal data. Verify the longest retry window in code first (hazard H5). |
| E5 | **Audit events** stay as long as the account (the pantry shows them as correction history) and go with it. | They are the person's own history. |
| E6 | The export leaves out **push keys** (it says how many devices and since when), **idempotency records**, and **image bytes**. | Push keys are secrets of the browser; idempotency records duplicate other data; dish images are shared generic assets, not the person's. |
| E7 | A support contact comes from configuration (`Privacy:ContactEmail`); without it, the support line is hidden. | No personal address in code or documents. |

## Invariants

- Every table with an owner column is covered by an exporter and an eraser, or listed as an exemption with a reason. A test reads the EF model and fails when a new owner table is added without one (Slice 1 adds the test, Slice 2 extends it to erasers).
- An export holds only the requester's rows. A deletion touches only the requester's rows. Cross-user isolation is tested for both.
- Modules keep their tables: each module exports and erases its own data through a contract in `Cocinaris.SharedKernel`; nothing reads another module's tables.
- Deletion is idempotent and resumable: each step is a set operation by owner id, recorded as done, and a restart continues at the first step not done.
- Shared data stays: `recipes.recipe_ideas`, `recipes.recipe_thumbnails`, and the `ingredients` tables lose only the person's id (`RequestedByUserId` set to null), never the row.
- The HTML export is static: no scripts, no external resources, every value HTML-escaped. Recipe text and names are hostile input.
- No exported or deleted content reaches logs or metrics. Metrics hold counts, durations, and closed codes.
- AI is not involved anywhere in this plan.

## Data map

Owner column per table, from `ApplicationDbContextModelSnapshot` on 2026-11-11.

| Table | Owner column | Export (Slice 1) | Deletion (Slice 2) |
|---|---|---|---|
| `identity.users` | `Id` | Account id and creation date | Deleted last |
| `profiles.user_profiles`, `preference_entries`, `ordered_code_entries`, `equipment_entries`, `change_history` | `OwnerUserId` | All | Deleted |
| `inventory.products`, `lots`, `transactions`, `preparation_batches`, `preparation_inputs`, `preparation_outputs`, `prepared_lots` | `OwnerUserId` | All | Deleted |
| `recipes.recipes`, `recipe_revisions`, `recipe_executions`, `generation_sessions`, `revision_proposals`, `saved_candidates` | `OwnerUserId` | All (hints and requests included; they are the person's words) | Deleted |
| `planning.menu_plans`, `menu_entries`, `lot_protections`, `package_sizes`, `shopping_checks`, `shopping_line_marks`, `shopping_plan_sessions`, `shopping_plan_cards`, `reminder_dismissals`, `push_delivery_days` | `OwnerUserId` | All | Deleted |
| `planning.push_subscriptions` | `OwnerUserId` | Count and creation dates only (E6) | Deleted first, so no notification is sent during deletion |
| `ai.usage_ledger` | `OwnerUserId` | Operation, model, tokens, cost, dates | Anonymized (D4) |
| `platform.audit_events` | `ActorUserId` | Event name, target type, date (metadata only when it names nothing of another person) | Deleted (E5) |
| `platform.idempotency_records` | `OwnerUserId` | Not exported (E6) | Deleted |
| `recipes.recipe_thumbnails`, `ingredients.resolution_queue` | `RequestedByUserId` | Not exported (shared) | Id set to null |
| `recipes.recipe_ideas`, `ingredients.aliases`, `catalog_items`, `catalog_state` | None | Not exported (shared reference data) | Untouched |

Outside the database: the browser's IndexedDB `cocinaris-offline` and the push subscription are cleared by the client after deletion, as on logout; host backups expire under the backup policy, which Slice 3 writes into the privacy document.

## Background work

The roadmap put PLAN-0039 (queue and outbox) before this plan. Deletion needs durability, not a queue: a row in `platform.account_deletions` holds the state and the steps done, and a hosted service, like `InventoryAutoRemovalWorker` and `PlanReminderPushWorker`, picks it up within seconds and on every start. The retention job is another hosted service, once a day. When PLAN-0039 lands, both can move to it without a change of contract. No ADR is needed because no new technology or pattern is introduced.

## Slices

### Slice 1: The page, the inventory, and the export

Branch: `agent/plan-0049-privacy-center`.

- [x] `IAccountDataExporter` in `Cocinaris.SharedKernel`: a module name, the tables it covers, and a method that writes its sections for one owner. One implementation per module (identity, profiles, inventory, recipes, planning, AI, platform), each reading only its own tables through its own store.
- [x] `GET /api/v1/privacy/export`: streams `cocinaris-dados-AAAA-MM-DD.zip` with `cocinaris-dados.json` (schema version, generation time, one section per module) and `cocinaris-dados.html` (sections in the person's language, escaped, no scripts). Rate limit: 3 per hour per person. `Cache-Control: no-store`.
- [x] Coverage test over the EF model: every entity with `OwnerUserId`, `ActorUserId`, or `RequestedByUserId` is covered by an exporter or listed as an exemption with a reason.
- [x] Page `/app/perfil/privacidade`, "Seus dados", linked from the profile overview:
  - "O que guardamos": the data categories in plain words, the sensitive ones (allergies, restrictions) marked.
  - "Quem processa": the host, the AI providers by name and what each receives, the push services, the identity provider (named generically), from one documented list.
  - "Baixar meus dados": one button; the download starts, and an error says so.
  - "Corrigir": links to profile data, preferences, equipment, pantry, recipes, and menus.
  - "Notificações": where they are turned on or off.
  - "Excluir minha conta": present but disabled with "Em breve" until Slice 2.
  - Support line from `Privacy:ContactEmail` (E7).
- [x] Copy in pt-BR, en, and es; privacy document updated (the page, the export, what it leaves out).

**Gate S1:** an integration test fills every exported table for person A and some for person B, downloads A's export, opens the ZIP, and checks every table's rows are there and no row of B is; a recipe named `<script>alert(1)</script>` appears escaped in the HTML; the coverage test passes; frontend tests for the page; one browser check of the page and the download at 360 px.

### Slice 2: Account deletion

Branch: `agent/plan-0049-s2-deletion`.

- [ ] `IAccountDataEraser` in `Cocinaris.SharedKernel`, one per module, each idempotent; the coverage test now also requires an eraser or an exemption.
- [ ] Sign-in time claim at OIDC sign-in; cookie validation rejects a session older than 30 days (E2) or older than a tombstone for its subject (E3). A session without the claim counts as signed in at its issue time.
- [ ] `POST /api/v1/privacy/account-deletion` with the typed word, after a recent sign-in (E1, else `reauthentication_required`): creates `platform.account_deletions` (subject hash, owner id, state, steps done, counts, times), signs the person out, returns 202. Repeating it while one runs returns the same request.
- [ ] `AccountDeletionWorker`: push subscriptions, planning, recipes, inventory, profiles, shared-table ids, AI anonymization (D4), platform, and `identity.users` last; each step in its own transaction, recorded as done; failures retried with backoff and counted in `cocinaris.privacy.deletion{outcome}`.
- [ ] Sign-in while a deletion runs is refused with a page that says so; after it completes, signing in starts an empty account (D2).
- [ ] Page: "Excluir minha conta" explains what goes, what stays (the login at the provider, D2; anonymized usage for 13 months, D4), offers the export first, asks for the word ("EXCLUIR", "DELETE", "ELIMINAR"), then shows a public page "Sua conta foi excluída". The client clears IndexedDB and unsubscribes push, as logout does.
- [ ] Runbook: how the owner deletes a login at the provider on request, and how to read a stuck deletion.

**Gate S2:** person A with rows in every table and person B with rows: after A's deletion, every A row is gone or anonymized and every B row is unchanged; a failure injected at step 4 resumes from step 4 after a restart; an old session of A is rejected; signing in again creates a new empty account; shared thumbnails and ideas remain; frontend tests; one browser run of the whole deletion on the replica.

### Slice 3: Retention

Branch: `agent/plan-0049-s3-retention`.

- [ ] `RetentionWorker`, once a day: idempotency records older than 7 days (E4), AI usage older than 13 months (D4), deletion tombstones older than 31 days (E3), push delivery days older than 30 days, abandoned shopping plans older than 30 days (today only when a new plan starts).
- [ ] Each rule is a set delete with a cap per run, logged as counts only, and measured by `cocinaris.privacy.retention_deleted{rule}`.
- [ ] Retention table in the privacy document for every category of "Retention", including those kept as long as the account and host backups (from the deployment runbook).

**Gate S3:** integration tests with a fake clock for each rule, including a row one day inside the limit that stays.

## Hazards

- **H1. A new owner table without export or deletion.** The coverage test is the guard; never add a broad exemption to make it pass.
- **H2. Module boundaries.** The Privacy orchestration must call module contracts only; the architecture tests must stay green without new exceptions.
- **H3. Large exports.** Stream the ZIP; never build it in memory. Recipe JSON can be large.
- **H4. Hostile text in the HTML export.** Escape every value; set `Content-Security-Policy: default-src 'none'` in the HTML's meta tag as well.
- **H5. Idempotency retention.** Check every scope's replay window before deleting at 7 days.
- **H6. Sessions on other devices.** Without the sign-in time claim, an old session could recreate an empty account for a deleted subject; the tombstone check must run before `FindOrCreateAsync`.
- **H7. Shared images.** A thumbnail can be used by another person's recipe; only the id is cleared.

## Excluded

Deleting the login at the identity provider (D2), legal review and the privacy notice text (owner and counsel), a deletion grace period (D1), consent records beyond the existing adult confirmation and push opt-in, data portability into another service's format, and processor deletion requests to AI providers (none keep data under the configured terms; to be confirmed in the legal review).

## Execution state

- **Current run delivery target:** Plan and Slice 1.
- **Current checkpoint:** Slice 1 implemented on `agent/plan-0049-privacy-center`, in a draft pull request.
- **Last completed step:** Export, page, coverage test, documents; see the Slice 1 log entry.
- **Exact next action:** The owner reviews and merges the Slice 1 pull request and deploys `main`. Then Slice 2 (account deletion) starts from a fresh `origin/main` on `agent/plan-0049-s2-deletion`.
- **Blockers:** None.
- **Partially modified areas:** None.
- **Documentation delivered:** This plan and its registry row; privacy document (page, processors, export, what is left out, coverage); deployment runbook (`Privacy__ContactEmail`, limit, metric) and `api.env.example`.
- **Validation performed:** See the Slice 1 log entry.
- **Known failures or limitations:** See the Slice 1 log entry.
- **Working tree state:** Clean after this commit.

## Progress log

### 2026-11-11 — Claude Code

- **Run delivery target:** Plan and Slice 1, after the owner merged PLAN-0065 Slice 6 (#102) and said "Merge feito, continue", with PLAN-0049 as the next step recommended earlier in the conversation.
- **Owner answers (2026-11-11):** D1 "Imediata", D2 "Só desvincular", D3 "ZIP: JSON + página legível", D4 "Anonimizar e manter 13 meses".
- **Changes:** This plan; registry row moved from the reserved roadmap to active plans.
- **Validation performed:** None; documentation only.
- **Next action:** See "Exact next action".

### 2026-11-11 — Claude Code (Slice 1)

- **Changes:**
  - **Backend.** `IAccountDataReader` and `IAccountDataSnapshot` in `Cocinaris.SharedKernel.Privacy`; `AccountDataCatalog` (32 exported tables by module, 3 exemptions with reasons) and `PostgreSqlAccountDataReader` (one read-only repeatable-read snapshot on its own connection; `to_jsonb(t) - omitted` per table, filtered by the owner column) in Infrastructure; `PrivacyExportService` (streamed ZIP: JSON with every row, columns stored as JSON written as JSON; HTML in pt-BR, en, or es, every value escaped, `default-src 'none'`), `AsyncFlushingStream`, and `GET /api/v1/privacy` and `GET /api/v1/privacy/export` in the API. Rate limit `privacy-export`, 3 per person per hour; metric `cocinaris.privacy.export{outcome}`; `PrivacyOptions.ContactEmail`.
  - **Rate limits per person.** All policies now partition by `sub`, then the name-identifier claim, then the address. Before, they used `sub`, then the display name, then the address; a subject mapped to the name identifier never matched `sub`, so people with the same display name, or every person behind one address, shared a limit. Found by the export's limit test.
  - **Frontend.** `/app/perfil/privacidade` ("Seus dados"), linked from the profile overview; `PrivacyRepository` and its live adapter (the ZIP is saved through an object URL so a failure or the hourly limit is said on the page); copy in pt-BR, en, and es.
  - **Formatting fix for PLAN-0065 Slice 6:** the repeat-limit migration's byte-order mark, in its own commit.
- **Deviations from this plan:**
  - **One catalog instead of one exporter class per module.** The tables are declared by module in `AccountDataCatalog` and read by one generic reader, because PostgreSQL makes each row JSON and a per-module class would only repeat table names. Each table is still read only through its owner column, and the coverage test covers the same ground. Slice 2's erasers follow the same catalog.
  - **The HTML shows codes as stored** (for example a restriction code) instead of localized labels; the labels live in the frontend. The JSON is complete either way.
  - **No 401 test for the export.** The integration host signs every request in; the route is under the authorized group like every other one.
- **Validation performed:** Integration: `TheExportHoldsEveryTableOfThePersonAndNothingOfAnyoneElse` (every catalog table present, only the person's rows in each, the other person's recipe and pantry absent from JSON and HTML, a recipe named `<script>alert(1)</script> Bolo` escaped, JSON columns written as JSON, en page) and `TheExportIsLimitedPerPerson` pass. Architecture: 17, of which 3 new (coverage, catalog names real tables and columns, same copy keys in three languages). Frontend: typecheck, lint, format, build, and 639 tests (3 new). OpenAPI exported, client regenerated and mirrored, drift check passed. Backend formatting verified. Proxy replica at 360 px: new `privacy-journey.mjs` 8 of 8 (link from the profile, no sideways scroll, controls at least 44 px, a dated ZIP of 192 KB with both files, the saved notice). `ApiAuthenticationTests` (56) and the two export tests pass after the rate-limit change, with the development identity provider running; without it, the 10 sign-in tests fail with 500, as they always do.
- **Not run:** the full local gates (one run on the pull request head follows); a real phone.
- **Next action:** See "Exact next action".

## Completion and handoff checklist

- [ ] All slices and gates are resolved truthfully.
- [ ] Required tests pass, or limitations are documented.
- [ ] Durable documentation, contracts, comments, XML documentation, and TSDoc are current.
- [ ] Security, privacy, localization, accessibility, and operational impacts were reviewed.
- [ ] `docs/plan-status.md` matches this plan.
- [ ] Pull requests link this plan and report validation evidence.
- [ ] Exact continuation instructions exist if the work is not complete.
