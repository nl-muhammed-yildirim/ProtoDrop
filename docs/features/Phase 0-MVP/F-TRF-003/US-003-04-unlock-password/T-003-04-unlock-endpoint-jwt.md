# T-003-04 — Endpoint 5: unlock, PBKDF2 verify → 7-day JWT (T-015)

**Story:** US-003-04 | **Spec:** FR-003-6, AC-003-2 (server half), TA-4.2#5/TA-9.1 | **Size:** M
**Depends on:** T-002-06 (PasswordHash written at send), T-003-01 (endpoint 4 accepts the token on subsequent calls)

---

## Context to read (only these)

- `US-003-04-unlock-password.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-003-recipient-page.md` → FR-003-6 + AC-003-2 + Test plan (unlock round-trip line)

## Instructions

1. Add **endpoint 5** `POST /api/v1/public/transfers/{linkId}/unlock`: body `{ password }`; verify against `Transfer.PasswordHash` with the same **`PasswordHasher<string>`** (PBKDF2-SHA256, TA-9.1) used at send time — constant-time comparison on unlock.
2. Correct → return a **7-day JWT** (HS256), claims `{ sub: linkId, pwd: true, exp: now+7d, jti }` (TA-4.2#5). Subsequent endpoint 4/6/7 calls accept it via `?t=` or header — no re-prompt within the token's life.
3. Wrong → **`WRONG_PASSWORD`** Problem+JSON (TA-4.1.3); no rate-limit lockout in MVP, but emit telemetry.
4. The 7-day token and the transfer's `ExpiresAtUtc` are independent — the earlier of the two wins; an expired transfer shows the expired screen regardless of a valid token (US-003-05).
5. Telemetry: **`password_correct`** / **`password_wrong`** (TA-10.2) — never include the password value in any event.

## Exit check

- [ ] Correct password → 200 + JWT with claims `{ sub, pwd:true, exp≈now+7d, jti }`; re-querying endpoint 4 with `?t=` returns the file list (no re-prompt)
- [ ] Wrong password → `WRONG_PASSWORD` Problem+JSON; file list still not visible
- [ ] Both telemetry events fire without ever carrying the password value

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; endpoint 4 + PasswordHash column in place).
Task T-003-04 — implement unlock (milestone task T-015, server half).
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-04-unlock-password/US-003-04-unlock-password.md (happy path + Alternative flows + Technical notes) and F-TRF-003-recipient-page.md (FR-003-6).
Do exactly:
1. Add endpoint 5 POST /api/v1/public/transfers/{linkId}/unlock: body { password }; verify against Transfer.PasswordHash with the same PasswordHasher<string> (PBKDF2-SHA256, TA-9.1) used at send time — constant-time comparison on unlock.
2. Correct → return a 7-day JWT (HS256), claims { sub: linkId, pwd: true, exp: now+7d, jti } (TA-4.2#5). Subsequent endpoint 4/6/7 calls accept it via ?t= or header — no re-prompt within the token's life.
3. Wrong → WRONG_PASSWORD Problem+JSON (TA-4.1.3); no rate-limit lockout in MVP, but emit telemetry.
4. Keep the 7-day token and the transfer's ExpiresAtUtc independent — the earlier of the two wins; an expired transfer shows the expired screen regardless of a valid token (US-003-05).
5. Emit password_correct / password_wrong telemetry (TA-10.2) — never include the password value in any event.
Done when: AC-003-2's server half holds — correct unlock mints the 7-day JWT and endpoint 4 accepts it via ?t=, wrong unlock returns WRONG_PASSWORD Problem+JSON, and both telemetry events fire without carrying the password.
Constraints: PasswordHasher is the only hasher (TA-9.1); the token is session-scoped state on the client (T-003-05 stores it in sessionStorage) — do not set a cookie here.
```
