# US-047-03 — Prove the erasure (DPO report)

**Feature:** F-XCT-004 — GDPR: Data Export & Erasure Completeness | **Status:** pending

---

**Story:** As the DPO answering a regulator or a customer, I want one screen that says which accounts were deleted, what was re-homed, and whether the telemetry sweep finished — so "is that email fully gone?" never requires an investigation.
**Actor:** Admin/DPO in the admin dashboard.
**Goal:** Admin → **Erasures**: a read-only, paged list of deleted accounts with sweep status, CSV-exportable.

## Preconditions

- At least one account deletion (US-008-06) — each deletion creates the row the list reads.
- `f-gdpr-sweep` exists (US-047-02); its marks are the `swept` state.

## Happy path

1. DPO opens Admin → **Erasures** → `GET /api/v1/admin/erasures?cursor=`.
2. Each row: masked email (`d****@x.com` — full email is PII, TA-9.4), `deletedAtUtc`, **re-homed transfer count** (the "files survive" number from US-008-06), sweep state: `pending` or `swept` + `sweptAtUtc`.
3. Cursor pagination for long lists; **Export CSV** downloads the current filter (all rows).
4. A `pending` row older than 2 days is highlighted (the sweep is lagging — operator action).

## Alternative flows

- **Sweep down:** rows accumulate as `pending`; no error anywhere — the screen is the alarm (highlighted, not a toast).
- **Regulator asks for the raw data:** CSV + the underlying table are the answer; the list is not the legal record, it's the *pointer*.

## Acceptance criteria

```gherkin
Given I deleted two accounts
When I open Admin → Erasures
Then both rows appear with masked emails, delete times, and re-homed transfer counts
And after the next sweep the rows show swept with the sweep time

Given the list is longer than one page
When I export CSV
Then the CSV contains every row, not just the visible page
```

## Edge cases

- Masking: non-DPO admins see `d****@x.com`; the full email is revealed only behind the same admin role that can see all user emails (F-TRF-011-3 convention — no new role invented here).
- A deletion that re-homed zero transfers shows `0` — that's the "no files survived" case and is *not* an error.
- The list is read-only: no cancel-delete from here (the operator's 30-day soft-delete undo, US-008-06, stays in the user-management screen).

## UI notes

- Admin → Erasures: table + cursor pagination + **Export CSV**; `pending` > 2 days → amber row highlight; helper "Sweep runs nightly; pending rows clear on the next run."

## Technical notes

- Reads the same erasures table `f-gdpr-sweep` marks; no new table (the `account_deleted`-driven row from US-008-06 + `SweptAtUtc` column added by T-068).
- CSV export: same streaming pattern as the org audit CSV (F-ENT-003) — one precedent, not a new mechanism.
- Masking helper shared with the user-list screen (TA-9.4 PII rule).

## Links

- Feature: `XCT-004-gdpr-export-erasure.md` (FR-047-6)
- Plan AC: AC-047-2 (verification surface)
- Related: US-008-06 (deletion + re-homing), US-011-03 (user list, where full emails live), US-047-02 (the sweep it reports on)
- Architecture: TA-9.4
- Milestone: T-068
