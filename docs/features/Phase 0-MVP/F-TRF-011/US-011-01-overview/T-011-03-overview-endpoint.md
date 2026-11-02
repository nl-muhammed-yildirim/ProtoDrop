# T-011-03 — GetOverviewQuery + endpoint 20: cached platform counts, 5-min TTL (FR-011-2/6)

**Story:** US-011-01 | **Spec:** FR-011-2/6, AC-011-1; TA-4.2#20, TA-10.2 | **Size:** M
**Depends on:** T-011-01 (admin gate)

---

## Context to read (only these)

- `US-011-01-overview.md` → happy path + Alternative flows + Technical notes (approximation note!)
- `../F-TRF-011-admin.md` → FR-011-6 + AC-011-1
- `../../F-TRF-009/US-009-04-delete/T-009-04-delete-transfer-endpoint.md` → endpoint shape reference

## Instructions

1. Add **`GetOverviewQuery`** (MediatR, `wa.application/UseCases/Admin/`) + endpoint 20 `GET /api/v1/admin/overview` (TA-4.2#20).
2. Four aggregates, each cached **5 minutes** in-memory (no Redis — ADR-008; per-instance, cross-replica divergence ≤ 5 min is acceptable): active transfers (`Status=1` count), storage used (`SUM(BlobRef.SizeBytes)` for active references), new users today (**UTC** day boundary — document in the tooltip data), emails 7 d (sent vs failed side by side).
   - ASSUMPTION line per US-011-01 Technical notes: email counts are the documented approximation — count of `EmailRecipient.NotifiedAtUtc` in window as "sent", DLQ count as "failed". State this in the query's summary comment.
3. Include the **recent transfers** list (last 20 by `CreatedAtUtc`, read-only preview — linkId, owner email, status, size, created) in the same payload; it is part of endpoint 20 (no new number).
4. Response includes a snapshot timestamp so the screen can render "Updated {time}" (FR-011-6 staleness stated on-screen).

## Exit check

- [ ] AC-011-1 server half: four numbers + recent-transfers list returned; second call within 5 min returns the cached snapshot (same timestamp)
- [ ] After ~5 min the snapshot refreshes (staleness never exceeds 5 min, EC/AC per FR-011-6)
- [ ] "New users today" is a UTC calendar day (US-011-01 edge case)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS).
Task T-011-03 — overview endpoint (milestone task T-024, TA-4.2#20).
Read first (only): US-011-01-overview.md (happy path + Technical notes), F-TRF-011-admin.md (FR-011-6 + AC-011-1).
Do exactly:
1. GetOverviewQuery + GET /api/v1/admin/overview (admin policy from T-011-01).
2. Four 5-min in-memory cached aggregates: active transfers, storage used (SUM BlobRef.SizeBytes), new users today (UTC day), emails 7 d sent/failed — approximation per US-011-01 Technical notes; document it in the query's summary comment.
3. Last-20 recent transfers by CreatedAtUtc DESC in the same payload; response carries the snapshot timestamp for "Updated {time}".
Done when: AC-011-1 holds server-side — counts within 5 min staleness, cached (second call returns the same snapshot), and the timestamp is present for the client.
Constraints: in-memory cache only (ADR-008); no new endpoint number; thin endpoint per TA-4.2a.
```