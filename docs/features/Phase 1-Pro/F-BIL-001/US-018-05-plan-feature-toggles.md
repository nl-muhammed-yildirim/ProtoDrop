# US-018-05 — Feature availability follows my plan

**Feature:** F-BIL-001 — Plan Catalog | **Status:** pending

---

**Story:** As a user, I want which features I can use (scheduling, branding, analytics, ads) to be decided by my plan row's `FeaturesJson` — not by scattered conditionals — so that "what can Pro do?" has one answer.
**Actor:** any user, developer (gates a feature), operator (toggles a global kill-switch).
**Goal:** feature toggles are plan data, read through `PlanContext` (F-BIL-003).

## Preconditions

- `Plan.FeaturesJson` holds `{ ads, scheduling, analytics, branding, sso_scim }` booleans (Part 2 / Appendix A values).
- `PlanContext` middleware in place (F-BIL-003).

## Happy path

1. Free plan: `scheduling=false`, `branding=false`, `analytics=false`, `ads=true` (launch: global flag off per D-14), `sso_scim=false`.
2. A feature gate reads `PlanContext.Features.scheduling` etc. — one read site per feature.
3. Changing a plan's `FeaturesJson` (or the global `feature.*` flag, TA-13.2) changes availability with no deploy.

## Alternative flows

- **Global kill-switch**: `feature.scheduling = false` disables it for *all* plans (precedence: global flag beats plan row — operator escape hatch).
- **Plan switch**: availability changes on the next request (30 s cache, TA-3.4).

## Acceptance criteria

```gherkin
Given I am on free
When I open the link screen
Then the schedule control is disabled with "Pro plan required"
And no analytics panel is queried

Given I upgrade to pro
Then scheduling, branding, and analytics become available without a reload of the plan screen
```

## Edge cases

- Precedence: global `feature.*` flag (kill-switch) overrides plan row — documented (F-BIL-003 technical notes).
- `sso_scim` is Business-only (Phase 3 consumes the same toggle).

## UI notes

- Gated controls: present but disabled with tooltip "Pro plan required" + deep-link to the plan screen (consistent across F-PRF-001/002/003/004).

## Technical notes

- `FeaturesJson` booleans → `PlanFeatures` POCO (ta-3.4 shape); `PlanContext` exposes it.
- No per-feature `if plan == pro` — code review rule (F-BIL-003 AC-020-4).

## Links

- Feature: `BIL-001-plan-catalog.md` (FR-018-1, FR-018-5)
- Related: F-BIL-003 (middleware), F-PRF-002 (scheduling), F-PRF-001 (branding), F-PRF-003 (analytics), F-PRF-004 (ads)
- Architecture: TA-3.4, TA-13.2
- Decisions: D-14 (ads at launch)
