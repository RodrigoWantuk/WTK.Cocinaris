# ADR-0007 — Official Frontend Platform, Monorepo Authority, and Generation Tooling

- **Status:** Accepted
- **Date:** 2026-07-31
- **Owners:** Cocinaris architecture / PLAN-0014
- **Decision scope:** Frontend platform, repository authority, and optional UI generation tools
- **Related issues:** None
- **Supersedes:** [ADR-0001](0001-frontend-platform-and-boundary.md)
- **Superseded by:** None

## Context

ADR-0001 required React/TypeScript and treated Lovable as the required design and generation workflow. The first substantial product prototype was completed in the temporary repository `RodrigoWantuk/kitchen-emergent` and must become the official monorepo frontend under `apps/frontend`. Continuing to treat Lovable as mandatory, or Emergent as an ongoing sync source, would create dual ownership and block PLAN-0011 and PLAN-0005.

## Decision drivers

- Keep React and TypeScript as durable frontend platform decisions.
- Establish a single official source of truth in the monorepo.
- Allow optional generation tools without making them architecture owners.
- Preserve strict backend boundary: no authoritative domain rules, secrets, tokens, or direct provider calls in the frontend.
- Support gradual live-backend integration through adapters and generated OpenAPI clients.

## Considered options

### Option A: Keep Lovable-mandatory ADR-0001 and execute PLAN-0004

**Advantages**

- Matches the original stakeholder generation workflow.

**Disadvantages and risks**

- Ignores the completed Emergent prototype.
- Delays official baseline and dependent plans.

### Option B: Treat Emergent as a continuing bidirectional sync source

**Advantages**

- Continues the temporary generation environment.

**Disadvantages and risks**

- Dual sources of truth; public monorepo and private tooling diverge.

### Option C: Import Emergent once; monorepo owns frontend; generation tools optional

**Advantages**

- Single official source; preserves delivered UX; clear archival path for Emergent.

**Disadvantages and risks**

- Requires cleanup of Emergent platform coupling and TypeScript migration work.

## Decision

- Use **React** and **TypeScript** for the Cocinaris frontend.
- The official frontend lives only at `WTK.Cocinaris/apps/frontend`.
- The Emergent repository `RodrigoWantuk/kitchen-emergent` at commit `69f798f66b7987c4ed785c52c90a5539bf46f52e` is the one-time historical snapshot imported by PLAN-0014. No bidirectional sync is planned; the temporary repository may be archived after validation.
- **Emergent** and **Lovable** are optional design/generation tools. They do not own authentication, persistence, domain rules, contracts, or deployment authority.
- Preserve the imported runtime (Create React App + CRACO) unless a later ADR changes it.
- Keep the frontend independently buildable, testable, deployable, and observable.
- Consume the backend through generated OpenAPI TypeScript clients or contracts when live adapters are enabled.
- Do not place provider secrets, long-lived identity tokens, authoritative quota, inventory arithmetic, reservation authority, unit conversion, or food-safety enforcement in frontend code.
- Require localization readiness, accessibility, responsive behavior, explicit uncertainty presentation, and mock/production isolation.

## Rationale

Option C preserves the delivered product surface while correcting ownership. React/TypeScript remain fixed. Generation tools remain useful but subordinate to the monorepo.

## Consequences

### Positive

- Dependent plans (PLAN-0005, PLAN-0011) have a clear frontend baseline owner.
- Historical Emergent work is auditable via exact commit provenance.
- Backend boundary remains enforceable through adapters and contracts.

### Negative

- Snapshot is JavaScript-first and requires prioritized TypeScript conversion.
- Emergent platform packages and scripts must be removed or justified.

### Neutral or follow-up

- Exact package set, test tooling, and localization library evolve under PLAN-0014 and later plans.
- Live session/API integration follows backend-managed cookies, CSRF, ETag/If-Match, Problem Details, and idempotency.

## Security and privacy impact

Frontend continues to use backend-managed browser sessions. No Keycloak JS adapter, no token storage, no direct database or AI provider access. Prototype fixtures must not ship as production authoritative data.

## AI and safety impact

No direct AI provider calls from the frontend. Uncertain safety or availability estimates must not be presented as guarantees.

## Operational impact

Frontend CI is independent of backend CI. Deployment remains independently scalable. Emergent-hosted assets and telemetry must not remain as production dependencies.

## Migration and rollback

- Import once under PLAN-0014 with documented provenance.
- Rollback is a git revert of the import/baseline commits; do not re-enable Emergent sync.
- Exit strategy for CRA/CRACO requires a later ADR if replaced.

## Validation

- Official tree is `apps/frontend` with production build, lint, typecheck, and tests.
- No `@emergentbase/*` or Emergent telemetry required for production builds.
- PLAN-0004 marked Superseded; PLAN-0005 and PLAN-0011 reference PLAN-0014.

## References

- [PLAN-0014](../../plans/PLAN-0014-integrate-emergent-frontend.md)
- [ADR-0001](0001-frontend-platform-and-boundary.md) (superseded)
- [apps/frontend/README.md](../../../apps/frontend/README.md)

## Note — 2026-10-28

The build runtime decision ("preserve Create React App + CRACO unless a later ADR changes it") is amended by [ADR-0008](0008-frontend-toolchain-vite.md): the frontend now uses Vite and Vitest. The monorepo authority and optional generation tooling decisions are unchanged.
