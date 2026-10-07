# T-008-02 — LoginCommand + endpoint 9 + session middleware (FR-008-1/2)

**Story:** US-008-01 | **Spec:** FR-008-1/2/5, AC-008-1 (login half), EC-008-1/4 | **Size:** M
**Depends on:** T-008-01 (SignupCommand — shared email normalization + `AppUser` row shape), T-056 (Problem+JSON pipeline)

---

## Context to read (only these)

- `US-008-01-signup-password.md` → happy path + Alternative flows
- `../../F-TRF-008-accounts.md` → FR-008-1/2/5 + AC-008-1 + EC-008-1 + Technical notes (session cookie format)

## Instructions

1. Add **`LoginCommand`** (MediatR, `wa.application/UseCases/Auth/`) + endpoint 9 `POST /api/v1/auth/login`. Normalize the email (lowercase + trim — same as T-008-01); look up the `AppUser`; no row **or** wrong password → 401 Problem+JSON **"Wrong password"** — never "user not found" (don't leak which emails exist); re-check min 8 chars; compare via ASP.NET Identity **`PasswordHasher.VerifyHashedPassword`**.
2. Add the **session middleware**: read cookie `wa.session`, parse `Base64(userId|exp|sig)` per TA-9.1 — verify the HMAC-SHA256 signature with `Wa:Jwt:Secret`; when valid and not expired, expose the user id to the endpoints (13–15). **30-day rolling** (TA-9.1): refresh `exp` to now + 30 d and re-sign the cookie whenever remaining lifetime is under 15 days.
3. On successful login set the session cookie (same format, 30-day expiry) and return 200 with the profile DTO — same shape as signup's result.
4. Telemetry: `login_success` on success, `login_failed` on wrong password (TA-10.2).

## Exit check

- [ ] Existing email + correct password → 200, cookie set, `login_success` emitted once
- [ ] Wrong password → 401 "Wrong password" (not "user not found"), `login_failed` emitted
- [ ] Email with different case signs in to the same row (EC-008-1) — login never creates a user
- [ ] Cookie round-trip: a second request carrying the cookie resolves the user id; tampered signature or expired → guest (401 on auth-required endpoints)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; SignupCommand + Problem+JSON pipeline in place).
Task T-008-02 — implement login + session middleware (milestone task T-021, part 2).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-01-signup-password/US-008-01-signup-password.md (happy path + Alternative flows) and F-TRF-008-accounts.md (FR-008-1/2/5).
Do exactly:
1. Add LoginCommand (MediatR, wa.application/UseCases/Auth/) + endpoint 9 POST /api/v1/auth/login. Normalize the email (lowercase + trim); no row or wrong password → 401 Problem+JSON "Wrong password" — never "user not found"; re-check min 8 chars; compare via ASP.NET Identity PasswordHasher.VerifyHashedPassword.
2. Add the session middleware: read cookie wa.session, parse Base64(userId|exp|sig) per TA-9.1 — verify HMAC-SHA256 with Wa:Jwt:Secret; expose the user id to endpoints when valid and not expired; 30-day rolling (TA-9.1) — refresh exp to now + 30 d and re-sign whenever remaining lifetime is under 15 days.
3. On successful login set the session cookie (same format, 30-day expiry) and return 200 with the profile DTO.
4. Telemetry: login_success on success, login_failed on wrong password (TA-10.2).
Done when: AC-008-1's login half holds — correct password signs in, wrong password is "Wrong password" + login_failed, and a signed-in request carries the user id from the cookie.
Constraints: thin endpoint (TA-4.2a); no ASP.NET Identity tables (ADR-007) — AppUser + AuthToken only; email stored lowercased (EC-008-1).
```
