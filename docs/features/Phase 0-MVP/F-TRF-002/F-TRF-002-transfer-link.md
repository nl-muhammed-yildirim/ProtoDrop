# F-TRF-002 — Transfer Creation & Link

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-002 | **Architecture:** TA-3.2 `Transfer`, TA-4.2#2–3, TA-7.1
**Milestone tasks:** T-010, T-011, T-013

---

## Description

After files finish uploading (F-TRF-001), the transfer is *finalized*: a globally unique, unguessable **link ID** (8 chars, Crockford base32) is minted and the browser-to-blob staging data is committed server-side. The sender then lands on the **link screen**, where they can add recipient emails, an optional password, a sender name and a note, and press **Send transfer**. Sending activates the transfer (status `Active`, `ExpiresAt = now + RETENTION_DAYS`), stores the recipients, and emits `transfer.created` so the email flow (F-TRF-006) can run. A transfer may be link-only (zero emails).

**Actors:** guest sender, account user.
**Value:** the link *is* the product surface — its format and reliability are the core of the brand promise.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-002-1 | On finalize, the transfer gets a **link ID**: 8 characters, Crockford base32 (no `0/O/1/I`), globally unique. |
| FR-002-2 | Link format: `{origin}/t/{linkId}` (e.g., `wetransfer.com/t/3f9k2a7x`). |
| FR-002-3 | The "link screen" shows: copy button (with success state), recipient email field(s), optional password field, "Send transfer" button. |
| FR-002-4 | Transfer metadata: sender name (required text, defaults to "Anonymous" if guest), sender email (if account user, else optional field), optional note message. |
| FR-002-5 | Recipient emails are validated per-address (comma/space separated accepted, auto-normalized). |
| FR-002-6 | "Send transfer" with zero emails is allowed — link-only transfer. |
| FR-002-7 | After send, sender sees a confirmation screen with the link and "Copy" plus "Send again" (creates a *new* transfer draft with the same files — files are copied, not aliased). |

## Acceptance criteria

```gherkin
AC-002-1: Finalize a transfer
  Then it receives an 8-char Crockford linkId
  And GET /t/{linkId} returns the recipient page with HTTP 200

AC-002-2: Send with emails "a@x.com, b@y.com "
  Then 2 valid addresses are stored (trailing space ignored)
  And one email is sent per address (see F-TRF-006)

AC-002-3: Send with 0 emails
  Then transfer is active and link-only

AC-002-4: Collision on linkId (astronomically unlikely)
  Then server regenerates once; still collision → 500 + telemetry
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-002-1 | Same user finalizes twice (double-click) | `Idempotency-Key` header on finalize; duplicate returns the original transfer |
| EC-002-2 | Guest sender email invalid at send | Address rejected inline; the transfer itself is not blocked |

## UI notes (UI-Reference §5.2)

- Card (max-width 560 px): "Your link is ready", copy field (read-only input + **Copy** with 2 s "Copied ✓" state).
- Form fields: recipients (textarea, one per line), password (optional, show/hide), note (500 max, counter), "From" display name (pre-filled if signed in).
- **Send transfer** primary; "Copy link only" ghost (link-only transfer, FR-002-6).
- Confirmation screen: link + copy + "Send again" (re-drafts from the same files, FR-002-7).

## Technical notes

- `FinalizeTransferCommand` (MediatR): idempotency-keyed (TA-4.1.5), server-side blob copy `staging/… → transfers/{id}/files/{fileId}`, creates `Transfer(Status=0)` + `FileItem` + `BlobRef(RefCount=1)`, sets `TotalBytes`/`FileCount`.
- `SendTransferCommand`: `Status=1`, `ExpiresAtUtc = now + RETENTION_DAYS` (TA-3.4), password → PBKDF2 `PasswordHasher` hash, recipients → `EmailRecipient` rows (unique per transfer, lowercased), emits `transfer.created` (TA-5.3).
- Link ID generation: domain `LinkId` value object; Crockford base32, 8 chars, retry-once on collision (AC-002-4), else 500 + `INTERNAL` telemetry.
- Blob layout: `transfers/{transferId}/files/{fileId}` — no folder nesting (TA-3.5).

## Test plan

- Unit: LinkId generation (charset, length, no ambiguous chars), email parsing/normalization, collision handling.
- Integration: finalize → `GET /t/{linkId}` 200; double-finalize with same `Idempotency-Key` returns same transfer (EC-002-1); send with `"a@x.com, b@y.com "` stores 2 normalized addresses; send with 0 emails → Active + no `EmailRecipient` rows; `transfer.created` payload matches TA-5.3.
- E2E: guest full flow draft→finalize→send→copy (T-013 manual pass).

## User stories

| ID | Story | File |
|---|---|---|
| US-002-01 | Get a unique link for my files | `US-002-01-unique-link.md` |
| US-002-02 | Send the transfer to recipients by email | `US-002-02-send-by-email.md` |
| US-002-03 | Protect my transfer with a password | `US-002-03-password.md` |
| US-002-04 | Add sender info and a note | `US-002-04-sender-note.md` |
| US-002-05 | Create a link-only transfer (no emails) | `US-002-05-link-only.md` |
