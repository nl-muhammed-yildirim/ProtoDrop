# T-002-06 — Password at send: PBKDF2 hash into Transfer.PasswordHash (FR-002-3/4)

**Story:** US-002-03 | **Spec:** FR-002-3 (password part), TA-9.1, TA-3.2 `Transfer.PasswordHash` | **Size:** M
**Depends on:** T-002-04 (SendTransferCommand — the hash is written there)

---

## Context to read (only these)

- `US-002-03-password.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-002-transfer-link.md` → FR-002-3/4 + Test plan (unit line: password part)

## Instructions

1. Extend **`SendTransferCommand`** to accept an optional password; when set, hash it with ASP.NET Identity **`PasswordHasher<string>`** (PBKDF2-SHA256 — the same hasher for users and transfers, TA-9.1) into `Transfer.PasswordHash` (`VARCHAR(256)`); the plain password is never persisted.
2. No password set → column stays null; the recipient page then shows the file list directly (no gate). Password is set at send time only — no later editing in MVP.
3. Unit tests: hash round-trips via `Verify`, two transfers with the same password produce different hashes (per-transfer salt), and a null password leaves the column null.

## Exit check

- [ ] Sending with a password stores a PBKDF2 hash (not plaintext) in `Transfer.PasswordHash`
- [ ] Sending without one leaves the column null — recipient page un-gated
- [ ] Unit tests pass: verify round-trip, salt uniqueness, null path

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; SendTransferCommand in place).
Task T-002-06 — add password handling to send.
Read first (only): docs/features/Phase 0-MVP/F-TRF-002/US-002-03-password/US-002-03-password.md (happy path + Alternative flows + Technical notes) and F-TRF-002-transfer-link.md (FR-002-3).
Do exactly:
1. Extend SendTransferCommand to accept an optional password; when set, hash it with ASP.NET Identity PasswordHasher<string> (PBKDF2-SHA256 — the same hasher for users and transfers, TA-9.1) into Transfer.PasswordHash (VARCHAR(256)); the plain password is never persisted.
2. No password set → column stays null; the recipient page then shows the file list directly (no gate). Password is set at send time only — no later editing in MVP.
3. Add unit tests: hash round-trips via Verify, two transfers with the same password produce different hashes (per-transfer salt), and a null password leaves the column null.
Done when: all three unit tests pass and the integration path stores PBKDF2 output, never plaintext.
Constraints: PasswordHasher is the only hasher allowed (TA-9.1) — no bCrypt/Argon2 at M0; the recipient-side gate itself is F-TRF-003's scope (T-015).
```
