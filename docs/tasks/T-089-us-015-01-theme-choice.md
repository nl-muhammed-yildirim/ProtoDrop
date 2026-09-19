# T-089 — Choose light, dark, or system theme

**Story:** US-015-01 | **Feature:** F-TRF-015 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-015/US-015-01-theme-choice.md`
**Coarse task (Milestone-Backlog.md):** T-027
**Status:** pending

---

## Scope

As a user, I want to pick light, dark, or "match my system" and have the choice stick, so that the product matches how I use my computer.

**Actor:** Signed-in user (choice stored on the account, `AppUser.Theme`, F-TRF-008-4); guest (system by default, FR-015-1).

**Goal:** theme = `system` | `light` | `dark`; the stored choice applies **before first paint** — no flash of the wrong theme.

Happy path:

1. Signed-in user opens **Profile → Theme** and picks **Dark**.
2. The value is stored on the account (`AppUser.Theme`); the profile select (UI-Reference §5.5) reflects the choice.
3. Every page load and refresh from then on: dark tokens are applied **before first paint** via the inline `data-theme` bootstrap (FR-015-3, AC-015-1).
4. Guest: no profile; the guest experience is `system` — the OS preference is respected and tracked live (FR-015-1, AC-015-2).
5. **System live-tracking:** while in `system` mode, an OS theme change with the tab open switches the app without a reload (FR-015-4, AC-015-3) — `matchMedia('(prefers-color-scheme: dark)')` listener.

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

## Exit check

- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-015/US-015-01-theme-choice.md`
- Feature: `TRF-015-dark-mode.md` (FR-015-1, FR-015-3, FR-015-4, AC-015-1…003)
- Related: US-015-02 (parity on every screen), US-008-04 (profile settings)
- Architecture: TA-8.2 (`core/theme`), UI-Reference §1
- Milestone: T-027
