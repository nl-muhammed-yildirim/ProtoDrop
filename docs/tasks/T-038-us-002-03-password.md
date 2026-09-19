# T-038 — Protect my transfer with a password

**Story:** US-002-03 | **Feature:** F-TRF-002 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-002/US-002-03-password.md`
**Coarse task (Milestone-Backlog.md):** T-011, T-015
**Status:** pending

---

## Scope

As a sender, I can set an optional password on the transfer so that only people who know the password can see and download the files, even if the link leaks.

**Actor:** Guest or signed-in user on the link screen.

**Goal:** Gate the recipient page behind a password without any account requirement on either side.

Happy path:

1. User optionally types a password in the password field (show/hide toggle).
2. On **Send transfer**, the password is hashed with PBKDF2 (ASP.NET Identity `PasswordHasher`, per-transfer salt) into `Transfer.PasswordHash`.
3. The transfer is active; the recipient page is a single password gate (F-TRF-003, US-003-04).
4. A correct password mints a 7-day unlock JWT so the recipient is not re-prompted (F-TRF-003-6, TA-9.1).

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

## Exit check

- [ ] Scenario 1: I set a password and send the transfer
- [ ] Scenario 2: I do not set a password
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-002/US-002-03-password.md`
- Feature: `TRF-002-transfer-link.md` (password part of FR-002-3/FR-002-4)
- Related: US-003-04 (recipient-side unlock)
- Architecture: TA-4.2#5, TA-9.1, TA-3.2 `Transfer.PasswordHash`
- Design: UI-Reference §5.2, §5.3
- Milestone: T-011, T-015
