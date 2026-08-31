# F-ENT-003 — Organization Admin & Audit

**Priority:** P1 (Phase 3) | **Phase:** 3 — Enterprise
**Spec source:** `02-feature-plan.md` §5 F-ENT-003 (outline → remapped below as FR-040-*) | **Architecture:** TA-3.2, TA-4.2, TA-5.3, TA-9.2
**Milestone tasks:** T-056 (M6)

---

## Description

The **organization** is the enterprise unit of membership, billing, and audit. An org has a slug, a plan (Business tier per D-07), members with roles, and an **immutable-style audit trail** of who did what, when, and from where. Every org-level mutation in Phase 3 (SSO config, SCIM sync, residency, subdomain, workspace changes, member changes, org plan change) lands in the same `OrgAuditEntry` table — one query answers "what happened in this org last month."

**Division of labor with F-ENT-006 (Workspaces):** org-level roles and membership live here; *per-group* plan + limit scoping lives in F-ENT-006. The outline's "groups (limits per group)" is the workspace layer; this feature only references it.

**Actors:** org admin (`OrgAdmin` role), member, platform `SuperAdmin` (existing, F-TRF-011), operator.
**Value:** IT can buy once, hand out seats, and produce the audit report the compliance team asked for — without calling us.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-040-1 | **Create org:** `name` (≤ 80, required), `slug` (3–32, `a-z0-9-`, unique, shown in URLs), `region` (F-ENT-004 — selected here), plan = Business (D-07; the only org plan in Phase 3). Creator becomes the first `OrgAdmin`. |
| FR-040-2 | **Members:** invite by email (link email via `f-email`, template `org_invite`, single-use `AuthToken` purpose 4 = org-invite, 7-day TTL). Accept → `OrgMember` row + `AppUser.PrimaryOrgId` (set only if the user has no other primary org; documented). |
| FR-040-3 | **Roles:** `OrgAdmin` (full org settings, members, audit, billing) and `Member` (send/receive within org limits). Exactly one role per membership. Role change / removal is an audited mutation. |
| FR-040-4 | **Member lifecycle:** invite → pending (`JoinedAtUtc = NULL`, `InvitedByEmail`); pending invites expire after 14 days (timer, `org_invite_expired` event); remove member = soft (`LeftAtUtc` added), re-homes that user's **org-attributed** transfers per F-TRF-008-8; deactivating keeps history. |
| FR-040-5 | **Audit log:** `OrgAuditEntry` (see DDL) written in the same transaction as the mutation (no event-bus lag — same rule as F-SGN-003). Covers: `org.created`, `org.member.invited`, `org.member.role_changed`, `org.member.removed`, `org.plan_changed`, `sso.config_changed`, `scim.secret_rotated`, `residency.report_viewed`, `subdomain.changed`, `workspace.*` (F-ENT-006), plus `document.signed` / `collection.entry.status_changed` **when the actor is an org member acting org-scoped**. |
| FR-040-6 | **Audit search:** filter by actor (user or `system`/`scim`), action prefix, entity type, and date range; cursor-paginated; newest first. |
| FR-040-7 | **CSV export:** `GET /orgs/{id}/audit/export.csv?…same filters…` → `text/csv`, max 10 000 rows (`206`-style streaming; beyond → `406 AUDIT_EXPORT_TOO_LARGE` with a hint to narrow the range). Export itself is audit-logged (`org.audit_exported`). |
| FR-040-8 | **Effective plan:** a member sending a transfer while org-attributed gets limits from the **org plan** (Business), not their personal plan; personal transfers (no org attribution) keep the personal plan. Attribution rule: `PrimaryOrgId` set + `WorkspaceId` (F-ENT-006) or default org workspace → org-scoped. |

## Acceptance criteria

```gherkin
AC-040-1: An org is created with a slug
  Then the slug is unique and usable in URLs
  And the creator is OrgAdmin
  And org.created is in the audit log

AC-040-2: An invite is accepted
  When the invited user clicks the link
  Then they become a Member and their PrimaryOrgId is set
  And org.member.invited is closed by an org.member.joined audit entry

AC-040-3: The audit log
  Given 100 org actions
  When I filter by actor and date range
  Then only matching entries are returned, newest first
  And the CSV export contains exactly the filtered rows (max 10 000)

AC-040-4: A member is removed
  Then their org-attributed transfers are re-homed as anonymous
  And personal transfers are untouched

AC-040-5: An org member sends a transfer
  Then the org plan limits (Business) apply, not their personal plan
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-040-1 | Invite email belongs to a user who already has a primary org | They may still join; `PrimaryOrgId` is **not** changed (first org wins, documented) — they remain a member of both, org-scoped sends only via the workspace picker |
| EC-040-2 | Remove the last `OrgAdmin` | Blocked: `ORG_LAST_ADMIN` (409) until another admin exists |
| EC-040-3 | Audit entry actor = `scim` (system) | `ActorAppUserId = NULL`, `ActorEmail = "scim:{orgSlug}"` — always filterable as an actor |
| EC-040-4 | CSV export with no rows | `200` with header row only |
| EC-040-5 | Org slug taken | `409 ORG_SLUG_TAKEN` at create; suggestion list in UI (3 alternatives) |
| EC-040-6 | Clock: audit `AtUtc` from the server, not the client | All audit writes use `GETUTCDATE()`/DB-side now in the same transaction |

## UI notes (UI-Reference §5.6 extension — org section of the admin area)

- **Org settings pages:** Overview (name, slug, plan, region chip, subdomain status), **Members** (table: name, email, role select, joined, last active; **Invite** primary; row actions: change role, remove (confirm modal)), **Audit** (4.7 table + filter bar: actor, action, entity, date range; **Export CSV** ghost button with row count in the toast after start), **Security** (SSO/SCIM — F-ENT-001/002 screens), **Billing** (org plan, seats used/allowed).
- Member row states: `invited` (amber chip, expiry countdown), `active` (green), `left` (gray).
- Audit action names render human-readable ("Member role changed — {user} → OrgAdmin") via i18n keys `audit.{action}`.

## Technical notes

- DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  CREATE TABLE dbo.Organization (
      Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Org PRIMARY KEY,
      Slug         VARCHAR(32) NOT NULL CONSTRAINT UQ_Org_Slug UNIQUE,
      Name         NVARCHAR(80) NOT NULL,
      PlanId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Org_Plan REFERENCES dbo.Plan(Id),
      Region       VARCHAR(16) NOT NULL CONSTRAINT DF_Org_Region DEFAULT 'westeurope',
      Subdomain    VARCHAR(64) NULL CONSTRAINT UQ_Org_Subdomain UNIQUE,
      CreatedAtUtc DATETIME2 NOT NULL
  );
  CREATE TABLE dbo.OrgMember (
      OrganizationId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_OM_Org REFERENCES dbo.Organization(Id),
      AppUserId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_OM_User REFERENCES dbo.AppUser(Id),
      Role            TINYINT NOT NULL CONSTRAINT DF_OM_Role DEFAULT 0,  -- 0 Member, 1 OrgAdmin
      InvitedByEmail  VARCHAR(320) NULL,
      JoinedAtUtc     DATETIME2 NULL,
      LeftAtUtc       DATETIME2 NULL,
      PRIMARY KEY (OrganizationId, AppUserId)
  );
  CREATE TABLE dbo.OrgAuditEntry (
      Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrgAudit PRIMARY KEY,
      OrganizationId   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_OA_Org REFERENCES dbo.Organization(Id),
      ActorAppUserId   UNIQUEIDENTIFIER NULL,
      ActorEmail       VARCHAR(320) NULL,
      Action           VARCHAR(64) NOT NULL,
      EntityType       VARCHAR(64) NOT NULL,
      EntityId         UNIQUEIDENTIFIER NULL,
      IpHash           VARCHAR(64) NULL,
      AtUtc            DATETIME2 NOT NULL,
      DetailsJson      NVARCHAR(MAX) NULL
  );
  CREATE INDEX IX_OrgAudit_Org_At ON dbo.OrgAuditEntry (OrganizationId, AtUtc DESC);
  CREATE INDEX IX_OrgAudit_Org_Actor ON dbo.OrgAuditEntry (OrganizationId, ActorAppUserId, AtUtc DESC);
  -- AppUser: PrimaryOrgId FK (nullable) — added here if not present from F-ENT-002
  ```
- Endpoints (TA-4.2 extension, ADR note): `POST /api/v1/orgs` (50), `GET /api/v1/orgs` (51), `GET /api/v1/orgs/{orgId}/members` (52), `POST /api/v1/orgs/{orgId}/members/invite` (53), `PATCH /api/v1/orgs/{orgId}/members/{userId}` (54), `DELETE /api/v1/orgs/{orgId}/members/{userId}` (55), `GET /api/v1/orgs/{orgId}/audit` (56), `GET /api/v1/orgs/{orgId}/audit/export.csv` (57), `PATCH /api/v1/orgs/{orgId}` (58, name/plan).
- MediatR: `CreateOrganizationCommand`, `ListMyOrgsQuery`, `ListOrgMembersQuery`, `InviteOrgMemberCommand`, `UpdateOrgMemberCommand`, `RemoveOrgMemberCommand`, `QueryOrgAuditQuery`, `ExportOrgAuditQuery`.
- Events (TA-5.3 extension): `org.created { orgId, slug, region }`, `org.member_changed { orgId, userId, action }`, `org.plan_changed { orgId, from, to }`.
- Telemetry (TA-10.2 extension): `org_created`, `org_audit_export { rows }`.
- CSV: `CsvHelper` via `Microsoft.AspNetCore.HttpResults` streaming; columns fixed: `atUtc, actor, action, entityType, entityId, ipHash, details`.

## Test plan

- Unit: slug validation + suggestion generator; role-transition rules (last admin); invite expiry query; CSV row mapping (BOM, quoting, CRLF).
- Integration: AC-040-1…040-5; 10k-row export boundary; audit row count invariant (each mutation ⇒ exactly one entry, transactional test with rollback).
- E2E: create org → invite second user (fake mail) → accept → change role → export CSV (Playwright + `f-email` log-only mode).

## User stories

| ID | Story | File |
|---|---|---|
| US-040-01 | Create our organization | `US-040-01-create-org.md` |
| US-040-02 | Invite my team and manage their roles | `US-040-02-manage-members.md` |
| US-040-03 | Prove what happened in our org | `US-040-03-audit-log.md` |
| US-040-04 | Apply the org plan to my team's transfers | `US-040-04-org-plan.md` |
