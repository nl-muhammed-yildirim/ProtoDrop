# F-XCT-002 — Search (Transfers)

**Priority:** P1 | **Phase:** X — Cross-cutting
**Spec source:** `02-feature-plan.md` §6 (F-XCT-002) | **Architecture:** TA-4.2, TA-3.4
**Gate:** `feature.search` (F-XCT-001) | **Milestone tasks:** T-066

---

## Description

"Find the transfer where I sent the logo files to the agency" is the question the moment My Files grows past ten rows. F-XCT-002 adds one search box to **My Files** (and later the admin transfer list) that finds transfers by **file name, recipient email, and sender name** — matching file name, recipient email and sender name via a literal case-insensitive substring (D-21 decided 2026-11-21: EF string.Contains over FileItem.OriginalName / EmailRecipient.Address / Transfer.SenderName — owner-scoped; no new infrastructure, no second storage system), with a kill switch.

**Actors:** signed-in user (searching their own transfers), admin (searching all transfers).
**Value:** discoverability of history without a new product surface; the admin's "which transfer is this complaint about?" becomes one keystroke.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-045-1 | `GET /api/v1/transfers?status=&cursor=&q=` gains a `q` parameter (1..100 chars, trimmed). When `q` is present, the list is filtered to transfers whose `SenderName`, `EmailRecipient.Address`, or `TransferFile.FileName` matches. |
| FR-045-2 | Matching is a **literal case-insensitive substring** (D-21 decided 2026-11-21): EF string.Contains over `FileItem.OriginalName`, `EmailRecipient.Address` and `Transfer.SenderName`, owner-scoped — no full-text index, no `SearchText` column, no schema change. Case/accents per the column's collation (CI_AS: case-insensitive, accent-sensitive — documented limitation EC-045-4). |
| FR-045-3 | Results are ordered **by relevance** — file match > recipient match > sender match (CASE-over-EXISTS rank predicate) when `q` is present, else by existing `createdAtUtc DESC` + cursor. Cursor pagination (TA-4.2) is preserved: the cursor is an opaque base64 token of `rank|createdAtUtc:O|id:N|term` (term last). |
| FR-045-4 | **Free tier:** search is available to all plans (it's an index on existing rows, no new metering). **Admin:** `GET /api/v1/admin/transfers?q=` (endpoint 21) reuses the same search predicate over *all* transfers, with the admin cursor shape. |
| FR-045-5 | **Gate:** the whole feature sits behind `feature.search` (F-XCT-001). Flag off → `q` parameter is ignored by the API (documented: no 404 on the list endpoint, so a stale SPA degrades to the unfiltered list) and the search box is not rendered. |
| FR-045-6 | **Escaping:** `q` is a literal case-insensitive substring — there are no full-text operators to leak, so `*`, `~`, `"` in the query match literally by construction (no wrapping/escaping needed). |
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
| EC-045-2 | `q` on a fresh DB (first rows) | No index to build — the substring predicate scans existing rows directly; every committed row is found immediately (documented, not user-visible in practice). |
| EC-045-3 | `q` longer than 100 chars | 400 `Q_TOO_LONG`; UI trims to the max before submit |
| EC-045-4 | Diacritics ("café" vs. "cafe") | Collation-dependent exact match (CI_AS: case-insensitive, accent-sensitive); documented limitation, no stemming — the substring is literal by construction (D-21 option B). |
| EC-045-5 | Feature flag flipped off while a search request is in flight | Worst case: one list response used the search predicate; the next is the plain list — no partial state |
| EC-045-6 | Same transfer matches on two fields (file + recipient) | Single result, no duplicate rows (matched by transfer id, rank = max of matching ranks) |

## UI notes

- My Files: search input above the list (UI-Reference pattern: list toolbar) with a 300 ms debounce; the cursor resets on query change; an "×" clears back to the unfiltered list.
- Empty state: "No transfers match 'query'." with a **Clear search** action.
- Admin → Transfers: same input; rows add an owner column when searched.
- Search box **absent** when `feature.search` is off (US-044-02 contract).

## Technical notes

- Index: none — D-21 (decided 2026-11-21) chose literal case-insensitive substring via EF string.Contains over FileItem.OriginalName / EmailRecipient.Address / Transfer.SenderName, owner-scoped; rank file > recipient > sender via a CASE predicate, transfer id de-dup by the single grouped query (no separate index maintenance).
- Fallback rule (plan: "Azure Search if needed"): if substring matching becomes a bottleneck at scale or multi-word relevance matters, add Azure Search as an optional writer — the endpoint contract (FR-045-1) is unchanged, so this is an ADR, not a feature change.
- `SearchTransfersQuery` (already in the Admin/ command list, TA-4.2) is extended with `q`; owner-scoped variant for endpoint 14.
- Escaping: `*`, `~`, `"` are literals by construction (plain substring — there are no full-text operators to leak); q is trimmed and capped at 100 chars (Q_TOO_LONG).
- Gate: `ResolveFlag("feature.search")` on both list endpoints (single call, 30 s cache).

## Test plan

- Unit: `q` escaping (wildcards as literals), max length, trim; relevance ordering over a fixture set (file/recipient/sender matches, multi-match de-dup).
- Integration: stock SQL 2022 Testcontainers image (no Full-Text component — D-21's reason for option B); `?q=` happy + no-match + too-long + literal-wildcard (`*`, `~`, `"`); owner scoping (user A's search never returns user B's transfer); admin search crosses owners; flag off → box hidden and `?q=` does not filter (degrades to the plain list per US-044-02).
- E2E: Playwright — create transfer with a distinctive file name → search finds it → clear search restores the list.
- Exit check (T-066): "Search finds a transfer by file name and by recipient in dev; flag off hides the box."

## User stories

| ID | Story | File |
|---|---|---|
| US-045-01 | Find a transfer by file name or recipient | `US-045-01-find-transfer.md` |
