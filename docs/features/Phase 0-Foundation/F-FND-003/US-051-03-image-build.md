# US-051-03 — Have an image ready before deploy exists

**Feature:** F-FND-003 — CI Pipeline (PR Gate) | **Status:** done (T-003, 2026-08-31)

---

**Story:** As the team preparing for first deployment, I want every PR to build the `wa-api` Docker image, so that by the time staging exists we already trust the image pipeline.
**Actor:** Developer / operator (human), CI pipeline.
**Goal:** The multi-stage Dockerfile works on day one; pushing is gated until the dev ACR exists.

## Preconditions

- `src/wa.api/Dockerfile` present (multi-stage: publish → `aspnet:10.0`).
- `.dockerignore` at repo root keeps context small (~3 MB).

## Happy path

1. PR opened; docker job runs.
2. Multi-stage build produces an image tagged `wa-api:ci-<sha>`.
3. No push happens (dev ACR not yet provisioned — E-003, Preflight P-03 owner "you").

## Alternative flows

- **Push enabled later:** once the repo variable `AZURE_CONTAINER_REGISTRY` is set, the commented push step activates (TA-12.1 stage 5).
- **Local check:** `docker build -t wa-api:local-check src/wa.api` reproduces CI locally (T-003 exit evidence).

## Acceptance criteria

```gherkin
Given a PR touching src/wa.api
When the docker job runs in ci.yml
Then an image tagged wa-api:ci-<sha> is built successfully
And no push to any registry occurs while AZURE_CONTAINER_REGISTRY is unset

Given the Dockerfile uses multi-stage publish → aspnet:10.0
When I inspect the built image locally (docker build -t wa-api:local-check)
Then it starts and serves /health when given the right env vars
```

## Edge cases

- Context bloat: `.dockerignore` excludes docs/tests/node_modules — context stays ~3 MB (T-003 note).
- Base image churn: `aspnet:10.0` tag is pinned by major; minor updates are acceptable, breaking = ADR.

## UI notes

- None.

## Technical notes

- The Dockerfile and `.dockerignore` are T-003 deliverables; the push gate is the documented E-003 escalation.
- CD pipelines (cd-staging.yml / cd-prod.yml) reuse this image shape — TA-12.2 rollback = redeploy previous tag.

## Links

- Feature: `F-FND-003-ci-pipeline.md` (FR-051-2, AC-051-3)
- Architecture: TA-12.1, TA-12.2
- Milestone: T-003
