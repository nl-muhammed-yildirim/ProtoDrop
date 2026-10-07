# T-002-07 — Sender name + note fields: pre-fill, 500-char counter (FR-002-4)

**Story:** US-002-04 | **Spec:** FR-002-4, AC-002-1 (sender half), EC-002-2 | **Size:** M
**Depends on:** T-002-03 (link screen card — the fields render inside it)

---

## Context to read (only these)

- `US-002-04-sender-note.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-002-transfer-link.md` → FR-002-4 + EC-002-2 + Test plan (unit line: sender part)

## Instructions

1. Add the **"From"** text input to the link screen: pre-filled with the display name when signed in; guests type freely; if left empty the server stores "Anonymous" (T-002-04 applies the default).
2. Guests may also provide an optional sender email — invalid addresses are rejected inline and do **not** block the transfer (EC-002-2); the address becomes `Reply-To` on notification emails (F-TRF-006-7).
3. Add the **note** textarea: max 500 chars with a live counter in `--fs-tiny`; the counter turns `--warning` at 450+; 501 chars → inline validation stops the send. Note is plain text (no markdown/HTML — XSS-safe).
4. Unicode names round-trip correctly ("Müller", "Zoë" — `NVARCHAR(100)`).

## Exit check

- [ ] Signed-in as "Ada Lovelace" → field pre-filled; guest with empty name → server stores "Anonymous" (integration assertion)
- [ ] A 300-char note sends and renders on the recipient page; a 501-char note is blocked inline
- [ ] Invalid guest sender email shows an inline error without disabling Send transfer

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-002-07 — build the sender name and note fields.
Read first (only): docs/features/Phase 0-MVP/F-TRF-002/US-002-04-sender-note/US-002-04-sender-note.md (happy path + Alternative flows + UI notes) and F-TRF-002-transfer-link.md (FR-002-4).
Do exactly:
1. Add the "From" text input to the link screen: pre-filled with the display name when signed in; guests type freely; if left empty the server stores "Anonymous" (T-002-04 applies the default).
2. Guests may also provide an optional sender email — invalid addresses are rejected inline and do not block the transfer (EC-002-2); the address becomes Reply-To on notification emails (F-TRF-006-7).
3. Add the note textarea: max 500 chars with a live counter in --fs-tiny; the counter turns --warning at 450+; 501 chars → inline validation stops the send. Note is plain text (no markdown/HTML — XSS-safe).
4. Keep Unicode names round-tripping correctly ("Müller", "Zoë" — NVARCHAR(100)).
Done when: AC-002-1's sender half holds — pre-fill works, guest defaults to Anonymous server-side, the 500-char note counter behaves as specified, and an invalid guest email never blocks the send.
Constraints: use design tokens only; no markdown rendering at M0 (plain text keeps the recipient page XSS-safe).
```
