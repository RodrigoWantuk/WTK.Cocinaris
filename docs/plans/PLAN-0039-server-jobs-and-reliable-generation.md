# PLAN-0039: Server Jobs and Reliable Recipe Generation

- **Status:** In Progress; scope agreed with the owner on 2026-11-11; Slices 1 and 2 merged (#106, #107) and deployed; Slice 3 in a draft PR
- **Type:** Implementation
- **Priority:** High (owner request)
- **Owner:** Claude Code (claimed 2026-11-11)
- **Created:** 2026-11-11
- **Last updated:** 2026-11-11
- **Branch:** Plan and Slice 1 `agent/plan-0039-reliable-generation`; each later slice uses the branch named in its section
- **Pull request:** #106 (plan and Slice 1, merged); #107 (Slice 2, merged); Slice 3 draft PR
- **Related ADRs:** ADR-0003 (PostgreSQL job state and outbox, RabbitMQ as target), ADR-0009 (AI gateway)
- **Dependencies:** None open.

## How to use this plan

Execute one slice at a time, in order. A slice is one pull request from a freshly fetched `origin/main`, and the next starts only after the owner merges the previous one. Agents never merge, approve, or enable auto-merge.

- **Deviation rule:** keep the outcome and the invariants, adapt the mechanics, and record the deviation in the progress log.
- **Stop conditions:** an invariant cannot be met, a decision below turns out to be needed again, or an unrelated failure blocks the gates.
- Before every commit, update `Execution state`, `Progress log`, and the registry row.

## Objective

Recipes for a menu are prepared on the server, so they finish with the app closed and survive restarts. All background work shares one job format. Recipe generation recovers from passing failures without the person noticing. When it still fails, the person is told plainly what happened and what to do. Every failure leaves a content-free record of why, so the causes can be found and fixed.

## Owner scope (2026-11-11)

The owner agreed on two items: "Sim, pode seguir dessa forma, apenas itens 1 e 2":

1. take the preparation of recipes out of the browser into a server job;
2. one format for all background jobs.

The outbox and RabbitMQ (ADR-0003's target) are left for later. ADR-0003 accepts polling the database without a broker "as a temporary bootstrap". RabbitMQ enters when one API process can no longer carry the background work, or when workers must scale on their own. Both are measured by the job metrics of Slice 2.

The owner added: "algumas vezes a geração de receita falha na primeira tentativa e precisa ser tentada de novo, precisamos garantir na verdade que nunca falhe, e se falhar, seja transparente pro usuário isso. Mas entender nos casos em que falhou, o por que falhou, é prioridade também."

"Never fails" cannot be promised for a model that answers freely. This plan makes it the target: the person sees a failure only after every recovery is spent. A metric reports the share of generations that ended in a failure the person saw, so the target can be checked.

## Evidence (production, 30 days to 2026-11-11, counts only)

| What | Count |
|---|---|
| "Cozinhar agora" selections | 5 selected, **3 failed** (`ai_output_invalid`) |
| Menu selections | 5 selected, **1 failed** (`ai_output_invalid`), **1 stuck in `Expanding`** |
| `recipe.expand_selected.v1` calls | 10 settled, 1 released (`reheatNotes` length), **1 stuck in `Reserved`** |
| `recipe.suggest_*` calls | 3 released with rule flags: summary word count, `assumptionsUsed` item length, `additionalIngredients` item count |

What the code shows behind them:

- **F1. Small format slips fail a whole recipe.** The schema limits informational text tightly: an assumption is at most 40 characters, `reheatNotes` 1 to 240, and a summary has a word limit. One slip fails validation; the single repair call often slips again.
- **F2. The fallback model is used only when the provider is down.** It is never tried for an invalid answer or a timeout.
- **F3. An interrupted selection never recovers.** If the request dies while the model works (the app closed, a deploy, a timeout), the generation session stays `Expanding`. The same request then gets "in progress" until the session expires two hours later.
- **F4. A stuck reservation blocks the person's AI.** A `Reserved` usage row counts toward the person's concurrent-call limit and the spend forever. After two of them, every AI request of that person is refused as a conflict, and menu dishes wait without end.
- **F5. The diagnosis loses the limits.** `InvalidFlags` removes every number, so "length between 1 and 240" is stored as "length between and". The ledger has one row per operation, not per provider call, so which call failed, and with which model, is not recorded.

## Decisions

| ID | Decision | Why |
|---|---|---|
| R1 | **Informational text is normalized before validation**, deterministically. Text over its limit is cut at a word boundary with "…"; an empty optional note is dropped; an informational list over its count keeps its first items. The fields are listed in one reviewed catalog. Ingredient names, quantities, units, steps, equipment, allergens, restrictions, and dish names are never normalized: their violations still go to repair. Each normalization is recorded. | F1. The AI stays untrusted: validation runs after normalization, and normalization only shortens text that is shown, never what is cooked or bought. |
| R2 | **A failed generation gets a second round on the fallback model**: the call plus its repair, after the primary round ends with an invalid answer, a timeout, or an outage. That makes at most four provider calls per generation, within the operation's cost estimate times two. | F2. A different model rarely repeats the same slip. |
| R3 | **A selection in `Expanding` for more than 5 minutes may be claimed again** by the same request, and its old reservation is released. | F3. 5 minutes is longer than two rounds of calls with their timeouts. |
| R4 | **A reservation older than 15 minutes no longer counts** toward concurrency or spend. A sweep releases it with the flag `abandoned`. | F4. |
| R5 | **Every provider call leaves a content-free attempt record** in `ai.generation_attempts`: time, operation, model, round, call, outcome code, violated rules with their limits but no values, latency, and tokens. It has no owner id, so it is not personal data, and it is kept 13 months like usage. `InvalidFlags` keeps the numbers of a rule and still removes quoted values and indices. | F5 and the owner's priority. |
| R6 | **A report for the owner:** `scripts/ops/ai-failure-report.sh [days]` prints, per operation, success on the first call, success after recovery, failures seen by people, and the top violated rules and models. It reads counts only. | So "why did it fail" is one command away. |
| R7 | **A menu dish is prepared by a server job** (Slice 2), queued when the person chooses it, after the 5-second undo window. The person waits for nothing; the board updates while it is open. | Owner item 1. |
| R8 | **Job retries:** up to 3 job attempts, each with the full recovery of R1 to R3, at 1 and 5 minutes apart. After that the dish is marked failed with its reason. | Some outages last minutes; three attempts cover them without spending without limit. |
| R9 | **What the person sees.** "Preparando a receita…" while it works. "Ainda tentando…" after a failed job attempt. After the last attempt: a reason in plain words for each closed code, plus "Tentar de novo" and "Trocar". | Owner: "se falhar, seja transparente". |
| R10 | **One job format** (Slice 2 and Slice 3): one table, `platform.jobs`, and one runner with leases and backoff. One set of metrics: `cocinaris.jobs.run{kind,outcome}` and `cocinaris.jobs.lag`. New background work uses it. Account deletion and dish images move onto it in Slice 3, and the scheduled jobs (retention, pantry auto-removal, notifications) run as scheduled kinds. Ingredient recognition keeps its own queue, which is also where unrecognized names are kept; it reports through the same metrics. | Owner item 2. |

## Invariants

- AI output is validated after normalization; normalization never changes what is cooked, bought, or checked against a restriction.
- No attempt record, metric, or log holds recipe, pantry, or typed text, or an owner id.
- A job and a request never generate the same recipe twice: the existing idempotency keys of the card and the selection stay the only keys.
- The person's AI limits (daily, monthly, concurrency) apply to jobs as to requests.
- Without AI, a menu still works with the person's recipes and the library, as today.

## Slices

### Slice 1: Generation that recovers, and tells why it failed

Branch: `agent/plan-0039-reliable-generation`.

- [x] R1: `RecipeOutputNormalizer` with its catalog of informational fields, applied before validation for expand, suggest, plan deck, and revise; normalizations recorded as `normalized:<rule>`.
- [x] R2: a fallback round in `RecipeAiInvocation`, with `AiProviderInvocationRequest.UseFallbackModel` honored by the DeepSeek provider and the fake.
- [x] R3: stale `Expanding` reclaimed in `TryClaimSelectionAsync`, its reservation released.
- [x] R4: stale reservations ignored in concurrency and spend, and released by a sweep that runs on every reservation of the person.
- [x] R5: `ai.generation_attempts` (migration), written for every call; `InvalidFlags` keeps limits; retention rule `ai_attempts` at 13 months.
- [x] R6: `scripts/ops/ai-failure-report.sh`; runbook section.
- [x] Metric `cocinaris.ai.generation{operation,outcome}`, with outcome `first_call`, `recovered`, or `failed`.
- [x] Tests: normalization of each listed field, and a field that must not be normalized; a fallback round after an invalid answer; a stale claim reclaimed; a stale reservation not blocking; attempt rows written without content; the report on a test database.

**Gate S1:** targeted tests; the live evaluation on the owner host if the owner runs it (`PLAN0039_LIVE_EVAL=1`, 20 expansions at most US$ 0.10), reporting the share recovered.

### Slice 2: Menu dishes prepared on the server

Branch: `agent/plan-0039-s2-jobs`.

- [x] `platform.jobs` (kind, owner, subject key unique per kind, state, attempts, next attempt, lease, last failure code, times) and `JobRunner` (`FOR UPDATE SKIP LOCKED`, lease 5 minutes, backoff per R8, woken on enqueue, polls every 15 seconds). Added to the privacy catalog: exported, deleted with the account, and finished jobs removed after 30 days by retention.
- [x] `ShoppingPlanApplicationService` and `RecipeCookNowApplicationService` gain owner-explicit paths for expansion, so no job needs a request.
- [x] Choosing an AI or library dish queues `plan_card_expansion` 5 seconds ahead; undo or removal cancels it; "Tentar de novo" queues it again.
- [x] The browser stops preparing: `PlanExpansionProvider` only refreshes the plan while a dish is pending and the page is visible.
- [x] R9 copy in three languages and the states on the board, the menu in use, and "Usar este cardápio".
- [x] Metrics `cocinaris.jobs.run{kind,outcome}` and `cocinaris.jobs.lag`.

**Gate S2:** integration tests: a job that runs with no request, a retry after a failure, a lease that expires and is taken again, a cancel on undo, and two runners on one job. Browser journey on the replica: choose three AI dishes, close the page, reopen it 30 seconds later, and find them ready.

### Slice 3: Every background job in one format

Branch: `agent/plan-0039-s3-unify`.

- [x] Account deletion and dish images run as job kinds; their own tables keep only their domain state.
- [x] Retention, pantry auto-removal, and plan reminders run as scheduled kinds of the same runner.
- [x] Ingredient recognition reports runs through the job metrics; it keeps its queue (R10).
- [x] Runbook: one query and one metric set for every job; how to retry or cancel a job by hand.

**Gate S3:** the existing tests of each moved job pass unchanged in behavior; the replica runs a deletion, an image, and a retention run through the runner.

## Hazards

- **H1. Normalizing too much.** Only catalog fields; a test fails if an ingredient, step, or restriction field is added to the catalog.
- **H2. Doubling cost.** The fallback round only after a failed primary round; the attempt records show its cost, and the report prints it.
- **H3. Two runners or a runner and a request on one dish.** The card's expansion key and the selection claim stay the guard; Slice 2 tests both races.
- **H4. Hidden failures.** A failure the person never sees still lands in the attempt records and the metric; "never fails for the person" must not mean "never recorded".

## Excluded

RabbitMQ, the transactional outbox, a separate worker process (ADR-0003 target, later), cook-now generation as a job (it stays a request the person waits on, with the recovery of Slice 1), and new AI operations.

## Execution state

- **Current run delivery target:** Slice 3, every background job in one format (the last slice).
- **Current checkpoint:** Slice 3 implemented on `agent/plan-0039-s3-unify`, in a draft pull request. Slices 1 and 2 merged via #106 and #107 and deployed with `56f079d`; #107's local gates passed 32 of 32.
- **Last completed step:** Every worker replaced by a job kind of one runner; see the Slice 3 log entry.
- **Exact next action:** The owner reviews and merges the Slice 3 pull request and deploys `main` (migration `MoveAccountDeletionAttemptsToJobs`). After a few days, run `scripts/ops/ai-failure-report.sh 7` and the job query of the runbook, and record here what they show. Then mark the plan Completed; RabbitMQ stays for a later plan, when the job metrics show one process is not enough.
- **Blockers:** None.
- **Partially modified areas:** None.
- **Documentation delivered:** Slices 1 and 2 documents, plus the runbook's job table, deletion and retention entries, the privacy document's retention line, and the ingredient identity components.
- **Validation performed:** See the Slice 3 log entry.
- **Known failures or limitations:** See the Slice 3 log entry.
- **Working tree state:** Clean after this commit.

## Progress log

### 2026-11-11 — Claude Code

- **Run delivery target:** Plan and Slice 1, after the owner agreed to items 1 and 2 and asked that generation never fail for the person, be transparent when it does, and that the cause of every failure be understood.
- **Evidence:** aggregate counts of `recipes.generation_sessions`, `ai.usage_ledger`, and `planning.shopping_plan_cards` on the owner host, codes and counts only; findings F1 to F5 from the code.
- **Next action:** See "Exact next action".

### 2026-11-11 — Claude Code (Slice 1)

- **Changes:**
  - **R1.** `RecipeOutputNormalizer` fits ten informational fields of a recipe answer before validation: the storage note, sensory cues, and the visual description texts and lists. Expand and revise use it; suggest and plan deck record their existing summary fit. `RecipeProtocolValidationResult.Normalized` carries what was fitted, and `RecipeExpandSemantics.MaxAppearanceWords` names the 36-word limit.
  - **R2.** `RecipeAiInvocation.InvokeWithRepairAsync` runs a second round with `AiProviderInvocationRequest.UseFallbackModel` after an invalid answer or an outage, never after a timeout. `DeepSeekAiProvider` calls the fallback model for it. The validator delegate now returns the validation result, and the four callers pass it through.
  - **R3.** `PostgreSqlRecipeGenerationStore.TryClaimSelectionAsync` takes over a selection `Expanding` for more than `ExpansionLease` (6 minutes) under the same key, and only one taker wins.
  - **R4.** The usage reservation releases the owner's `Reserved` rows older than `AbandonedReservationAge` (15 minutes) as `abandoned` at no cost, inside its advisory lock; `CountOpenReservationsAsync` ignores them.
  - **R5.** Migration `AddAiGenerationAttempts`, `ai.generation_attempts` with no owner column, written by `PostgreSqlAiAttemptStore` on its own connection through `RecordingAiProvider`, which wraps the DeepSeek provider. `InvalidFlags` and the new `RuleLabels` keep a rule's numbers and drop quoted values, indices, and identifiers. The `ai_attempts` retention rule keeps rows 13 months.
  - **R6.** `scripts/ops/ai-failure-report.sh`. **Metric** `cocinaris.ai.generation{operation,outcome}`.
- **Deviations from this plan:**
  - **The abandoned-reservation sweep runs inside the reservation**, for the reserving owner only, not as a separate job: that is where it matters, and the advisory lock already serializes it. Other owners' rows are released the next time they reserve.
  - **The second round is skipped after a timeout** while the person waits; Slice 2's job retries cover timeouts.
  - **The live evaluation (Gate S1) was not run.** It needs the owner's provider key; the failure report on real use replaces it as evidence after deploy.
- **Validation performed:**
  - **Unit:** 496, of which 8 new or changed. The normalization tests cover the storage note, sensory cues, the visual word limit, ingredient and step fields left untouched, the catalog's safe fields, and text fitting. Two rounds make six calls, the last three on the fallback model. An invalid answer is recovered on the fallback round, and all four calls are recorded with their outcomes, rounds, and models. A timeout gets no second round. The rule labels keep limits and drop pantry ids.
  - **Integration:** `AbandonedWorkNoLongerBlocksAndEveryCallIsRecorded`: two dead reservations are released and do not block, a selection stuck `Expanding` for ten minutes is taken over and completes, and attempt rows are written without content or owner. The retention and idea library tests pass with the new rule.
  - **Architecture:** 19. **Backend formatting** verified.
- **Not run:** the full local gates (one run on the pull request head follows); the live evaluation.
- **Next action:** See "Exact next action".

### 2026-11-11 — Claude Code (Slice 2)

- **Run delivery target:** Slice 2, after the owner merged #106 ("PR aprovado, siga"). The run was interrupted once by the session ending; it resumed from the working tree, which was intact, and reran the interrupted test runs.
- **Changes:**
  - **Jobs.** In `Cocinaris.SharedKernel.Jobs`: `IJobQueue`, `IJobStore`, `IJobHandler`, `JobResult`, and `JobSignal`.
    - `PostgreSqlJobStore` queues by upsert on `(Kind, SubjectKey)`: a restart resets a queued job and never a running one. It claims with `FOR UPDATE SKIP LOCKED` and a lease, and records results. A retry past the last attempt becomes a failure, and runs are capped at 40.
    - `JobProcessor` and `JobRunner` run two jobs at a time, are woken on enqueue, and poll every 15 seconds. The lease is 10 minutes and the backoff is 1 minute, then 5. Metrics: `cocinaris.jobs.run{kind,outcome}` and `cocinaris.jobs.lag`.
    - Migration `AddJobs`. `platform.jobs` is in the privacy catalog (exported, deleted with the account), and the `finished_jobs` retention rule deletes finished jobs after 30 days.
  - **Server expansion.**
    - `RecipeCookNowApplicationService.SelectCandidateForOwnerAsync` and `ShoppingPlanApplicationService.ExpandForOwnerAsync`; a failed attempt can leave the card pending (`failCard: false`).
    - `PlanCardExpansionJobHandler` gives up at once on budget or AI-off codes. Waiting for the person's other AI work uses no attempt. A failed attempt counts `ExpansionRetries` on the card. After the last attempt the card is failed with its code.
    - Choosing a dish queues the job 5 seconds ahead, with the interface language from `Accept-Language`. Undo and removal cancel it, and reading the plan queues a lost one.
    - `POST …/cards/{id}/expansion-retry` restarts it. The view gains `retrying`.
  - **Frontend.**
    - `PlanExpansionRunner` no longer prepares anything: it reads the plan every 4 seconds while a dish is pending and the page is visible, and waits 15 seconds after a failed read.
    - "Tentar de novo" calls the new route. The board says "Ainda tentando…" and shows the reason under a failed dish, in three languages.
    - The "stopped" notice and its copy are gone.
- **Deviations from this plan:**
  - **`POST …/expansion` stays** for older clients (a cached app), and for tests. Both paths share the card's keys, so they never make two recipes.
  - **The lease is 10 minutes, not 5.** Two rounds of an expansion can take about 4 minutes, and a lease shorter than the work would let a second runner start it.
  - **The browser journey of Gate S2 was not run.** The replica has no AI provider, so a dish chosen there cannot be prepared; the integration tests run the whole job with the fake provider instead.
- **Validation performed:**
  - **Integration, all new:**
    - `AChosenDishIsPreparedOnTheServerAndRetriesBeforeItSaysWhy`: the job is queued after the undo window; a first attempt with four invalid answers leaves the dish pending with `retrying`; the next one prepares it with no request from the app, and its meal exists.
    - `AfterTheLastAttemptTheDishSaysWhyAndCanBeTriedAgain`: after three attempts, the dish is failed with its reason; "Tentar de novo" queues it again and it is prepared.
    - `UndoCancelsALostJobIsQueuedAgainAndTwoRunnersNeverShareOne`: undo cancels the job; a lost job is queued again by reading the plan; after a lease ends, two racing runners make one run.
  - **Regression:** 86 planning and recipe integration tests and 93 pantry, receipt, and recipe tests pass. Two of them were failing on `main` since #106: `AnAiRevisionChangesNothingUntilTheOwnerAcceptsIt` and `HistoryKeepsEveryRequestAndACandidateCanStillBeAskedForLater`. They queued invalid answers for one round, so the fallback round of Slice 1 succeeded where the tests expected a failure. They now queue answers for both rounds. The behavior was right; #106's local gates were interrupted by the session ending before they reported, so this was not caught there.
  - **Frontend:** typecheck, lint, format, and 644 tests.
  - **Frontend tests changed:** the three follower tests were rewritten, and one board test is new (retrying, the reason, and "Tentar de novo" calling the server).
  - **Architecture:** 19.
- **Not run:** the full local gates (one run on the pull request head follows); the browser journey, see above.
- **Next action:** See "Exact next action".

### 2026-11-11 — Claude Code (Slice 3)

- **Run delivery target:** Slice 3, after the owner merged #107 and #108, asked for the deploy (done, `56f079d`), and said "Siga com o slice 3 então".
- **Changes:**
  - **Scheduled kinds.** `IScheduledJob` (first run, next run) and `JobResult.Repeat`. A repeat starts the row over with every attempt back. The runner queues each scheduled kind at start when it has no waiting row, so a deploy keeps planned times. It also turns any failed or thrown run of a scheduled kind into a repeat at its next time, keeping `LastFailureCode`, and sleeps until the next due job (`IJobStore.NextDueAtAsync`), at most 15 seconds.
  - **Workers replaced.** Six hosted services are gone. `retention` is daily, `pantry_auto_removal` every 30 minutes, and `plan_reminders` at the top of each hour. `dish_images` and `ingredient_resolution` take one item per run, run again at once while work waits, and otherwise wait their poll time. They are registered under the same conditions as before, and `AddJobHandler<T>()` registers each.
  - **Account deletion as jobs.** One `account_deletion` job per deletion, queued with the request, up to 20 attempts. `AccountDeletionSignal`, `AccountDeletionProcessor`, and `AccountDeletionWorker` are gone. `IAccountDeletionStore` lost `ListDueAsync` and `RecordFailureAsync` and gained `ListPendingAsync`. Migration `MoveAccountDeletionAttemptsToJobs` drops `Attempts`, `NextAttemptAt`, and `FailureCode` from `platform.account_deletions`.
  - **Recovery.** `IJobRecovery` and the `job_recovery` kind, every 10 minutes; `AccountDeletionRecovery` queues pending deletions that lost their job.
- **Deviations from this plan:**
  - **Dish images stay a scheduled drain of their own table**, like ingredient names, instead of one job per image. Their states (`Pending`, `Generating`, `Ready`, `Failed`) are shown to people and drive reuse by similarity, so the table keeps them; the runner now runs and measures them.
  - **One item per run for the drains**, so a menu dish being prepared never waits behind a row of images.
- **Validation performed:**
  - **New integration test:** `ScheduledJobsAreQueuedOnceRepeatAfterEveryRunAndLostDeletionsComeBack`. The scheduled kinds are queued once across two starts. A retention run made to fail (a hidden table) waits for its next day with its code and does not end. The recovery queues a pending deletion that lost its job.
  - **Changed tests:** the account deletion tests run through `JobProcessor`, and the failed attempt is checked on the job; the worker removals in the thumbnail tests and the test host are updated.
  - **Results:** the 87 integration tests of `RecipeExecutionApiTests` pass. They cover jobs, deletion, retention, pantry auto-removal, notifications, images, and recipes. Also 496 unit and 19 architecture tests, and backend formatting verified.
- **Not run:** the full local gates (one run on the pull request head follows).
- **Next action:** See "Exact next action".

## Completion and handoff checklist

- [ ] All slices and gates are resolved truthfully.
- [ ] Required tests and evaluations pass, or limitations are documented.
- [ ] Durable documentation, contracts, comments, XML documentation, and TSDoc are current.
- [ ] Security, privacy, safety, localization, accessibility, AI, and operational impacts were reviewed.
- [ ] `docs/plan-status.md` matches this plan.
- [ ] Pull requests link this plan and report validation evidence.
- [ ] Exact continuation instructions exist if the work is not complete.
