# US-043-02 — Give a workspace its own plan and limits

**Feature:** F-ENT-006 — Workspaces | **Status:** pending

---

**Story:** As an org admin, I want each workspace to have its own plan and limit overrides, so that the render team gets 100 GB and the legal team gets 50 MB — without two invoices.
**Actor:** OrgAdmin.
**Goal:** one override editor; the resolution order does the rest.

## Preconditions

- Workspace exists (US-043-01); org on Business.

## Happy path

1. Workspace row → **Edit** → plan select (defaults to org plan) + overrides editor (only the keys that make sense: `MAX_TRANSFER_SIZE`, `MAX_ZIP_SIZE`, `RETENTION_DAYS`, `STORAGE_QUOTA`, `ACTIVE_TRANSFERS_MAX`).
2. Save → `LimitsOverrides` (partial JSON — only keys I set override; unset = inherit, FR-043-1).
3. A member of that workspace now sends with those limits; their **own** plan and the org's other workspaces are untouched.

## Alternative flows

- **Override looser than the plan** (larger): allowed in Phase 3, EC-043-1 — helper text: "Overrides win in both directions."
- **Override to 0:** means "disabled for new sends" (EC-043-5), shown as a "sends disabled" chip.
- **Plan change on the workspace:** existing transfers finish their life (F-BIL-003-2 rule); new sends use the new limits.

## Acceptance criteria

```gherkin
Given a workspace overrides MAX_TRANSFER_SIZE to 100 GB
When a member sends a 60 GB transfer from it
Then it succeeds (their personal plan would reject it)
And a member in the General workspace is unaffected

Given an override sets STORAGE_QUOTA to 0
Then new sends from that workspace get "workspace disabled"
And its existing transfers keep working
```

## Edge cases

- Unknown key in the override JSON → rejected at save (schema is the closed LimitsRecord set, FR-043-1).
- Override applies **only** to the named key — every other limit inherits from the workspace plan, then the org plan (AC-043-5).
- Two overrides that conflict by construction (e.g. `MAX_TRANSFER_SIZE` > `STORAGE_QUOTA` with 1 active allowed) → allowed (admin's call), no validation beyond type/range.

## UI notes

- Editor: a small key/value list with human labels ("Max transfer size", "Storage quota") and free-text numeric inputs (GB / days); values inherit by default (a gray "inherited: {value}" under each).
- After save, the workspace row's plan chip shows the override marker: "Business · 2 overrides".

## Technical notes

- `UpdateWorkspaceCommand`; endpoint 69.
- Merge in `ResolvePlanLimitsQuery` (F-BIL-003 extension, US-040-04): `override → workspace.Plan.LimitsJson → org plan → personal` — **one** code path, no feature reads overrides directly (FR-043-4 rule).
- Audit: `workspace.*` actions with the diff in `detailsJson` (PII-safe: limits only).

## Links

- Feature: `ENT-006-workspaces.md` (FR-043-1/4, AC-043-1/5)
- Architecture: TA-3.4 (LimitsRecord), F-BIL-003 (resolution middleware)
- Related: US-040-04 (what the org plan is), US-043-03 (sends actually use it)
