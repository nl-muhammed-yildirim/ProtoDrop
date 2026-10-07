# T-002-03 — Link screen: "Your link is ready" + copy field (FR-002-2/3, UI §5.2)

**Story:** US-002-01 | **Spec:** FR-002-2/3, AC-002-1 (copy half), TA-4.3 `/t/{linkId}/sent` | **Size:** M
**Depends on:** T-001-10 (finalize — hands the browser the TransferDto with linkId)

---

## Context to read (only these)

- `US-002-01-unique-link.md` → happy path + Edge cases + UI notes
- `../../F-TRF-002-transfer-link.md` → FR-002-3 + UI notes (card line)

## Instructions

1. Build the link screen in **`src/wa.web/src/features/sender/LinkScreen.tsx`**: a card (max-width 560 px) headed **"Your link is ready"** with a read-only copy field showing the full public URL `{origin}/t/{linkId}` and a **Copy** button.
2. Copy uses `navigator.clipboard` with an `execCommand` fallback; success state shows "Copied ✓" in `--success` text for 2 s, then reverts (UI-Reference §4.5).
3. The link is the only hero element — no other content above it; the form fields (recipients/password/note) render below as placeholders wired to T-002-05…T-002-07.

## Exit check

- [ ] After finalize, the screen shows the full public URL for the minted linkId
- [ ] Copy puts the URL on the clipboard and shows "Copied ✓" for 2 s (unit test with a mocked clipboard)
- [ ] The URL has no query parameters or tracking fragments

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-002-03 — build the link screen copy field.
Read first (only): docs/features/Phase 0-MVP/F-TRF-002/US-002-01-unique-link/US-002-01-unique-link.md (happy path + Edge cases + UI notes) and F-TRF-002-transfer-link.md (FR-002-3).
Do exactly:
1. Build the link screen in src/wa.web/src/features/sender/LinkScreen.tsx: a card (max-width 560 px) headed "Your link is ready" with a read-only copy field showing the full public URL {origin}/t/{linkId} and a Copy button.
2. Copy uses navigator.clipboard with an execCommand fallback; success state shows "Copied ✓" in --success text for 2 s, then reverts (UI-Reference §4.5).
3. Keep the link as the only hero element — no other content above it; the form fields (recipients/password/note) render below as placeholders wired to T-002-05…T-002-07.
Done when: after finalize the screen shows the full public URL, copy works with the 2 s success state, and the URL carries no query parameters or tracking fragments.
Constraints: use design tokens only; the linkId comes from the TransferDto returned by finalize (TA-4.2#2) — do not re-fetch at M0.
```
