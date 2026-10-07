# T-002-04 — SendTransferCommand + endpoint 3: activate, recipients, transfer.created (T-011)

**Story:** US-002-05 | **Spec:** FR-002-6, AC-002-3, TA-4.2#3/TA-5.3 | **Size:** M
**Depends on:** T-002-01 (LinkId — already set at finalize), T-002-02 (idempotency middleware covers this endpoint), T-054-01/02 (IEventPublisher + outbox)

---

## Context to read (only these)

- `US-002-05-link-only.md` → happy path + Alternative flows + Technical notes
- `US-002-02-send-by-email.md` → happy path + Technical notes (parsing rules)
- `../../F-TRF-002-transfer-link.md` → FR-002-5/6 + AC-002-2/3 + Test plan (integration line)

## Instructions

1. Add **`SendTransferCommand`** (MediatR, `wa.application/UseCases/Transfers/`) + endpoint 3 `POST /api/v1/transfers/{id}/send`.
2. Parse recipients: split on `[\r\n,;\s]+`, trim, drop empties, validate per-address (.NET `MailAddress`), lowercase; cap at `MAX_EMAILS` (default 20 — via `ILimitsProvider`, never a literal); store one **`EmailRecipient`** row per unique address (`(TransferId, Address)` unique).
3. Set **`Status=1`**, **`ExpiresAtUtc = now + RETENTION_DAYS`** (TA-3.4), persist `SenderName`/`SenderEmail`/`Note` defaults (`SenderName ?? "Anonymous"`; signed-in user's email used when present).
4. Zero emails is valid — link-only transfer: no `EmailRecipient` rows, `transfer.created` emitted with `emails: []`.
5. Emit **`transfer.created`** via the outbox in the same transaction window as the status flip (TA-5.3 payload exactly: `{ transferId, linkId, totalBytes, fileCount, emails:[], senderEmail?, scheduledSendAt? }`).

## Exit check

- [ ] Send with `"a@x.com, b@y.com "` stores 2 normalized addresses (trailing space ignored, lowercased) — AC-002-2
- [ ] Send with 0 emails → Status=1, zero EmailRecipient rows, no email sent — AC-002-3
- [ ] `transfer.created` payload matches TA-5.3 exactly (unit/integration assertion on the outbox row)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; finalize endpoint, idempotency middleware, IEventPublisher + outbox in place).
Task T-002-04 — implement send (milestone task T-011).
Read first (only): docs/features/Phase 0-MVP/F-TRF-002/US-002-05-link-only/US-002-05-link-only.md (happy path + Technical notes), US-002-02-send-by-email.md (Technical notes parsing rules), and F-TRF-002-transfer-link.md (FR-002-5/6).
Do exactly:
1. Add SendTransferCommand (MediatR, wa.application/UseCases/Transfers/) + endpoint 3 POST /api/v1/transfers/{id}/send.
2. Parse recipients: split on [\r\n,;\s]+, trim, drop empties, validate per-address (.NET MailAddress), lowercase; cap at MAX_EMAILS (default 20 via ILimitsProvider, never a literal); store one EmailRecipient row per unique address ((TransferId, Address) unique).
3. Set Status=1, ExpiresAtUtc = now + RETENTION_DAYS (TA-3.4), persist SenderName/SenderEmail/Note defaults (SenderName ?? "Anonymous"; signed-in user's email used when present).
4. Treat zero emails as valid — link-only transfer: no EmailRecipient rows, transfer.created emitted with emails: [].
5. Emit transfer.created via the outbox in the same transaction window as the status flip (TA-5.3 payload exactly: { transferId, linkId, totalBytes, fileCount, emails:[], senderEmail?, scheduledSendAt? }).
Done when: AC-002-2 and AC-002-3 hold — normalized addresses stored, link-only send works with zero rows, and the outbox row carries the exact TA-5.3 payload.
Constraints: thin endpoint (TA-4.2a); email *sending* is F-TRF-006's scope (T-019) — this task only stores recipients and emits the event; idempotency comes from T-002-02's middleware, do not re-implement it here.
```
