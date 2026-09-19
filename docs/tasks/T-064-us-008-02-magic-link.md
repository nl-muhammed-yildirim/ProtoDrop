# T-064 — Sign in with a magic link

**Story:** US-008-02 | **Feature:** F-TRF-008 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-008/US-008-02-magic-link.md`
**Coarse task (Milestone-Backlog.md):** T-021
**Status:** pending

---

## Scope

As a user who hates passwords, I want an emailed link that signs me in — no password to remember, and no "password" in my head if I'm a one-off sender.

**Actor:** Any user (existing or new email) on the auth screen.

**Goal:** One email, one tap, signed in — with a link that can't be reused.

Happy path:

1. User types their email and presses **Email me a magic link**.
2. "Link sent to {email}" state; the email arrives with a button → `?ml={token}`.
3. Clicking it redeems the token (`AuthToken` row: `Purpose=1`, `RedeemedAtUtc` set) → session cookie → profile.
4. The token is **single-use**: a second click (same tab, or a forwarded link) → "This link has expired." + "Send a new link."

## Acceptance criteria

```gherkin
Given I request a magic link for my email
When I click it once
Then I am signed in with a session cookie

Given I click the same magic link a second time
When the redeem request runs
Then the second click is invalid (single-use)
And I see "This link has expired." with a "Send a new link." button

Given the link was not clicked within 10 minutes
When I click it
Then it behaves exactly like a redeemed link (expired state)
```

## Edge cases

- Token: 256-bit random, stored **hashed** (`sha256`) — a DB leak doesn't reveal working links (TA-9.1).
- The magic link email reuses the email worker (F-TRF-006), template `magic-link`.
- Rate limit: `/auth/*` 10/min per IP (TA-9.5) slows spam of link emails.

## Exit check

- [ ] Scenario 1: I request a magic link for my email
- [ ] Scenario 2: I click the same magic link a second time
- [ ] Scenario 3: the link was not clicked within 10 minutes
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-008/US-008-02-magic-link.md`
- Feature: `TRF-008-accounts.md` (FR-008-1)
- Plan AC: AC-008-2
- Architecture: TA-9.1, TA-4.2#10–11
- Related: US-008-03 (forgot password reuses this mechanism)
- Milestone: T-021
