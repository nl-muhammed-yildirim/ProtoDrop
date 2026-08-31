# F-TRF-010 — Re-send Transfer

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-010 | **Architecture:** TA-7.3, TA-3.2 (`BlobRef`), ADR-009
**Milestone tasks:** T-023

---

## Description

"A link expired or I got the email wrong — let me send the same files again **in one click**." Re-send creates a **new** transfer (new link, fresh expiry, fresh download count) that **shares the old transfer's blobs** — no data copy, instant, free. The trick is `BlobRef`: physical storage items are reference-counted, so a blob is only deleted when *no* transfer references it anymore. This is why re-send is a headline feature, not an afterthought.

**Actors:** signed-in owner (guest transfers can't be re-sent — they have no owner), recipient (gets the new link).
**Value:** the #1 support-ticket killer; makes the 7-day retention feel forgiving.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-010-1 | "Re-send" on any non-deleted transfer: creates a **new** transfer (new `LinkId`, fresh `ExpiresAtUtc = now + RETENTION_DAYS`, fresh `DownloadsCount=0`), copies file metadata, **shares blobs** via `BlobRef` (`RefCount++` per shared blob — no data copy, ADR-009). |
| FR-010-2 | The new draft is **pre-filled** with the original's emails, password, and note; the user can edit everything before sending (F-TRF-002 link screen, US-002-02…04). |
| FR-010-3 | The original transfer is marked `SupersededBy = newTransferId` (informational; both can be active simultaneously). |
| FR-010-4 | Re-send of a transfer whose blobs are already physically deleted (grace passed) → `FILES_GONE` error: "Files were deleted — upload again." No half-created draft remains (draft cleanup). |

## Acceptance criteria

```gherkin
AC-010-1: Re-send an expired transfer within grace
  Then a new transfer is created, blobs shared (no duplication in storage)
  And the original is intact and still downloadable

AC-010-2: Re-send a transfer whose blobs are gone
  Then the error names the problem and no half-created transfer remains (draft cleanup)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-010-1 | Re-send of a re-send | Works: new transfer shares the same `BlobRef`s again (`RefCount` 2 → 3); `SupersededBy` chain is linear (newest wins) |
| EC-010-2 | Original transfer deleted by owner while re-send draft is open | Finalize of the pending re-send fails with `FILES_GONE` (draft screen error, no orphan rows) |
| EC-010-3 | Re-send changes file selection | Not supported in MVP: re-send is all-or-nothing (same file set); editing files = new upload (documented on the pre-filled screen: "Re-sending the same files") |
| EC-010-4 | Guest original | No owner → no My Files → no re-send button (the affordance simply isn't rendered; the API would return `FORBIDDEN`) |

## UI notes (UI-Reference §5.2, §5.4)

- My Files row action **Re-send** (ghost, link icon) → link screen with pre-filled fields + banner: "Re-sending 'render.mp4' and 2 more files. You can change the details before sending."
- Confirmation after re-send: "New link ready." + copy + the *new* URL (the old URL, if still active, keeps working — both live).
- `FILES_GONE` state on the draft screen: "Files were deleted — upload again." + **Upload** button (→ `/`).

## Technical notes

- `ResendTransferCommand` (endpoint 16, owner):
  1. Verify all the transfer's `BlobRef`s still exist and are not physically deleted (`PhysicallyDeletedAtUtc IS NULL` and blob head OK) — else `FILES_GONE`.
  2. Create new `Transfer(Status=0, ExpiresAtUtc = now + RETENTION_DAYS, DownloadsCount=0)`, new `FileItem` rows → **same `BlobRefId`s**, `RefCount++` per shared blob.
  3. Copy `EmailRecipient` addresses, `PasswordHash`, `Note`, `SenderName`/`SenderEmail` as draft pre-fill (the draft is editable before `SendTransferCommand`).
  4. On send: `SupersededBy = newTransferId` set on the original.
- Storage math: re-send costs **0 bytes** (ADR-009); the admin storage meter must not double-count shared bytes (count per `BlobRef`, not per `FileItem`).
- Telemetry: `transfer.created` for the new transfer (normal path); metric `active_transfers` moves by +1.

## Test plan

- Unit: refcount bump logic (1→2, 2→3); `FILES_GONE` predicate; supersede-chain invariant (no cycles).
- Integration: re-send an expired transfer → new linkId, same `BlobRefId`s, `RefCount` 2, original `Status` unchanged, `SupersededBy` set on send; re-send after `f-delete-blobs` ran → `FILES_GONE`; storage byte total unchanged (sum of `BlobRef.SizeBytes`); AC-010-1…010-2.
- E2E: My Files → re-send → edit note → send → new link works, old link still works (T-023 exit).

## User stories

| ID | Story | File |
|---|---|---|
| US-010-01 | Re-send an expired transfer in one click | `US-010-01-resend-click.md` |
| US-010-02 | Edit the pre-filled details before re-sending | `US-010-02-edit-prefill.md` |
| US-010-03 | Not pay for duplicated storage | `US-010-03-shared-blobs.md` |
