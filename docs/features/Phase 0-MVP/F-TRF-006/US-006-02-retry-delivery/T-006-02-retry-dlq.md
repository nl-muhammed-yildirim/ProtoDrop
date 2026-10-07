# T-006-02 — Retry + dead-letter: backoff via SB delivery count, email.failed (FR-006-4)

**Story:** US-006-02 | **Spec:** FR-006-4, AC-006-2, TA-5.1/TA-6.7/TA-10.3 | **Size:** M
**Depends on:** T-006-01 (f-email consumer — this task hardens its failure path)

---

## Context to read (only these)

- `US-006-02-retry-delivery.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-006-email.md` → FR-006-4 + AC-006-2 + Technical notes (retries line)

## Instructions

1. Keep **`f-email` stateless**: the retry counter is the Service Bus delivery count — no local retry ledger (TA-5.1). On exception → `throw` to redeliver, except terminal business failures (which complete without retry).
2. Subscription `core/email`: **MaxDeliveryCount 5** ⇒ 3 visible retries after the first attempt; backoff across the delivery cycle ≈ **1 min / 10 min / 1 h** (FR-006-4); DLQ retention 30 d (TA-5.1).
3. Final failure → message dead-letters; emit **`email.failed`** (`{transferId, to, attempt = last, reason = error code}`); the **`dlq_count > 0`** alert condition becomes true (P2, TA-10.3).
4. Track metric **`email_failures_1h`**; alert threshold 50/h (TA-10.3).
5. Transient blip: a retry within the cycle succeeds → normal success path with `email.sent` carrying `attempt > 1`.

## Exit check

- [ ] Fake sender returning 429 every time: 3 retries observable, then DLQ + `email.failed` + alert condition true (AC-006-2)
- [ ] Transient first-attempt failure → success on second delivery with `attempt = 2`
- [ ] DLQ replay after a real success → no double send (`NotifiedAtUtc` guard)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; f-email consumer in place).
Task T-006-02 — implement the retry/dead-letter contract.
Read first (only): docs/features/Phase 0-MVP/F-TRF-006/US-006-02-retry-delivery/US-006-02-retry-delivery.md (happy path + Alternative flows + Edge cases) and F-TRF-006-email.md (FR-006-4).
Do exactly:
1. Keep f-email stateless: the retry counter is the Service Bus delivery count — no local retry ledger (TA-5.1). On exception → throw to redeliver, except terminal business failures (which complete without retry).
2. Set up subscription core/email with MaxDeliveryCount 5 ⇒ 3 visible retries after the first attempt; backoff across the delivery cycle ≈ 1 min / 10 min / 1 h (FR-006-4); DLQ retention 30 d (TA-5.1).
3. Final failure → message dead-letters; emit email.failed ({transferId, to, attempt = last, reason = error code}); the dlq_count > 0 alert condition becomes true (P2, TA-10.3).
4. Track metric email_failures_1h; alert threshold 50/h (TA-10.3).
5. Keep the transient path clean: a retry within the cycle succeeds → normal success path with email.sent carrying attempt > 1.
Done when: AC-006-2 holds — a fake sender failing every time shows 3 retries then DLQ + email.failed + alert condition, and a first-attempt-only failure recovers on the second delivery with attempt = 2.
Constraints: stateless function (broker is the state); terminal business failures (e.g. unknown transferId) complete without retry — do not redeliver them; replay safety comes from T-006-01's NotifiedAtUtc guard.
```
