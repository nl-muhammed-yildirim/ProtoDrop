# T-044 — Unlock a password-protected transfer

**Story:** US-003-04 | **Feature:** F-TRF-003 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-003/US-003-04-unlock-password.md`
**Coarse task (Milestone-Backlog.md):** T-015
**Status:** pending

---

## Scope

As the recipient of a password-protected transfer, I want to enter the password and get in, so that only the intended reader can see the files — and so a refresh doesn't ask me again.

**Actor:** Recipient who knows the password (out-of-band), on a transfer with a password set (F-TRF-002-3).

**Goal:** One password field → correct entry unlocks the file list; the unlocked state survives a browser refresh.

Happy path:

1. Recipient opens the link: the page is a **single centered password card** — no file list, no "From:" details beyond the sender name (FR-003-6, UI-Reference §5.3).
2. Recipient types the password and submits.
3. Correct password → endpoint 5 verifies, returns a 7-day JWT; the app stores it in `sessionStorage` and re-queries the page.
4. The file list + downloads now render (US-003-01); a **refresh in the same tab** does not re-prompt (token sent as `?t=` / header on subsequent calls, TA-4.2#5).

## Acceptance criteria

```gherkin
  When the page loads
  Then only the password field is visible (no file list, no SAS minted)

Given I enter the correct password
When I submit
Then the file list and downloads become available
And the unlock token is stored in sessionStorage
And a page refresh in the same tab does not re-prompt

Given I enter a wrong password
When I submit
Then the field shakes and "Wrong password" is shown
And the file list is still not visible
And a password_wrong telemetry event is emitted
```

## Edge cases

- Password with spaces/Unicode: compared after the same PBKDF2 path as at send time (no trimming surprises — sender and recipient sides share `PasswordHasher`, TA-9.1).
- The unlock JWT is 7 days but the transfer's `ExpiredAtUtc` is usually sooner — the earlier of the two wins; no error, just the expired screen (US-003-05).
- Token in `sessionStorage` (not cookie): tab-isolated by design; a second tab re-prompts (documented trade-off, F-TRF-003 technical notes).

## Exit check

- [ ] Scenario 1: I enter the correct password
- [ ] Scenario 2: I enter a wrong password
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-003/US-003-04-unlock-password.md`
- Feature: `TRF-003-recipient-page.md` (FR-003-6, AC-003-2)
- Related: US-002-03 (sender sets it), US-013-01 (error mapping)
- Architecture: TA-4.2#5, TA-9.1
- Design: UI-Reference §5.3
- Milestone: T-015
