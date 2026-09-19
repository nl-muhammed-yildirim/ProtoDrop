# T-076 — See platform health at a glance

**Story:** US-011-01 | **Feature:** F-TRF-011 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-011/US-011-01-overview.md`
**Coarse task (Milestone-Backlog.md):** T-024
**Status:** pending

---

## Scope

As the operator, I want the first admin screen to answer "is everything fine?" in one look, so that I don't have to dig into tables to find a fire.

**Actor:** Operator or SuperAdmin.

**Goal:** Overview = 4 numbers + a peek at recent transfers.

Happy path:

1. Admin → Overview shows four stat cards:
2. Below: a recent-transfers table (last 20 by `CreatedAtUtc`, read-only preview of the Transfers screen).
3. Numbers are cached 5 minutes; the screen says "Updated {time}" (staleness stated, FR-011-6).

## Acceptance criteria

```gherkin
Given the platform has 1,200 active transfers, 4.2 TB in storage, 17 new users today, 12,000 emails sent and 3 failed in 7 days
When I open Overview
Then the four stat cards show these numbers within 5 minutes staleness
And the "Updated {time}" line is visible

Given I reload 1 minute after a deploy
When the cards render
Then they reflect the latest cached snapshot (not stale beyond 5 min)
```

## Edge cases

- UTC day boundary: "new users today" is a UTC calendar day (documented in the tooltip).
- Caching is per-instance in-memory (no Redis, ADR-008) — cross-replica divergence ≤ 5 min, acceptable for overview numbers.

## Exit check

- [ ] Scenario 1: the platform has 1,200 active transfers, 4.2 TB in storage, 17 new users today, 12,000 emails sent and 3 failed in 7 days
- [ ] Scenario 2: I reload 1 minute after a deploy
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-011/US-011-01-overview.md`
- Feature: `TRF-011-admin.md` (FR-011-2, FR-011-6)
- Plan AC: AC-011-1
- Architecture: TA-4.2#20, TA-10.2
- Milestone: T-024
