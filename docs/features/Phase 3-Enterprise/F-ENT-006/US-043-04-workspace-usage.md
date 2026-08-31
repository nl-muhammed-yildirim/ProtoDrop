# US-043-04 — See each team's usage separately

**Feature:** F-ENT-006 — Workspaces | **Status:** pending

---

**Story:** As an org admin, I want per-workspace usage and a per-workspace view of My Files, so that I can see which team is eating the quota — and what they've sent.
**Actor:** OrgAdmin (full view); member (own workspace).
**Goal:** live meters per workspace + a My Files filter that matches reality.

## Preconditions

- Org with ≥ 1 workspace and some transfers.

## Happy path

1. Workspaces list: each row shows live meters — storage used/allowed, active transfers used/allowed (same scoped queries as F-BIL-003-3, per workspace), plus 30-day sent bytes/counts.
2. My Files: workspace filter row (All workspaces / each workspace) — members see **only their own**; org admins see all (FR-043-6).
3. Transfer rows show the workspace name in the meta line (`--fg-muted`).

## Alternative flows

- **Member, own workspace only:** the filter is absent (there's exactly one scope) — less chrome, same data.
- **Admin, "All workspaces":** rows tagged by workspace; a click on a tag filters to it.

## Acceptance criteria

```gherkin
Given two workspaces with transfers
When I open the workspaces list
Then each row's meters reflect only that workspace's data

Given I am a member
When I open My Files
Then I see only my workspace's transfers by default
And an org admin can switch to see every workspace
```

## Edge cases

- Workspace with zero transfers → meters at 0, "No sends yet" (`--fs-small`), not an error.
- A transfer's workspace was **deleted** → rows show "General" (re-homed, FR-043-7) with a tooltip "workspace archived".
- Meter staleness: computed live at read (no cache beyond 30 s plan-cache) — matches F-BIL-003-3 semantics.

## UI notes

- Meters: thin progress bar (`--accent`), used/allowed in `--fs-tiny` ("412 GB / 100 GB" in `--danger` when over, same rule as F-BIL-003-3 plan screen).
- 30-day line: "Sent 12.4 GB · 87 transfers (30 d)" — one line, no chart in Phase 3 (charts are a P3.5 decision, D-28).
- My Files: workspace filter pills under the status pills (All / each workspace); rows show the workspace in the meta line.

## Technical notes

- `ListWorkspacesQuery` returns meters from scoped aggregates over `Transfer` (`WorkspaceId`, `Status`, `TotalBytes`) — index `IX_Transfer_WS` (F-ENT-006 DDL).
- My Files (F-TRF-009 endpoint 14) gains `workspaceId` filter param; default = session's workspace for members, `All` for admins.
- No new events — usage is a read-time query (FR-043-5; events would be overkill and are documented as such).

## Links

- Feature: `ENT-006-workspaces.md` (FR-043-5/6, AC-043-3)
- Architecture: TA-3.2 (IX_Transfer_WS), F-TRF-009 (My Files extension), F-BIL-003 (meter semantics)
- Related: US-043-01 (the workspaces), US-043-03 (what feeds the numbers)
