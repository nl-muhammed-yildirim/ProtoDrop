# T-066 — Manage my profile

**Story:** US-008-04 | **Feature:** F-TRF-008 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-008/US-008-04-profile.md`
**Coarse task (Milestone-Backlog.md):** T-021
**Status:** pending

---

## Scope

As a signed-in user, I want to set my display name, my theme, and see my plan, so that my transfers say "From: Ada Lovelace" and the app looks the way I like.

**Actor:** Signed-in user on the profile screen.

**Goal:** A small profile: identity + appearance + plan visibility.

Happy path:

1. Profile shows: display name (editable), theme select (system / light / dark), plan line ("Free" + link to plan screen, P1), danger zone.
2. **Display name:** `PATCH /auth/me { displayName }` → saved; all **new** transfers default "From" to it (F-TRF-008-7); existing transfers keep their stored `SenderName`.
3. **Theme:** `PATCH /auth/me { theme }` → stored on the account; guests also get a theme choice (guests: browser setting, F-TRF-015).
4. **Sign out:** clears the cookie; the top bar returns to `Sign in`.

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

## Exit check

- [ ] Scenario 1: I set my display name to "Ada Lovelace"
- [ ] Scenario 2: I switch my theme to dark
- [ ] Scenario 3: I sign out
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-008/US-008-04-profile.md`
- Feature: `TRF-008-accounts.md` (FR-008-4)
- Architecture: TA-4.2#12–13, TA-3.2
- Related: US-015-01 (theme behavior), US-002-04 ("From" default)
- Milestone: T-021
