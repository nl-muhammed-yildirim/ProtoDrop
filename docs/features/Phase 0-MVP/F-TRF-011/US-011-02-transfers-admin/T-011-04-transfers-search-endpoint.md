# T-011-04 — SearchTransfersQuery + endpoint 21 GET: admin transfer search (FR-011-3)

**Story:** US-011-02 | **Spec:** FR-011-3, AC/US-011-02 gherkin (search); TA-4.2#21, TA-4.1.4 | **Size:** M
**Depends on:** T-011-01 (admin gate)

---

## Context to read (only these)

- `US-011-02-transfers-admin.md` → happy path 1–2 + Alternative flows + Technical notes (SQL mapping)
- `../F-TRF-011-admin.md` → FR-011-3 + Technical notes (Search mapping line)
- `../../F-TRF-009/US-009-01-history-list/T-009-05-my-files-ui.md` → cursor paging usage reference

## Instructions

1. Add **`SearchTransfersQuery`** (MediatR, `wa.application/UseCases/Admin/`) + endpoint 21 GET `GET /api/v1/admin/transfers?q=&status=&cursor=` (TA-4.2#21).
2. Predicates per FR-011-3 (Technical notes mapping): exact **`linkId`** (`Transfer.LinkId`, case-insensitive), recipient email substring via `EXISTS (SELECT 1 FROM EmailRecipient WHERE TransferId=t.Id AND Address LIKE @q)`, status, size range on `TotalBytes`, date range on `CreatedAtUtc`.
   - DECISION line: TA-4.2 shows the query string as `?q=&status=&cursor=`; FR-011-3 requires size + date ranges. Add optional params `minBytes/maxBytes/from/to` — same endpoint, no renumbering.
3. **Cursor pagination** (TA-4.1.4): `?cursor={base64(created_at:id)}&limit=25`, response includes `nextCursor` (null when done); order `CreatedAtUtc DESC`.
4. Rows carry everything the screen needs (US-011-02 happy path 2 table columns): linkId, owner email, status, files count, size, created, downloads.

## Exit check

- [ ] US-011-02 AC: search `linkId` "3f9k2a7x" → exactly that transfer (exact match, case-insensitive)
- [ ] US-011-02 AC: search recipient email "ada@x.com" → all transfers that include the address (`EmailRecipient` EXISTS)
- [ ] Status / size range / date-range filters compose with `q`; no results → empty list (not an error — US-011-02 Alternative flows)
- [ ] 30+ matches → page 1 = 25 rows + working `nextCursor` walk (TA-4.1.4, FR-011-3 cursor pagination)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS).
Task T-011-04 — admin transfers search (milestone task T-024, TA-4.2#21 GET).
Read first (only): US-011-02-transfers-admin.md (happy path + Technical notes), F-TRF-011-admin.md (FR-011-3 + Search mapping line), TA-4.1.4 in docs/03-technical-architecture.md.
Do exactly:
1. SearchTransfersQuery + GET /api/v1/admin/transfers?q=&status=&cursor= (admin policy from T-011-01).
2. Predicates per FR-011-3: exact linkId (case-insensitive), recipient email substring via EmailRecipient EXISTS, status, size range on TotalBytes (new optional params minBytes/maxBytes — DECISION noted in the task doc), date range on CreatedAtUtc (from/to).
3. Cursor pagination per TA-4.1.4 (limit 25 default, nextCursor null when done); rows: linkId, owner email, status, files count, size, created, downloads.
Done when: both US-011-02 search gherkin cases pass — exact linkId returns exactly one row; recipient email substring returns all matching transfers — and paging walks 30+ results with limit=25 pages.
Constraints: unit-test the predicate builder (spec Test plan); GETs write no audit rows (T-011-02 scope).
```