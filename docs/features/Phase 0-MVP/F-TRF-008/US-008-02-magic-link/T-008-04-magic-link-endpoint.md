# T-008-04 — Magic link: request + redeem, single-use token (FR-008-1)

**Story:** US-008-02 | **Spec:** FR-008-1/3, AC-008-2, EC-008-3 | **Size:** M
**Depends on:** T-008-02 (session middleware — redeem sets the cookie), T-054 (IEventPublisher + outbox), T-006 (f-email consumer — template set)

---

## Context to read (only these)

- `US-008-02-magic-link.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-008-accounts.md` → FR-008-1/3 + AC-008-2 + EC-008-3 + Technical notes (token format)

## Instructions

1. Add **`RequestMagicLinkCommand`** + endpoint `POST /api/v1/auth/magic-link`: generate a 256-bit random token, store it **hashed** (`sha256`) in an `AuthToken` row with `Purpose=1`, 10-min TTL; send the email via the f-email worker (template `magic-link`, button → the auth screen deep link carrying `?ml={token}`).
2. Add **`RedeemMagicLinkCommand`** + endpoint 10 `POST /api/v1/auth/magic-link/redeem`: look up the token by hash; set `RedeemedAtUtc` (single-use — first redeem wins, later tabs see the expired state); unknown email → create the `AppUser` on redeem (sign-up is sign-in, FR-008-3) with `user_created` emitted before `login_success`.
3. Expired (>10 min) and redeemed tokens are **indistinguishable** — both return the same "This link has expired" state + "Send a new link" button (EC-008-3: don't leak which). The token row stays in `AuthToken` until swept.
4. On success set the session cookie via T-008-02's middleware and return 200 with the profile DTO; telemetry `login_success`.

## Exit check

- [ ] Request → email arrives with a working link (dev log-sender visible)
- [ ] First click signs in (cookie set); second click of the same link → "This link has expired." + "Send a new link" (AC-008-2)
- [ ] Link clicked after 10 min → identical expired state as a redeemed one (EC-008-3)
- [ ] New email + magic link → account created on redeem; `user_created` then `login_success` emitted in order

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; session middleware + f-email consumer in place).
Task T-008-04 — implement magic link request + redeem (milestone task T-021, part 4).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-02-magic-link/US-008-02-magic-link.md (happy path + Alternative flows + Technical notes) and F-TRF-008-accounts.md (FR-008-1/3).
Do exactly:
1. Add RequestMagicLinkCommand + POST /api/v1/auth/magic-link: 256-bit random token, stored hashed (sha256) in an AuthToken row with Purpose=1 and 10-min TTL; email via the f-email worker (template magic-link, button → auth screen deep link carrying ?ml={token}).
2. Add RedeemMagicLinkCommand + endpoint 10 POST /api/v1/auth/magic-link/redeem: look up by hash, set RedeemedAtUtc (single-use — first redeem wins); unknown email → create AppUser on redeem (FR-008-3) with user_created emitted before login_success.
3. Expired (>10 min) and redeemed tokens are indistinguishable — same "This link has expired" state + "Send a new link" button (EC-008-3); the token row stays in AuthToken until swept.
4. On success set the session cookie via T-008-02's middleware, return 200 with the profile DTO; telemetry login_success.
Done when: AC-008-2 holds — first click signs in, second click is invalid (single-use), and a new email gets an account created on redeem.
Constraints: token stored hashed (TA-9.1) — DB leak doesn't reveal working links; rate limit /auth/* 10/min per IP already applies (TA-9.5); no ASP.NET Identity tables (ADR-007).
```
