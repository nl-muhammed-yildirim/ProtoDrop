# US-002-02 — Send the transfer to recipients by email

**Feature:** F-TRF-002 — Transfer Creation & Link | **Status:** pending

---

**Story:** As a sender, I want to send my transfer to one or many recipient email addresses, so that they get the link delivered straight to their inbox with no copy-paste from me.
**Actor:** Guest or signed-in user on the link screen.
**Goal:** Activate the transfer and notify every valid recipient email.

## Preconditions

- Transfer finalized; link screen open.

## Happy path

1. User types recipient addresses (comma- or newline-separated) into the recipients field.
2. Pressing **Send transfer** runs `SendTransferCommand`:
   - addresses parsed, trimmed, lowercased, validated per-address;
   - transfer → `Status=1`, `ExpiresAtUtc = now + RETENTION_DAYS`;
   - one `EmailRecipient` row per unique address;
   - `transfer.created` emitted (payload per TA-5.3).
3. Each recipient gets exactly one notification email (F-TRF-006).
4. The confirmation screen shows the link + copy + "Send again".

## Alternative flows

- **Mixed valid/invalid addresses:** invalid ones are rejected inline with a per-address error; valid ones proceed.
- **Sender's own address in the list:** stored, but the email worker skips sending to the sender (F-TRF-006-3).
- **More than `MAX_EMAILS` (default 20):** the field overflows with an inline error; the API also caps (EC-006-1).

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

## UI notes

- Recipients textarea: one address per line, `--fs-small` helper text "One per line — you can also separate with commas."
- Inline validation under the field on blur and on submit (`--danger` text, no blocking modal).
- **Send transfer** primary button; on success the whole card swaps to the confirmation state.

## Technical notes

- `SendTransferCommand` (MediatR) in `wa.application/UseCases/Transfers/`.
- Email parsing: split on `[\r\n,;\s]+`, trim, drop empties, validate with .NET `MailAddress`, lowercase.
- `EmailRecipient` unique constraint `(TransferId, Address)` (TA-3.2) makes re-sends idempotent per address.
- Emission of `transfer.created` happens in the same transaction window as the status flip (outbox, TA-5.2).

## Links

- Feature: `TRF-002-transfer-link.md` (FR-002-3, FR-002-5)
- Plan AC: AC-002-2
- Related: US-006-01 (the emails themselves)
- Architecture: TA-4.2#3, TA-5.3, TA-3.2 `EmailRecipient`
- Design: UI-Reference §5.2
- Milestone: T-011, T-013
