# T-008-05 — ForgotPasswordCommand + endpoint 11: anti-enumeration link (FR-008-6)

**Story:** US-008-03 | **Spec:** FR-008-6, TA-4.2#11 | **Size:** S
**Depends on:** T-008-04 (magic-link mechanics — same token row + redeem endpoint), T-054 (IEventPublisher + outbox)

---

## Context to read (only these)

- `US-008-03-forgot-password.md` → happy path + Alternative flows + Technical notes
- `../US-008-02-magic-link/US-008-02-magic-link.md` → Technical notes (token mechanics, single-use)
- `../../F-TRF-008-accounts.md` → FR-008-6 + Technical notes

## Instructions

1. Add **`ForgotPasswordCommand`** + endpoint 11 `POST /api/v1/auth/forgot-password`: normalize the email (lowercase + trim); if a row exists, insert an `AuthToken` with **`Purpose=2`**, 10-min TTL, stored hashed (`sha256`) — same token mechanics as T-008-04.
2. Send the email via the f-email worker (template `forgot-password`, subject **"Your ProtoDrop link"**). **Unregistered email: no row, no send** — but the response is identical either way (anti-enumeration; MVP shows the "Link sent to {email}" state without a neutral email).
3. Redeem reuses endpoint 10 as-is (all purposes); after redeeming a `Purpose=2` token the user is simply signed in — the profile offers "Set a new password" (P1 polish; old password still works until changed). No separate reset logic in MVP.
4. Telemetry: request-volume metric on this endpoint (abuse signal) + `login_success` on redeem via T-008-04's path.

## Exit check

- [ ] Registered email → `AuthToken` row with `Purpose=2` created; email sent via worker (dev log-sender visible)
- [ ] Unregistered email → no token row, no send — response body identical to the registered case (no "email not found")
- [ ] Clicking the emailed link signs the user in (endpoint 10 handles `Purpose=2`); second click → expired state (single-use inherited from T-008-04)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; magic-link request/redeem + f-email consumer in place).
Task T-008-05 — implement forgot password (milestone task T-021, part 5).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-03-forgot-password/US-008-03-forgot-password.md (happy path + Alternative flows + Technical notes), US-008-02-magic-link.md (Technical notes — token mechanics), and F-TRF-008-accounts.md (FR-008-6).
Do exactly:
1. Add ForgotPasswordCommand + endpoint 11 POST /api/v1/auth/forgot-password: normalize the email; if a row exists insert an AuthToken with Purpose=2, 10-min TTL, stored hashed (sha256) — same mechanics as T-008-04.
2. Send via the f-email worker (template forgot-password, subject "Your ProtoDrop link"). Unregistered email: no row, no send — response identical either way (anti-enumeration; MVP shows the "Link sent to {email}" state without a neutral email).
3. Redeem reuses endpoint 10 as-is (all purposes); after redeeming a Purpose=2 token the user is simply signed in — profile offers "Set a new password" (P1 polish; old password still works until changed). No separate reset logic in MVP.
4. Telemetry: request-volume metric on this endpoint (abuse signal) + login_success on redeem via T-008-04's path.
Done when: FR-008-6 holds — registered email gets a Purpose=2 token row + emailed link that signs the user in, and an unregistered email produces no row/send with an identical response body.
Constraints: forgot ≠ password reset in MVP (documented on the link screen); token stored hashed (TA-9.1); rate limit /auth/* 10/min per IP already applies (TA-9.5).
```
