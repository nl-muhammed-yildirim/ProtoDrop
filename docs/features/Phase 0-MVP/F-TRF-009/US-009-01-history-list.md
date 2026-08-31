# US-009-01 — Review my transfers

**Feature:** F-TRF-009 — My Files (History) | **Status:** pending

---

**Story:** As a signed-in user, I want a list of all my transfers with their key facts, so that "what did I send, and is it still alive?" is answered in one look.
**Actor:** Signed-in user on `/files`.
**Goal:** A scannable, paginated list with the numbers that matter.

## Preconditions

- Signed in; `/files` route (SPA, TA-4.3).

## Happy path

1. The list shows the user's transfers, most recent first.
2. Each row: name (first file or "Transfer"), file count, total size, recipient count, status chip, expiry countdown, download count ("87/100").
3. Page 1 = 25 rows (cursor pagination, TA-4.1.4); "Next / Prev" navigation.
4. **New transfer** primary button → `/`.

## Alternative flows

- **Empty:** icon + "No transfers yet." + CTA button "Send something" → `/` (FR-009-5).
- **>25 transfers:** cursor paging; `nextCursor` in state (UI-Reference §4.7).

## Acceptance criteria

```gherkin
Given I have 30 transfers
When I open My Files
Then the first page shows 25 rows
And the oldest visible row is my 25th most recent transfer
And a Next control is available

Given I have a transfer expiring in 2 days
When the list renders
Then its row shows a live countdown ("expires in 2 days")

Given I have zero transfers
When I open My Files
Then the empty state shows with a CTA to the upload surface
```

## Edge cases

- Countdowns tick from `ExpiresAtUtc` client-side (recomputed on render; no per-second server calls).
- `DownloadLimit` rows: chip "Download limit reached" (amber) + countdown replaced by the cap note.
- Deleted rows are gone from the list (hard for the user; the jobs finish physically).

## UI notes (UI-Reference §5.4)

- Row: 44 px, name truncated (`title`), meta in `--fs-small` `--fg-muted`, status chips with text (not color-only, F-TRF-016).
- Sort fixed: `CreatedAtUtc DESC` (no user re-sort in MVP).

## Technical notes

- `ListMyTransfersQuery` (endpoint 14): owner-scoped, `ORDER BY CreatedAtUtc DESC`, `?cursor=&limit=25` (TA-4.1.4).
- Recipient count: subquery `COUNT(*) EmailRecipient` (cheap at MVP scale) or cached column (implementation note, not a requirement).

## Links

- Feature: `TRF-009-my-files.md` (FR-009-1, FR-009-2, FR-009-5)
- Plan AC: AC-009-1
- Architecture: TA-4.2#14, TA-3.3
- Milestone: T-022
