# US-010-02 — Edit the pre-filled details before re-sending

**Feature:** F-TRF-010 — Re-send Transfer | **Status:** pending

---

**Story:** As a user re-sending a transfer, I want the old emails, password, and note to come along — but editable — so that I can fix the recipient list without re-typing everything.
**Actor:** Signed-in user on the re-send link screen.
**Goal:** Pre-filled, not pre-cooked: everything from the original, all changeable before send.

## Preconditions

- Re-send draft exists (US-010-01).

## Happy path

1. The link screen shows: **original recipient emails** in the recipients field, original password, original note, original sender name.
2. User edits any of them (adds an email, clears the password, rewrites the note).
3. **Send transfer** → the *edited* values are stored on the new transfer (not the original's).
4. The original transfer's values are untouched (it can still be re-sent again later with its own values).

## Alternative flows

- **Send as link-only:** user clears all emails → the new transfer is link-only (FR-002-6 path).
- **Change password to empty:** the new transfer is unprotected; the old one keeps its password (independent transfers).

## Acceptance criteria

```gherkin
Given the original transfer had emails "a@x.com" and a password
When I open the re-send screen
Then both the email and the password are pre-filled
When I remove "a@x.com", add "b@y.com", and clear the password
Then the new transfer is sent to b@y.com only, with no password
And the original transfer still has its original email and password
```

## Edge cases

- Pre-fill is a **snapshot copy** of the original's values at re-send time (stored on the draft), not a live view — editing never mutates the original.
- `MAX_EMAILS` cap applies to the edited list (F-TRF-007).

## UI notes

- All standard link-screen fields (UI-Reference §5.2) with the re-send banner (US-010-01).
- No "what changed" diff UI in MVP — the banner covers it.

## Technical notes

- Draft pre-fill values are materialized by `ResendTransferCommand` (copied into the new draft's pending state); `SendTransferCommand` reads the *submitted* form, not the original.
- Nothing special at the API level — the form is the truth (F-TRF-002).

## Links

- Feature: `TRF-010-resend.md` (FR-010-2)
- Related: US-002-02…04 (the fields themselves)
- Architecture: TA-7.3, TA-4.2#3
- Milestone: T-023
