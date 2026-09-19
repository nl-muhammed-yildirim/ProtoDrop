# T-067 — See my transfers in My Files

**Story:** US-008-05 | **Feature:** F-TRF-008 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-008/US-008-05-attribution.md`
**Coarse task (Milestone-Backlog.md):** T-021, T-022
**Status:** pending

---

## Scope

As a signed-in user, I want every transfer I create to appear in My Files automatically, so that I have one place to find, re-send, or delete my files.

**Actor:** Signed-in user creating transfers.

**Goal:** Attribution by default — no extra "save to account" checkbox.

Happy path:

1. User sends a transfer while signed in.
2. `Transfer.OwnerAppUserId` = their user id; `SenderName` defaults to their display name (US-002-04).
3. The transfer appears in My Files immediately (`GET /transfers`, most recent first — F-TRF-009).
4. Row shows: file count, size, recipients count, status, expiry countdown, downloads.

## Acceptance criteria

```gherkin
Given I am signed in as "Ada Lovelace"
When I create and send a transfer
Then it is listed in My Files
And "From" on the recipient page is "Ada Lovelace"
And the owner column is my user id (server-side)

Given I created a transfer as a guest
When I sign up and open My Files
Then the guest transfer is not listed
```

## Edge cases

- The top bar shows `Files` (My Files entry) only when signed in (UI-Reference §5.1).
- Attribution is at **send** time (the draft may have been created as a guest; finalize/send while signed-in attributes it — server resolves the owner from the session at send).

## Exit check

- [ ] Scenario 1: I am signed in as "Ada Lovelace"
- [ ] Scenario 2: I created a transfer as a guest
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-008/US-008-05-attribution.md`
- Feature: `TRF-008-accounts.md` (FR-008-7)
- Plan AC: AC-008-3
- Related: US-009-01 (the list itself)
- Architecture: TA-4.2#14–15, TA-3.3
- Milestone: T-021, T-022
