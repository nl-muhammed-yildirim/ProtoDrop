# T-056 — Unsubscribe from a sender's emails

**Story:** US-006-03 | **Feature:** F-TRF-006 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-006/US-006-03-unsubscribe.md`
**Coarse task (Milestone-Backlog.md):** T-019
**Status:** pending

---

## Scope

As a recipient who receives too many transfers from one particular sender, I want to unsubscribe, so that I stop getting their emails without losing my files.

**Actor:** Any recipient who received a transfer email.

**Goal:** One click → permanently muted for that sender (per-sender, not global).

Happy path:

1. Recipient clicks "Unsubscribe" in the email footer → `GET /unsubscribe/{token}`.
2. Token = HMAC of the suppression row id; the API inserts `EmailSuppression (Address, SenderEmail, CreatedAtUtc)` (idempotent via unique constraint).
3. Redirect to `/` with a success toast: "You are on the list no more."
4. Future transfers from the same sender's email no longer notify this address (FR-006-5).
5. The suppression row is visible (and removable) in the admin dashboard (F-TRF-011-2).

## Acceptance criteria

```gherkin
Given a recipient who unsubscribes from sender s@x.com
When the same sender later sends another transfer to that address
Then no email is sent
And an EmailSuppression row exists for (address, s@x.com)
And the address still works for transfers from other senders

Given the unsubscribe link is opened twice
When the second request arrives
Then it is a no-op with the same success landing page
```

## Edge cases

- Suppression is per (address, sender) — a different sender still emails (that's the design; documented).
- The check happens in `f-email` at send time; a suppression created mid-batch can at most save or miss one email (EC-006-3, documented).
- Token validation failure (tampered) → 404 (don't leak "token invalid" detail), warn telemetry.

## Exit check

- [ ] Scenario 1: a recipient who unsubscribes from sender s@x.com
- [ ] Scenario 2: the unsubscribe link is opened twice
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-006/US-006-03-unsubscribe.md`
- Feature: `TRF-006-email.md` (FR-006-5)
- Plan AC: AC-006-3
- Architecture: TA-6.7, TA-3.2
- Related: US-011-04 (admin can list/remove suppressions)
- Milestone: T-019
