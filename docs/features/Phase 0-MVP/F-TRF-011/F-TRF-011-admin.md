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
| US-011-01 | See platform health at a glance | `US-011-01-overview/US-011-01-overview.md` |
| US-011-02 | Find and act on any transfer | `US-011-02-transfers-admin/US-011-02-transfers-admin.md` |
| US-011-03 | Manage users and plans | `US-011-03-users-admin/US-011-03-users-admin.md` |
| US-011-04 | Edit feature flags and see the audit trail | `US-011-04-flags-audit/US-011-04-flags-audit.md` |

**Implementation tasks:** one file per task — each story folder holds its story .md + its task files (context-friendly; execute top-to-bottom).

| Story | Task | File | Status |
|---|---|---|---|
| US-011-04 | T-011-01 Admin gate + role policy: `Wa:AdminEmails` config, SuperAdmin/Operator per-request (TA-9.2, FR-011-1) | `US-011-04-flags-audit/T-011-01-admin-gate-role-policy.md` | ☐ |
| US-011-04 | T-011-02 Audit write helper: `AuditLog` row + `admin.action` event in the same request (FR-011-5, TA-5.3) | `US-011-04-flags-audit/T-011-02-audit-write-helper.md` | ☐ |
| US-011-01 | T-011-03 GetOverviewQuery + endpoint 20: live counts, 5-min cache (FR-011-6, TA-4.2#20) | `US-011-01-overview/T-011-03-overview-endpoint.md` | ☐ |
| US-011-02 | T-011-04 Transfers search + endpoint 21 GET: linkId / email / status / size / date, cursor paging (FR-011-3) | `US-011-02-transfers-admin/T-011-04-transfers-search-endpoint.md` | ☐ |
| US-011-02 | T-011-05 Force-delete transfer + endpoint 21 DELETE: `transfer.deleted` reason `admin` (FR-011-4) | `US-011-02-transfers-admin/T-011-05-force-delete-endpoint.md` | ☐ |
| US-011-03 | T-011-06 Users list + endpoint 22 GET: email/plan/storage/transfers, cursor paging (FR-011-2) | `US-011-03-users-admin/T-011-06-users-list-endpoint.md` | ☐ |
| US-011-03 | T-011-07 Set plan + force-delete user, endpoint 22 PATCH/DELETE (FR-011-2; FR-BIL-001-4 semantics) | `US-011-03-users-admin/T-011-07-user-plan-and-delete-endpoint.md` | ☐ |
| US-011-04 | T-011-08 GetFlagsQuery + SetFlagCommand, endpoint 23: unknown key → 404, validate before commit (TA-13.2, FR-011-5) | `US-011-04-flags-audit/T-011-08-set-flag-endpoint.md` | ☐ |
| US-011-04 | T-011-09 Suppressions list/remove + recent-actions read: catalog rows 24–25 (FR-011-2, FR-011-5) | `US-011-04-flags-audit/T-011-09-suppressions-and-audit-read-endpoint.md` | ☐ |
| US-011-01 | T-011-10 Admin shell + Overview screen: stat cards, recent transfers (UI-Reference §5.6) | `US-011-01-overview/T-011-10-overview-ui.md` | ☐ |
| US-011-02 | T-011-11 Admin Transfers screen: search + force-delete (FR-011-3/4, UI-Reference §5.6) | `US-011-02-transfers-admin/T-011-11-transfers-ui.md` | ☐ |
| US-011-03 | T-011-12 Admin Users screen: plan edit + force-delete (FR-011-2, UI-Reference §5.6) | `US-011-03-users-admin/T-011-12-users-ui.md` | ☐ |
| US-011-04 | T-011-13 Admin Flags & Suppressions screens + Recent actions panel (FR-011-5, UI-Reference §5.6) | `US-011-04-flags-audit/T-011-13-flags-suppressions-ui.md` | ☐ |

**Story done when:** all tasks checked + full AGENT.md §4 gate green + AC-011-1…AC-011-2 verified with every mutation audit-logged with the actor email (T-024 row scope + exit check). Then T-024 can be marked `done` in `Milestone-Backlog.md`.
