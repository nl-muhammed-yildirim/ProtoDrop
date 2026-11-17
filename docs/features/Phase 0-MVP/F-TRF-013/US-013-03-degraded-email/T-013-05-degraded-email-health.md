# T-013-05 — Degraded-email: send unaffected + admin health counts (FR-013-4, AC-013-3)

**Story:** US-013-03 | **Spec:** FR-013-4 / AC-013-3 + edge cases (dedup by eventId + NotifiedAtUtc) | **Size:** S
**Depends on:** T-056-04-style email worker (F-TRF-006 machinery — already built); F-TRF-011 admin Overview for the health panel

---

## Context to read (only these)

- `US-013-03-degraded-email.md` → Happy path + Alternative flows + Edge cases + Technical notes
- `../F-TRF-013-errors.md` → FR-013-4 row + AC-013-3
- TA-5.3 / TA-6.2 in `docs/03-technical-architecture.md` → the outbox/event seam; Hub only touched inside `f-email` (concurrency 10, 5-min timeout)

## Instructions

1. **Prove the seam holds:** with a fake-failing sender in dev (forced DLQ per F-TRF-013's test plan), sending a valid transfer still returns **201 + link screen** — `transfer.created` published; Hub only touched inside `f-email`. No API-level failure at all (FR-013-4).
2. Verify retry/dedup machinery does the rest: failed deliveries retry on the Service Bus cycle until Hub recovers; consumer dedups by `eventId` + `NotifiedAtUtc` so recovery sends no duplicates (F-TRF-006 EC-006-4); final failure → DLQ (30-day retention) + `email.failed` telemetry + the `DLQ_COUNT > 0` admin alert.
3. **Admin health surface** (Overview, F-TRF-011 US-011-01): pending count, failed count, last-successful-delivery age, DLQ count — with email down in dev the panel shows a non-zero pending/failed count while the sender saw nothing (FR-013-4).
   - If the F-TRF-011 Overview already renders these counts, this task is wiring + verification only; do not build a second health surface.
4. Sender-side UX is unchanged by design: no "email down" state on the send screen (US-013-03 UI notes).

## Exit check

- [ ] AC-013-3: with email down at send time, the transfer is sent → 201 + working link; admin email health shows the pending/failed count
- [ ] US-013-03 AC block 2: after recovery the retry cycle delivers pending notifications (`NotifiedAtUtc` set) with no duplicates for the same event (eventId + NotifiedAtUtc dedup)
- [ ] FR-013-4: transfers are never blocked by email — the send path has zero synchronous Hub calls

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop.
Task T-013-05 — degraded-email verification + health surface (milestone task T-025, scope "degraded states for Blob/email down").
Read first (only): US-013-03-degraded-email.md (all sections), F-TRF-013-errors.md (FR-013-4 + AC-013-3), TA-5.3/TA-6.2 in docs/03-technical-architecture.md, the F-TRF-011 admin Overview screen.
Do exactly:
1. Forced-DLQ dev test (fake sender): send → 201 + working link; transfer.created still published.
2. Verify retry cycle + eventId/NotifiedAtUtc dedup + DLQ path with email.failed telemetry.
3. Wire/verify admin Overview email health counts (pending, failed, last-successful age, DLQ) — reuse F-TRF-011's panel; no second surface.
Done when: AC-013-3 holds both ways — send succeeds while Hub is down with the counts visible to the operator, and recovery delivers all pending without duplicates (FR-013-4).
Constraints: no sender-side UX change; no new metrics beyond email_failures_1h / DLQ_COUNT already in TA-10.2/TA-10.3.
```
