# US-006-04 — Reply goes to the sender (branded email)

**Feature:** F-TRF-006 — Email Notifications | **Status:** pending

---

**Story:** As a recipient, when I reply to the transfer email, I want the reply to land with the sender, so that "quick question about the file" doesn't vanish into a no-reply void.
**Actor:** Recipient who presses Reply on the notification email; sender who provided an email address.
**Goal:** `Reply-To` = the sender's email when they gave one; brand stays consistent (`From` is always `no-reply@{domain}`).

## Preconditions

- Transfer has `SenderEmail` set (signed-in user's email, or a guest's optional address — F-TRF-002, US-002-04).

## Happy path

1. The notification email is sent with `From: no-reply@protodrop.com` and `Reply-To: {SenderEmail}`.
2. Recipient presses Reply in their mail client.
3. The reply is delivered to the sender's inbox (their mail client behavior, not ours — but the header makes it happen).

## Alternative flows

- **Sender gave no email (guest, anonymous):** `Reply-To` absent or = From (no-reply); replying goes nowhere useful — the email body itself says "This transfer was sent by {senderName}" without promising a reply path.
- **Sender's address bounces:** not our problem at MVP (their own address; documented).

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

## UI notes (email design)

- Footer keeps "You received this because {senderName} sent you a transfer." — sets expectations about who to talk to.
- No "Reply to no-reply…" copy anywhere.

## Technical notes

- `f-email` (TA-6.7): builds the CH email with `ReplyTo = Transfer.SenderEmail ?? null`.
- `From` constant from config `Wa:Email:FromAddress` (seeded `no-reply@protodrop.com`, D-04).

## Links

- Feature: `TRF-006-email.md` (FR-006-7)
- Architecture: TA-6.7
- Related: US-002-04 (where SenderEmail comes from)
- Milestone: T-019
