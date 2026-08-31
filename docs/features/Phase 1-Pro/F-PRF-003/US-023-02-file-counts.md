# US-023-02 — Know which files mattered

**Feature:** F-PRF-003 — Download Analytics | **Status:** pending

---

**Story:** As a Pro user, I want per-file download counts and a "top downloads" list, so that I know which files recipients actually grabbed.
**Actor:** Pro/Business user.
**Goal:** per-file counts (incl. "download-all" as its own row) + a top-5 "Top downloads" list on the My Files detail.

## Preconditions

- User on Pro/Business; transfer with files.

## Happy path

1. Detail page shows a per-file table: name, count.
2. "download-all" (zip) is its own row, separate from single files.
3. "Top downloads" list: top 5 files by count, tie-broken by recency.

## Alternative flows

- **Single-file transfer**: the per-file table has one row; "download-all" absent (no zip for 1 file, F-TRF-004).
- **No downloads**: counts at zero / empty state (EC-023-1).

## Acceptance criteria

```gherkin
Given a transfer with 3 files
When the detail page loads
Then each file shows its download count
And "download-all" is a separate entry

Given a "Top downloads" list
When rendered
Then it ranks by count, tie-break recency, max 5 rows
```

## Edge cases

- Tie on count → most recent activity wins (deterministic, unit-tested).
- "download-all" never ranks above the files it bundles (it's a row, not a file) — listed separately.

## UI notes

- Table rows `--fs-small`; top-5 as a ranked list with counts in `--fg-muted`.

## Technical notes

- Per-file: `GROUP BY FileId` (NULL = download-all) over `DownloadEvent`.
- Top-5 cut + tie-break in the query (unit-tested formatter).

## Links

- Feature: `PRF-003-download-analytics.md` (FR-023-2, FR-023-3, AC-023-5)
- Architecture: TA-3.2
- Related: F-TRF-004 (zip), F-TRF-009 (My Files detail)
