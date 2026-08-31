# US-020-04 — Feature availability follows my plan

**Feature:** F-BIL-003 — Plan Enforcement | **Status:** pending

---

**Story:** As a user, I want which features are available (scheduling, branding, analytics, ads) to follow my plan via `PlanContext.Features` — so that a single plan row governs everything and no feature hardcodes its own gate.
**Actor:** any user, developer (gates a feature).
**Goal:** feature gating reads `PlanContext.Features.*`; one read site per feature.

## Preconditions

- `PlanContext` middleware (US-020-01) in place; `FeaturesJson` on plan rows (F-BIL-001).

## Happy path

1. A feature (e.g. scheduled send) reads `PlanContext.Features.scheduling`.
2. Free → false (control disabled, "Pro plan required"); Pro/Business → true.
3. Changing the plan row or the global `feature.*` flag changes availability with no deploy.

## Alternative flows

- **Global kill-switch**: `feature.scheduling=false` disables for all plans (operator escape hatch, precedence over plan row).
- **Ads**: `Features.ads` (Free=true) AND global `feature.ads` (launch: off, D-14) → ad slot only when both allow (F-PRF-004).

## Acceptance criteria

```gherkin
Given I am on free
When I request the schedule control
Then it is disabled with "Pro plan required"
And no analytics query runs

Given I upgrade to pro
When the next request resolves
Then the feature is available (≤ 30 s cache)
```

## Edge cases

- Precedence: global `feature.*` flag beats plan row (documented, F-BIL-001 US-018-05).
- Gated features: scheduling (F-PRF-002), branding (F-PRF-001), analytics (F-PRF-003), ads (F-PRF-004).

## UI notes

- Gated controls: present + disabled + tooltip "Pro plan required" + plan-screen deep-link (consistent across all F-PRF-*).

## Technical notes

- `PlanContext.Features` POCO; 30 s cache (TA-3.4).
- No per-feature `if plan == pro` — code review rule (AC-020-4).

## Links

- Feature: `BIL-003-plan-enforcement.md` (FR-020-5, AC-020-4)
- Related: F-BIL-001 (FeaturesJson), F-PRF-001/002/003/004
- Architecture: TA-3.4, TA-13.2
