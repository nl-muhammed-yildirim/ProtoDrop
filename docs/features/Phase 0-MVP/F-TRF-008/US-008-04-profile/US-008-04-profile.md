# US-008-04 — Manage my profile

**Feature:** F-TRF-008 — Accounts | **Status:** pending

---

**Story:** As a signed-in user, I want to set my display name, my theme, and see my plan, so that my transfers say "From: Ada Lovelace" and the app looks the way I like.
**Actor:** Signed-in user on the profile screen.
**Goal:** A small profile: identity + appearance + plan visibility.

## Preconditions

- Signed in (session cookie).

## Happy path

1. Profile shows: display name (editable), theme select (system / light / dark), plan line ("Free" + link to plan screen, P1), danger zone.
2. **Display name:** `PATCH /auth/me { displayName }` → saved; all **new** transfers default "From" to it (F-TRF-008-7); existing transfers keep their stored `SenderName`.
3. **Theme:** `PATCH /auth/me { theme }` → stored on the account; guests also get a theme choice (guests: browser setting, F-TRF-015).
4. **Sign out:** clears the cookie; the top bar returns to `Sign in`.

## Alternative flows

- **Display name empty:** server falls back to the email local part (never "From: (blank)").
- **Unicode names:** "Müller", "Zoë" round-trip (`NVARCHAR(100)`, TA-3.2).
- **Plan line:** read-only in MVP ("Free — upgrade coming soon" is *not* shown; just "Free").

## Acceptance criteria

```gherkin
Given I set my display name to "Ada Lovelace"
When I create a new transfer
Then the recipient page shows "From: Ada Lovelace"

Given I switch my theme to dark
When I sign out and back in
Then the dark theme is applied (persisted on the account)

Given I sign out
When the top bar renders
Then it shows "Sign in" again
```

## Edge cases

- Profile edits never touch `SenderName` on existing transfers (those are snapshots at send time — FR-002-4).
- `PATCH /auth/me` partial updates: only provided fields change.

## UI notes (UI-Reference §5.5)

- Profile card: name field + **Save**; theme select; plan line; divider; danger zone (US-008-06).
- Top bar avatar menu: Profile / Sign out.

## Technical notes

- Endpoints 12–13 (logout, me GET/PATCH): `UpdateProfileCommand` (name, theme), `LogoutCommand`.
- Theme stored on `AppUser.Theme` (TA-3.2); bootstrap reads it pre-paint (F-TRF-015, no flash).

## Links

- Feature: `TRF-008-accounts.md` (FR-008-4)
- Architecture: TA-4.2#12–13, TA-3.2
- Related: US-015-01 (theme behavior), US-002-04 ("From" default)
- Milestone: T-021
