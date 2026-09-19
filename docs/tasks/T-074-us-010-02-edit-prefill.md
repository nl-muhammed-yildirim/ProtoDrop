# T-074 — Edit the pre-filled details before re-sending

**Story:** US-010-02 | **Feature:** F-TRF-010 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-010/US-010-02-edit-prefill.md`
**Coarse task (Milestone-Backlog.md):** T-023
**Status:** pending

---

## Scope

As a user re-sending a transfer, I want the old emails, password, and note to come along — but editable — so that I can fix the recipient list without re-typing everything.

**Actor:** Signed-in user on the re-send link screen.

**Goal:** Pre-filled, not pre-cooked: everything from the original, all changeable before send.

Happy path:

1. The link screen shows: **original recipient emails** in the recipients field, original password, original note, original sender name.
2. User edits any of them (adds an email, clears the password, rewrites the note).
3. **Send transfer** → the *edited* values are stored on the new transfer (not the original's).
4. The original transfer's values are untouched (it can still be re-sent again later with its own values).

## Acceptance criteria

```gherkin
Given the original transfer had emails "a@x.com" and a password
When I open the re-send screen
Then both the email and the password are pre-filled
When I remove "a@x.com", add "b@y.com", and clear the password
Then the new transfer is sent to b@y.com only, with no password
And the original transfer still has its original email and password
```

## Edge cases

- Pre-fill is a **snapshot copy** of the original's values at re-send time (stored on the draft), not a live view — editing never mutates the original.
- `MAX_EMAILS` cap applies to the edited list (F-TRF-007).

## Exit check

- [ ] Scenario 1: the original transfer had emails "a@x.com" and a password
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-010/US-010-02-edit-prefill.md`
- Feature: `TRF-010-resend.md` (FR-010-2)
- Related: US-002-02…04 (the fields themselves)
- Architecture: TA-7.3, TA-4.2#3
- Milestone: T-023
