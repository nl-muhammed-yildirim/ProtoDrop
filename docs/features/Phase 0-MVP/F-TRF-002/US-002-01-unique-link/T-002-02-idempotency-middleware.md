# T-002-02 — Idempotency-Key middleware for finalize + send (TA-4.1.5, EC-002-1)

**Story:** US-001-01 | **Spec:** TA-4.1.5, AC-002-1 (double-finalize half), EC-002-1 | **Size:** M
**Depends on:** T-001-10 (finalize endpoint — the first consumer), T-052-03 (IdempotencyKey table is one of TA-3.2's 15 tables — migration in place)

---

## Context to read (only these)

- `US-002-01-unique-link.md` → Alternative flows + Technical notes (Idempotency-Key line)
- `../../F-TRF-002-transfer-link.md` → EC-002-1 + Test plan (integration line)

## Instructions

1. Add an idempotency middleware for **`POST /api/v1/transfers/draft/{draftId}/finalize`** and **`POST /api/v1/transfers/{id}/send`**: read the `Idempotency-Key` header, look up the stored key (24 h TTL), on hit return the stored `ResultJson` with the original status code.
2. On miss: run the request once; store key + response for 24 h. A double-clicked finalize returns the **original transfer** — no second link is minted (EC-002-1).
3. Client keys are deterministic opaque strings: `finalize-{draftId}` for finalize, `send-{linkId}` for send (TA-4.1.5 storage contract; SHA-256 hex form optional per US-002-01 Technical notes).

## Exit check

- [ ] Double-finalize with the same key returns the identical transfer — exactly one Transfer row, one linkId
- [ ] A second POST send with the same key does not duplicate EmailRecipient rows or re-emit `transfer.created`
- [ ] Keys older than 24 h are pruned (TTL honored in tests)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; finalize endpoint + IdempotencyKey table in place).
Task T-002-02 — add the idempotency middleware.
Read first (only): docs/features/Phase 0-MVP/F-TRF-002/US-002-01-unique-link/US-002-01-unique-link.md (Alternative flows + Technical notes Idempotency-Key line) and F-TRF-002-transfer-link.md (EC-002-1).
Do exactly:
1. Add idempotency middleware for POST /api/v1/transfers/draft/{draftId}/finalize and POST /api/v1/transfers/{id}/send: read the Idempotency-Key header, look up the stored key (24 h TTL), on hit return the stored ResultJson with the original status code.
2. On miss run the request once; store key + response for 24 h. A double-clicked finalize returns the original transfer — no second link is minted (EC-002-1).
3. Accept deterministic opaque client keys: finalize-{draftId} for finalize, send-{linkId} for send (TA-4.1.5 storage contract; SHA-256 hex form optional per US-002-01 Technical notes).
Done when: the integration tests hold — double-finalize returns the identical transfer with exactly one linkId, and a replayed send does not duplicate EmailRecipient rows or re-emit transfer.created.
Constraints: middleware is generic (header + storage only) — no transfer-specific logic inside it; 24 h TTL is the contract, do not make it configurable at M0.
```
