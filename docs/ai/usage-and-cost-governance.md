# AI Usage, Cost, Quota, and Abuse Governance

- **Status:** Accepted
- **Last updated:** 2026-07-28

## Objective

AI cost is one of Cocinaris's largest variable operational risks. Usage governance is a first-class product and architecture capability.

## Central ledger

The authoritative PostgreSQL ledger records:

- user and entitlement plan;
- operation type;
- provider and model;
- prompt or workflow version;
- input, output, reasoning, and cached usage when reported;
- provider cost and currency;
- reserved allowance;
- settled allowance;
- adjustment and refund;
- idempotency key;
- timestamp and result.

Redis may accelerate rejection checks but does not own the official balance.

## User-visible allowance

Raw model tokens are not necessarily comparable across providers or models. The UI may present normalized `AI credits`, operation allowances, or another understandable unit while still exposing enough detail for transparency.

The user can see:

- current allowance;
- usage in the active period;
- remaining allowance;
- reset time;
- operation cost before confirmation when material;
- rejected or refunded usage;
- plan upgrade impact.

The final public unit and conversion require a commercial plan.

## Reservation and settlement

```text
Authorize user and operation
→ estimate maximum permitted cost
→ reserve allowance
→ execute model operation
→ record actual provider usage
→ settle actual charge
→ release unused reservation
→ refund according to failure policy
```

The operation must not start if the maximum permitted budget cannot be reserved, except for an explicit emergency or support policy.

## Limits

Controls may apply by:

- user;
- household;
- subscription;
- operation;
- model class;
- minute, hour, day, or billing period;
- concurrent jobs;
- upload size and count;
- global provider and project budget.

Limits are strict and user-safe. A rejected operation explains the limit and when capacity returns.

## Model routing

The gateway chooses the least expensive model that meets a documented quality threshold.

Typical routing:

- small model for classification and simple extraction;
- intermediate model for structured parsing and ordinary adaptation;
- capable model for broad planning and complex troubleshooting;
- deterministic rules for arithmetic, state, restriction, and safety checks.

Provider and model fallback must account for cost, schema capability, latency, privacy, and evaluated quality.

## Cost reduction

- reuse normalized and validated generated content;
- avoid sending irrelevant history;
- cap fields and collections in JSON;
- summarize noncritical narrative context only when justified;
- cache only content that is safe to share under the defined cache key;
- batch suitable background operations;
- reject redundant duplicate jobs through idempotency;
- use provider caching features only after privacy and cost analysis;
- monitor cost per operation, active user, conversion, and retained user.

## Commercial direction

The accepted direction is:

- highly limited free plan;
- possible ads for free users;
- paid subscription without ads and with higher AI allowance.

Exact prices, credits, ad provider, billing, refunds, and regional taxes are deferred. Advertising cannot be introduced without privacy, consent, age-policy, security, and user-experience review.

## Multiple-account and automated abuse

No single signal reliably proves account duplication. Controls combine:

- verified email;
- adaptive CAPTCHA or challenge;
- account-creation and login rate limits;
- disposable-email policy;
- IP and network velocity;
- privacy-reviewed device risk signals with bounded retention;
- suspicious usage and job concurrency;
- provider-payment signals for paid plans;
- manual review and appeal for high-impact actions.

Shared networks and legitimate multiple users must be considered. Avoid irreversible decisions based only on IP address or opaque fingerprinting.

## Prompt and upload abuse

- treat URLs, images, receipts, recipes, and user text as hostile input;
- isolate parsing from system prompts and credentials;
- enforce content, size, and object limits before provider calls;
- prevent prompt-injected content from selecting tools, providers, authorization, or hidden context;
- redact secrets and identifiers;
- scan uploads before permanent storage;
- limit outbound requests and validate URL targets;
- log abuse categories without storing unnecessary content.

## Budget protection

Operations can be disabled by feature flag, provider, model, account plan, region, or global emergency switch. Alert on cost-rate anomalies before the monthly invoice becomes the first signal of abuse.
