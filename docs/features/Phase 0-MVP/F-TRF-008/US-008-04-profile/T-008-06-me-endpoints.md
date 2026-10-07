# T-008-06 — GET/PATCH/DELETE /auth/me + logout (endpoint 12–13)

**Story:** US-008-04 (+ US-008-06 delete half) | **Spec:** FR-008-4/8, TA-4.2#12–13 | **Size:** M
**Depends on:** T-008-02 (session middleware — these endpoints require the cookie), T-054 (IEventPublisher + outbox)

---

## Context to read (only these)

- `US-008-04-profile.md` → happy path + Alternative flows + Technical notes
- `../US-008-06-delete-account/US-008-06-delete-account.md` → happy path + Technical notes (re-home SQL)
- `../../F-TRF-008-accounts.md` → FR-008-4/8 + EC-008-2 + Technical notes

## Instructions

1. **GET /auth/me** (endpoint 13, cookie required): return the profile DTO — display name, theme, plan, email. No row for the session user id (deleted) → 404.
2. **PATCH /auth/me**: partial update via `UpdateProfileCommand` — only provided fields change (`displayName`, `theme`). Empty display name → server falls back to the email local part (never "From: (blank)"). Unicode round-trips (`NVARCHAR(100)`).
3. **POST /auth/logout** (endpoint 12): clear the session cookie; top bar returns to `Sign in`. No telemetry event required beyond metrics.
4. **DELETE /auth/me**: transaction over user + transfer re-homing — soft-delete the `AppUser` (`DeletedAtUtc`), then `UPDATE Transfer SET OwnerAppUserId = NULL WHERE OwnerAppUserId=@me AND Status IN (0,1,3)` (drafts, active, download-limit; expired rows die via jobs anyway) keeping `SenderName` intact; profile-specific rows removed (suppressions kept — keyed by address pair). Emit **`account_deleted`** carrying the userId (PII sweep reference, TA-5.3); clear the session cookie.

## Exit check

- [ ] GET /auth/me with a valid cookie → profile DTO (name, theme, plan, email)
- [ ] PATCH displayName only → name changes, theme untouched; empty name falls back to the email local part
- [ ] Logout clears the cookie — next GET /auth/me is 401/404 (guest)
- [ ] DELETE /auth/me → `AppUser` soft-deleted, all owned transfers have `OwnerAppUserId=NULL` with `SenderName` intact, recipient pages still work, `account_deleted` emitted once (EC-008-2)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; session middleware + outbox in place).
Task T-008-06 — implement the /auth/me endpoints + logout (milestone task T-021, part 6).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-04-profile/US-008-04-profile.md (happy path + Alternative flows + Technical notes), US-008-06-delete-account.md (happy path + Technical notes — re-home SQL), and F-TRF-008-accounts.md (FR-008-4/8).
Do exactly:
1. GET /auth/me (endpoint 13, cookie required): profile DTO — display name, theme, plan, email; no row → 404.
2. PATCH /auth/me via UpdateProfileCommand: partial update (only provided fields change); empty displayName falls back to the email local part; Unicode round-trips (NVARCHAR(100)).
3. POST /auth/logout (endpoint 12): clear the session cookie.
4. DELETE /auth/me: transaction — soft-delete AppUser (DeletedAtUtc), then UPDATE Transfer SET OwnerAppUserId = NULL WHERE OwnerAppUserId=@me AND Status IN (0,1,3) keeping SenderName intact; profile-specific rows removed (suppressions kept); emit account_deleted carrying the userId (TA-5.3); clear the session cookie.
Done when: FR-008-4 and FR-008-8 hold — profile read/update/logout work via the cookie, and DELETE re-homes every owned transfer to anonymous with SenderName preserved.
Constraints: PATCH is partial (only provided fields change); soft-delete keeps the 30-day operator undo window (hard delete is P1 admin); existing transfers' SenderName is a snapshot at send time — never rewritten by profile edits (FR-002-4).
```
