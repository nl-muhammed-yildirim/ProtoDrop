# US-011-02 — Find and act on any transfer

**Feature:** F-TRF-011 — Admin Dashboard | **Status:** pending

---

**Story:** As the operator, I want to search any transfer by linkId, recipient email, status, size, or date range — and force-delete it — so that support tickets and abuse reports end in an action, not a shrug.
**Actor:** Operator (read) / SuperAdmin (delete).
**Goal:** From "user says link 3F9K2A7X is leaking" to "found + killed" in under a minute.

## Preconditions

- Admin signed in; Transfers screen accessible.

## Happy path

1. Filter bar: search (exact `linkId`, or recipient email substring), status select, size range, date range (created).
2. Results: table (UI-Reference §4.7) — linkId, owner email, status, files, size, created, downloads.
3. Row actions: **View** (recipient page, new tab), **Force-delete** (confirm modal: "Force-delete transfer {linkId}? 3 files, 1.2 GB." → same semantics as user delete, reason `admin`).
4. Every mutation audit-logged (US-011-04).

## Alternative flows

- **Search by recipient email:** finds transfers where that address is an `EmailRecipient` (the "who got a hold of my file?" query).
- **No results:** empty table row "No transfers match." (not an error).
- **Operator role:** search + view, force-delete disabled with tooltip (FR-011-1).

## Acceptance criteria

```gherkin
Given I search for linkId "3f9k2a7x"
When the search runs
Then exactly that transfer is returned (exact match, case-insensitive)

Given I search for recipient email "ada@x.com"
When the search runs
Then all transfers that include that address are listed

Given I force-delete a transfer
When the confirm is pressed
Then transfer.deleted is emitted with reason admin
And the audit log has a row with my email as actor
```

## Edge cases

- Force-delete while a recipient downloads: same semantics as user delete (EC-011-2, EC-009-1).
- Cursor pagination on results (TA-4.1.4).

## UI notes

- Table per 4.7; filter bar: search input + status select + date range; actions as ghost buttons; delete confirm modal.

## Technical notes

- `SearchTransfersQuery` (endpoint 21 GET): predicates per FR-011-3; `AdminDeleteTransferCommand` (endpoint 21 DELETE) → `transfer.deleted` reason `admin`.
- Email search: `EXISTS (SELECT 1 FROM EmailRecipient WHERE TransferId=t.Id AND Address LIKE @q)`.

## Links

- Feature: `TRF-011-admin.md` (FR-011-3, FR-011-4)
- Architecture: TA-4.2#21
- Related: US-009-04 (same delete command, different reason)
- Milestone: T-024
