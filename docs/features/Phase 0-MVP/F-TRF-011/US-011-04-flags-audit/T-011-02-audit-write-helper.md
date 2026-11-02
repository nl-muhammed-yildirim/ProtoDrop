# T-011-02 — Audit write helper: `AuditLog` row + `admin.action` event (FR-011-5, TA-3.2/TA-5.3)

**Story:** US-011-04 | **Spec:** FR-011-5, AC-011-2 (audit half); TA-3.2 `AuditLog`, TA-5.3 | **Size:** S
**Depends on:** T-011-01 (gate — actor = admin email from session), T-004 (`AuditLog` entity + DDL)

---

## Context to read (only these)

- `US-011-04-flags-audit.md` → happy path 4 (audit row + event in the same request)
- `../F-TRF-011-admin.md` → FR-011-5 + Technical notes (Audit line)
- `../../F-TRF-009/US-009-04-delete/T-009-04-delete-transfer-endpoint.md` → event emission pattern (`transfer.deleted`, TA-5.3)

## Instructions

1. Add a small **audit write helper** in `wa.application` (e.g. `IAuditWriter`): writes one `AuditLog` row — `(ActorEmail, Action, EntityType, EntityId, DetailsJson)` per TA-3.2 — and emits the **`admin.action`** event `{ actor, action, entityType, entityId, details }` (TA-5.3) in the same request.
   - DECISION line: TA-4.2 marks admin endpoints as emitting `admin.action`; the spec's FR-011-5 says "every admin mutation" — reads (GETs) do **not** write audit rows (only mutations). Keep that scope; note it here rather than in the backlog row.
2. The helper takes the actor email explicitly (from the session, resolved at endpoint level) so commands stay thin per TA-4.2a.
3. Unit test: one call → exactly one `AuditLog` row + one `admin.action` publish; no event for read paths.

## Exit check

- [ ] Helper writes `AuditLog (ActorEmail, Action, EntityType, EntityId, DetailsJson)` verbatim per TA-3.2
- [ ] Emits `admin.action` with the exact TA-5.3 payload shape `{ actor, action, entityType, entityId, details }` (FR-011-5)
- [ ] Unit test proves no audit row for reads; one row + one event per mutation

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS — events via the existing IEventPublisher/outbox from T-006).
Task T-011-02 — audit write helper (milestone task T-024).
Read first (only): US-011-04 story (happy path 4), F-TRF-011-admin.md (FR-011-5 + Technical notes Audit line), TA-3.2/TA-5.3 in docs/03-technical-architecture.md, T-009-04 task doc for the event pattern.
Do exactly:
1. Add IAuditWriter in wa.application: writes one AuditLog row (ActorEmail, Action, EntityType, EntityId, DetailsJson) and emits admin.action {actor, action, entityType, entityId, details} in the same request.
2. Actor email passed explicitly from endpoint/session — commands stay thin (TA-4.2a).
3. Unit tests: one call = exactly one row + one event; reads write nothing.
Done when: FR-011-5 holds for any mutation that uses the helper — the actor's email is in the AuditLog row and in the admin.action payload, both in the same request.
Constraints: no new entity/table (T-004 DDL); no audit rows on GETs; keep the helper out of endpoint bodies.
```