# T-008-01 — SignupCommand + endpoint 8: sign-up is sign-in (FR-008-3)

**Story:** US-008-01 | **Spec:** FR-008-1/3/5, AC-008-1, EC-008-1/4 | **Size:** M
**Depends on:** T-056 (Problem+JSON pipeline), T-052 (WaDbContext — `AppUser` table seeded)

---

## Context to read (only these)

- `US-008-01-signup-password.md` → happy path + Alternative flows
- `../../F-TRF-008-accounts.md` → FR-008-1/3/5 + AC-008-1 + EC-008-1/4 + Technical notes

## Instructions

1. Add **`SignupCommand`** (MediatR, `wa.application/UseCases/Auth/`) + endpoint 8 `POST /api/v1/auth/signup`.
2. Sign-up **is** sign-in: unknown email → create `AppUser` (email lowercased + trimmed, `DisplayName` derived from the email local part when empty, plan Free); known email → same result as a login (no "email taken" error — EC-008-4).
3. Password rules: min 8 chars (inline validation upstream; server re-checks), hashed with ASP.NET Identity **`PasswordHasher`** (PBKDF2-SHA256) into `AppUser.PasswordHash`.
4. On success set the session cookie (`wa.session`, format per TA-9.1 — the middleware lands in T-008-02; here just emit it from the command result) and return 200 with the profile DTO.

## Exit check

- [ ] New email + 10-char password → account created, cookie set, `user_created` emitted once (AC-008-1)
- [ ] Same email again with a different case signs up into the same row — no duplicate (EC-008-1/4)
- [ ] Wrong-password path is not here (T-008-02) — signup never rejects an existing email

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; WaDbContext + Problem+JSON pipeline in place).
Task T-008-01 — implement signup (milestone task T-021, part 1).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-01-signup-password/US-008-01-signup-password.md (happy path + Alternative flows) and F-TRF-008-accounts.md (FR-008-1/3/5).
Do exactly:
1. Add SignupCommand (MediatR, wa.application/UseCases/Auth/) + endpoint 8 POST /api/v1/auth/signup.
2. Sign-up is sign-in: unknown email → create AppUser (email lowercased + trimmed, DisplayName derived from the email local part when empty, plan Free); known email → same result as a login (no "email taken" error — EC-008-4).
3. Password rules: min 8 chars (server re-checks), hashed with ASP.NET Identity PasswordHasher (PBKDF2-SHA256) into AppUser.PasswordHash.
4. On success set the session cookie (wa.session, format per TA-9.1 — middleware lands in T-008-02; here just emit it from the command result) and return 200 with the profile DTO.
Done when: AC-008-1 holds — new email creates the account with a session cookie, user_created fires once, and re-signing up with a different case of an existing email finds the same row.
Constraints: thin endpoint (TA-4.2a); no ASP.NET Identity tables (ADR-007) — AppUser + AuthToken only; email stored lowercased (EC-008-1).
```
