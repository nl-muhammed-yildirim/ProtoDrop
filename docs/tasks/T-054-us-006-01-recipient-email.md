# T-054 — Recipients get a notification email with the link

**Story:** US-006-01 | **Feature:** F-TRF-006 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-006/US-006-01-recipient-email.md`
**Coarse task (Milestone-Backlog.md):** T-019
**Status:** pending

---

## Scope

As a recipient, I want a clear email with the download link in my inbox, so that I don't have to ask "where is the file?" in chat.

**Actor:** Recipient address listed on a sent transfer.

**Goal:** Exactly one email per address, with everything needed to get the files, within ~1 minute of send.

Happy path:

1. `f-email` consumes `transfer.created` from `core/email`.
2. For each unique address (sender's own address skipped — FR-006-3; suppressed address+sender skipped — FR-006-5): render the template (Stubble, localized per F-TRF-014-5) and send via Communication Hub.
3. Email content: subject `Transfer from {senderName}`; body = sender name, file list (names + human sizes), one button **Download files** → `{origin}/t/{linkId}`, footer "This link expires in N days." + unsubscribe.
4. On success: `EmailRecipient.NotifiedAtUtc` set, `email.sent` emitted.
5. Recipient taps the button → recipient page (F-TRF-003) → files.

## Acceptance criteria

```gherkin
Given a transfer sent to 3 addresses (one of them the sender's own)
When transfer.created is consumed
Then exactly 2 emails are sent
And each email shows the file list with sizes and the single Download files button
And the footer shows the correct expiry countdown in days

Given a link-only transfer
When transfer.created is consumed
Then no email is sent
```

## Edge cases

- `+1`/`+2` aliases count as distinct addresses (EC-006-2).
- File names in the body are plain text, escaped; sizes human-formatted (1024-based, TA-8.5 `formatBytes` equivalent).
- Email send lag target: inbox < 60 s p95 (TA-15).

## Exit check

- [ ] Scenario 1: a transfer sent to 3 addresses (one of them the sender's own)
- [ ] Scenario 2: a link-only transfer
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-006/US-006-01-recipient-email.md`
- Feature: `TRF-006-email.md` (FR-006-1, FR-006-2, FR-006-3, FR-006-6, FR-006-8)
- Plan AC: AC-006-1
- Architecture: TA-6.7, TA-5.3, TA-15
- Related: US-002-02 (event origin), US-006-05 (language)
- Milestone: T-019
