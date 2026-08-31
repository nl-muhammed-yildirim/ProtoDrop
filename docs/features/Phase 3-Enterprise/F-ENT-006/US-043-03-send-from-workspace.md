# US-043-03 — Send as my team, not as me

**Feature:** F-ENT-006 — Workspaces | **Status:** pending

---

**Story:** As a member, I want my work transfers to belong to my team, so that when I leave, the files still have an owner.
**Actor:** Org member.
**Goal:** the send flow just works — "Sending from {workspace}" is a line, not a decision.

## Preconditions

- Member of the org, assigned to a workspace (US-043-01); org on Business.

## Happy path

1. I start a transfer; the link screen shows "Sending from **{workspace}**" under the "From" line (read-only — my membership decides, FR-043-3).
2. Send → the transfer stores `WorkspaceId`; its limits and quota were the workspace's (US-043-02).
3. My Files shows it under that workspace (US-043-04 filter); if I'm re-assigned later, the transfer **keeps** its workspace (history, EC from US-043-01).

## Alternative flows

- **Personal send:** "Personal send instead" ghost link (US-040-04) → no workspace line, personal plan/quota.
- **Workspace archived while I'm in it:** the line becomes "Sending from **General**" automatically (assignment falls back, FR-043-7).

## Acceptance criteria

```gherkin
Given I am in the Marketing workspace
When I send a transfer
Then it is attributed to that workspace
And its limits were Marketing's, not mine

Given my workspace is archived
When I send a transfer
Then it is attributed to General
```

## Edge cases

- Member with **no** workspace assignment (impossible — General is the fallback) — defensive: resolve to General, log `workspace_missing` telemetry.
- Guest (signed out) opening the link screen → never org-scoped (US-040-04).
- Mid-session re-assignment by an admin: my next send uses the new workspace; in-flight drafts keep the old attribution (documented, no cross-tab sync).

## UI notes

- "Sending from **{workspace}**" — `--fs-small`, `--fg-muted`, workspace name bolded; "Personal send instead" ghost link below it, only for users with a primary org.
- No modal, no explanation — the name does the work.

## Technical notes

- `Transfer.WorkspaceId` set at send (TA-4.2 #3 extension: payload gains `workspaceId`); draft creation resolves it from the session (`OrgMember.WorkspaceId` → General fallback).
- Resolution is server-side; the client line is decorative (a lying client can't change attribution).
- Telemetry: existing send events gain `attribution: org|personal` (US-040-04) — `workspaceId` is PII-safe (org-scoped id, not an email).

## Links

- Feature: `ENT-006-workspaces.md` (FR-043-3, AC-043-4)
- Architecture: TA-4.2 #3, TA-3.2 (Transfer.WorkspaceId)
- Related: US-040-04 (plan attribution), US-043-04 (seeing it in My Files)
