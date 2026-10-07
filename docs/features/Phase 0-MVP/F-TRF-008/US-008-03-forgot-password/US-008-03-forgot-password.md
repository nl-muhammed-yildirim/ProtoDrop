# US-008-03 — Recover my account when I forgot the password

**Feature:** F-TRF-008 — Accounts | **Status:** pending

---

**Story:** As a user who forgot their password, I want to get a magic link that signs me in (or lets me set a new password), so that "forgot" doesn't mean "new account."
**Actor:** Registered user (or anyone — the email goes either way).
**Goal:** A working path from "I forgot" to signed-in, without support.

## Preconditions

- The user's email is registered (or not — we don't say which).

## Happy path

1. User opens "Forgot password?", types their email, presses **Send link**.
2. "Link sent to {email}" state (identical whether or not the email exists — no account enumeration).
3. The email contains a magic link (`Purpose=2`).
4. Clicking it: if the account has a password set → sign in; then the profile offers "Set a new password" (or MVP: the link signs in directly, and the old password still works until changed).
5. The user is signed in.

## Alternative flows

- **Unregistered email:** same "Link sent" state; no email is sent (or a neutral "if registered" email — MVP: no send, state shown); no data leak.
- **Link reused/expired:** same behavior as US-008-02 (single-use, 10 min).

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

## UI notes

- Flow: auth card → "Forgot password?" ghost under the password field → email field → "Link sent to {email}".
- Profile (P1 niceness, MVP-allowed): "New password" field pair under the plan line.

## Technical notes

- `ForgotPasswordCommand` (endpoint 11): creates `AuthToken (Purpose=2)`; email via worker (template `forgot-password`).
- Redeem endpoint 10 handles all purposes; post-redeem UI routes to profile with a "set password" hint.
- Telemetry: `login_success` on redeem; metrics for request volume (abuse signal).

## Links

- Feature: `TRF-008-accounts.md` (FR-008-6)
- Architecture: TA-4.2#11, TA-9.1
- Related: US-008-02 (same token mechanics)
- Milestone: T-021
