# US-027-01 — Review every submission in one place

**Feature:** F-COL-003 — Submissions Dashboard | **Status:** pending

---

**Story:** As a collector, I want every submission for my collection in one list — person, files, size, status, when — so that I never have to open individual messages to see who's sent what.
**Actor:** collector (owner).
**Goal:** `/collections/{id}` renders all entries, newest first, with the exact state of each.

## Preconditions

- A collection with ≥ 0 entries; owner opens it.

## Happy path

1. Dashboard lists each entry: sender name (or "Anonymous"), email (if given), file count, total size, status chip, submit time.
2. Newest first; cursor pagination (TA-4.1.4).
3. Count line above the list: "12 submissions".

## Alternative flows

- **Empty collection**: icon + "No submissions yet." + **Copy link** shortcut.
- **Closed collection**: entries still listed (history), with the closed chip.

## Acceptance criteria

```gherkin
Given I have 3 submissions
When I open the dashboard
Then all 3 entries are listed with person, size, status, and time
And the newest is first
```

## Edge cases

- Anonymous entries render "Anonymous" (no email) — not an error state.
- Large collections (200 entries) page by cursor (FR-027-5).

## UI notes

- Admin-table pattern (UI-Reference §4.7) but lighter: sticky header, rows with status chips, row actions ghost.
- Status chips: `Received` (gray) / `Accepted` (blue) / `Declined` (amber) / `Done` (green) — text always present (F-TRF-016).

## Technical notes

- `GET /collections/{id}/entries?q=&status=&cursor=` (endpoint 30, TA-4.2 extension).
- `ListEntriesQuery` (MediatR, `Collections/` area).

## Links

- Feature: `COL-003-submissions-dashboard.md` (FR-027-1, FR-027-5, AC-027-1)
- Related: F-COL-002 (entries), F-COL-005 (status)
