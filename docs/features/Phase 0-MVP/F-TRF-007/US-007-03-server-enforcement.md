# US-007-03 — Limits are enforced even if the client lies

**Feature:** F-TRF-007 — Free-Tier Limits | **Status:** pending

---

**Story:** As the operator, I want every limit enforced server-side at the money moments, so that a bug (or a determined user) calling the API directly can't sneak past the free tier.
**Actor:** Operator (rule owner); any API caller (the "liar").
**Goal:** The client-side check is UX; the server check is the law.

## Preconditions

- The API is callable without the UI (e.g. `curl` against `api.protodrop.com`).

## Happy path

1. `POST /transfers/draft` with `files[].sizeBytes` totaling 5.2 GB (Free) → `TRANSFER_SIZE_EXCEEDED` (Problem+JSON, TA-4.1.3).
2. `POST .../send` with 21 addresses → cap error naming `MAX_EMAILS`.
3. Finalize of a 6th GB after 5 GB already active → `STORAGE_QUOTA_EXCEEDED`.
4. Each rejection names the value that tripped it (`details` carries the numbers).

## Alternative flows

- **Client lied about sizes** (metadata-only draft!): finalize verifies each blob's real size against the declared `sizeBytes` (±0 tolerance); mismatch → `VALIDATION` with the file name (the blob `Properties.Length` is ground truth).
- **Flag override:** limits change without deploy (US-007-04) — the "law" itself is data.

## Acceptance criteria

```gherkin
Given a raw API call creating a 5.2 GB draft on the Free plan
When the draft is created
Then the response is 422 TRANSFER_SIZE_EXCEEDED
And the details contain both numbers (5.2 GB, 5 GB limit)

Given the client reports a file as 100 MB but uploads 300 MB
When finalize runs
Then the mismatch is caught against the real blob size
And the response names the file
```

## Edge cases

- Sizes are bytes (1024-based) end-to-end; no float rounding (BIGINT).
- The server check is idempotent and cheap (metadata reads); no re-upload required.

## UI notes

- The UI just renders the Problem+JSON `title`/`details` (TA-4.1.3) — no per-code UI branches in MVP beyond the known codes.

## Technical notes

- Enforcement map (feature file FR-007-2): `CreateDraftCommand` (transfer size, single file), `FinalizeTransferCommand` (quota, active transfers, size truth), `SendTransferCommand` (emails cap, retention assignment).
- Closed error-code list (TA-4.1.3): `TRANSFER_SIZE_EXCEEDED`, `STORAGE_QUOTA_EXCEEDED`, `VALIDATION`.

## Links

- Feature: `TRF-007-limits.md` (FR-007-2)
- Plan AC: AC-007-1
- Architecture: TA-4.1.3, TA-3.4
- Milestone: T-009, T-020
