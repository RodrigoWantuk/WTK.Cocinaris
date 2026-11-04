# PLAN-0046: Pantry List and Receipt Photo Input

- **Status:** Paused
- **Type:** Implementation
- **Priority:** High (owner asked to conclude it on 2026-11-04)
- **Owner:** Claude Code
- **Created:** 2026-11-04 (the roadmap ID dates from the PLAN-0031 amendment; no plan file existed)
- **Last updated:** 2026-11-04
- **Branch:** `agent/plan-0046-pantry-import`
- **Pull request:** #80, merged by the owner; deployed `2542fa3` on 2026-11-04
- **Related documents:** [`docs/ai/pantry-import.md`](../ai/pantry-import.md), [`docs/product/initial-release.md`](../product/initial-release.md), [`docs/security/privacy-and-data-protection.md`](../security/privacy-and-data-protection.md)

## Scope

The accepted first release requires manual list entry, AI parsing of a manual list, receipt-photo parsing, immediate source-image deletion, and no automatic retry that retains the image.

- **Phase 1, plain list:** `PantryListParser`, deterministic and with no AI, with the review screen. This was uncommitted work from an earlier session, now rebased on `main`.
- **Phase 2, AI list:** "Organizar com IA", on explicit request, for lists the plain reader cannot understand.
- **Phase 3, receipt photo:** "Foto da nota fiscal", feeding the same review, off by default.

## Decisions

- **No object storage.** The roadmap title named it, but nothing in this flow is stored. Memory for one request satisfies the deletion requirement more strictly than a temporary bucket. Permanent photos stay with PLAN-0047.
- **Raw body, not a form,** because ASP.NET buffers larger form files to a temporary disk file.
- **Vision goes through OpenRouter,** because DeepSeek (ADR-0009) has no vision model in use and the deployment already holds an OpenRouter key. The model is configuration. No ADR: this adds a replaceable provider call under ADR-0005's gateway rules and does not change ADR-0009's recipe provider.
- **Off by default.** Sending receipts, which can show a tax id, an address, and card digits, to a third party is the owner's decision. Owner, 2026-11-04: keep it off for now; it will matter later.
- **The AI is opt-in per use,** so plain lists stay free and work without it.

## Acceptance criteria

- [x] A typed or pasted list becomes editable proposals with no AI; nothing is saved until confirmation.
- [x] The AI can organize a messy list on request, with validated output, usage accounting, and one repair.
- [x] A receipt photo becomes proposals in the same review, with doubtful items marked.
- [x] The photo is never written to disk and is wiped after the call; a failure is reported with no retry; metadata does not leave the host.
- [x] Without the AI or the setting, the option is hidden and manual input still works.
- [x] The owner decides on enabling receipts: not for now (2026-11-04).
- [ ] When enabled later: a real receipt is read successfully.
- [ ] The AI list runs on the real model (the live evaluation in PLAN-0060).

## Execution state

- **Current run delivery target:** all three phases.
- **Current checkpoint:** merged and deployed (`2542fa3`) with receipt reading off; the service is healthy. Plain list and AI list are live.
- **Validation (lean):**
  - **Backend unit:** `PantryImport`, `PantryListParser`, `RecipeAiDirection`, and `RecipeCookNow` filters, 71/71.
  - **Backend integration (Testcontainers):** list, AI list, receipt, disabled state, image preparation, and PLAN-0060's history and revision, 8/8.
  - **Frontend:** typecheck; `PantryListPage` and `ProductionInventoryForm` tests, 25/25; i18n catalogs.
  - **Contracts:** OpenAPI regenerated and client synced.
- **Not run:**
  - **Real models:** the list and receipt operations have only run against fakes. No real receipt has been read, and the default vision model is unevaluated.
  - Browser walkthrough and a phone camera test.
  - Full suites.
- **Known limits:**
  - "Wiped" covers the byte arrays this code owns. The base64 copy in the outgoing request and native decoder buffers are released, not zeroed, and the runtime may hold copies until collection.
  - `data_collection: deny` depends on OpenRouter and the upstream honoring it.
  - The digit rule catches document and card numbers by length; a shorter personal value in a name would pass and is visible in the review.
- **Exact next action:** Run PLAN-0060's live evaluation, which also covers the AI list. When the owner wants receipts:
  1. Review OpenRouter's terms and evaluate the vision model on real receipts.
  2. Set `Ai__Receipts__Enabled=true` in `/etc/wtk-cocinaris/api.env` and restart the service.
  3. Read one real receipt from a phone and check that nothing personal appears in the review.
- **Blockers:** None; receipt reading waits for the owner's decision to enable it.

## Progress log

- 2026-11-04: The owner merged #80 and asked for deployment with receipts left off; `2542fa3` is deployed. Paused until the owner wants receipts.
- 2026-11-04: Corrected PLAN-0060's live evaluation command, which would have sourced an environment file that is not shell-safe.
- 2026-11-04: Moved the Phase 1 work to a branch from fresh `main` and delivered Phases 2 and 3 with tests and documentation.
