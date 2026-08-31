# US-020-01 — One place decides my plan

**Feature:** F-BIL-003 — Plan Enforcement | **Status:** pending

---

**Story:** As a developer, I want the user's plan (limits + feature toggles) resolved **once at request start** into a `PlanContext`, so that no feature has its own plan check and limits can't drift between features.
**Actor:** developer (adds a limit check), user (feels consistent enforcement).
**Goal:** a single `PlanContextMiddleware` → `PlanContext` in the request scope; commands read it, never `AppUser.PlanId`.

## Preconditions

- `Plan` rows + `ILimitsProvider` (F-BIL-001, TA-3.4) in place.
- Thin-endpoint rule (TA-4.2a): endpoints map HTTP → MediatR request only.

## Happy path

1. Request starts → middleware resolves `User → Plan` (guest → Free).
2. `PlanContext { Limits, Features, PlanCode }` attached to the request scope.
3. Every command/query that needs limits reads `PlanContext` — no scattered plan checks.

## Alternative flows

- **Guest request**: resolves Free; account-only limits apply at account finalize (F-BIL-003-4, US-020-04).
- **Deleted user**: resolves last-known plan (soft-deleted row).

## Acceptance criteria

```gherkin
Given any authenticated request
When it reaches a MediatR handler
Then PlanContext is populated with that user's plan limits and features

Given a command references AppUser.PlanId directly
When reviewed
Then it fails (no scattered plan checks)
```

## Edge cases

- Unknown plan code → `UNKNOWN_PLAN` (defensive).
- Global `feature.*` kill-switch precedence over plan row (F-BIL-001 US-018-05).

## UI notes

- None directly — the middleware is invisible; its effect is consistent limits.

## Technical notes

- `PlanContextMiddleware` before MediatR dispatch; `PlanContext` POCO in `wa.application`.
- Commands receive it via scope service / request metadata (never query `Plan`).

## Links

- Feature: `BIL-003-plan-enforcement.md` (FR-020-1, FR-020-4, AC-020-1/4/5)
- Architecture: TA-3.4, TA-4.2a
- Related: F-BIL-001 (plans), F-TRF-007 (enforcement points)
