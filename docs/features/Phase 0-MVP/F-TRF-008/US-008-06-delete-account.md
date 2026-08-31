# US-008-06 — Delete my account (GDPR)

**Feature:** F-TRF-008 — Accounts | **Status:** pending

---

**Story:** As a user leaving, I want to delete my account and know what happens to my files, so that erasure is honest and reversible-enough.
**Actor:** Signed-in user in the profile danger zone.
**Goal:** One confirmed click → account gone, transfers survive as anonymous, no orphaned PII.

## Preconditions

- Signed in; profile danger zone visible.

## Happy path

1. User presses **Delete account** → confirm modal with GDPR copy: "Your account and profile are deleted. Your active transfers keep working and show 'From: Ada Lovelace' without your email."
2. `DELETE /auth/me`:
   - `AppUser` soft-deleted (`DeletedAtUtc`);
   - active transfers re-homed: `OwnerAppUserId = NULL` (`SenderName` kept — recipient pages unaffected);
   - profile-specific rows removed (suppressions kept, keyed by address pair).
3. `account_deleted` emitted; session cleared; top bar → `Sign in`.
4. The user's My Files is now empty (no owner).

## Alternative flows

- **Re-creation:** same email signs up again → new user id; old transfers remain anonymous (not re-attached — documented).
- **Deletion while a transfer is in flight of being re-sent:** re-send finalize later may fail `FILES_GONE` (the blobs only die with the transfer's own life — normally not an issue).

## Acceptance criteria

```gherkin
Given I have 3 active transfers and I delete my account
When the delete completes
Then my AppUser row is soft-deleted
And all 3 transfers have OwnerAppUserId NULL with their SenderName intact
And the recipient pages still work

Given I delete my account
When I sign up again with the same email
Then a new account is created and my old transfers stay anonymous
```

## Edge cases

- GDPR erasure includes telemetry PII reference: `account_deleted` event carries the userId so a later PII sweep can scrub (F-XCT-004, outline).
- Soft-delete (not hard) keeps the 30-day undo window for the operator; hard delete is a P1 admin action.

## UI notes

- Danger zone: red-tinted **Delete account** (ghost-danger), confirm modal names exactly what survives and what doesn't (UI-Reference §5.5).
- Post-delete: redirect to `/` with toast "Your account was deleted."

## Technical notes

- `DeleteAccountCommand` (endpoint 13 DELETE): transaction over user + transfer re-homing; `account_deleted` (TA-5.3).
- Re-home SQL: `UPDATE Transfer SET OwnerAppUserId = NULL WHERE OwnerAppUserId=@me AND Status IN (0,1,3)` (drafts, active, download-limit; expired rows get deleted by jobs anyway).

## Links

- Feature: `TRF-008-accounts.md` (FR-008-8)
- Architecture: TA-4.2#13, TA-9.4 (PII policy)
- Milestone: T-021
