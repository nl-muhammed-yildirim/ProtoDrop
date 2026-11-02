# US-015-01 — Choose light, dark, or system theme

**Feature:** F-TRF-015 — Dark Mode | **Status:** pending

---

**Story:** As a user, I want to pick light, dark, or "match my system" and have the choice stick, so that the product matches how I use my computer.
**Actor:** Signed-in user (choice stored on the account, `AppUser.Theme`, F-TRF-008-4); guest (system by default, FR-015-1).
**Goal:** theme = `system` | `light` | `dark`; the stored choice applies **before first paint** — no flash of the wrong theme.

## Preconditions

- The user can reach the profile screen (signed-in) — or is a guest on any screen.

## Happy path

1. Signed-in user opens **Profile → Theme** and picks **Dark**.
2. The value is stored on the account (`AppUser.Theme`); the profile select (UI-Reference §5.5) reflects the choice.
3. Every page load and refresh from then on: dark tokens are applied **before first paint** via the inline `data-theme` bootstrap (FR-015-3, AC-015-1).
4. Guest: no profile; the guest experience is `system` — the OS preference is respected and tracked live (FR-015-1, AC-015-2).
5. **System live-tracking:** while in `system` mode, an OS theme change with the tab open switches the app without a reload (FR-015-4, AC-015-3) — `matchMedia('(prefers-color-scheme: dark)')` listener.

## Alternative flows

- **Signed-in user in `system` mode changes their OS theme:** it follows (they chose to follow). A user in explicit `light`/`dark` does *not* follow OS changes — the account setting wins (EC-015-1).
- **Guest with OS in dark mode:** landing loads dark immediately (AC-015-2) — the bootstrap reads `matchMedia` before paint.
- **User picks `Light` while OS is dark:** explicit choice wins; no flash either way (the bootstrap honors the stored value first).

## Acceptance criteria

```gherkin
  When any page loads (or refreshes)
  Then dark tokens are applied before first paint (no light flash)

  When the landing page loads
  Then dark tokens are applied

  When the OS preference changes
  Then the app switches theme without a reload
```

## Edge cases

- **`prefers-reduced-motion`** is independent of theme — it disables animation, not theming (EC-015-2, UI-Reference §3).
- **Bootstrap inputs and their order:** cached `/auth/me` theme (signed-in) → `localStorage` last-known → `matchMedia` (guest/system) → `light` default. The order is unit-tested (F-TRF-015 test plan).
- **Admin screens** use the same mechanism — no per-area themes (EC-015-3).

## UI notes

- Theme control: profile screen select (UI-Reference §5.5) — three options, current value marked.
- No inline theme toggle on the landing (kept minimal per product analysis).
- Elevation rule differs per theme (light: 1px shadow; dark: 1px border, no shadow) — the tokens handle it, components don't choose.

## Technical notes

- `core/theme` bootstrap: inline script / 0-dep pre-paint script sets `data-theme` on `<html>` before first paint (TA-8.2).
- `tokens.css` maps tokens per `data-theme`; Tailwind config consumes the CSS variables — components reference tokens only (UI-Reference §1).
- Live OS tracking: `matchMedia` listener active **only** while in `system` mode.

## Links

- Feature: `TRF-015-dark-mode.md` (FR-015-1, FR-015-3, FR-015-4, AC-015-1…003)
- Related: US-015-02 (parity on every screen), US-008-04 (profile settings)
- Architecture: TA-8.2 (`core/theme`), UI-Reference §1
- Milestone: T-027
