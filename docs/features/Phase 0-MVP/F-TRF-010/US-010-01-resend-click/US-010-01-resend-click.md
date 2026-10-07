# US-010-01 — Re-send an expired transfer in one click

**Feature:** F-TRF-010 — Re-send Transfer | **Status:** pending

---

**Story:** As a user whose link expired, I want to re-send the same transfer in one click, so that a new working link exists without re-uploading 4 GB.
**Actor:** Signed-in owner of an expired (within grace) transfer.
**Goal:** New link, fresh expiry, fresh download count — same files, zero data copy.

## Preconditions

- The original transfer exists (status 1, 2, or 3) and its blobs are not physically deleted.

## Happy path

1. User presses **Re-send** (My Files, US-009-03) → pre-filled link screen.
2. `ResendTransferCommand`: new `Transfer (Status=0)`, new `FileItem` rows → **same `BlobRefId`s** with `RefCount++` each; original emails/password/note pre-filled.
3. User presses **Send transfer** → new link minted, `ExpiresAtUtc = now + RETENTION_DAYS`, `DownloadsCount=0`, `transfer.created` emitted.
4. Original transfer: `SupersededBy = newTransferId`; still downloadable if it was active.
5. Storage: **no extra bytes** (the meter is unchanged).

## Alternative flows

- **Blobs already physically deleted** (grace passed, refcount 0): `FILES_GONE` → "Files were deleted — upload again." + **Upload**; the half-created draft is rolled back.
- **Re-send of an active transfer:** fully allowed — two live links, same files (use case: "I want a link with a fresh 100 downloads left").

## Acceptance criteria

```gherkin
Given an expired transfer whose grace window is still open
When I re-send it
Then a new transfer exists with a new linkId
And it shares the original's BlobRefs (RefCount incremented, no new blob bytes)
And the original is still intact

Given the transfer's blobs were physically deleted
When I re-send it
Then I see "Files were deleted — upload again."
And no half-created transfer remains (the draft is cleaned up)
```

## Edge cases

- New linkId = fresh 8-char Crockford (F-TRF-002) — no link reuse across re-sends.
- The re-send draft pre-fill is **editable** before send (US-010-02).
- Concurrency: two re-send clicks → idempotency key (TA-4.1.5) returns the same new draft.

## UI notes

- Re-send banner on the link screen: "Re-sending 'render.mp4' and 2 more files. You can change the details before sending."
- `FILES_GONE` state: single screen, "Files were deleted — upload again." + **Upload** primary.

## Technical notes

- Endpoint 16 `POST /transfers/{id}/resend` (owner); `ResendTransferCommand` (TA-4.2a); steps per F-TRF-010 technical notes.
- `FILES_GONE` (TA-4.1.3) when any `BlobRef` is physically deleted or missing.
- AC-010-1: verify blob count unchanged (T-023 exit check).

## Links

- Feature: `TRF-010-resend.md` (FR-010-1, FR-010-3, FR-010-4)
- Plan AC: AC-010-1, AC-010-2
- Architecture: TA-7.3, ADR-009
- Related: US-009-03 (entry point)
- Milestone: T-023
