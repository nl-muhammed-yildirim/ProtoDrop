# T-037 — Send the transfer to recipients by email

**Story:** US-002-02 | **Feature:** F-TRF-002 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-002/US-002-02-send-by-email.md`
**Coarse task (Milestone-Backlog.md):** T-011, T-013
**Status:** pending

---

## Scope

As a sender, I want to send my transfer to one or many recipient email addresses, so that they get the link delivered straight to their inbox with no copy-paste from me.

**Actor:** Guest or signed-in user on the link screen.

**Goal:** Activate the transfer and notify every valid recipient email.

Happy path:

1. User types recipient addresses (comma- or newline-separated) into the recipients field.
2. Pressing **Send transfer** runs `SendTransferCommand`:
3. Each recipient gets exactly one notification email (F-TRF-006).
4. The confirmation screen shows the link + copy + "Send again".

## Acceptance criteria

```gherkin
Given I type "a@x.com, b@y.com " (trailing space)
When I press "Send transfer"
Then 2 valid addresses are stored (trailing space ignored, lowercased)
And one email is sent per address

Given I type "bad-address" and "ok@x.com"
When I press "Send transfer"
Then "bad-address" is rejected inline
And "ok@x.com" is stored and notified

Given I list 25 addresses (limit 20)
When I press "Send transfer"
Then an inline error names the 20-address limit
```

## Edge cases

- Duplicate address typed twice → stored once (unique per transfer).
- `+1` / `+2` Gmail alias addresses are treated as distinct (EC-006-2, documented).
- Normalization: `A@X.COM` → `a@x.com`; stored form is the lowercased ASCII form.

## Exit check

- [ ] Scenario 1: I type "a@x.com, b@y.com " (trailing space)
- [ ] Scenario 2: I type "bad-address" and "ok@x.com"
- [ ] Scenario 3: I list 25 addresses (limit 20)
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-002/US-002-02-send-by-email.md`
- Feature: `TRF-002-transfer-link.md` (FR-002-3, FR-002-5)
- Plan AC: AC-002-2
- Related: US-006-01 (the emails themselves)
- Architecture: TA-4.2#3, TA-5.3, TA-3.2 `EmailRecipient`
- Design: UI-Reference §5.2
- Milestone: T-011, T-013
