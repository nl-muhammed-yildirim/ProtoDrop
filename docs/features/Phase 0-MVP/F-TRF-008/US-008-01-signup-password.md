# US-008-01 — Sign up and sign in with email + password

**Feature:** F-TRF-008 — Accounts | **Status:** pending

---

**Story:** As a new user, I want to create an account with my email and a password — in one form — so that my transfers get attributed to me and I can use My Files.
**Actor:** New user (or returning user) on the auth screen.
**Goal:** One form does both jobs: unknown email → create, known email → sign in.

## Preconditions

- Auth screen reachable from the top bar (`Sign in`) or after pressing **Continue** as a guest wanting history.

## Happy path

1. User types email + password (min 8, helper text) and presses **Continue**.
2. **Unknown email:** `AppUser` created (email lowercased, `DisplayName` = empty→derived from email local part, plan Free), password hashed with `PasswordHasher`, session cookie set → profile screen.
3. **Known email, correct password:** session cookie set (30-day rolling, TA-9.1) → profile screen.
4. The top bar now shows the avatar (profile, theme, sign out); new transfers are attributed (US-008-05).

## Alternative flows

- **Wrong password:** "Wrong password" inline (no "user not found" — don't leak which emails exist); `login_failed` telemetry.
- **Password < 8 chars:** inline validation before submit.
- **Email case differs from stored:** signs in (lowercased compare, EC-008-1).

## Acceptance criteria

```gherkin
Given I type a new email and a 10-char password
When I press Continue
Then my account is created, a session cookie is set, and the profile screen shows
And my display name defaults from my email

Given I type my existing email with the correct password
When I press Continue
Then I am signed in (same cookie behavior as sign-up)

Given I type an existing email with the wrong password
When I press Continue
Then "Wrong password" is shown
And login_failed is emitted
```

## Edge cases

- Sign-up **is** sign-in (FR-008-3): no separate "you're already registered" path — same form, same button.
- `user_created` telemetry only on the create branch; `login_success` on both.

## UI notes (UI-Reference §5.5)

- Single card, `Sign in` / `Sign up` tabs (copy difference only); fields per spec; **Continue** primary; "Email me a magic link" ghost secondary.
- Password helper: "At least 8 characters."

## Technical notes

- Endpoints 8 (`/auth/signup`) and 9 (`/auth/login`); shared normalization (lowercase, trim).
- Cookie `wa.session` per TA-9.1; 30 d rolling.
- Telemetry: `user_created`, `login_success`, `login_failed` (TA-10.2).

## Links

- Feature: `TRF-008-accounts.md` (FR-008-1, FR-008-3, FR-008-5)
- Plan AC: AC-008-1
- Architecture: TA-4.2#8–9, TA-9.1
- Milestone: T-021
