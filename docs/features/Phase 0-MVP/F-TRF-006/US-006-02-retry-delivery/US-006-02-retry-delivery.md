# US-006-02 — Failed deliveries retry and dead-letter

**Feature:** F-TRF-006 — Email Notifications | **Status:** pending

---

**Story:** As the operator, I want a failed email to retry automatically and, if it keeps failing, to be visible to me as a dead letter, so that "the email never arrived" tickets are diagnosable.
**Actor:** Operator (sees health), recipient (eventually gets the email or an admin finds the DLQ).
**Goal:** No silent email loss: retry → alert → human.

## Preconditions

- Subscription `core/email`: MaxDeliveryCount 5, DLQ retention 30 d (TA-5.1).

## Happy path

1. A send fails (e.g. Communication Hub 429 / 5xx / timeout).
2. The message is redelivered; `f-email` retries — visible backoff across the delivery cycle ≈ 1 min / 10 min / 1 h (FR-006-4), 3 retries after the first attempt.
3. On final failure: message dead-letters; `email.failed` emitted (`attempt` = last, `reason` = error code); `dlq_count` > 0 alert fires (P2, TA-10.3).
4. Operator sees it in the DLQ alert + admin email-failure health; can replay manually later.

## Alternative flows

- **Transient blip resolves:** a retry within the cycle succeeds → normal success path (US-006-01), `email.sent` with `attempt > 1`.
- **DLQ replay:** admin/operator re-queues the message; consumer reprocesses; `NotifiedAtUtc` guard prevents double-send if it had actually succeeded.

## Acceptance criteria

```gherkin
Given Communication Hub returns 429 for every attempt
When the message fails its deliveries
Then it is retried 3 times with backoff
And on final failure it is dead-lettered
And email.failed is emitted and the DLQ alert condition is true

Given a transient failure on the first attempt only
When the second delivery is tried
Then the email is sent
And email.sent is emitted with attempt 2
```

## Edge cases

- Retry counter = SB delivery count; the function stays stateless (no local retry ledger) — the broker is the state (TA-5.1).
- `email_failures_1h > 50` is the metric alert (TA-10.3); individual `email.failed` events carry `reason` for diagnosis.

## UI notes

- Admin: email health on Overview (sent/failed 7 d, F-TRF-011-2); DLQ itself is an Azure-side operation with an admin note (runbook).

## Technical notes

- `f-email` (TA-6.2): concurrency 10, 5-min timeout; on exception → `throw` (redeliver) except terminal business failures.
- Telemetry: `email.sent`/`email.failed` (`{transferId, to, attempt, reason?}`, TA-5.3); metric `email_failures_1h`.
- Alert: `dlq_count > 0` → P2 (TA-10.3).

## Links

- Feature: `TRF-006-email.md` (FR-006-4)
- Plan AC: AC-006-2
- Architecture: TA-5.1, TA-6.7, TA-10.3
- Milestone: T-019
