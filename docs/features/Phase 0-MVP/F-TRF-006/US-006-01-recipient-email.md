# US-006-01 — Recipients get a notification email with the link

**Feature:** F-TRF-006 — Email Notifications | **Status:** pending

---

**Story:** As a recipient, I want a clear email with the download link in my inbox, so that I don't have to ask "where is the file?" in chat.
**Actor:** Recipient address listed on a sent transfer.
**Goal:** Exactly one email per address, with everything needed to get the files, within ~1 minute of send.

## Preconditions

- `transfer.created` event emitted with a non-empty `emails[]` (F-TRF-002, US-002-02).

## Happy path

1. `f-email` consumes `transfer.created` from `core/email`.
2. For each unique address (sender's own address skipped — FR-006-3; suppressed address+sender skipped — FR-006-5): render the template (Stubble, localized per F-TRF-014-5) and send via Communication Hub.
3. Email content: subject `Transfer from {senderName}`; body = sender name, file list (names + human sizes), one button **Download files** → `{origin}/t/{linkId}`, footer "This link expires in N days." + unsubscribe.
4. On success: `EmailRecipient.NotifiedAtUtc` set, `email.sent` emitted.
5. Recipient taps the button → recipient page (F-TRF-003) → files.

## Alternative flows

- **Link-only transfer** (`emails: []`): consumer short-circuits, no send, no per-address work (FR-006-6).
- **Duplicate consumption of the event:** `eventId` dedup + `NotifiedAtUtc` already set → skip (EC-006-4).

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

## UI notes (email design)

- Minimal inline HTML, 600 px, calm tone, one accent button, no exclamation points.
- Plain-text version always included (`text/plain` part).

## Technical notes

- Consumer `f-email` (TA-6.7): resolve template → send (`From = no-reply@{domain}`, D-04) → mark notified → emit `email.sent`.
- Sender-self detection: address == `Transfer.SenderEmail` (case-insensitive, lowercased).
- Suppression lookup: `EmailSuppression (Address, SenderEmail)` (TA-3.2).

## Links

- Feature: `TRF-006-email.md` (FR-006-1, FR-006-2, FR-006-3, FR-006-6, FR-006-8)
- Plan AC: AC-006-1
- Architecture: TA-6.7, TA-5.3, TA-15
- Related: US-002-02 (event origin), US-006-05 (language)
- Milestone: T-019
