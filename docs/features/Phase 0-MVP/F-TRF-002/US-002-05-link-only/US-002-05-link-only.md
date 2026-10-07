# US-002-05 — Create a link-only transfer (no emails)

**Feature:** F-TRF-002 — Transfer Creation & Link | **Status:** pending

---

**Story:** As a sender, I want to get just the link without emailing anyone, so that I can share it through my own channel (chat, forum, printed QR) and keep control of when people hear about it.
**Actor:** Guest or signed-in user on the link screen.
**Goal:** Activate a transfer with zero recipient emails.

## Preconditions

- Transfer finalized; link screen open.

## Happy path

1. User leaves the recipients field empty and presses **Copy link only** (ghost button) — or **Send transfer** with zero emails.
2. `SendTransferCommand` runs with an empty email list:
   - transfer → `Status=1`, `ExpiresAtUtc = now + RETENTION_DAYS`;
   - **no** `EmailRecipient` rows;
   - `transfer.created` emitted with `emails: []`.
3. The confirmation screen shows the link + **Copy**.
4. No emails are sent at all (FR-006-6).

## Alternative flows

- **Mixed:** if the user typed emails then cleared them, the link-only path still applies (zero emails after parsing).
- **Adding emails later:** not supported in MVP (the link is the only surface; P1 adds re-send/management).

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

## UI notes

- Two actions on the link screen: **Send transfer** (primary, always) and **Copy link only** (ghost) — the latter is the recommended path when no emails are typed (FR-002-6).
- Confirmation: link + copy + "Send again" (FR-002-7: re-drafts from the same files).

## Technical notes

- `SendTransferCommand.Emails` is `IReadOnlyList<string>`; empty list is valid, not a validation error.
- `transfer.created` payload has `emails: []` (TA-5.3) so the email worker can skip immediately.
- "Send again" (FR-002-7) creates a **new** transfer draft sharing the same files (copy of `FileItem` → same `BlobRef`, `RefCount++`), not an alias.

## Links

- Feature: `TRF-002-transfer-link.md` (FR-002-6, FR-002-7)
- Plan AC: AC-002-3
- Related: US-006-01 (email flow is skipped here)
- Architecture: TA-4.2#3, TA-5.3
- Design: UI-Reference §5.2
- Milestone: T-011, T-013
