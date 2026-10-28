# PLAN-0008: Define Lean Launch, Acquisition, AI Unit Economics, and Monetization

- **Status:** Ready
- **Type:** Operations
- **Priority:** High
- **Owner:** Unassigned launch, growth, and AI-economics agent
- **Created:** 2026-07-29
- **Last updated:** 2026-07-29T18:20:00Z
- **Branch:** `agent/plan-0008-lean-launch-ai-economics`
- **Pull request:** Not opened
- **Related issues:** None
- **Related ADRs:** ADR-0005, ADR-0006
- **Dependencies:** Product foundation accepted; controlled recruitment may start before production readiness; real-user AI execution requires an approved AI Gateway implementation, privacy controls, and a deployable product baseline

## Objective

Define and execute a capital-efficient path from prelaunch validation through a 15–30-person alpha, a 50–100-person closed beta, and a controlled 100–200-active-user public release while:

- finding users without requiring a pre-existing audience;
- avoiding broad, unmeasured advertising spend;
- routing AI work to the cheapest model that meets operation-specific quality, safety, privacy, and latency requirements;
- capping global and per-user AI cost before every provider call;
- learning actual activation, retention, willingness-to-pay, and cost-to-serve;
- introducing a freemium and founding-member monetization hypothesis without presenting pricing as final before evidence exists.

The plan must produce an executable acquisition system, not a generic marketing document. Every channel must have an owner, audience, promise, budget, conversion event, stop condition, and evidence record.

## Context

Cocinaris is a consumer web product for adults who want to cook more often, reduce delivery dependence, use food they already have, waste less, and reduce the cognitive burden of planning meals.

The earliest growth problem is not mass reach. It is finding a small number of representative users who will actually onboard, enter inventory, request help, cook, return, and explain where the product fails. Buying broad traffic before those behaviors are understood can create registrations without learning, retention, or sustainable unit economics.

AI cost is also not determined by registered-account count alone. It depends on:

- monthly active AI users;
- operations per active user;
- input and output tokens per operation;
- model and provider routing;
- retries, validation failures, and fallbacks;
- prompt caching;
- image or document processing;
- platform fees and taxes;
- abuse and accidental loops.

Cocinaris therefore needs acquisition and AI cost controls before public scale.

## Guiding decisions

1. **Do not advertise everywhere.** Begin with one audience, one problem statement, one conversion event, and one paid channel at a time.
2. **Recruit conversations before registrations.** Alpha acquisition optimizes for qualified conversations and observed onboarding, not raw clicks.
3. **Use founder-led work deliberately.** Manual outreach, assisted onboarding, and interviews are valid learning tools during alpha.
4. **Use communities by participating, not spamming.** Publish useful content, disclose the founder relationship, respect community rules, and ask for testers only where appropriate.
5. **Use creators on performance-aligned terms where possible.** Prefer affiliate revenue, credits, or a limited founding-partner arrangement over large upfront sponsorships.
6. **Treat paid media as an experiment.** Every campaign has a fixed budget, a narrow hypothesis, and a stop rule.
7. **Do not use AI where deterministic code is sufficient.** Inventory calculations, filters, lifecycle transitions, alerts, credits, quotas, and authoritative safety rules remain deterministic.
8. **Route by operation, not by brand loyalty.** A tiny model may handle normalization while a stronger model handles constrained weekly planning.
9. **Never make free inference a production dependency.** Free endpoints may disappear, rate-limit, change models, or have unsuitable data policies. Use them only with synthetic data for development or evaluation when explicitly allowed.
10. **Measure cost per useful outcome.** Cost per activated user, retained user, completed cooking execution, and paid conversion matters more than cost per raw request.

## Scope

### Included

- launch stages and admission gates;
- alpha and closed-beta recruitment playbooks;
- founder-led outreach;
- community participation;
- microcreator and affiliate experiments;
- short-form demonstration content;
- waitlist, invite, referral, and founding-member loops;
- a bounded Meta lead or click-to-message experiment;
- later Google Search validation for proven high-intent queries;
- Product Hunt preparation for a sufficiently open international launch;
- channel attribution and experiment records;
- AI operation inventory and deterministic/AI split;
- multi-provider AI routing through the Cocinaris AI Gateway;
- OpenRouter as an optional provider aggregator;
- ultra-light candidate models such as Qwen and Ministral families;
- quality, structured-output, localization, privacy, latency, uptime, and cost evaluation;
- global, per-user, per-operation, and per-model budgets;
- credit reservation, settlement, and fallback behavior;
- freemium, founding-member, annual-plan, add-on-credit, referral, and affiliate hypotheses;
- stage-specific metrics and stop/go gates.

### Excluded

- implementing production AI calls in this documentation plan;
- selecting a permanent provider or permanent model without evaluation evidence;
- clinical nutrition or medical positioning;
- targeting children or collecting data from minors;
- broad display campaigns, indiscriminate boosted posts, or simultaneous campaigns across many networks;
- influencer sponsorships with large guaranteed upfront fees;
- final legal, tax, accounting, subscription, refund, or advertising-policy decisions;
- claims that an alpha or beta price is the permanent public price;
- lifetime plans that create uncapped recurring AI liability;
- unlimited AI usage;
- selling or sharing user data for advertising.

## Research baseline as of 2026-07-29

Prices and platform capabilities change. The execution agent must refresh this table before using it for budget approval.

| Candidate or platform | Observed baseline | Planning implication |
|---|---:|---|
| OpenRouter pay-as-you-go | Model list price plus a published platform fee | Include platform fee, tax, and currency spread in cost ceilings |
| Qwen3 8B through OpenRouter | Approximately USD 0.05/M input and USD 0.40/M output | Candidate for bounded extraction, normalization, classification, and simple structured transformations |
| Qwen3.5 Flash through OpenRouter | Approximately USD 0.065/M input and USD 0.26/M output | Candidate for lightweight multilingual structured tasks and low-cost general assistance |
| Ministral 3 3B through OpenRouter | Approximately USD 0.10/M input and USD 0.10/M output | Candidate where output volume dominates cost |
| OpenRouter free endpoints | Limited requests and a changing model pool | Development/evaluation only with synthetic data; never a production SLO or privacy dependency |
| OpenRouter routing/privacy controls | Provider selection, data-policy filtering, and zero-data-retention controls are available | Require explicit privacy-compatible routing rather than relying on defaults |
| Meta lead and click-to-message ads | Native lead forms and conversations through supported Meta messaging surfaces | Suitable for a small alpha recruitment experiment optimized for qualified conversations |
| Google Keyword Planner | Search volume, cost, and forecast estimates | Use before spending on Search; do not assume demand for invented product terminology |
| Product Hunt | Public launch and community feedback platform | Use after the product is accessible enough for the community; closed beta is not the primary launch moment |

These candidates are hypotheses. No candidate enters production solely because it is inexpensive.

## Target user and acquisition promise

### Initial target

Prioritize adults who:

- live alone or as a couple;
- use delivery frequently because deciding and organizing food is difficult;
- want to spend less without adopting a complex meal-prep lifestyle;
- regularly forget food in the refrigerator or freezer;
- can prepare basic meals but struggle with planning, inventory awareness, or confidence;
- are willing to provide feedback during an early product stage.

Do not target sensitive health traits, diagnoses, body-image concerns, or medical conditions in ad copy or audience definitions.

### Problem-first promise

Lead with the outcome rather than the technology:

> Cocinaris helps you decide what to cook using what you already have, what still needs attention, and how much effort you want to spend.

Avoid generic positioning such as “AI recipe generator.”

### Founding Kitchen cohort

Create a named early-access cohort with:

- 30 initial seats;
- no payment card required;
- a clearly temporary free period;
- assisted onboarding;
- direct feedback access;
- transparent early-product limitations;
- no promise that every requested feature will be implemented;
- optional founding-member offer only after the user has experienced value.

Suggested recruitment offer:

> We are inviting 30 adults who want to cook more and depend less on delivery. Participation includes a short assisted setup, free early access, and direct influence through structured feedback. This is product research, not a sales call.

## Acquisition channel order

### Channel 1: Founder-led direct recruitment

Use before paid scale.

Sources may include:

- personal and professional contacts who match the target problem;
- referrals from each accepted participant;
- local cooking, household-organization, budgeting, and apartment-living communities;
- people who have publicly discussed delivery dependence, food waste, or meal-decision fatigue where outreach is allowed.

Required behavior:

- personalize the message;
- state why the person appears relevant;
- request a short conversation, not an immediate purchase;
- disclose that the product is early;
- do not scrape private data or automate spam;
- maintain a contact and consent record.

### Channel 2: Microcreator founding partners

Contact creators in practical cooking, beginner cooking, living alone, home organization, household budgeting, and realistic meal planning.

Preferred initial profile:

- a small but engaged audience;
- content aligned with ordinary meals rather than aspirational gourmet imagery;
- willingness to test the product personally;
- no requirement for a large guaranteed fee.

Offer hypotheses:

- free paid-tier access while actively testing;
- a tracked referral code;
- 20% recurring net subscription revenue for the first 12 months of each referred paying account;
- optional bonus after a minimum number of activated or paying referrals;
- no payment for raw impressions alone in the first experiment.

Any affiliate arrangement requires clear disclosure and jurisdiction-appropriate terms.

### Channel 3: Organic community participation

Use communities to learn language and objections before asking for testers.

Operating rules:

- participate with a real founder identity where appropriate;
- answer existing questions usefully;
- avoid copying the same promotional message across groups;
- ask moderators before recruiting where rules are unclear;
- publish a concrete demonstration or lesson, not only a landing-page link;
- record the community, post, response quality, and resulting qualified conversations.

Potential community themes:

- practical home cooking;
- budget cooking;
- reducing delivery spending;
- living alone;
- household organization;
- weekly meal planning;
- reducing food waste;
- local Brazilian technology and startup communities for product feedback.

### Channel 4: Demonstration content

Create short, repeatable content around actual user problems:

- “I entered what was in the refrigerator and chose a 30-minute effort limit.”
- “Three products needed attention; here is how the plan changed.”
- “The planned recipe was missing one ingredient; here are the alternatives.”
- “The same pantry produced a quick meal and a weekend meal.”
- “What Cocinaris does differently from a recipe catalog.”

Each asset must use a single call to action: join the waitlist, request an alpha invitation, or start a qualified conversation.

Do not manufacture testimonials or present mock behavior as production capability.

### Channel 5: Referral loop

After a user reaches activation, invite one relevant adult.

Initial hypothesis:

- inviter receives 10 Cocinaris credits after the invitee activates;
- invitee receives 10 bonus credits after activation;
- no reward for raw email submission;
- cap rewards and detect self-referral and abuse;
- preserve consent and allow invitation suppression.

### Channel 6: Small paid Meta experiment

Use Meta lead forms or click-to-message only after the landing page and onboarding offer are understandable.

Initial bounded experiment:

- **Budget:** BRL 300 total;
- **Duration:** 14 days;
- **Daily ceiling:** approximately BRL 20–22;
- **Audience:** Brazil, adults, broad enough for platform learning, with copy focused on cooking more and reducing delivery dependence;
- **Creative:** two short demonstrations and one static problem/solution variant;
- **Conversion:** qualified alpha conversation or completed screening form, not a page view;
- **Required fields:** minimum necessary contact and screening information;
- **Stop rule:** pause a creative after meaningful spend without qualified conversations, policy problems, or evidence that the promise attracts the wrong audience;
- **Success evidence:** at least ten qualified conversations with a recorded source and a measurable path to invitation.

Do not run multiple ad networks at the same time during this experiment.

### Channel 7: Google Search experiment

Run only after Keyword Planner and organic conversations identify actual high-intent language.

Candidate problem queries may involve:

- what to cook with what I have;
- how to stop ordering delivery;
- organize refrigerator food;
- meal planning for one or two people;
- use ingredients before they expire.

Use exact and phrase-level hypotheses before broad matching. Send traffic to a problem-specific landing page. Stop if search intent is primarily informational and does not produce qualified signups.

### Channel 8: Public launch surfaces

Use Product Hunt and broader international launch channels after:

- the product is publicly accessible;
- onboarding works without founder assistance for most users;
- demo data and privacy disclosures are clear;
- support and incident handling exist;
- the team can respond throughout launch day;
- the launch is not a closed beta that prevents community participation.

Product Hunt is a visibility event, not a substitute for retention.

## Paid acquisition policy

Paid acquisition must follow this sequence:

1. define one audience;
2. define one observed problem;
3. define one promise;
4. define one creative family;
5. define one conversion event;
6. define a fixed total budget;
7. instrument attribution before spending;
8. run long enough to collect evidence without silently raising budget;
9. interview resulting users;
10. decide stop, revise, or scale.

Prohibited early behavior:

- advertising on “every platform” simultaneously;
- optimizing for impressions, followers, or cheap clicks without activation data;
- allowing platform automation to expand the budget without a hard account-level cap;
- creating many audience segments with too little data;
- retargeting before adequate consent and privacy implementation;
- using sensitive inference or health-related targeting;
- changing landing page, creative, audience, and onboarding simultaneously, making results uninterpretable.

## Launch stages

### Stage 0: Prelaunch recruitment readiness

Required before admitting alpha users:

- problem-first landing page;
- waitlist and screening form;
- privacy notice and consent record;
- invite and suppression workflow;
- analytics events with no unnecessary private content;
- support contact;
- known-limitations page;
- global AI kill switch;
- no real provider call until AI budget controls are active;
- feedback interview script;
- experiment ledger.

### Stage 1: Assisted alpha — 15 to 30 admitted users

Operating model:

- invitation only;
- assisted setup offered to every user;
- founder observes onboarding with consent;
- at least ten structured interviews;
- weekly release and feedback cadence;
- strict global AI budget, initially BRL 50 per month;
- no paid tier required at entry;
- users may receive a future founding-member offer after activation.

Initial evidence gates:

- 15–30 admitted adults representing the target problem;
- at least 60% start onboarding;
- at least 40% reach activation;
- at least 25% of activated users return within seven days;
- at least five users complete or materially advance a cooking execution;
- qualitative evidence identifies repeated rather than one-off needs;
- AI cost per activated and retained user is measurable;
- no unresolved privacy, authorization, or food-safety release blocker.

Thresholds are planning hypotheses and must be interpreted with the small sample and interview evidence.

### Stage 2: Closed beta — 50 to 100 admitted users

Entry requirements:

- alpha blockers resolved;
- self-service onboarding is viable;
- attribution works;
- AI quotas and fallbacks work;
- support and incident ownership are explicit;
- user export and deletion paths meet the release baseline;
- at least one acquisition channel has produced activated users.

Operating model:

- waitlist waves rather than one mass import;
- referral loop enabled with caps;
- one creator experiment;
- one small paid acquisition experiment at a time;
- founding-member pricing test;
- weekly unit-economics review.

Evidence gates:

- activation, seven-day retention, and 30-day retention measured by channel;
- at least one channel produces repeated activated users;
- paid interest or conversion evidence exists;
- AI cost remains inside stage budget under retries and fallbacks;
- abuse, support burden, and deletion requests are operationally manageable;
- no production dependency on free or unpinned model routing.

### Stage 3: Controlled public release — 100 to 200 active users

Entry requirements:

- closed-beta retention and support evidence is acceptable;
- subscription and credit accounting are tested;
- provider budgets and kill switches are tested;
- model quality evaluations pass in all supported locales;
- public legal and pricing copy is approved;
- incident response, status communication, backups, and restore evidence exist.

Operating model:

- freemium with bounded AI credits;
- founding-member offer remains capacity-limited;
- acquisition budget scales only for channels with activated-user evidence;
- public launch surfaces may be used;
- model and provider costs reviewed at least weekly initially.

## Activation, retention, and revenue definitions

### Activation

The initial activation event is:

1. user completes enough profile context for the selected workflow;
2. user records at least five inventory products or explicitly completes a supported inventory-bypass path;
3. user receives a useful suggestion, plan, or recipe adaptation;
4. user starts or completes a cooking or planning action.

The analytics implementation must keep the sub-events separately visible. Do not hide a broken step inside one aggregate metric.

### Retention

Track:

- D1 return;
- D7 return;
- D30 return;
- weekly active cooking/planning users;
- repeated inventory updates;
- repeated suggestion acceptance;
- completed cooking executions;
- delivery-reduction self-report as optional research, not an authoritative claim.

### Revenue

Track:

- free-to-paid conversion;
- trial-to-paid conversion when trials exist;
- monthly and annual plan selection;
- credits purchased;
- creator and referral attribution;
- refunds and chargebacks;
- net revenue after payment fees, tax, refunds, affiliate share, and AI cost;
- payback period by acquisition channel.

## AI operation taxonomy

### Tier 0: Deterministic — no model call

Examples:

- inventory arithmetic and lifecycle transitions;
- unit conversion;
- expiration dates entered by the user;
- list filtering and sorting;
- shopping-list differences;
- quotas, credits, billing, and entitlements;
- authorization and ownership;
- deterministic food-safety and validation rules;
- known substitutions from curated structured data;
- alerts and notifications derived from authoritative state.

### Tier 1: Ultra-light model

Candidate operations:

- product-name normalization suggestions;
- bounded classification into a controlled taxonomy;
- language detection;
- short structured extraction from already-trusted text;
- rewriting a short instruction for clarity;
- choosing among a small set of deterministic options;
- non-safety-critical intent classification.

Constraints:

- small context;
- strict JSON schema;
- low output token limit;
- no medical or food-safety authority;
- deterministic validation;
- no unrestricted user-history context;
- no retry escalation without budget reservation.

### Tier 2: Light general model

Candidate operations:

- simple recipe adaptation;
- portion adjustment explanation;
- substitutions with multiple preferences;
- basic troubleshooting grounded in current execution context;
- short shopping or preparation explanations;
- structured import cleanup after deterministic extraction.

### Tier 3: Strong model

Candidate operations:

- weekly planning with multiple constraints;
- recipe generation under inventory, preference, equipment, time, and cleanup constraints;
- uncommon troubleshooting where deterministic and light-model paths fail;
- high-complexity reconciliation or explanation.

Strong-model use requires a clear user-visible benefit and a higher credit cost.

### Escalation policy

Escalation may occur only for:

- schema validation failure;
- operation-specific quality score below threshold;
- unsupported language behavior;
- explicit complex-workflow routing;
- a documented recoverable provider failure.

Do not escalate merely because a model response is stylistically imperfect.

Every escalation records:

- operation ID;
- initial model policy;
- selected provider/model/version;
- reason;
- token usage and cost;
- validation result;
- fallback result;
- user-facing outcome.

## Model and provider evaluation

### Evaluation dataset

Create versioned synthetic and consented evaluation cases covering:

- English, Portuguese (Brazil), and Spanish;
- measured and availability inventory;
- common Brazilian food names;
- ambiguous product names;
- dietary preferences and restrictions without medical advice;
- missing fields;
- conflicting constraints;
- prompt-injection attempts in imported text;
- unsafe or uncertain food situations;
- long and short contexts;
- expected structured-output failures.

Do not use private production data without an approved purpose, minimization, and consent basis.

### Candidate scorecard

Score every operation/model pair on:

- valid structured-output rate;
- domain accuracy;
- unsupported-claim rate;
- safety and refusal behavior;
- localization quality;
- latency percentiles;
- provider uptime;
- input, cached-input, output, and retry cost;
- context-window fit;
- provider data policy;
- zero-data-retention availability;
- version pinning and deprecation risk.

The cheapest model wins only when all minimum quality and policy thresholds pass.

### OpenRouter policy

When OpenRouter is used:

- call it only through the Cocinaris AI Gateway;
- keep model slugs, provider allowlists, fallbacks, and price ceilings in versioned policy/configuration;
- pin model versions where available;
- require provider compatibility with required parameters and structured output;
- deny providers that collect or train on user data for real-user workflows;
- require zero-data-retention routing where the operation demands it;
- disable prompt logging unless an approved, minimized debugging workflow explicitly enables it;
- do not expose OpenRouter keys to the browser;
- record actual routed provider and model;
- account for the platform fee and currency/tax overhead;
- maintain direct-provider portability for strategically important operations.

## AI budget and credit controls

### Required budgets

Define hard ceilings for:

- global daily spend;
- global monthly spend;
- environment;
- user daily spend;
- user monthly spend;
- operation invocation count;
- operation maximum input tokens;
- operation maximum output tokens;
- model/provider daily spend;
- concurrent requests;
- retry and fallback count.

### Reservation lifecycle

Every billable AI operation follows:

1. calculate worst-case credit and currency reserve;
2. reject or degrade before calling the provider if reserve is unavailable;
3. reserve user and global budget atomically;
4. execute with idempotency and timeout;
5. validate schema and domain constraints;
6. settle actual usage and provider fee;
7. release unused reserve;
8. record failures and fallback cost;
9. expose a stable user-facing credit result without exposing provider token accounting.

### Kill switches

Provide kill switches for:

- all AI;
- one workflow;
- one model;
- one provider;
- image/document operations;
- free users;
- new invocations while allowing already completed cached results.

Degraded behavior must remain useful and honest.

## Illustrative ultra-light cost envelope

Assume one moderate active user consumes approximately 300,000 input tokens and 60,000 output tokens per month. This is intentionally conservative for tiny structured tasks and must be replaced by measured operation-level data.

At the observed candidate prices before taxes and payment/currency overhead:

| Candidate | Approximate model cost per moderate active user | Approximate cost for 100 active users | Approximate cost for 200 active users |
|---|---:|---:|---:|
| Qwen3 8B | USD 0.039 | USD 3.90 | USD 7.80 |
| Qwen3.5 Flash | USD 0.0351 | USD 3.51 | USD 7.02 |
| Ministral 3 3B | USD 0.036 | USD 3.60 | USD 7.20 |

After an illustrative 5.5% platform fee, these remain well below the earlier all-frontier-model scenarios. Actual Cocinaris cost will be higher because some operations will escalate, fail, retry, process images, or use stronger models.

The goal is not to force all work through a tiny model. The goal is to keep the majority of low-risk tokens at Tier 0 or Tier 1 while reserving expensive models for operations where they measurably improve user outcomes.

## Monetization hypotheses

### Free plan

Initial hypothesis:

- core inventory and manual organization;
- manual shopping and planning features appropriate to the release stage;
- bounded monthly AI credits;
- deterministic degraded behavior after credits are exhausted;
- no “unlimited AI” wording;
- no card required for the first alpha cohort.

### Founding Member

Initial test, not final pricing:

- BRL 14.90 per month or BRL 149 per year;
- limited to the first 100 paying members or another explicit capacity;
- 80–120 monthly Cocinaris credits, calibrated after alpha usage;
- no ads;
- early access to selected features;
- priority feedback channel;
- price guarantee limited to a stated period rather than lifetime AI liability.

### Standard plan hypothesis

After evidence:

- BRL 24.90 per month or BRL 249 per year;
- higher bounded credit allowance;
- clear overage or add-on-credit option;
- annual discount that preserves margin;
- no promise of unlimited provider usage.

### Additional credits

Initial hypothesis:

- a simple pack such as 100 credits for BRL 9.90;
- credits represent user-visible operation value, not raw tokens;
- reserve expiration, refund, and consumer-law treatment require legal/accounting review;
- prevent credit packs from obscuring an uncompetitive subscription.

### Conversion moments

Offer the paid plan only after meaningful value, such as:

- first completed cooking execution;
- second weekly planning request;
- attempt to use a higher-value operation after free credits are exhausted;
- repeated recipe adaptation;
- activation plus a positive feedback signal.

Do not place a hard paywall before the user can understand Cocinaris.

## Required analytics and experiment ledger

Every acquisition experiment records:

- experiment ID;
- start and end dates;
- owner;
- channel;
- audience;
- problem statement;
- promise and creative version;
- landing page version;
- budget and hard ceiling;
- impressions, clicks, conversations, waitlist entries, invites, activations, D7/D30 retention, paid conversions;
- AI cost and support cost attributable to the cohort;
- qualitative interview themes;
- decision: stop, revise, repeat, or scale;
- reason and next experiment.

Required event names must be defined in an implementation plan before instrumentation. Analytics must avoid private pantry contents, notes, restrictions, recipe text, or raw prompts unless an explicitly approved minimized event requires them.

## Substantial run delivery target

- **Target outcome:** Deliver the complete launch-readiness package through Stage 1 admission readiness, including durable go-to-market documentation, AI routing/cost policy, monetization hypotheses, acquisition assets, experiment ledger, screening/onboarding scripts, analytics event specification, and a validated first-channel experiment design.
- **Minimum acceptable evidence:** Reviewed documents, current model/provider pricing refresh, operation-level model scorecard template, approved privacy constraints, stage gates, fixed budgets, landing/waitlist copy, outreach and creator scripts, experiment IDs, and validation against product/architecture/security documents.
- **Adjacent checkpoints to continue through when unblocked:** Build the first alpha contact list, prepare creator outreach, create the first demonstration assets, and configure the bounded Meta experiment without activating spend until readiness approval.
- **Valid early-stop conditions:** Product cannot safely accept users, privacy/legal decision is missing, AI Gateway budget controls are unavailable for real calls, acquisition platform account access is unavailable, or an approved stakeholder decision is required.

## Documentation deliverables

### Durable documentation

Execution should create or update:

```text
docs/launch/README.md
docs/launch/go-to-market.md
docs/launch/alpha-recruitment-playbook.md
docs/launch/acquisition-experiment-ledger.md
docs/launch/metrics-and-stage-gates.md
docs/ai/model-routing-and-cost-governance.md
docs/ai/model-evaluation-scorecard.md
docs/product/monetization-and-entitlements.md
docs/security/privacy-and-data-protection.md
docs/operations/platform-and-reliability.md
```

The exact structure may be adjusted to existing indexes, but divergent duplicate policy documents are prohibited.

### Code-level documentation

When later implementation plans add acquisition, billing, analytics, credit, or AI Gateway code:

- all project-owned public/protected .NET APIs require XML documentation;
- non-obvious internal cost, credit, routing, privacy, attribution, idempotency, and failure contracts require XML documentation or equivalent durable explanation;
- exported reusable TypeScript analytics, consent, paywall, waitlist, and experiment components require TSDoc/JSDoc when behavior is not self-evident;
- comments explain budget invariants, privacy boundaries, attribution limitations, and fallback rationale rather than narrating syntax;
- generated provider SDK code is documented at the adapter and schema boundary.

No code-level documentation is required merely to create this operations plan; later code delivery plans inherit the repository-wide standard.

## Assumptions and open questions

### Assumptions

- Brazil is the first acquisition market.
- Portuguese (Brazil) is the first marketing locale.
- The product remains adult-only.
- The owner can operate a small server environment without material initial infrastructure pressure.
- AI is the primary variable operating-cost concern.
- A dedicated AI Gateway remains the only permitted provider-call boundary.
- OpenRouter may be used, but direct providers remain supported through adapters.
- Initial recruitment can begin with a mock-rich prototype for interviews, but real pantry data and AI operations require production-shaped privacy and security controls.

### Open questions

- Which payment provider, tax setup, legal entity, and invoice flow will support subscriptions in Brazil?
- What exact free and paid credit allowances follow measured alpha usage?
- Which operation is the first paid conversion trigger?
- Which user research and analytics platform satisfies privacy requirements?
- Which creator affiliate terms are legally and operationally acceptable?
- Does the alpha remain Brazil-only or admit a small Spanish/English research cohort?
- What AI quality thresholds are required for each operation?
- Which providers meet the required data-retention and regional-processing constraints?

These questions do not block plan approval. They must be resolved before the phase that depends on them.

## Architecture and contract impact

This plan does not authorize direct implementation. Future implementation affects:

- AI Gateway operation registry and provider adapters;
- quota, credit, entitlement, and billing modules;
- analytics and consent boundaries;
- waitlist, invitations, referrals, and affiliate attribution;
- user-visible pricing and credit contracts;
- provider configuration and secrets;
- observability, cost reporting, and kill switches;
- privacy documentation and deletion/export scope.

An ADR is required if execution selects a durable provider aggregator as mandatory infrastructure, changes authoritative billing ownership, creates a new user-tracking architecture, or introduces a durable cross-module contract not covered by accepted ADRs.

OpenRouter must remain behind the AI Gateway and must not appear in frontend contracts.

## Execution phases

### Phase 1: Refresh evidence and establish operating baselines

- [ ] Refresh model prices, platform fees, free-tier limits, provider availability, data policies, and routing controls.
- [ ] Inventory every planned AI operation and classify it into Tier 0–3.
- [ ] Define initial token/context/output ceilings by operation.
- [ ] Define stage budgets in BRL and provider billing currency.
- [ ] Define acquisition and monetization hypotheses without presenting them as final facts.
- [ ] Reconcile the plan registry and assign an owner.

**Exit criteria**

- Current source evidence and review date are recorded.
- Every planned AI operation has a deterministic/model tier and an initial budget owner.
- No real-user provider call depends on a free endpoint or permissive data-collection default.
- Applicable documentation is current.

### Phase 2: Produce launch-readiness documentation and assets

- [ ] Create the durable launch, AI cost, model evaluation, metrics, and monetization documents.
- [ ] Write the problem-first landing-page copy.
- [ ] Write waitlist and screening questions.
- [ ] Write assisted-onboarding and interview scripts.
- [ ] Write founder outreach and creator outreach templates.
- [ ] Define the Founding Kitchen offer and limitations.
- [ ] Create the experiment ledger and attribution taxonomy.
- [ ] Define privacy-safe analytics events.

**Exit criteria**

- Another agent can execute recruitment without conversation history.
- Every asset has a single purpose, audience, and next action.
- Privacy, adult-only scope, and truthful prototype limitations are explicit.
- The phase is a substantial coherent documentation package.

### Phase 3: Prepare AI evaluation and budget controls

- [ ] Create operation-specific multilingual evaluation cases.
- [ ] Evaluate at least two ultra-light candidates and one fallback candidate per Tier 1 operation.
- [ ] Evaluate at least one light and one strong candidate for Tier 2/3 operations.
- [ ] Record actual provider, model version, structured-output validity, quality, latency, and cost.
- [ ] Specify reserve/settle/release accounting and kill-switch behavior.
- [ ] Define model deprecation and price-change response.
- [ ] Create a separate implementation plan for AI Gateway, quotas, or credits when required.

**Exit criteria**

- Candidate selection is evidence-based per operation.
- Cost ceilings include retries, platform fee, tax/currency margin, and fallback reserve.
- Privacy-compatible provider routing is proven for real-user workflows.
- No model is selected solely on benchmark marketing or low price.

### Phase 4: Recruit and operate the assisted alpha

- [ ] Build a 60-person qualified prospect list to fill 15–30 seats.
- [ ] Start founder-led outreach in small batches.
- [ ] Contact at least ten relevant microcreators with the founding-partner proposal.
- [ ] Participate in selected communities before posting recruitment requests.
- [ ] Publish at least three truthful demonstration assets.
- [ ] Admit users in waves.
- [ ] Conduct at least ten interviews.
- [ ] Review activation, retention, cost, support, and safety weekly.

**Exit criteria**

- Alpha admission range is reached or a documented acquisition hypothesis fails.
- Activation and retention are measured by source.
- Repeated user needs and blockers are documented.
- AI and support costs are measurable per activated and retained user.

### Phase 5: Run one bounded paid acquisition experiment

- [ ] Confirm landing page, screening, attribution, privacy, and support readiness.
- [ ] Configure the BRL 300 Meta lead or click-to-message experiment.
- [ ] Obtain stakeholder approval before spend activation.
- [ ] Run two demonstration creatives and one static variant.
- [ ] Review lead quality throughout the experiment without silently expanding budget.
- [ ] Interview a sample of resulting prospects and users.
- [ ] Record stop, revise, repeat, or scale decision.

**Exit criteria**

- Total spend does not exceed the approved ceiling.
- Qualified-conversation and activated-user costs are known.
- Results can be attributed to a stable audience/promise/creative configuration.
- No second paid platform starts without a separate approved experiment.

### Phase 6: Launch the closed beta and founding-member test

- [ ] Resolve alpha release blockers.
- [ ] Admit users in waves until 50–100 total admitted users.
- [ ] Enable capped referral rewards.
- [ ] Run one creator partnership with tracked attribution.
- [ ] Offer founding-member pricing after activation.
- [ ] Measure conversion, refunds, AI cost, support cost, retention, and affiliate share.
- [ ] Adjust credit allowances only through a documented experiment.

**Exit criteria**

- At least one repeatable channel produces activated users.
- Willingness-to-pay evidence exists.
- Unit economics are calculable by cohort and channel.
- AI quotas and degraded behavior protect the stage budget.

### Phase 7: Decide controlled public release

- [ ] Review stage-gate evidence.
- [ ] Approve final initial public pricing and credit allowances.
- [ ] Approve legal, privacy, payment, refund, and tax behavior.
- [ ] Confirm support and incident capacity.
- [ ] Select the next acquisition channel based on evidence.
- [ ] Prepare public launch assets only if self-service onboarding and retention are acceptable.

**Exit criteria**

- Public release is approved, delayed with exact blockers, or cancelled based on evidence.
- Scale budget is tied to activated and retained user economics.
- Product Hunt or other public launches do not precede product accessibility and support readiness.

## Testing and validation plan

### AI and cost

- operation-level golden-set evaluations in `en`, `pt-BR`, and `es`;
- structured-output validity tests;
- deterministic validation and unsafe-output tests;
- prompt-injection tests for imported content;
- provider data-policy and zero-retention verification;
- token and currency cost calculation tests;
- reserve/settle/release concurrency and idempotency tests;
- per-user and global budget boundary tests;
- retry/fallback cost tests;
- model-deprecation and price-ceiling tests;
- kill-switch and degraded-behavior tests.

### Acquisition and monetization

- waitlist consent and suppression checks;
- invite abuse and self-referral checks;
- attribution event validation;
- landing-page localization and accessibility review;
- screening-form minimization review;
- payment, entitlement, refund, and credit contract tests under a later implementation plan;
- affiliate attribution and disclosure review;
- experiment-ledger completeness review;
- manual audit that no prototype capability is marketed as production behavior.

### Stage evidence

- cohort-based activation and retention reports;
- AI cost per registered, activated, retained, and paying user;
- CAC per qualified conversation, activated user, retained user, and paying user;
- support time per activated user;
- channel interview synthesis;
- go/no-go decision record.

## Cross-cutting impact

### Security and privacy

- Do not send full pantry, notes, restrictions, profile, and history when an operation needs only a subset.
- Do not use real-user prompts with free or unknown-policy providers.
- OpenRouter/provider privacy defaults must not be trusted implicitly; enforce policy in configuration and requests.
- Waitlist, ad, creator, referral, and analytics data require purpose limitation, consent where required, retention, suppression, export, and deletion handling.
- Advertising audiences must not be built from sensitive food, allergy, health, or behavioral data.
- API keys and provider configuration remain backend-only.

### Food safety

- Ultra-light models must not authoritatively decide whether food is safe.
- Deterministic curated rules and explicit uncertainty remain mandatory.
- Marketing must not promise food-safety guarantees.
- Troubleshooting and substitutions must preserve allergy and contamination guardrails.

### AI behavior and cost

- Cost is controlled before calls, not merely reported afterward.
- Users see Cocinaris credits and operation value, not provider token jargon.
- Models are replaceable by policy and adapter.
- Fallbacks are bounded and observable.
- Free users always have useful deterministic behavior after credits are exhausted.

### Localization and accessibility

- Acquisition and product copy starts in `pt-BR` and remains translatable to `en` and `es`.
- Forms, pricing, credits, consent, and errors require accessible labels and keyboard operation.
- Currency, dates, decimal quantities, and pluralization follow locale rules.
- Creator and ad assets require captions and must not depend solely on audio or color.

### Operations and observability

- Dashboard stage budget, daily spend, provider/model spend, operation cost, retry cost, and anomaly alerts.
- Alert before and at hard budget thresholds.
- Record model/provider/version on every billable operation.
- Maintain incident response for runaway loops, provider price changes, model deprecation, quota exhaustion, billing failures, and acquisition-platform account restrictions.
- Back up experiment and financial records according to retention policy.

## Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Broad ads generate cheap clicks but no retained users | High | High | Optimize alpha for qualified conversations and activation; one bounded channel at a time |
| Founder has no existing audience | High | Medium | Founding cohort, direct outreach, microcreator partners, communities, demonstrations, referrals, then bounded paid recruitment |
| Community posts are treated as spam | Medium | High | Participate first, ask moderators, disclose founder role, tailor useful content, follow community rules |
| Creator experiment produces impressions without users | Medium | Medium | Performance-aligned compensation based on activated or paying referrals |
| Tiny model returns low-quality or unsafe outputs | High | High | Operation-specific evaluation, strict schemas, deterministic validation, escalation, and prohibited safety authority |
| OpenRouter provider stores or trains on user data | Medium | Critical | Enforce data-collection denial, zero-retention where required, provider allowlists, and privacy verification |
| Free model disappears or changes | High | High | Never use free routing as a production dependency; pin paid candidates and maintain fallbacks |
| Model price changes silently harm margin | Medium | High | Versioned price ceilings, scheduled refresh, alerts, kill switches, and routing fallback |
| AI calls loop or retry excessively | Medium | Critical | Atomic reservation, idempotency, timeout, bounded retry/fallback, concurrency caps, global kill switch |
| Founding price is too low | Medium | Medium | Time/cap-limited hypothesis, bounded credits, net-margin review, no lifetime guarantee |
| Paywall appears before product value | Medium | High | Convert after activation or a second high-value operation |
| Analytics collects private pantry or prompt data | Medium | Critical | Event minimization, schema review, consent, retention, deletion, and prohibited raw content |
| Alpha users are friends who do not represent the market | Medium | High | Qualification criteria, external creator/community/paid cohorts, and channel-separated evidence |
| Product launches publicly before self-service readiness | Medium | High | Stage gates, support readiness, and public-launch approval decision |

## Acceptance criteria

- [ ] A complete launch and acquisition strategy exists in durable repository documentation.
- [ ] Alpha, closed-beta, and controlled-public stage gates are explicit and measurable.
- [ ] The first 15–30 users can be recruited without assuming an existing owned audience.
- [ ] Paid acquisition begins with one fixed-budget experiment and cannot silently broaden.
- [ ] Every acquisition experiment records audience, promise, budget, conversion, retention, cost, qualitative evidence, and decision.
- [ ] Every planned AI operation is classified into deterministic or model Tier 1–3.
- [ ] At least two ultra-light candidates are evaluated per Tier 1 operation before production selection.
- [ ] Provider data policy and zero-retention requirements are enforced for real-user workflows.
- [ ] Global and per-user AI budgets, reservation, settlement, retries, fallbacks, and kill switches are specified and later tested.
- [ ] Freemium, founding-member, add-on-credit, referral, and affiliate hypotheses are documented as experiments rather than final guarantees.
- [ ] Unit economics can be calculated per channel and cohort.
- [ ] No free model, unlimited AI promise, broad-display campaign, or lifetime AI liability is required.
- [ ] Durable documentation is complete and current.
- [ ] Later code comments, XML documentation, and TSDoc/JSDoc requirements are explicit.
- [ ] The substantial run delivery target is reached or a valid early-stop reason is documented.
- [ ] No unsupported completion claims remain.

## Execution state

This section must be updated before every agent-created commit.

- **Current run delivery target:** Deliver a decision-ready, executable launch/acquisition/AI-unit-economics plan and register it without starting unauthorized production acquisition or AI implementation.
- **Current checkpoint:** PLAN-0008 is fully specified and ready for assignment. It defines staged acquisition, a concrete no-audience recruitment path, one bounded paid experiment, ultra-light model evaluation, OpenRouter privacy controls, AI budgets, and monetization hypotheses.
- **Last completed step:** Created the plan and prepared its registry entry; reconciled PLAN-0007 delivery as merged.
- **Exact next action:** Owner reviews and merges this plan, then assigns an operations/growth/AI-economics agent to execute Phases 1–2 while PLAN-0003 and PLAN-0004 continue independently.
- **Blockers:** Real-user AI calls require an approved AI Gateway implementation and privacy controls; alpha recruitment assets may be prepared before those dependencies are complete.
- **Partially modified areas:** Documentation and plan registry only.
- **Documentation delivered:** Complete PLAN-0008 launch, acquisition, AI routing/cost, and monetization execution specification.
- **Validation performed:** Cross-checked accepted product, AI Gateway, privacy, operations, and agent-governance direction; reviewed current OpenRouter candidate pricing/privacy capabilities and current acquisition-platform guidance; separated hypotheses from durable decisions.
- **Known failures or limitations:** Model prices, provider availability, advertising products, and channel economics are time-sensitive and must be refreshed before spend or production routing. No campaign, provider call, billing system, analytics implementation, or production code was executed.
- **Working tree state:** Documentation changes prepared for atomic commit.

## Progress log

### 2026-07-29T18:20:00Z — AI launch and economics planning agent

- **Run delivery target:** Produce a complete executable plan for low-capital launch, early-user recruitment without an existing audience, ultra-light model routing, AI cost controls, and initial monetization.
- **Checkpoint:** PLAN-0008 specified and registered as `Ready`.
- **Changes included in the commit:** Added PLAN-0008 and updated the central registry; reconciled PLAN-0007 delivery from open PR to merged.
- **Documentation and code-documentation delivered:** Durable operations plan covering stages, acquisition channels, paid experiment policy, model tiers, OpenRouter policy, budget/credit controls, evaluation, metrics, monetization, privacy, safety, and handoff. No code-level documentation applies because no executable code changed.
- **Validation performed:** Reviewed accepted repository requirements and current official platform/model information; verified that the plan does not authorize direct provider calls outside the AI Gateway, unlimited AI, broad unmeasured advertising, or production dependence on free models.
- **Result:** A future agent can execute alpha readiness and AI-economics work without conversation history.
- **Next action:** Open the documentation PR for owner review and merge.
- **Blockers or handoff notes:** Prices and platform behavior must be refreshed at execution time. Production AI and paid acquisition require their respective readiness approvals.

## Completion and handoff checklist

- [ ] All execution phases and acceptance criteria are resolved truthfully.
- [x] The current planning run delivered a substantial coherent, decision-ready result.
- [x] Required research and planning validation is documented.
- [x] Durable plan documentation is current.
- [x] Code-level documentation requirements for later implementation are explicit.
- [x] Security, privacy, food safety, localization, accessibility, AI, acquisition, monetization, and operations impacts were reviewed.
- [x] `docs/plan-status.md` matches this plan after the planned atomic commit.
- [ ] Pull request description links this plan and reports validation evidence.
- [x] No hidden or unexplained partial work remains in the planning delivery.
- [x] Exact continuation instructions exist.
- [ ] Delivery state and branch-cleanup responsibility are recorded after PR creation.
