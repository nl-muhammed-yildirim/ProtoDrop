# US-002-03 — Protect my transfer with a password

**Feature:** F-TRF-002 — Transfer Creation & Link | **Status:** pending

---

**Story:** As a sender, I can set an optional password on the transfer so that only people who know the password can see and download the files, even if the link leaks.
**Actor:** Guest or signed-in user on the link screen.
**Goal:** Gate the recipient page behind a password without any account requirement on either side.

## Preconditions

- Transfer finalized; link screen open. Password is **optional** per plan (all MVP plans allow it).

## Happy path

1. User optionally types a password in the password field (show/hide toggle).
2. On **Send transfer**, the password is hashed with PBKDF2 (ASP.NET Identity `PasswordHasher`, per-transfer salt) into `Transfer.PasswordHash`.
3. The transfer is active; the recipient page is a single password gate (F-TRF-003, US-003-04).
4. A correct password mints a 7-day unlock JWT so the recipient is not re-prompted (F-TRF-003-6, TA-9.1).

## Alternative flows

- **No password set:** the recipient page shows the file list directly (no gate).
- **Password cleared after send:** not supported in MVP (password is set at send time only).
- **Wrong password:** shake animation + "Wrong password" (no lockout in MVP; `password_wrong` telemetry).

## Acceptance criteria

```gherkin
Given I set a password and send the transfer
When a recipient opens the link
Then only the password field is visible (no file list, no sizes)
When the recipient enters the correct password
Then the file list appears and a 7-day unlock token is stored client-side
When the recipient refreshes
Then they are not re-prompted

Given I do not set a password
When a recipient opens the link
Then the file list is shown directly
```

## Edge cases

- Password minimum length: 4 (soft guidance in the UI); no common-password check in MVP.
- The hash column is `VARCHAR(256)`; the plain password is never persisted (TA-3.2).
- Constant-time comparison on unlock (TA-9.1).
- Case-sensitive by default; helper text says "Passwords are case-sensitive".

## UI notes

- Password field: show/hide eye icon, `--radius-sm`, same style as other inputs (UI-Reference §4.5).
- On the recipient side the gate is a single centered card (US-003-04) — no file list leak, no byte leak.
- Helper: "Only people with this password can download."

## Technical notes

- Hashing: `PasswordHasher<string>` (PBKDF2-SHA256) — same hasher for users and transfers (TA-9.1).
- Unlock: `POST /api/v1/public/transfers/{linkId}/unlock` → `{ token }` JWT HS256, claims `{ sub: linkId, pwd: true, exp: now+7d, jti }` (TA-4.2#5, TA-9.1).
- Telemetry: `password_correct` / `password_wrong` (TA-10.2).

## Links

- Feature: `TRF-002-transfer-link.md` (password part of FR-002-3/FR-002-4)
- Related: US-003-04 (recipient-side unlock)
- Architecture: TA-4.2#5, TA-9.1, TA-3.2 `Transfer.PasswordHash`
- Design: UI-Reference §5.2, §5.3
- Milestone: T-011, T-015
