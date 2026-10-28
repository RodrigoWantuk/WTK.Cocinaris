# Security and Abuse Requirements

- **Status:** Accepted
- **Last updated:** 2026-07-28

## Security goals

- isolate every user's private household, inventory, recipe, execution, photo, and usage data;
- protect credentials and provider secrets;
- prevent duplicate or unauthorized inventory mutations;
- bound AI, upload, notification, and account-creation cost;
- detect and recover from queue, provider, and worker failures;
- provide support diagnostics without exposing unnecessary personal data;
- preserve evidence for security incidents under controlled retention.

## Identity and session

- use Keycloak through standard OpenID Connect;
- use Authorization Code Flow and current OAuth security practices;
- keep provider access and refresh tokens out of frontend storage;
- use backend-managed `HttpOnly`, `Secure`, and appropriately scoped cookies;
- implement CSRF protection for cookie-authenticated state changes;
- rotate, revoke, and expire sessions;
- require verified email for applicable account flows;
- require MFA for privileged operational accounts;
- support global sign-out and incident revocation.

## Authorization

Cocinaris owns domain authorization. Every private resource is checked against the authenticated internal user and household context.

Keycloak roles may represent global operational roles, but they do not replace recipe ownership, household membership, sharing snapshot rules, or inventory authorization.

Privileged support access is least-privilege, time-bounded where practical, purpose-recorded, and audited.

## Input and upload security

- validate media type by content, not only extension;
- enforce size, pixel, page, and count limits;
- isolate temporary uploads;
- scan permanent files and high-risk inputs;
- normalize filenames and object keys;
- reject active or unexpected content;
- validate and restrict outbound URL fetching to prevent SSRF;
- use network egress policy for ingestion workers;
- delete transient source images according to product policy;
- treat parsed content as untrusted.

## API and application controls

- schema and domain validation;
- per-user and per-operation rate limits;
- request timeouts and cancellation;
- idempotency for retryable mutations;
- optimistic concurrency and conflict responses;
- secret storage outside source control;
- dependency, container, and secret scanning;
- security headers and controlled CORS;
- audit for destructive and privileged operations;
- feature flags and emergency integration disablement.

## AI-specific threats

- prompt injection in URLs, images, receipts, recipe text, and user instructions;
- attempts to reveal prompts, secrets, other users' data, or hidden context;
- oversized context and cost exhaustion;
- schema evasion;
- unsafe cooking output;
- cross-user cache contamination;
- tool or provider selection manipulation.

Mitigations include strict structured context, no arbitrary tool authority, domain validation, output limits, isolation, redaction, operation-specific prompts, provider timeouts, evaluations, and user confirmation.

## Abuse controls

- adaptive challenge for suspicious sign-up;
- account and IP/network velocity limits;
- disposable-email policy;
- multiple-account risk detection using several bounded signals;
- AI concurrency and allowance limits;
- upload and job limits;
- email and push anti-spam controls;
- sharing-link entropy, expiry, revocation, and access limits;
- anomaly alerts and manual appeal path.

Do not treat a shared IP or device signal as conclusive proof of abuse.

## Food-safety security boundary

Malicious or malformed imported content cannot override allergy, restriction, storage, temperature, doneness, or authorization rules. A prompt instruction inside a recipe source is content, not system authority.

## Incident readiness

Document and test:

- credential compromise;
- cross-user authorization failure;
- data export or deletion failure;
- AI provider data leakage;
- unexpected cost spike;
- malicious upload;
- queue duplication or poison message;
- notification abuse;
- backup exposure;
- food-safety defect requiring user communication.

## References

- OAuth 2.0 Security Best Current Practice: https://www.rfc-editor.org/info/rfc9700/
- ASP.NET Core rate limiting: https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0
