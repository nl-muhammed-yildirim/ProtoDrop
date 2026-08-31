# US-043-01 — Split the team into workspaces

**Feature:** F-ENT-006 — Workspaces | **Status:** pending

---

**Story:** As an org admin, I want to group my team into named workspaces, so that Marketing, Legal, and Engineering each have their own space — and their own budget.
**Actor:** OrgAdmin.
**Goal:** one modal to create a workspace; one select per member to assign it.

## Preconditions

- Org exists (F-ENT-003); members invited (US-040-02).

## Happy path

1. Org settings → **Workspaces** → **New workspace** → modal: name (unique per org), optional description, plan (defaults to the org's plan — US-043-02), no overrides yet.
2. Create → row appears with its meters at zero; the **General** workspace (created with the org, US-040-01) is always there.
3. Members screen: each member row has a workspace select → I move people in (exactly one workspace per member; General is the default, FR-043-2).

## Alternative flows

- **Name clash:** `409 WORKSPACE_NAME_TAKEN` with the existing workspace's name shown.
- **Archive:** confirm modal → members fall back to General, existing transfers keep working (FR-043-7); the workspace row is grayed, not deleted.
- **Delete:** only when ≤ 1 member and no active transfers; re-homes to General.

## Acceptance criteria

```gherkin
Given I create a workspace "Marketing"
Then it appears in the list with a zero-usage state
And I can assign a member to it

Given a member is in exactly one workspace
When I assign them to a second workspace
Then they leave the first (General if unassigned elsewhere)
```

## Edge cases

- General workspace: name fixed, not renamable/deletable (EC-043-6).
- Assigning a member who has active transfers in another workspace → the transfers keep their `WorkspaceId` (history), only future sends change (documented).
- Two admins assigning the same member concurrently → last write wins, both audited (EC-043-4).

## UI notes

- Workspaces list: name, member count, plan chip, storage + active-transfer meters (thin bars, `--fs-tiny`), status chip (`active`/`archived`).
- Members screen (F-ENT-003): workspace select in the row (General first, alphabetical after).
- Empty state for a workspace: "No members yet — assign someone from the Members screen."

## Technical notes

- `CreateWorkspaceCommand`; endpoint 67; `Workspace` DDL (F-ENT-006).
- Membership: `OrgMember.WorkspaceId` update (one row — uniqueness enforced, no add/remove list).
- Audit: `workspace.created`, `workspace.member_changed` (F-ENT-003 table).
- Cache: 30 s `WorkspacesCache` (TA-3.4 pattern) — role/membership changes visible in ≤ 30 s.

## Links

- Feature: `ENT-006-workspaces.md` (FR-043-1/2, AC-043-4)
- Architecture: TA-3.2 (Workspace, OrgMember.WorkspaceId), TA-4.2 #67
- Related: US-043-02 (limits on this workspace), US-040-02 (the member rows), US-040-01 (General workspace birth)
