# T-039 — Add sender info and a note

**Story:** US-002-04 | **Feature:** F-TRF-002 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-002/US-002-04-sender-note.md`
**Coarse task (Milestone-Backlog.md):** T-011, T-013
**Status:** pending

---

## Scope

As a sender, I want to set who the transfer is "From" and add a short note to the recipient, so that the person opening the link knows who sent it and what the files are.

**Actor:** Guest or signed-in user on the link screen.

**Goal:** Make the recipient page personal and self-explanatory without an account.

Happy path:

1. The "From" field is pre-filled: display name if signed in, else empty.
2. Guest can type a name (required text; if left empty, the server stores "Anonymous").
3. Signed-in users get their sender email used as `Reply-To` on notification emails (F-TRF-006-7).
4. An optional note (max 500 chars, live counter) is shown in a blockquote on the recipient page.
5. On send, `SenderName`, `SenderEmail`, `Note` are persisted on the transfer.

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

## Exit check

- [ ] Scenario 1: I am signed in as "Ada Lovelace"
- [ ] Scenario 2: I am a guest and I leave the name empty
- [ ] Scenario 3: I type a 300-char note
- [ ] Scenario 4: the note is 501 chars
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-002/US-002-04-sender-note.md`
- Feature: `TRF-002-transfer-link.md` (FR-002-4, EC-002-2)
- Related: US-006-04 (Reply-To branding), F-PRF-001 (P1 org branding builds on this)
- Architecture: TA-3.2, TA-4.2#3
- Design: UI-Reference §5.2, §5.3
- Milestone: T-011, T-013
