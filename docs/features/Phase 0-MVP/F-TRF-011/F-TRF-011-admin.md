# F-TRF-011 — Admin Dashboard (Basic)

**Priority:** P1 | **Phase:** 0 — Core Transfer (shipped with MVP for ops)
**Spec source:** `02-feature-plan.md` F-TRF-011 | **Architecture:** TA-4.2#20–23, TA-9.2, TA-3.2 (`AuditLog`)
**Milestone tasks:** T-024

---

## Description

The operator's cockpit: what's live on the platform, who's on what plan, and the knobs that change behavior without a deploy. Two roles — **Operator** (read) and **SuperAdmin** (full) — seeded manually. Screens: **Overview** (live counts), **Transfers** (search + force-delete), **Users** (search + plan edit + force-delete), **Flags** (list & edit), **Suppressions** (email unsubscribes). Every admin mutation is audit-logged with the actor's email.

**Actors:** Operator, SuperAdmin (both seeded emails; P0 gate = `Wa:AdminEmails` config list, TA-9.2).
**Value:** ops independence — flags, kill-switches, and a paper trail before the first real money arrives.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-011-1 | Roles: `Operator` (read-only) and `SuperAdmin` (full). Seeded manually (email list in config, `Wa:AdminEmails`, TA-9.2). |
| FR-011-2 | Screens: **Overview** (active transfers, storage used, new users today, emails sent/failed 7 d); **Transfers** (search by linkId / recipient email / size / status + delete); **Users** (search, plan, storage used, force-delete); **Flags** (list & edit feature flags); **Suppressions** (email suppressions, list + remove). |
| FR-011-3 | Transfers search: by exact `linkId`, by recipient email (via `EmailRecipient`), by status, by size range, by date range; cursor pagination (TA-4.1.4). |
| FR-011-4 | Force-delete a transfer: same semantics as user delete (F-TRF-009-3) — `transfer.deleted` with reason `admin`. |
| FR-011-5 | Every admin mutation is audit-logged: `AuditLog (ActorEmail, Action, EntityType, EntityId, DetailsJson)` + `admin.action` event (TA-5.3). |
| FR-011-6 | Overview numbers are cached 5 minutes (staleness acceptable, stated on the screen: "Updated {time}"). |

## Acceptance criteria

```gherkin
AC-011-1: Operator opens Overview
  Then they see live counts within 5 min staleness (cached 5 min)

AC-011-2: SuperAdmin edits MAX_TRANSFER_SIZE flag
  Then new finalizes use the new value without a deploy
  And the change is audit-logged with the actor email

AC-011-3: Operator (read) tries to edit a flag
  Then they get FORBIDDEN and no audit row is created
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-011-1 | Two SuperAdmins edit the same flag concurrently | Last write wins on the `FeatureFlag` row (single-key PK); both writes are audit-logged — the audit trail is the truth of intent |
| EC-011-2 | Force-delete a transfer being viewed by a recipient | Same as user delete (EC-009-1): in-flight SAS may outlive the row |
| EC-011-3 | Admin email signs in as a normal user first | The same cookie carries both roles (session + `Wa:AdminEmails` check); role is derived per request, not stored in the token claims |
| EC-011-4 | Flag edited to a bad JSON value | Save is rejected inline ("Invalid JSON") — the old value stays live (validate before commit) |

## UI notes (UI-Reference §5.6)

- Left nav: Overview / Transfers / Users / Flags / Suppressions.
- Overview: 4 stat cards (active transfers, storage used, new users today, emails sent/failed 7 d) + recent transfers table (last 20, link to Transfers screen).
- Transfers / Users: table component (UI-Reference §4.7) — sticky header, filter bar (search + status select + date range), row actions as ghost buttons, confirm modal for deletes ("Force-delete transfer {linkId}? 3 files, 1.2 GB.").
- Flags: key/value editor with `--fs-small` JSON preview; **Save** per key; audit hint "Changes are logged."
- Suppressions: address, sender, date, **Remove** ghost button.
- Role badge in the top right ("Operator" / "SuperAdmin"); Operator sees action buttons as disabled (with tooltip), not hidden.

## Technical notes

- Endpoints 20–23 (TA-4.2): `GET /admin/overview` (5-min in-memory cache, TA-4.2a `GetOverviewQuery`), `GET /admin/transfers` + `DELETE /admin/transfers/{id}`, `GET /admin/users` + `PATCH /admin/users/{id}` (set plan), `GET /admin/flags` + `PUT /admin/flags/{key}`.
- AuthZ: session + email ∈ `Wa:AdminEmails` (TA-9.2); Operator = read set of endpoints; SuperAdmin = all. (P0: single `Wa:AdminEmails` list is the gate; role split is the UI+route policy.)
- Search mapping: `linkId` → `Transfer.LinkId`; email → `EXISTS (SELECT 1 FROM EmailRecipient WHERE …)`; size/date → `TotalBytes`/`CreatedAtUtc` ranges.
- Audit: `AuditLog` row + `admin.action` event in the same request (TA-5.3 payload `{actor, action, entityType, entityId, details}`).
- Plan change on a user: `AppUser.PlanId` update; limits take effect at the next finalize (FR-BIL-001-4 semantics — documented: "applies to new transfers").

## Test plan

- Unit: search predicate building; flag JSON validation; role policy table (endpoint × role).
- Integration: AC-011-1 (cache present), AC-011-2 (flag change → new draft uses value; audit row with actor), AC-011-3 (Operator PUT flag → `FORBIDDEN`, no audit row); force-delete → `transfer.deleted` reason `admin`.
- E2E: admin login → overview renders (T-024 exit, manual pass).

## User stories

| ID | Story | File |
|---|---|---|
| US-011-01 | See platform health at a glance | `US-011-01-overview.md` |
| US-011-02 | Find and act on any transfer | `US-011-02-transfers-admin.md` |
| US-011-03 | Manage users and plans | `US-011-03-users-admin.md` |
| US-011-04 | Edit feature flags and see the audit trail | `US-011-04-flags-audit.md` |
