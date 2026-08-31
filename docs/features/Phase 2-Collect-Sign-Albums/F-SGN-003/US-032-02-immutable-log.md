# US-032-02 — Trust that the log can't be quietly edited

**Feature:** F-SGN-003 — Audit Trail | **Status:** pending

---

**Story:** As a sender (and my regulator), I want the audit log to be append-only, so that nobody can quietly change what happened after the fact.
**Actor:** sender, operator, regulator.
**Goal:** honest MVP immutability: no UPDATE/DELETE path in the API — a DBA-level delete is the documented escape hatch, and hash-chaining is explicitly a later decision (D-22).

## Preconditions

- Any document with audit entries.

## Happy path

1. Every audit write goes through the append path — the API surface has exactly one write (`AuditEntry` INSERT) and zero update/delete endpoints.
2. The state changes (sign/decline/void) and their audit rows commit in one transaction — if the sign rolls back, the trail rolls back with it.
3. The product says what "immutable" means here: the plain-language statement (US-032-03) scopes it — no overclaiming of hash-chaining.

## Alternative flows

- **Escaping the log**: a DBA-level delete exists as the escape hatch — documented, not hidden (the product's honesty rule, 01-product-analysis).
- **GDPR erasure of a signer** (EC-032-4): erasure deletes *their* rows — the hash makes them findable via the (recipient, type) pair (documented tension between immutability and erasure; erasure wins for the data subject).

## Acceptance criteria

```gherkin
Given audit entries exist
When the API is exercised
Then no endpoint updates or deletes them (only INSERT)

Given a sign action is rolled back
When the transaction completes
Then no Signed audit row exists either
```

## Edge cases

- GC while export in flight (EC-032-3): export reads before GC — documented race, acceptable at 90 d retention.
- "Immutable" ≠ "crypto-proof": the D-22 decision (hash-chaining) is named as future work, so the copy doesn't overpromise.

## UI notes

- No UI claims "tamper-proof" — the footer states "append-only" language (FR-032-4 string).
- The timeline (US-032-01) is the only reader; there is no "edit activity" screen.

## Technical notes

- `AuditEntry` is `BIGINT IDENTITY` PK, append-only by convention + lint: a code-review rule (no `UPDATE`/`DELETE` statements against the table outside migrations — testable with a schema test).
- Retention: rows live with the document; deleted at `DOCUMENT_RETENTION_DAYS` (proposed 90, D-22) — the whole table for that document (FR-032-6).
- Erasure path: delete by (recipientId, type) find-key (EC-032-4).

## Links

- Feature: `SGN-003-audit-trail.md` (FR-032-1, FR-032-6, EC-032-3/4)
- Related: US-032-01 (the log this story protects), F-TRF-011 (admin audit for the *admin* actions — a separate trail)
