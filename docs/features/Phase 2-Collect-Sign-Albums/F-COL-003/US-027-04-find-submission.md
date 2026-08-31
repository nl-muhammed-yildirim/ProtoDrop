# US-027-04 — Find a specific person's submission

**Feature:** F-COL-003 — Submissions Dashboard | **Status:** pending

---

**Story:** As a collector with many submissions, I want to search by name or email, so that I can find one specific person's files in a large collection.
**Actor:** collector (owner).
**Goal:** filter bar — search (name partial / email partial, case-insensitive) + status filter — over the entries list.

## Preconditions

- A collection with ≥ 1 entry.

## Happy path

1. Type "maria" → only entries whose name or email contains "maria" (case-insensitive).
2. Combine with a status filter (All / Received / Accepted / Declined / Done).
3. Results page by cursor (TA-4.1.4).

## Alternative flows

- **Exact email match first**: an email that matches exactly sorts before partial matches (deterministic order, unit-tested).
- **No results**: "No submissions match 'maria'." (not an error).

## Acceptance criteria

```gherkin
Given I have submissions from "Maria K." and "John D."
When I search "maria"
Then only Maria's entries appear

When I search an exact email
Then that person's entries appear (first, before partial matches)
```

## Edge cases

- Search is on the stored values only (no full-text index in MVP — `CONTAINS`/`LIKE` on the column; F-XCT-002 pattern).
- Empty search + no status filter = the full list (default view).

## UI notes

- Filter bar above the table (4.7): search input + status `<select>`; applied live (no submit).

## Technical notes

- `q` parameter mapped to `WHERE SenderName LIKE '%..%' OR SenderEmail LIKE '%..%'` (SQL-escaped `%`/`_`).
- Deterministic sort: exact email > name match > recency (unit-tested).

## Links

- Feature: `COL-003-submissions-dashboard.md` (FR-027-4, AC-027-3)
- Architecture: TA-4.1.4
- Related: F-XCT-002 (search policy)
