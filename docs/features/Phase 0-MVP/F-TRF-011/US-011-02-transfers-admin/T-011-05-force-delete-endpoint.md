# T-011-05 — AdminDeleteTransferCommand + endpoint 21 DELETE: force-delete, reason `admin` (FR-011-4)

**Story:** US-011-02 | **Spec:** FR-011-4, US-011-02 gherkin (force-delete), EC-011-2; TA-4.2#21, TA-5.3 | **Size:** M
**Depends on:** T-009-04 (`DeleteTransferCommand` — refcount path reused with reason `admin`), T-011-01 (gate: SuperAdmin-only write)

---

## Context to read (only these)

- `US-011-02-transfers-admin.md` → happy path 3 + Alternative flows
- `../F-TRF-011-admin.md` → FR-011-4 + EC-011-2
- `../../F-TRF-009/US-009-04-delete/T-009-04-delete-transfer-endpoint.md` → the delete mechanics this task reuses

## Instructions

1. Add **`AdminDeleteTransferCommand`** (MediatR, `wa.application/UseCases/Admin/`) + endpoint 21 DELETE `DELETE /api/v1/admin/transfers/{id}` (TA-4.2#21) — SuperAdmin only (T-011-01 policy table).
2. Reuse T-009-04's delete semantics **verbatim**: mark the row `Deleted` (reason **`admin`** — not a hard delete), emit **`transfer.deleted`** `{ transferId, reason: "admin" }` (TA-5.3 — the catalog already lists this event with the `"admin"` value), decrement each `BlobRef` `RefCount`; sole-reference blob gets `PhysicallyDeletedAtUtc = now + 24h`.
   - DECISION line: FR-011-4 says "same semantics as user delete (F-TRF-009-3)" — the spec does not state whether admin delete reuses T-009-04's idempotent `NOT_FOUND` double-delete behavior. Reuse it (second call → `NOT_FOUND`, client treats as success) unless review disagrees.
3. In the same request, write the audit via T-011-02's helper: `AuditLog` row + **`admin.action`** `{ actor, action: "transfer.force_delete", entityType: "transfer", entityId: id, details }` (FR-011-5; TA-4.2 marks endpoint 21 as emitting `admin.action`).
   - DECISION line: the concrete `action` strings (`transfer.force_delete`, etc.) are not enumerated in the spec — pick snake_case names and list them here as the contract for review.

## Exit check

- [ ] US-011-02 AC: force-delete emits `transfer.deleted` with reason `admin` exactly once (FR-011-4)
- [ ] US-011-02 AC: the audit log has a row with the admin's email as actor + the `admin.action` event (FR-011-5)
- [ ] Refcount path identical to T-009-04 — sole-reference blob gets the 24 h buffer, shared blobs untouched
- [ ] EC-011-2: in-flight recipient download unaffected — same as user delete (EC-009-1), SAS may outlive the row

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS).
Task T-011-05 — admin force-delete (milestone task T-024, TA-4.2#21 DELETE).
Read first (only): US-011-02 story (happy path 3), F-TRF-011-admin.md (FR-011-4 + EC-011-2), T-009-04 task doc (mechanics to reuse verbatim).
Do exactly:
1. AdminDeleteTransferCommand + DELETE /api/v1/admin/transfers/{id}, SuperAdmin only.
2. Reuse DeleteTransferCommand's path with reason "admin": row marked Deleted, transfer.deleted {transferId, reason:"admin"}, RefCount decrements, sole refs get PhysicallyDeletedAtUtc = now + 24h; double-delete → NOT_FOUND (DECISION line in the task doc).
3. Audit via IAuditWriter: AuditLog row + admin.action event in the same request (FR-011-5).
Done when: US-011-02's force-delete gherkin holds — transfer.deleted reason admin emitted once, audit row with the actor's email present, and EC-011-2 behaves exactly like user delete.
Constraints: one command, two reasons (same pattern as T-009-04's note); no new event names beyond TA-5.3; action string contract per the DECISION line in this doc.
```