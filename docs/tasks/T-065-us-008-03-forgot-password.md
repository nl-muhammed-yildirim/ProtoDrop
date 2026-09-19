# T-065 — Recover my account when I forgot the password

**Story:** US-008-03 | **Feature:** F-TRF-008 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-008/US-008-03-forgot-password.md`
**Coarse task (Milestone-Backlog.md):** T-021
**Status:** pending

---

## Scope

As a user who forgot their password, I want to get a magic link that signs me in (or lets me set a new password), so that "forgot" doesn't mean "new account."

**Actor:** Registered user (or anyone — the email goes either way).

**Goal:** A working path from "I forgot" to signed-in, without support.

Happy path:

1. User opens "Forgot password?", types their email, presses **Send link**.
2. "Link sent to {email}" state (identical whether or not the email exists — no account enumeration).
3. The email contains a magic link (`Purpose=2`).
4. Clicking it: if the account has a password set → sign in; then the profile offers "Set a new password" (or MVP: the link signs in directly, and the old password still works until changed).
5. The user is signed in.

## Acceptance criteria

```gherkin
Given I request a forgot-password link for my registered email
When I click the emailed link
Then I am signed in
And the token is marked redeemed (single-use)

Given I request a link for an unregistered email
When the request is made
Then the "Link sent to {email}" state is shown (no "email not found" error)
```

## Edge cases

- **Forgot ≠ password reset in MVP:** the link signs the user in (they can then set a new password from the profile — P1 polish). Documented in the link screen: "You're signed in. Set a new password in your profile."
- Anti-enumeration: identical response bodies for known/unknown emails.

## Exit check

- [ ] Scenario 1: I request a forgot-password link for my registered email
- [ ] Scenario 2: I request a link for an unregistered email
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-008/US-008-03-forgot-password.md`
- Feature: `TRF-008-accounts.md` (FR-008-6)
- Architecture: TA-4.2#11, TA-9.1
- Related: US-008-02 (same token mechanics)
- Milestone: T-021
