# US-008-02 — Sign in with a magic link

**Feature:** F-TRF-008 — Accounts | **Status:** pending

---

**Story:** As a user who hates passwords, I want an emailed link that signs me in — no password to remember, and no "password" in my head if I'm a one-off sender.
**Actor:** Any user (existing or new email) on the auth screen.
**Goal:** One email, one tap, signed in — with a link that can't be reused.

## Preconditions

- Email delivery works (F-TRF-006 worker).

## Happy path

1. User types their email and presses **Email me a magic link**.
2. "Link sent to {email}" state; the email arrives with a button → `?ml={token}`.
3. Clicking it redeems the token (`AuthToken` row: `Purpose=1`, `RedeemedAtUtc` set) → session cookie → profile.
4. The token is **single-use**: a second click (same tab, or a forwarded link) → "This link has expired." + "Send a new link."

## Alternative flows

- **New email + magic link:** account created on redeem (sign-up is sign-in, FR-008-3) — `user_created` then `login_success`.
- **Expired (10 min):** same "link expired" state as a redeemed one (indistinguishable on purpose — don't leak which).
- **Multiple tabs:** the first redeem wins; later tabs see the expired state (EC: single-use by design).

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

## UI notes

- "Link sent to {email}" state replaces the form (no double-submission possible); "Didn't get it? Send another" ghost (10 s cooldown counter).
- Expired state: one line + **Send a new link** button (UI-Reference §5.5).

## Technical notes

- `POST /auth/magic-link` (implicit in forgot/redeem endpoints 10–11 mapping; feature file FR-008-1) → `AuthToken` insert; `POST /auth/magic-link/redeem` (endpoint 10).
- `Purpose`: 0=signup, 1=login, 2=forgot (TA-3.2).
- Telemetry: `login_success` (and `user_created` when the account was created on redeem).

## Links

- Feature: `TRF-008-accounts.md` (FR-008-1)
- Plan AC: AC-008-2
- Architecture: TA-9.1, TA-4.2#10–11
- Related: US-008-03 (forgot password reuses this mechanism)
- Milestone: T-021
