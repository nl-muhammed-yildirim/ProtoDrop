# T-011-06 — GetUsersQuery + endpoint 22 GET: user list (FR-011-2 Screens → Users)

**Story:** US-011-03 | **Spec:** FR-011-2 (Screens → Users), FR-011-5 (audit is mutation-only); TA-4.2#22 GET, TA-4.1.4 | **Size:** M
**Depends on:** T-011-01 (admin gate)

---

## Context to read (only these)

- `US-011-03-users-admin.md` → happy path 1 + Edge cases (storage per user rule)
- `../F-TRF-011-admin.md` → FR-011-2 (Screens row: Users — "search, plan, storage used, force-delete")
- `../../F-TRF-009/US-009-01-history-list/T-009-05-my-files-ui.md` → cursor paging usage reference

## Instructions

1. Add **`GetUsersQuery`** (MediatR, `wa.application/UseCases/Admin/`) + endpoint 22 GET `GET /api/v1/admin/users?q=&cursor=` (TA-4.2#22).
2. Per-user row: email, plan (`AppUser.PlanId` → seeded `Plan.Name`, Free/Pro/Business), **storage used** (computed — SUM of `BlobRef.SizeBytes` for blobs the user owns references to, per-BlobRef counting US-010-03), **active transfer count** (computed — `COUNT` where `Status=1`), created (`AppUser.CreatedAtUtc`).
   - DECISION line: US-011-03's table columns are email / plan select / storage used / active transfers / created. None of these is a stored column on `AppUser` (DDL TA-3.2 has only `PlanId`, no last-login, no size). P0 computes storage + count in the query; **no new columns**. Note it here so review doesn't expect a migration.
3. Search by email/substring (`?q=`); cursor pagination per TA-4.1.4 (`limit=25` default).
4. Rows are read-only (mutations live in T-011-07) — no audit row on GET (FR-011-5 is mutation-only).

## Exit check

- [ ] US-011-03 AC: search "ada@x.com" → row shows plan, storage used, active transfer count (computed; gherkin "row shows their plan, storage used, and active transfer count")
- [ ] All seeded users appear; cursor walk over 25+ rows works (TA-4.1.4) — FR-011-2 Users screen is complete server-side
- [ ] No `q` → full list; no match → empty list (not an error)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS).
Task T-011-06 — admin users list (milestone task T-024, TA-4.2#22 GET).
Read first (only): US-011-03-users-admin.md (happy path 1 + Edge cases), F-TRF-011-admin.md (FR-011-2 Screens row), TA-4.1.4 in docs/03-technical-architecture.md.
Do exactly:
1. GetUsersQuery + GET /api/v1/admin/users?q=&cursor= (admin policy from T-011-01).
2. Rows: email, plan name (AppUser.PlanId -> Plan.Name), storage used = SUM(BlobRef.SizeBytes) over owned refs (US-010-03 per-ref counting), active transfer count = COUNT(Status=1), created. No new AppUser columns (DECISION line in the task doc).
3. Email substring search; cursor pagination limit 25 default (TA-4.1.4).
Done when: US-011-03's list gherkin holds server-side — a searched user returns plan + storage used + active count, and every seeded user is reachable via the cursor walk.
Constraints: read-only GETs (no audit rows); no EF migration in this task; thin endpoint per TA-4.2a.
```