# T-078 — Manage users and plans

**Story:** US-011-03 | **Feature:** F-TRF-011 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-011/US-011-03-users-admin.md`
**Coarse task (Milestone-Backlog.md):** T-024
**Status:** pending

---

## Scope

As the operator, I want to find any user, see their plan and storage, change their plan, or force-delete them, so that billing disputes and abuse end with a click.

**Actor:** Operator (read) / SuperAdmin (edit).

**Goal:** A users table with plan control and a kill switch.

Happy path:

1. Users screen: table (email, plan select, storage used, active transfers, created), search by email/substring, cursor paging.
2. **Change plan:** SuperAdmin picks a plan in the row select → `PATCH /admin/users/{id}`; limits apply to **new transfers** (FR-BIL-001-4 semantics: next finalize, not retroactive).
3. **Force-delete user:** confirm modal → soft-delete (same as self-service GDPR delete, US-008-06: transfers re-homed to anonymous), reason `admin`.
4. All mutations audit-logged with actor email.

## Acceptance criteria

```gherkin
Given I search for "ada@x.com"
When the user list renders
Then the row shows their plan, storage used, and active transfer count

Given I change a user's plan from Free to Pro
When the save succeeds
Then their next finalize uses Pro limits
And an audit row records actor + old plan + new plan

Given I force-delete a user
When the confirm is pressed
Then their active transfers are re-homed to anonymous
And the audit log records the action
```

## Edge cases

- Plan select values come from `Plan` table (Free/Pro/Business rows, seeded).
- Storage used per user = sum of `BlobRef` bytes they own references to (per-`BlobRef` counting, US-010-03).

## Exit check

- [ ] Scenario 1: I search for "ada@x.com"
- [ ] Scenario 2: I change a user's plan from Free to Pro
- [ ] Scenario 3: I force-delete a user
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-011/US-011-03-users-admin.md`
- Feature: `TRF-011-admin.md` (FR-011-2)
- Architecture: TA-4.2#22
- Related: US-008-06 (delete semantics), US-007-01 (plan limits)
- Milestone: T-024
