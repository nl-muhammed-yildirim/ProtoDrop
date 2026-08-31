# US-002-04 — Add sender info and a note

**Feature:** F-TRF-002 — Transfer Creation & Link | **Status:** pending

---

**Story:** As a sender, I want to set who the transfer is "From" and add a short note to the recipient, so that the person opening the link knows who sent it and what the files are.
**Actor:** Guest or signed-in user on the link screen.
**Goal:** Make the recipient page personal and self-explanatory without an account.

## Preconditions

- Transfer finalized; link screen open.

## Happy path

1. The "From" field is pre-filled: display name if signed in, else empty.
2. Guest can type a name (required text; if left empty, the server stores "Anonymous").
3. Signed-in users get their sender email used as `Reply-To` on notification emails (F-TRF-006-7).
4. An optional note (max 500 chars, live counter) is shown in a blockquote on the recipient page.
5. On send, `SenderName`, `SenderEmail`, `Note` are persisted on the transfer.

## Alternative flows

- **Guest with an email:** the guest may also provide a sender email (optional field) used as `Reply-To`.
- **Invalid guest sender email:** the address is rejected inline; the transfer itself is not blocked (EC-002-2).
- **Note empty:** no blockquote rendered on the recipient page.

## Acceptance criteria

```gherkin
Given I am signed in as "Ada Lovelace"
When I send the transfer
Then the recipient page shows "From: Ada Lovelace"

Given I am a guest and I leave the name empty
When I send the transfer
Then the recipient page shows "From: Anonymous"

Given I type a 300-char note
When I send the transfer
Then the note appears on the recipient page
And the 500-char counter never overflowed

Given the note is 501 chars
When I try to send
Then an inline validation stops the send
```

## Edge cases

- Unicode names (e.g. "Müller", "Zoë") round-trip correctly (`NVARCHAR(100)` — TA-3.2).
- "From" on the recipient page is display-only; it is not an email by default unless the sender provided one.
- Note is plain text (no markdown/HTML) to keep the recipient page XSS-safe.

## UI notes

- "From" text input; disabled (read-only look) when signed in unless the user explicitly edits it (per-transfer override, P1 branding builds on this).
- Note: textarea with `500` counter in `--fs-tiny`, turns `--warning` at 450+.
- Recipient page: "From: {senderName}" as the page header; note as a `--bg-subtle` blockquote.

## Technical notes

- `SenderName NVARCHAR(100) NOT NULL`, `SenderEmail VARCHAR(320) NULL`, `Note NVARCHAR(500) NULL` (TA-3.2).
- Defaults applied in `SendTransferCommand`: `SenderName ?? "Anonymous"`; `SenderEmail = user.Email` if signed in.
- Note rendered as escaped text on the recipient page (no `dangerouslySetInnerHTML`).

## Links

- Feature: `TRF-002-transfer-link.md` (FR-002-4, EC-002-2)
- Related: US-006-04 (Reply-To branding), F-PRF-001 (P1 org branding builds on this)
- Architecture: TA-3.2, TA-4.2#3
- Design: UI-Reference §5.2, §5.3
- Milestone: T-011, T-013
