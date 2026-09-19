# T-055 — Failed deliveries retry and dead-letter

**Story:** US-006-02 | **Feature:** F-TRF-006 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-006/US-006-02-retry-delivery.md`
**Coarse task (Milestone-Backlog.md):** T-019
**Status:** pending

---

## Scope

As the operator, I want a failed email to retry automatically and, if it keeps failing, to be visible to me as a dead letter, so that "the email never arrived" tickets are diagnosable.

**Actor:** Operator (sees health), recipient (eventually gets the email or an admin finds the DLQ).

**Goal:** No silent email loss: retry → alert → human.

Happy path:

1. A send fails (e.g. Communication Hub 429 / 5xx / timeout).
2. The message is redelivered; `f-email` retries — visible backoff across the delivery cycle ≈ 1 min / 10 min / 1 h (FR-006-4), 3 retries after the first attempt.
3. On final failure: message dead-letters; `email.failed` emitted (`attempt` = last, `reason` = error code); `dlq_count` > 0 alert fires (P2, TA-10.3).
4. Operator sees it in the DLQ alert + admin email-failure health; can replay manually later.

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

## Exit check

- [ ] Scenario 1: Communication Hub returns 429 for every attempt
- [ ] Scenario 2: a transient failure on the first attempt only
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-006/US-006-02-retry-delivery.md`
- Feature: `TRF-006-email.md` (FR-006-4)
- Plan AC: AC-006-2
- Architecture: TA-5.1, TA-6.7, TA-10.3
- Milestone: T-019
