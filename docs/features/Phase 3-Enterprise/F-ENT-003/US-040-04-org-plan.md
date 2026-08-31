# US-040-04 — Apply the org plan to my team's transfers

**Feature:** F-ENT-003 — Organization Admin & Audit | **Status:** pending

---

**Story:** As a member, I want the organization's Business plan to apply to my work transfers, so that my personal free limits don't get in the way of a 100 GB render.
**Actor:** Org member (org-scoped sends).
**Goal:** one consistent rule — org-attributed sends get org-plan limits; personal sends keep personal limits.

## Preconditions

- Org on Business (US-040-01); I'm a member with a workspace (F-ENT-006).

## Happy path

1. I sign in (local or SSO) and start a transfer; the link screen shows "Sending from {workspace}" (F-ENT-006).
2. Limits at finalize/send resolve from the **org plan** (via the workspace, F-ENT-006 resolution order), not my personal plan.
3. My storage and active-transfer quota are the workspace's, not my personal 5 GB (F-ENT-006-4).
4. "On behalf of org" is automatic for members — no per-file choice in Phase 3 (F-ENT-006-3 simplification).

## Alternative flows

- **Personal send:** a signed-in user with a primary org can mark a send as personal (link-screen toggle, default on when org-scoped) → personal plan, personal quota, no workspace line.
- **Guest with an org link:** guests are never org-scoped (no session → no attribution).

## Acceptance criteria

```gherkin
Given I am a member of an org on Business
When I send a 100 GB transfer (allowed by the org plan)
Then it succeeds where my personal Free plan would have rejected it
And the transfer is attributed to my workspace

Given I mark a send as personal
Then my personal plan and quota apply
And the workspace line is not shown
```

## Edge cases

- Mid-flight plan change (org downgraded by the buyer): active transfers finish their life (F-BIL-003-2 rule at org scope); new sends use the new plan.
- Workspace override (F-ENT-006) narrows the org plan for that team — resolution order is workspace → org → personal (US-043-02).
- Member in two orgs (EC-040-1): the attribution follows the `PrimaryOrgId` unless the workspace picker names the other org (Phase 3: primary only — documented).

## UI notes

- Link screen: "Sending from **{workspace}**" under the "From" line, `--fs-small` `--fg-muted`; a "Personal send instead" ghost link below it (only for users with a primary org).
- Plan screen (F-BIL-003) shows **two** blocks for org members: "Your personal plan" and "Your organization plan (applies to work transfers)" — honest about which is which.

## Technical notes

- Attribution: `Transfer.WorkspaceId` (F-ENT-006 DDL) is set at send from the session; `OwnerAppUserId` stays the person (history + My Files unchanged).
- `ResolvePlanLimitsQuery` (F-BIL-003) extended with `workspaceId` — the **only** change; no feature reads the org directly.
- Telemetry: existing `upload_started`/`transfer.created` gain `attribution: org|personal` (PII-safe enum) — no new event names.

## Links

- Feature: `ENT-003-org-admin.md` (FR-040-8, AC-040-5)
- Architecture: TA-3.4 (limits resolution), F-BIL-003 (middleware)
- Related: US-043-02 (workspace overrides refine this), US-040-02 (membership is what makes it apply)
