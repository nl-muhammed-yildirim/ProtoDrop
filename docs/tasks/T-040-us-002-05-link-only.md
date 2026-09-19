# T-040 — Create a link-only transfer (no emails)

**Story:** US-002-05 | **Feature:** F-TRF-002 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-002/US-002-05-link-only.md`
**Coarse task (Milestone-Backlog.md):** T-011, T-013
**Status:** pending

---

## Scope

As a sender, I want to get just the link without emailing anyone, so that I can share it through my own channel (chat, forum, printed QR) and keep control of when people hear about it.

**Actor:** Guest or signed-in user on the link screen.

**Goal:** Activate a transfer with zero recipient emails.

Happy path:

1. User leaves the recipients field empty and presses **Copy link only** (ghost button) — or **Send transfer** with zero emails.
2. `SendTransferCommand` runs with an empty email list:
3. The confirmation screen shows the link + **Copy**.
4. No emails are sent at all (FR-006-6).

## Acceptance criteria

```gherkin
Given I leave the recipients field empty
When I press "Send transfer"
Then the transfer is active (status 1)
And zero EmailRecipient rows exist
And no email is sent

Given I press "Copy link only"
When the link screen responds
Then the transfer is activated
And the link is copied to the clipboard
And the confirmation screen appears
```

## Edge cases

- Whitespace-only recipients field → treated as zero emails.
- Link-only transfers are indistinguishable from emailed ones to a recipient (same page).
- The sender's own copy of the link is not counted as a download (downloads count on the recipient page, F-TRF-003).

## Exit check

- [ ] Scenario 1: I leave the recipients field empty
- [ ] Scenario 2: I press "Copy link only"
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-002/US-002-05-link-only.md`
- Feature: `TRF-002-transfer-link.md` (FR-002-6, FR-002-7)
- Plan AC: AC-002-3
- Related: US-006-01 (email flow is skipped here)
- Architecture: TA-4.2#3, TA-5.3
- Design: UI-Reference §5.2
- Milestone: T-011, T-013
