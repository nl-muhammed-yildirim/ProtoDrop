# T-008-07 — Profile screen: name, theme, plan + avatar menu (UI-Reference §5.5)

**Story:** US-008-04 | **Spec:** FR-008-4, AC-008-3 (client half), TA-4.2#12–13 | **Size:** M
**Depends on:** T-008-06 (GET/PATCH /auth/me — the screen renders and posts to it), T-008-03 (top bar avatar menu shell)

---

## Context to read (only these)

- `US-008-04-profile.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-008-accounts.md` → FR-008-4 + UI notes (profile card layout)

## Instructions

1. Build the profile screen in `wa.web`: display name field + **Save** (`PATCH /auth/me { displayName }`), theme select (system / light / dark, `PATCH /auth/me { theme }` — stored on the account; bootstrap reads it pre-paint so there's no flash of wrong theme, F-TRF-015), plan line (read-only "Free"), divider, danger zone (US-008-06 renders its button here).
2. **Sign out** in the top bar avatar menu → `POST /auth/logout` → top bar returns to `Sign in`.
3. Display name empty on save → server falls back to the email local part; show the saved value (never "From: (blank)"). Unicode names ("Müller", "Zoë") round-trip without mangling.
4. Plan line is read-only in MVP — no "upgrade coming soon" copy, just "Free".

## Exit check

- [ ] Set display name to "Ada Lovelace" → saved; a new transfer's recipient page shows "From: Ada Lovelace" (AC-008-3 client half)
- [ ] Switch theme to dark → sign out and back in → dark theme applied from the account (persisted, no flash)
- [ ] Sign out → top bar shows "Sign in" again
- [ ] Empty display name save → falls back to email local part; Unicode names round-trip

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS; GET/PATCH /auth/me + logout in place).
Task T-008-07 — build the profile screen (milestone task T-021, part 7).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-04-profile/US-008-04-profile.md (happy path + Alternative flows + UI notes) and F-TRF-008-accounts.md (FR-008-4).
Do exactly:
1. Build the profile screen in wa.web: display name field + Save (PATCH /auth/me { displayName }), theme select system/light/dark (PATCH /auth/me { theme} — stored on the account, read pre-paint so there's no flash of wrong theme), plan line (read-only "Free"), divider, danger zone (US-008-06 renders its button here).
2. Sign out in the top bar avatar menu → POST /auth/logout → top bar returns to Sign in.
3. Empty display name on save → server falls back to the email local part; show the saved value (never "From: (blank)"). Unicode names round-trip without mangling.
4. Plan line is read-only in MVP — just "Free".
Done when: AC-008-3's client half holds — the display name persists and shows as "From" on new transfers, the theme survives sign-out/sign-in from the account, and signing out returns the top bar to Sign in.
Constraints: UI per UI-Reference §5.5 (profile card layout); profile edits never touch SenderName on existing transfers (snapshots at send time — FR-002-4); guests get a theme choice via browser setting (F-TRF-015), not this screen.
```
