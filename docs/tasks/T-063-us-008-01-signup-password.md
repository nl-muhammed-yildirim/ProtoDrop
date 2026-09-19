# T-063 — Sign up and sign in with email + password

**Story:** US-008-01 | **Feature:** F-TRF-008 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-008/US-008-01-signup-password.md`
**Coarse task (Milestone-Backlog.md):** T-021
**Status:** pending

---

## Scope

As a new user, I want to create an account with my email and a password — in one form — so that my transfers get attributed to me and I can use My Files.

**Actor:** New user (or returning user) on the auth screen.

**Goal:** One form does both jobs: unknown email → create, known email → sign in.

Happy path:

1. User types email + password (min 8, helper text) and presses **Continue**.
2. **Unknown email:** `AppUser` created (email lowercased, `DisplayName` = empty→derived from email local part, plan Free), password hashed with `PasswordHasher`, session cookie set → profile screen.
3. **Known email, correct password:** session cookie set (30-day rolling, TA-9.1) → profile screen.
4. The top bar now shows the avatar (profile, theme, sign out); new transfers are attributed (US-008-05).

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

## Exit check

- [ ] Scenario 1: I type a new email and a 10-char password
- [ ] Scenario 2: I type my existing email with the correct password
- [ ] Scenario 3: I type an existing email with the wrong password
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-008/US-008-01-signup-password.md`
- Feature: `TRF-008-accounts.md` (FR-008-1, FR-008-3, FR-008-5)
- Plan AC: AC-008-1
- Architecture: TA-4.2#8–9, TA-9.1
- Milestone: T-021
