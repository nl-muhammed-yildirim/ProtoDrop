# US-013-03 — Not lose transfers when email is down

**Feature:** F-TRF-013 — Errors, 404s & Degraded States | **Status:** pending

---

**Story:** As a sender sending a transfer while email delivery is down, I want the send itself to still succeed, so that "email is broken" never means "my transfer failed."
**Actor:** Sender (feels nothing — that's the point), operator (sees the failure health).
**Goal:** email is *eventually consistent*: `transfer.created` succeeds regardless; `f-email` retries, and the admin surface shows the pending/failed count.

## Preconditions

- Email delivery (Communication Hub) is failing or down at send time; the transfer itself is valid.

## Happy path

1. Sender presses **Send transfer** (F-TRF-002) → the API creates the transfer, stores recipients, emits `transfer.created` → 201 + link screen (the link *works* even though no emails have left yet).
2. `f-email` picks up the event; each failed delivery retries on the Service Bus cycle (≈1 min / 10 min / 1 h, F-TRF-006-4) until Hub recovers.
3. The admin Overview shows the email health: pending/failed count and `email_failures_1h` (FR-013-4; F-TRF-011) — the operator knows email is behind, the user never did.
4. On recovery, the retries deliver everything; `EmailRecipient.NotifiedAtUtc` gets set (F-TRF-006-8).

## Alternative flows

- **Final failure (all retries exhausted):** the message dead-letters (DLQ, 30-day retention) + `email.failed` telemetry + the `DLQ_COUNT > 0` admin alert (US-006-02). The link is still working; the operator can re-trigger.
- **Link-only transfers:** no email dependency at all (`emails: []` short-circuits the worker, F-TRF-006-6).
- **Recipient who got no email yet:** opening the link directly works fine — email is a *notification*, not a gate (the link is the source of truth).

## Acceptance criteria

```gherkin
  When the transfer is sent
  Then the send succeeds (the link works, status Active)
  And the email health metrics show the pending/failed count

Given email delivery has been failing and then recovers
When the retry cycle runs
Then pending notifications are delivered (NotifiedAtUtc set)
And no duplicate emails are sent for the same event
```

## Edge cases

- **No user-visible "email down" state on the send screen** — by design (the sender already has the link; the emails are a convenience, FR-013-4). The one exception: nothing here changes the link screen UX.
- **Duplicate event delivery:** consumer dedups by `eventId` + `NotifiedAtUtc` (F-TRF-006 EC-006-4) — the eventual-consistency machinery must not *create* duplicates while recovering.
- **Long outage (> DLQ):** messages in the DLQ are replayable by the operator from admin (F-TRF-011-4 audit trail shows them).

## UI notes

- Sender side: no new UI — the send confirmation screen is unchanged ("Emails are on their way" is *not* shown during an outage; MVP copy stays simple and correct).
- Admin side: email health panel on Overview (F-TRF-011, US-011-01): pending count, failed count, last-successful-delivery age, DLQ count.

## Technical notes

- The API never calls Hub synchronously — `transfer.created` is the seam (TA-5.3); Hub is only touched inside `f-email` (concurrency 10, 5-min timeout, TA-6.2).
- Metrics: `email_failures_1h`; alert threshold 50/h (TA-10.3); DLQ alert `DLQ_COUNT > 0`.
- Replay path: admin re-queues DLQ messages (F-TRF-011-4).

## Links

- Feature: `TRF-013-errors.md` (FR-013-4, AC-013-3)
- Related: US-006-01 (normal delivery), US-006-02 (retries/DLQ), US-011-01 (admin health)
- Architecture: TA-5.3, TA-6.2, TA-6.7
- Milestone: T-025 (health surface), T-019 (delivery machinery)
