# US-022-02 — Know exactly what a scheduled send does

**Feature:** F-PRF-002 — Scheduled Send | **Status:** pending

---

**Story:** As a Pro user scheduling a transfer, I want the semantics spelled out — "the link works early; emails go out on schedule" — so that I never misjudge whether a recipient can already download.
**Actor:** Pro/Business user, their recipients.
**Goal:** one documented sentence on the link screen + a one-line state on the recipient page before due.

## Preconditions

- User on Pro/Business, link screen open.

## Happy path

1. Link screen schedule helper: "The link works early; emails go out on schedule." (FR-022-1, exact copy).
2. Confirmation screen states the scheduled time.
3. Recipient page before due: "Files are ready — email goes out at {when}" (`--fs-small`, `--fg-muted`, one line).

## Alternative flows

- **Recipient downloads early**: allowed and counted (documented, FR-022-1/FR-022-6).
- **No schedule**: the helper is hidden (normal flow, no copy change).

## Acceptance criteria

```gherkin
Given I scheduled a transfer
When I am on the confirmation screen
Then it states the scheduled time and that emails go out then

Given a recipient opens the link before the due time
When the page renders
Then the one-line "email goes out at {when}" state is shown
And download is available
```

## Edge cases

- Time display: sender-pick UTC, rendered to the recipient in their browser's locale (client formatting, no server localization).
- The sentence is i18n (F-TRF-014), one string key, no exclamation points.

## UI notes

- One line, calm tone (UI-Reference §7): no "Heads up!" — facts only.

## Technical notes

- Strings in i18n files; recipient pre-due state from `GET /public/transfers/{linkId}` (`scheduledSendAt` + status).

## Links

- Feature: `PRF-002-scheduled-send.md` (FR-022-1, FR-022-6, AC-022-1)
- Related: F-TRF-014 (i18n), F-TRF-003 (recipient page)
