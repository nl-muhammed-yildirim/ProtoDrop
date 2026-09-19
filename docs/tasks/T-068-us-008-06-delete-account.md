# T-068 — Delete my account (GDPR)

**Story:** US-008-06 | **Feature:** F-TRF-008 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-008/US-008-06-delete-account.md`
**Coarse task (Milestone-Backlog.md):** T-021
**Status:** pending

---

## Scope

As a user leaving, I want to delete my account and know what happens to my files, so that erasure is honest and reversible-enough.

**Actor:** Signed-in user in the profile danger zone.

**Goal:** One confirmed click → account gone, transfers survive as anonymous, no orphaned PII.

Happy path:

1. User presses **Delete account** → confirm modal with GDPR copy: "Your account and profile are deleted. Your active transfers keep working and show 'From: Ada Lovelace' without your email."
2. `DELETE /auth/me`:
3. `account_deleted` emitted; session cleared; top bar → `Sign in`.
4. The user's My Files is now empty (no owner).

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

## Exit check

- [ ] Scenario 1: I have 3 active transfers and I delete my account
- [ ] Scenario 2: I delete my account
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-008/US-008-06-delete-account.md`
- Feature: `TRF-008-accounts.md` (FR-008-8)
- Architecture: TA-4.2#13, TA-9.4 (PII policy)
- Milestone: T-021
