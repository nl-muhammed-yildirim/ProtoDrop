# T-008-03 — Auth screen: sign-in/sign-up tabs + magic-link ghost (UI-Reference §5.5)

**Story:** US-008-01 (+ US-008-02/03 entry points) | **Spec:** FR-008-1/3, AC-008-1 (client half), EC-008-4 | **Size:** M
**Depends on:** T-008-02 (login endpoint + session cookie — the form posts to it), T-008-05 (magic-link endpoint — the ghost button posts to it)

---

## Context to read (only these)

- `US-008-01-signup-password.md` → happy path + UI notes
- `../US-008-02-magic-link/US-008-02-magic-link.md` → UI notes ("Link sent" state, cooldown)
- `../US-008-03-forgot-password/US-008-03-forgot-password.md` → happy path (forgot flow entry)
- `../../F-TRF-008-accounts.md` → FR-008-1/3 + UI notes

## Instructions

1. Build the auth screen in `wa.web`: single card with **Sign in** / **Sign up** tabs — same form, the tab only changes headline copy (no separate "you're already registered" path, FR-008-3).
2. Fields: email + password (min 8, helper text "At least 8 characters."), inline validation before submit; **Continue** primary button posts to `/auth/signup` on the sign-up tab and `/auth/login` on the sign-in tab (same form, same button — EC-008-4).
3. Add **"Email me a magic link"** ghost secondary under the form → `POST /auth/magic-link`; on success replace the form with the "Link sent to {email}" state (no double-submission possible) + "Didn't get it? Send another" ghost with a 10 s cooldown counter.
4. Add **"Forgot password?"** ghost under the password field → email-only step → `POST /auth/forgot-password` → same "Link sent to {email}" state (identical whether or not the email exists — no account enumeration).
5. On any success (signup/login/magic-link redeem) navigate to the profile screen; top bar switches from `Sign in` to the avatar menu (profile, theme, sign out).

## Exit check

- [ ] Sign-up tab + new email → account created, cookie set, profile screen shows (AC-008-1 client half)
- [ ] Sign-in tab + wrong password → "Wrong password" inline; no "user not found" anywhere
- [ ] Tab switch changes copy only — same fields, same button
- [ ] Magic-link ghost → "Link sent to {email}" state replaces the form; cooldown counter blocks re-send for 10 s
- [ ] Forgot-password flow shows "Link sent to {email}" for both known and unknown emails

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS; signup/login/magic-link/forgot endpoints in place).
Task T-008-03 — build the auth screen (milestone task T-021, part 3).
Read first (only): docs/features/Phase 0-MVP/F-TRF-008/US-008-01-signup-password/US-008-01-signup-password.md (happy path + UI notes), US-008-02-magic-link.md (UI notes — "Link sent" state + 10 s cooldown), US-008-03-forgot-password.md (happy path), and F-TRF-008-accounts.md (FR-008-1/3).
Do exactly:
1. Build the auth screen in wa.web: single card with Sign in / Sign up tabs — same form, tab only changes headline copy (FR-008-3).
2. Fields: email + password (min 8, helper "At least 8 characters."), inline validation before submit; Continue posts to /auth/signup on the sign-up tab and /auth/login on the sign-in tab (EC-008-4).
3. Add "Email me a magic link" ghost secondary → POST /auth/magic-link; on success replace the form with "Link sent to {email}" + "Didn't get it? Send another" ghost with 10 s cooldown counter.
4. Add "Forgot password?" ghost under the password field → email-only step → POST /auth/forgot-password → identical "Link sent to {email}" state (no account enumeration).
5. On any success navigate to the profile screen; top bar switches from Sign in to the avatar menu (profile, theme, sign out).
Done when: AC-008-1 holds in the browser — new email creates the account and lands on the profile screen, wrong password shows "Wrong password" inline, and both magic-link entry points reach the "Link sent" state.
Constraints: UI per UI-Reference §5.5 (single card, tabs are copy-only); no separate registration path — sign-up is sign-in; guests get a theme choice via browser setting (F-TRF-015), not here.
```
