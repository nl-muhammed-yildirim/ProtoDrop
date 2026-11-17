# US-016-03 — See everything with clear contrast and focus

**Feature:** F-TRF-016 — Accessibility | **Status:** pending

---

**Story:** As a user with low vision or a color-vision deficiency, I want every focusable element to show a clear focus ring and every piece of status to be readable without relying on color alone, so that nothing is hidden from me.
**Actor:** Low-vision user, color-blind user, and honestly everyone (a visible focus ring helps mouse users too).
**Goal:** WCAG 2.1 AA on all P0 surfaces (FR-016-1): visible 2 px focus ring, text contrast ≥ 4.5:1, and color is never the only signal (FR-016-4).

## Preconditions

- Any P0 surface in either theme (theme parity is a precondition — US-015-02).

## Happy path

1. **Focus:** Tab through the landing page; every focusable element — drop zone button, file-row removes, **Send.**, link screen fields, **Copy** — shows a **2 px solid `--accent` focus ring with a 2 px offset** (`:focus-visible`, FR-016-4).
2. **Text:** all body/label text meets ≥ 4.5:1 against its background in *both* themes (values computed from the token pairs, not eyeballed — F-TRF-016 test plan).
3. **Color is never the only signal:** status chips carry text (not just a colored dot), the failed-row state pairs `--danger` text with the **Retry** button, the progress bar has a % / byte figure next to it, and the "Copied ✓" state has the checkmark *and* the text (UI-Reference §8).
4. **`--fg-faint` discipline:** the weakest token is used only for placeholders/disabled — the audit checks usage sites (F-TRF-016 UI notes).

## Alternative flows

- **Mouse focus:** `:focus-visible` means mouse clicks don't leave rings on everything (reduced noise) while keyboard focus always rings — the WCAG-visible focus contract.
- **Motion:** `prefers-reduced-motion` disables all animation (FR-016-5, EC-015-2); the wrong-password shake still conveys "Wrong password" in text (the shake is a fallback-safe *addition*).
- **Protanopia/simulated color-blind checks** are part of the manual audit for every status chip and progress element (AC-016-4).

## Acceptance criteria

```gherkin
  When focused via keyboard
  Then a visible focus ring appears (2 px --accent, 2 px offset; never outline:none without replacement)

  When they view status chips and progress
  Then status is distinguishable without color (text/labels/icons present)

Given any text element on a P0 surface
When the contrast is computed from the token pair (both themes)
Then text contrast is at least 4.5:1 (--fg-faint only for placeholders/disabled)
```

## Edge cases

- **Focus ring vs high-contrast OS mode:** the 2 px ring survives the OS high-contrast palette because it's an `outline` (not a box-shadow) — verified in the manual matrix (F-TRF-016 test plan).
- **Buttons with icon-only content** (row remove ✕) rely on the `aria-label` (US-016-01) — but the *visual* still pairs with context: the icon sits in the row's action column with the row's name adjacent.
- **Token-driven:** contrast is asserted per token pair (light *and* dark) — a new token pair entering `tokens.css` must pass the check before merge (CI script per F-TRF-016 test plan).

## UI notes

- Focus ring: 2 px solid `--accent`, offset 2 px, on every focusable element (UI-Reference §3).
- Status chips: text + color (UI-Reference §8); progress: bar + numeric %/bytes.
- The contrast table lives in UI-Reference §1 (tokens) — this story's audit consumes it, doesn't redefine it.

## Technical notes

- CSS: `:focus-visible` (not `:focus` for mouse) — the ring is a global rule in `core/ui`, not per-component (TA-8.2).
- Manual audit (T-027 exit): WCAG 2.1 AA checklist per P0 surface, contrast computed from tokens.
- The Playwright check asserts the computed outline on focused elements (F-TRF-016 test plan).

## Links

- Feature: `TRF-016-a11y.md` (FR-016-1, FR-016-4, FR-016-5, AC-016-3, AC-016-4)
- Related: US-015-02 (token parity in both themes), US-016-01 (keyboard focus order)
- Architecture: TA-8.2
- Design: UI-Reference §1, §3, §8
- Milestone: T-027
