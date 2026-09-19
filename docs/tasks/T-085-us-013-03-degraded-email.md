# T-085 — Not lose transfers when email is down

**Story:** US-013-03 | **Feature:** F-TRF-013 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-013/US-013-03-degraded-email.md`
**Coarse task (Milestone-Backlog.md):** T-025 (health surface), T-019 (delivery machinery)
**Status:** pending

---

## Scope

As a sender sending a transfer while email delivery is down, I want the send itself to still succeed, so that "email is broken" never means "my transfer failed."

**Actor:** Sender (feels nothing — that's the point), operator (sees the failure health).

**Goal:** email is *eventually consistent*: `transfer.created` succeeds regardless; `f-email` retries, and the admin surface shows the pending/failed count.

Happy path:

1. Sender presses **Send transfer** (F-TRF-002) → the API creates the transfer, stores recipients, emits `transfer.created` → 201 + link screen (the link *works* even though no emails have left yet).
2. `f-email` picks up the event; each failed delivery retries on the Service Bus cycle (≈1 min / 10 min / 1 h, F-TRF-006-4) until Hub recovers.
3. The admin Overview shows the email health: pending/failed count and `email_failures_1h` (FR-013-4; F-TRF-011) — the operator knows email is behind, the user never did.
4. On recovery, the retries deliver everything; `EmailRecipient.NotifiedAtUtc` gets set (F-TRF-006-8).

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

## Exit check

- [ ] Scenario 1: email delivery has been failing and then recovers
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-013/US-013-03-degraded-email.md`
- Feature: `TRF-013-errors.md` (FR-013-4, AC-013-3)
- Related: US-006-01 (normal delivery), US-006-02 (retries/DLQ), US-011-01 (admin health)
- Architecture: TA-5.3, TA-6.2, TA-6.7
- Milestone: T-025 (health surface), T-019 (delivery machinery)
