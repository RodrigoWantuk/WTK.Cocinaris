# ADR-0004 — Identity and Browser Session

- **Status:** Accepted
- **Date:** 2026-07-28

## Context

The application requires account creation, email verification, recovery, external logins, session control, future MFA and passkeys, and strong separation between identity and domain authorization. Building password and identity infrastructure directly would create unnecessary security risk.

## Decision

- Use Keycloak as the initial identity provider.
- Integrate through standard OpenID Connect rather than provider-specific application-wide adapters.
- Use current Authorization Code Flow security practices.
- Use a backend/BFF-managed browser session with secure cookies.
- Do not store long-lived access or refresh tokens in React browser storage.
- Assign every Cocinaris user an internal UUID independent from the Keycloak subject.
- Map identities by issuer and subject and preserve future identity linking.
- Keep household, recipe, inventory, sharing, and application authorization in Cocinaris.
- Use identity-provider roles only for limited global operational roles.
- Require email verification for applicable flows and MFA for privileged internal accounts.
- Initial desired user flows include email/password, verification, recovery, Google login, session revocation, and account deletion. Exact realm configuration is an implementation plan.

## Alternatives considered

- Build authentication internally: rejected due password, recovery, MFA, social-login, and session risk.
- SaaS-only identity provider: not selected because the project values deployability and provider portability.
- Tokens directly in the React application: rejected to reduce token exposure and centralize session policy.

## Consequences

- Keycloak becomes an operational dependency requiring backup, upgrade, monitoring, and secure administration.
- Identity deletion and domain deletion require a coordinated privacy workflow.
- Same-origin proxying is preferred where practical, with CSRF protection for cookie-authenticated mutations.
- A future identity-provider replacement remains possible through OIDC and internal user identity.

## Amendment — 2026-10-28 (owner decision, PLAN-0031)

- Cocinaris integrates with **any standards-compliant OIDC provider**. Keycloak remains the development and test fixture (`infrastructure/keycloak`, compose stack, integration tests), not a deployment requirement.
- A deployment documents only its OIDC parameters (authority, client ID and secret, redirect and sign-out callback URIs). Repository documentation does not name the provider a deployment uses.
- Internal user identity, issuer/subject mapping, the backend-managed session, and domain authorization are unchanged. The consequence about operating Keycloak applies only where a deployment chooses Keycloak.
- The deployment provider's account flows (verification, recovery, social login, MFA) are that provider's responsibility. Cocinaris still requires the issuer to supply a stable subject.

## References

- Keycloak administration and OIDC: https://www.keycloak.org/docs/latest/server_admin/
- OAuth 2.0 Security Best Current Practice: https://www.rfc-editor.org/info/rfc9700/
