# T-071 — Re-send a transfer from My Files

**Story:** US-009-03 | **Feature:** F-TRF-009 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-009/US-009-03-resend.md`
**Coarse task (Milestone-Backlog.md):** T-023
**Status:** pending

---

## Scope

As a user whose link expired or whose recipient list was wrong, I want a one-click re-send from My Files, so that I'm back to a working link in seconds.

**Actor:** Signed-in owner of a non-deleted transfer.

**Goal:** Row action → pre-filled link screen → new working link.

Happy path:

1. User presses **Re-send** on a row (ghost, link icon).
2. The link screen opens pre-filled: same files, original emails/password/note (F-TRF-010, US-010-02), banner "Re-sending 'render.mp4' and 2 more files."
3. User reviews (may edit) and presses **Send transfer**.
4. Confirmation: "New link ready." + the **new** URL + copy. The original link (if still active) keeps working.

## Acceptance criteria

```gherkin
Given I press Re-send on an expired transfer
When I confirm the pre-filled send
Then a new transfer is created with a new link
And the new link works while the old one (if not expired) still works

Given the transfer's blobs were already deleted
When I press Re-send
Then I see "Files were deleted — upload again." and no half-created transfer remains
```

## Edge cases

- The original row gains a subtle "Re-sent" mark (P1 niceness, MVP: just the new row above it).
- `SupersededBy` link is set on the original when the new one is sent (informational, F-TRF-010-3).

## Exit check

- [ ] Scenario 1: I press Re-send on an expired transfer
- [ ] Scenario 2: the transfer's blobs were already deleted
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-009/US-009-03-resend.md`
- Feature: `TRF-009-my-files.md` (FR-009-3)
- Related: US-010-01, US-010-02
- Architecture: TA-4.2#16
- Milestone: T-023
