# F-XCT-002 — Search (Transfers)

**Priority:** P1 | **Phase:** X — Cross-cutting
**Spec source:** `02-feature-plan.md` §6 (F-XCT-002) | **Architecture:** TA-4.2, TA-3.4
**Gate:** `feature.search` (F-XCT-001) | **Milestone tasks:** T-066

---

## Description

"Find the transfer where I sent the logo files to the agency" is the question the moment My Files grows past ten rows. F-XCT-002 adds one search box to **My Files** (and later the admin transfer list) that finds transfers by **file name, recipient email, and sender name** using plain SQL full-text search first — no new infrastructure, no second storage system, and a kill switch.

**Actors:** signed-in user (searching their own transfers), admin (searching all transfers).
**Value:** discoverability of history without a new product surface; the admin's "which transfer is this complaint about?" becomes one keystroke.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-045-1 | `GET /api/v1/transfers?status=&cursor=&q=` gains a `q` parameter (1..100 chars, trimmed). When `q` is present, the list is filtered to transfers whose `SenderName`, `EmailRecipient.Address`, or `TransferFile.FileName` matches. |
| FR-045-2 | Matching uses **SQL Server full-text search first** (per plan §6): a full-text index on `TransferFile.FileName`, `EmailRecipient.Address`, and `Transfer.SenderName` (or a single `SearchText` computed column joining the three, indexed full-text). Plain `CONTAINS`/`CONTAINSTABLE` semantics; case/accents per the column's collation (`database_default` + `VARBINARY`-free — files and emails are ASCII-dominant, documented). |
| FR-045-3 | Results are ordered **by relevance** (`CONTAINSTABLE` rank) when `q` is present, else by existing `createdAtUtc DESC` + cursor. Cursor pagination (TA-4.2) is preserved: the cursor is an opaque `q|rank|id` token for search pages. |
| FR-045-4 | **Free tier:** search is available to all plans (it's an index on existing rows, no new metering). **Admin:** `GET /api/v1/admin/transfers?q=` (endpoint 21) reuses the same search predicate over *all* transfers, with the admin cursor shape. |
| FR-045-5 | **Gate:** the whole feature sits behind `feature.search` (F-XCT-001). Flag off → `q` parameter is ignored by the API (documented: no 404 on the list endpoint, so a stale SPA degrades to the unfiltered list) and the search box is not rendered. |
| FR-045-6 | **Escaping:** `q` is user input against `CONTAINS` — values are wrapped/escaped so `*`, `~`, `"` in the query are literals, not full-text operators (prevents `*` matching everything). |
| FR-045-7 | **Scoping:** a signed-in user's `q` matches only their transfers (owner-scoped query — search never crosses owners); the admin search is explicitly all-transfers and marked as such. |
| FR-045-8 | Telemetry: search is covered by existing events — the list endpoint emits no new event type; a searched list request is indistinguishable from any other list request in TA-10.2 (no closed-list drift). |

## Acceptance criteria

```gherkin
AC-045-1: A user searches their own history
  Given a transfer with a file "q4-financial-report.pdf" and a recipient a@b.com
  When the user searches "financial" in My Files
  Then the transfer appears first in the results
  And transfers without a matching file name, recipient, or sender do not appear

AC-045-2: The admin searches all transfers
  Given feature.search is true
  When an admin searches "a@b.com" in Admin → Transfers
  Then transfers from any owner sent to that address are listed
  And each row shows the owner email

AC-045-3: Full-text operators are literals
  Given a file named "report *.pdf"
  When the user searches "report *"
  Then the match is by literal text only (the * is not a wildcard)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-045-1 | `q` with no matches | Empty list, "No transfers match '…'" — not an error |
| EC-045-2 | Full-text index not yet built (fresh DB, first rows) | Rows committed after the index's initial fill are found immediately; the build window is < 1 min on seeded sizes (documented, not user-visible in practice) |
| EC-045-3 | `q` longer than 100 chars | 400 `Q_TOO_LONG`; UI trims to the max before submit |
| EC-045-4 | Diacritics ("café" vs. "cafe") | Collation-dependent exact match; documented limitation, no stemming (plan: "SQL `CONTAINS` first") |
| EC-045-5 | Feature flag flipped off while a search request is in flight | Worst case: one list response used the search predicate; the next is the plain list — no partial state |
| EC-045-6 | Same transfer matches on two fields (file + recipient) | Single result, no duplicate rows (matched by transfer id, rank = max of matching ranks) |

## UI notes

- My Files: search input above the list (UI-Reference pattern: list toolbar) with a 300 ms debounce; the cursor resets on query change; an "×" clears back to the unfiltered list.
- Empty state: "No transfers match 'query'." with a **Clear search** action.
- Admin → Transfers: same input; rows add an owner column when searched.
- Search box **absent** when `feature.search` is off (US-044-02 contract).

## Technical notes

- Index: full-text index on a `SearchText` computed/persisted column = `SenderName + ' ' + recipient addresses + ' ' + file names`, maintained in the same transactions that create transfers/re-send (no new worker). `CONTAINSTABLE` + rank for ordering; transfer id de-dup.
- Fallback rule (plan: "Azure Search if needed"): if full-text index maintenance becomes a bottleneck > 1k rps or multi-word relevance matters, add Azure Search as an optional writer — the endpoint contract (FR-045-1) is unchanged, so this is an ADR, not a feature change.
- `SearchTransfersQuery` (already in the Admin/ command list, TA-4.2) is extended with `q`; owner-scoped variant for endpoint 14.
- Escaping: `q` → escaped literal in the `CONTAINS` expression; no user-controlled operators.
- Gate: `ResolveFlag("feature.search")` on both list endpoints (single call, 30 s cache).

## Test plan

- Unit: `q` escaping (wildcards as literals), max length, trim; relevance ordering over a fixture set (file/recipient/sender matches, multi-match de-dup).
- Integration: full-text index present in Testcontainers migration (SQL 2022 supports full-text on the Developer edition — note if the local image differs: fall back to a `LIKE`-based fake for the unit layer); `?q=` happy + no-match + too-long + operator-literal; owner scoping (user A's search never returns user B's transfer); admin search crosses owners; flag off → `q` ignored.
- E2E: Playwright — create transfer with a distinctive file name → search finds it → clear search restores the list.
- Exit check (T-066): "Search finds a transfer by file name and by recipient in dev; flag off hides the box."

## User stories

| ID | Story | File |
|---|---|---|
| US-045-01 | Find a transfer by file name or recipient | `US-045-01-find-transfer.md` |
