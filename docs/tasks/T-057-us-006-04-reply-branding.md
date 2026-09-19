# T-057 — Reply goes to the sender (branded email)

**Story:** US-006-04 | **Feature:** F-TRF-006 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-006/US-006-04-reply-branding.md`
**Coarse task (Milestone-Backlog.md):** T-019
**Status:** pending

---

## Scope

As a recipient, when I reply to the transfer email, I want the reply to land with the sender, so that "quick question about the file" doesn't vanish into a no-reply void.

**Actor:** Recipient who presses Reply on the notification email; sender who provided an email address.

**Goal:** `Reply-To` = the sender's email when they gave one; brand stays consistent (`From` is always `no-reply@{domain}`).

Happy path:

1. The notification email is sent with `From: no-reply@protodrop.com` and `Reply-To: {SenderEmail}`.
2. Recipient presses Reply in their mail client.
3. The reply is delivered to the sender's inbox (their mail client behavior, not ours — but the header makes it happen).

## Acceptance criteria

```gherkin
Given a signed-in sender "Ada" with email ada@x.com
When the notification email is sent
Then the Reply-To header is ada@x.com
And the From header is no-reply@protodrop.com
And the subject is "Transfer from Ada"

Given a guest sender with no email
When the notification email is sent
Then no Reply-To header is present (or it equals From)
```

## Edge cases

- `Reply-To` is read from `Transfer.SenderEmail` — NOT from the authenticated session at email time (the transfer row is the source; the sender may have logged out by then).
- Case: stored lowercased (EC-008-1) — headers preserve the stored form.

## Exit check

- [ ] Scenario 1: a signed-in sender "Ada" with email ada@x.com
- [ ] Scenario 2: a guest sender with no email
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-006/US-006-04-reply-branding.md`
- Feature: `TRF-006-email.md` (FR-006-7)
- Architecture: TA-6.7
- Related: US-002-04 (where SenderEmail comes from)
- Milestone: T-019
