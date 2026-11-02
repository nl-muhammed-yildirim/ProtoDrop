# US-011-03 — Manage users and plans

**Feature:** F-TRF-011 — Admin Dashboard | **Status:** pending

---

**Story:** As the operator, I want to find any user, see their plan and storage, change their plan, or force-delete them, so that billing disputes and abuse end with a click.
**Actor:** Operator (read) / SuperAdmin (edit).
**Goal:** A users table with plan control and a kill switch.

## Preconditions

- Admin signed in.

## Happy path

1. Users screen: table (email, plan select, storage used, active transfers, created), search by email/substring, cursor paging.
2. **Change plan:** SuperAdmin picks a plan in the row select → `PATCH /admin/users/{id}`; limits apply to **new transfers** (FR-BIL-001-4 semantics: next finalize, not retroactive).
3. **Force-delete user:** confirm modal → soft-delete (same as self-service GDPR delete, US-008-06: transfers re-homed to anonymous), reason `admin`.
4. All mutations audit-logged with actor email.

## Alternative flows

- **Plan change for a user with over-quota active transfers:** allowed (the overage just counts; new sends block until under, F-BIL-003-2 semantics).
- **Deleted user's email re-registered later:** new row; old transfers remain anonymous.

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

## UI notes

- Table per 4.7; plan cell is a `<select>` (disabled for Operator); force-delete ghost (trash) + confirm modal.

## Technical notes

- `SearchUsersQuery` (endpoint 22 GET), `SetUserPlanCommand` (endpoint 22 PATCH); force-delete = `DeleteAccountCommand` semantics with `reason=admin`.
- Audit: `admin.action` + `AuditLog` (US-011-04).

## Links

- Feature: `TRF-011-admin.md` (FR-011-2)
- Architecture: TA-4.2#22
- Related: US-008-06 (delete semantics), US-007-01 (plan limits)
- Milestone: T-024
