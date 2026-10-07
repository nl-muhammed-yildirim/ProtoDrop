# T-002-08 — "Copy link only" + confirmation screen (FR-002-6/7, AC-002-3)

**Story:** US-002-05 | **Spec:** FR-002-6/7, AC-002-3, TA-4.3 `/t/{linkId}/sent` | **Size:** M
**Depends on:** T-002-03 (link screen), T-002-04 (send endpoint — zero-email path)

---

## Context to read (only these)

- `US-002-05-link-only.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-002-transfer-link.md` → FR-002-6/7 + AC-002-3 + UI notes (confirmation line)

## Instructions

1. Add the **Copy link only** ghost button to the link screen: pressing it sends with zero emails (`SendTransferCommand` with an empty list — T-002-04), copies the link to the clipboard, and shows the confirmation screen.
2. Whitespace-only recipients field → treated as zero emails (same path).
3. Build the **confirmation screen** at `/t/{linkId}/sent`: the link + **Copy** + **"Send again"** (FR-002-7: creates a *new* transfer draft with the same files — files are copied, not aliased; the re-draft wiring itself is F-TRF-010's endpoint 16 later, at M0 the button pre-fills a new draft client-side).
4. After any successful send (with or without emails), the link screen card swaps to this confirmation state — no separate route navigation needed for the email path.

## Exit check

- [ ] "Copy link only" activates the transfer with zero EmailRecipient rows and copies the link (AC-002-3)
- [ ] Whitespace-only recipients field follows the same link-only path
- [ ] Confirmation screen shows the link + Copy + Send again after both email and link-only sends

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-002-08 — add copy-link-only and the confirmation screen.
Read first (only): docs/features/Phase 0-MVP/F-TRF-002/US-002-05-link-only/US-002-05-link-only.md (happy path + Alternative flows + Technical notes) and F-TRF-002-transfer-link.md (FR-002-6/7).
Do exactly:
1. Add the Copy link only ghost button to the link screen: pressing it sends with zero emails (SendTransferCommand with an empty list — T-002-04), copies the link to the clipboard, and shows the confirmation screen.
2. Treat a whitespace-only recipients field as zero emails (same path).
3. Build the confirmation screen at /t/{linkId}/sent: the link + Copy + Send again (FR-002-7: creates a new transfer draft with the same files — files are copied, not aliased; the re-draft wiring itself is F-TRF-010's endpoint 16 later, at M0 the button pre-fills a new draft client-side).
4. After any successful send (with or without emails), swap the link screen card to this confirmation state — no separate route navigation needed for the email path.
Done when: AC-002-3 holds end-to-end — copy-link-only activates with zero rows, whitespace-only input follows the same path, and both send paths land on the confirmation screen.
Constraints: use design tokens only; "Send again" is client-side pre-fill at M0 — do not call endpoint 16 (that arrives with F-TRF-010).
```
