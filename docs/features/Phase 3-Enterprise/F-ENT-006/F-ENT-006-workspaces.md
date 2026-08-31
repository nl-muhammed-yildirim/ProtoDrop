# F-ENT-006 — Workspaces

**Priority:** P2 (Phase 3, later half) | **Phase:** 3 — Enterprise
**Spec source:** `02-feature-plan.md` §5 F-ENT-006 (outline → remapped below as FR-043-*) | **Architecture:** TA-3.2, TA-3.4, TA-4.2, TA-5.3
**Milestone tasks:** T-062 (M6)

---

## Description

Large orgs don't have one limit for everyone: Marketing ships 20 GB renders, Legal sends 50 MB contracts, and the dev team shouldn't be blocked by Marketing's quota. A **workspace** is a named group inside an organization with its own members, its own **plan assignment**, and its own **limit overrides** on top of the plan. It is the implementation of the outline's "groups (limits per group)" — F-ENT-003 owns membership of the org; this feature owns membership *and scoping* of the group.

**Resolution order (frozen, read at send time):** workspace limit override → workspace plan → org plan → personal plan. The F-BIL-003 middleware (F-BIL-003-1, "one place decides my plan") gains a `WorkspaceContext` input; no other feature reads workspaces directly.

**Actors:** org admin (creates workspaces, assigns members/plans), member (sends from a workspace), operator.
**Value:** one org invoice, many independent budgets — the difference between "IT bought it" and "every team actually uses it."

## Functional requirements

| ID | Requirement |
|---|---|
| FR-043-1 | **Create workspace:** `name` (≤ 80, unique per org), optional `description` (≤ 200), `PlanId` (defaults to the org's plan), optional per-limit overrides (`LimitsJson` subset — only keys present override; unset = inherit). Every org gets an implicit **General** workspace at creation (all org members, org plan, no overrides). |
| FR-043-2 | **Workspace members:** org admin assigns **org members** (not external emails) to workspaces; a member may belong to **exactly one workspace** (the General workspace is the fallback membership — reassignment, not addition). `OrgMember.WorkspaceId` (nullable = General). |
| FR-043-3 | **Send flow:** signed-in org members see a workspace line on the link screen (F-TRF-002 screen): "Sending from {workspace name}" — read-only (membership decides it; no per-send picker in Phase 3, documented simplification). The transfer stores `WorkspaceId`. |
| FR-043-4 | **Limits resolution:** at finalize/send, the middleware resolves: `WorkspaceLimits = workspace.Overrides ∪ workspace.Plan.Limits`; quota (storage, active transfers) is **per workspace**, not per org — a workspace at its quota shows "Workspace storage full" and does not block other workspaces. |
| FR-043-5 | **Per-workspace usage:** each workspace has live meters: `storageBytes`, `activeTransfers` (same scoped queries as F-BIL-003-3), plus 30-day `sentBytes`/`sentCount` (aggregate over the org's `Transfer` rows — no new event stream). |
| FR-043-6 | **My Files scoping:** My Files gains a workspace filter (All workspaces / each workspace the member can see — members see only their own workspace; org admins see all). Transfer rows show the workspace name in `--fg-muted`. |
| FR-043-7 | **Lifecycle:** archive (sends stop, existing transfers keep working, member goes back to General) and delete (only when no active transfers and ≤ 1 member; re-homes to General). Transitions audit-logged (`workspace.*`). |

## Acceptance criteria

```gherkin
AC-043-1: An org admin creates a workspace with a plan and a limit override
  Then it appears in the workspace list
  And a member assigned to it sends with the overridden limit, not the org plan

AC-043-2: A workspace hits its storage quota
  Then new finalizes from that workspace fail with "Workspace storage full"
  And the General workspace is unaffected

AC-043-3: A member's My Files
  Then it shows only their workspace's transfers by default
  And an org admin can switch the filter to see every workspace

AC-043-4: A workspace is archived
  Then its members send from General
  And its existing transfers are unaffected

AC-043-5: Resolution order
  Given an override on MAX_TRANSFER_SIZE only
  Then that key uses the override
  And every other key inherits from the workspace plan
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-043-1 | Override that is *looser* than the plan (larger limit) | Allowed in Phase 3 (org admin's call); documented as "override wins, in either direction" |
| EC-043-2 | Workspace with zero members | Valid — no one can send from it until assigned (shown as "empty" chip in admin) |
| EC-043-3 | Org archived with workspaces | All workspaces archived atomically; transfers keep working (org de-provisioning per F-ENT-003) |
| EC-043-4 | Two admins assign the same member to two workspaces concurrently | Last write wins + audit entry; member's session re-resolves on next request (documented, no cross-tab sync in Phase 3) |
| EC-043-5 | Workspace override sets a limit to `0` | Treated as "disabled for new sends" with a `409 WORKSPACE_DISABLED`-style message (documented; `0` = off is the only use of zero) |
| EC-043-6 | General workspace | Never renameable/deletable; its name is always "General" |

## UI notes (UI-Reference §5.6 extension — Org settings → "Workspaces")

- List: rows = name, member count, plan chip, quota meters (thin bar, used/allowed for storage + active transfers, `--fs-tiny`), status chip (`active` green / `archived` gray). **New workspace** primary → modal: name, description, plan select, overrides editor (a small key/value list with only the limits that make sense per plan — `MAX_TRANSFER_SIZE`, `MAX_ZIP_SIZE`, `RETENTION_DAYS`, `STORAGE_QUOTA`, `ACTIVE_TRANSFERS_MAX`).
- **Members screen (F-ENT-003):** each row gains a workspace select (General is the default option).
- **My Files:** workspace filter pill row under the status pills; transfer rows show workspace in the meta line.
- **Link screen:** "Sending from {workspace}" line under the "From" field, `--fs-small` `--fg-muted`, no interaction.

## Technical notes

- DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  CREATE TABLE dbo.Workspace (
      Id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Workspace PRIMARY KEY,
      OrganizationId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_WS_Org REFERENCES dbo.Organization(Id),
      Name              NVARCHAR(80) NOT NULL,
      Description       NVARCHAR(200) NULL,
      PlanId            UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_WS_Plan REFERENCES dbo.Plan(Id),
      LimitsOverrides   NVARCHAR(MAX) NULL,   -- partial LimitsRecord JSON
      Status            TINYINT NOT NULL CONSTRAINT DF_WS_Status DEFAULT 0,  -- 0 Active,1 Archived
      CreatedAtUtc      DATETIME2 NOT NULL,
      CONSTRAINT UQ_Workspace_Org_Name UNIQUE (OrganizationId, Name)
  );
  -- OrgMember: workspace scoping
  ALTER TABLE dbo.OrgMember ADD
      WorkspaceId UNIQUEIDENTIFIER NULL CONSTRAINT FK_OM_WS REFERENCES dbo.Workspace(Id);
  -- Transfer:
  ALTER TABLE dbo.Transfer ADD
      WorkspaceId UNIQUEIDENTIFIER NULL CONSTRAINT FK_T_WS REFERENCES dbo.Workspace(Id);
  CREATE INDEX IX_Transfer_WS ON dbo.Transfer (WorkspaceId, Status, CreatedAtUtc DESC);
  ```
- Endpoints (TA-4.2 extension, ADR note): `POST /api/v1/orgs/{orgId}/workspaces` (67), `GET /api/v1/orgs/{orgId}/workspaces` (68), `PATCH /api/v1/orgs/{orgId}/workspaces/{wsId}` (69), `POST /api/v1/orgs/{orgId}/workspaces/{wsId}/archive` (70), `PATCH /api/v1/orgs/{orgId}/workspaces/{wsId}/members/{userId}` (71).
- MediatR: `CreateWorkspaceCommand`, `ListWorkspacesQuery`, `UpdateWorkspaceCommand`, `ArchiveWorkspaceCommand`, `AssignWorkspaceMemberCommand`; resolution: `ResolvePlanLimitsQuery` (F-BIL-003) extended with `WorkspaceId`.
- Events (TA-5.3 extension): `workspace.created { orgId, workspaceId }`, `workspace.member_changed { workspaceId, userId }`, `workspace.archived { workspaceId }`.
- Telemetry (TA-10.2 extension): `workspace_usage { orgId, workspaceId, storageBytes, activeTransfers }` (hourly rollup is overkill — computed on read, no event needed; keep the event for audit).
- Cache: workspace + overrides in the 30 s `FlagsCache`-style cache (TA-3.4 addition: `WorkspacesCache`).

## Test plan

- Unit: override merge (partial JSON over plan limits — unknown keys rejected, `0` handling); resolution-order table (override → workspace plan → org plan → personal); membership uniqueness.
- Integration: AC-043-1…043-5; quota isolation (two workspaces, one full); archive re-homes membership; General workspace invariants (name, non-deletable).
- E2E: create org → workspace + override → assign member → send with overridden limit → My Files filter (Playwright).

## User stories

| ID | Story | File |
|---|---|---|
| US-043-01 | Split the team into workspaces | `US-043-01-create-workspace.md` |
| US-043-02 | Give a workspace its own plan and limits | `US-043-02-workspace-limits.md` |
| US-043-03 | Send as my team, not as me | `US-043-03-send-from-workspace.md` |
| US-043-04 | See each team's usage separately | `US-043-04-workspace-usage.md` |
