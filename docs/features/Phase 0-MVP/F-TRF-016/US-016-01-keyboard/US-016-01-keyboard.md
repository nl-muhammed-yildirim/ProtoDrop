# US-016-01 — Complete a transfer without a mouse

**Feature:** F-TRF-016 — Accessibility | **Status:** pending

---

**Story:** As a keyboard-only user (or someone with a broken arm), I want to complete the whole transfer flow using only Tab/Enter/Space, so that the product never requires a pointer.
**Actor:** Keyboard-only user, guest or signed-in, on any P0 surface.
**Goal:** the *entire* send flow — select → stage → remove → send → add recipients → send transfer → copy link — and the recipient flow — unlock → download — work without a mouse (FR-016-2).

## Preconditions

- The user can reach the landing page; the drop zone exposes its visible **Choose files** button (F-TRF-001, FR-016-2).

## Happy path

1. **Select:** Tab to the **Choose files** button → Enter opens the native picker (the canonical path — drag is *additional*, never required, EC-016-2).
2. **Stage:** Tab through the staging list; each file row's remove control (✕) is focusable with an `aria-label` "Remove {name}" (UI-Reference §4.2 pattern); Space/Enter activates.
3. **Send:** Tab to **Send.** → Enter → the link screen loads with focus on the first actionable element (the copy field/button).
4. **Recipients & options:** Tab through the recipient field, password (show/hide), note, **Send transfer** — all native form controls, all reachable, in DOM order.
5. **Copy link:** Tab to **Copy** → Enter → "Copied ✓" state (visible, and announced — US-016-02).
6. **Recipient side:** open link → Tab to password (if any) → Enter → Tab to **Download all** → Enter.

## Alternative flows

- **Paste path (Ctrl+V)** is a keyboard affordance too — it complements, never replaces, the button (US-001-01).
- **Mobile:** touch targets ≥ 44 px (F-TRF-017-4); the recipient page's sticky download button is the *same* focusable control as its in-flow position (one element, two positions — EC-016-3).
- **Error states:** failed upload rows expose **Retry** in tab order (US-001-05); form errors are announced and their fields reachable (EC-016-4).

## Acceptance criteria

```gherkin
  When they use Tab/Enter/Space only
  Then they can complete: select files → remove one → Send → add recipient → Send transfer → copy link

Given a password-protected transfer link
When the user uses only the keyboard
Then they can enter the password and trigger both "Download all" and per-file downloads

Given a focusable element
When it is focused via keyboard
Then a visible focus indicator appears (see US-016-03)
```

## Edge cases

- Focus **never gets trapped**: modals (confirm delete in My Files) trap focus *within* the modal and restore it to the trigger on close (standard pattern, enforced in the `core/ui` modal component).
- The native file picker is an OS surface — the a11y contract is that the *button* that opens it is perfect (label, focus, activation).
- **Tab order = DOM order**: no positive `tabindex` values in features (lint/review rule); the staging list's per-row controls interleave in reading order.

## UI notes

- Drop zone: `role=button`, localized `aria-label` "Upload files", **visible** **Choose files** button (F-TRF-001 UI notes) — drag is the visual sugar, the button is the contract.
- File rows: remove buttons labeled "Remove {name}", download buttons "Download {name}, {size}" (F-TRF-016 UI notes).

## Technical notes

- E2E: Playwright `tab`-sequence assertion of the full guest flow (F-TRF-016 test plan, T-027 "keyboard-only full flow").
- No third-party a11y kit — plain ARIA + `:focus-visible` (TA-8.2); this story's test is the Playwright tab-sequence, which catches ordering regressions.

## Links

- Feature: `TRF-016-a11y.md` (FR-016-2, AC-016-1, EC-016-2, EC-016-3)
- Related: US-016-03 (focus visibility), US-001-01 (drop zone contract), US-017-03 (touch targets)
- Architecture: TA-8.2
- Design: UI-Reference §3
- Milestone: T-027
