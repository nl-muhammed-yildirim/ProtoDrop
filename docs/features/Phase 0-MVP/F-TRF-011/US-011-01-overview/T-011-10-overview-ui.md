# T-011-10 — Admin shell + Overview screen: stat cards, recent transfers (FR-011-2, FR-011-6)

**Story:** US-011-01 | **Spec:** FR-011-2 (Overview), FR-011-6; AC-011-1 | UI-Reference §5.6 / §4.7 | **Size:** M
**Depends on:** T-011-03 (endpoint 20), T-011-01 (role resolved from session)

---

## Context to read (only these)

- `US-011-01-overview.md` → Happy path, Alternative flows, AC gherkin, UI notes
- `../F-TRF-011-admin.md` → FR-011-2 / FR-011-6 + UI notes (left nav, role badge)
- `docs/UI-Reference.md` §5.6 (Admin), §4.7 (table component); existing `src/wa.web/src/features/<name>/` pattern + `App.tsx` route registration

## Instructions

1. Create **`src/wa.web/src/features/admin/`** (same pattern as existing `features/<name>/`) with the admin layout: left nav **Overview / Transfers / Users / Flags / Suppressions** (UI-Reference §5.6) + role badge top right ("Operator" / "SuperAdmin") from the session resolved by T-011-01.
2. Register `/admin/*` routes in `src/wa.web/src/App.tsx` using the existing react-router pattern; guard on the admin gate (404/redirect for non-admins).
3. **Overview** (`/admin/overview`) from endpoint 20: four stat cards — active transfers, storage used, new users today, emails sent vs failed (7 d) — grid 2×2 on narrow; values `--fs-h1`, labels `--fs-small` `--fg-muted`.
4. Show **"Updated {time}"** under the cards — staleness stated on the screen (FR-011-6).
5. Recent transfers table: last 20 by `CreatedAtUtc`, read-only §4.7 table, "View all" → Transfers screen. Email failures > 0 → failed count in `--danger`; high expiry job lag (TA-10.2 metric) → amber tint + tooltip (US-011-01 Alternative flows).

## Exit check

- [ ] AC-011-1: Operator opens Overview → cards show live counts within 5-min staleness; the "Updated {time}" line is visible (US-011-01 gherkin)
- [ ] FR-011-6: staleness stated on the screen ("Updated {time}")
- [ ] Left nav has all five items and the role badge matches the session role (T-011-01)
- [ ] T-024 exit E2E passes: admin login → Overview renders (F-TRF-011 Test plan, manual pass)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10 API + React in src/wa.web).
Task T-011-10 — admin shell + Overview screen (milestone task T-024; UI-Reference §5.6).
Read first (only): US-011-01 story, F-TRF-011-admin.md (FR-011-2/FR-011-6 + UI notes), docs/UI-Reference.md §5.6 and §4.7, an existing features/<name>/ folder + App.tsx for the pattern.
Do exactly:
1. features/admin/ layout: left nav Overview/Transfers/Users/Flags/Suppressions; role badge (Operator/SuperAdmin) from session.
2. /admin/* routes in App.tsx (existing react-router pattern), guarded on the admin gate.
3. Overview screen: four stat cards from GET /api/v1/admin/overview (2×2 grid narrow; --fs-h1 values, --fs-small --fg-muted labels), "Updated {time}" line (FR-011-6), recent transfers table (last 20 by CreatedAtUtc, read-only, §4.7, "View all" → Transfers).
4. Failed-email count in --danger; expiry-lag amber tint + tooltip when the TA-10.2 metric is high (US-011-01 alternative flows).
Done when: AC-011-1 holds — cards show counts within 5-min staleness and "Updated {time}" is visible — FR-011-6's staleness note renders, nav + role badge work, and admin login → Overview renders (T-024 exit E2E).
Constraints: no new design tokens beyond §5.6/§4.7; read-only screen for both roles; data only from endpoint 20.
```