# F-TRF-008 — Accounts (Sign-up / Sign-in)

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-008 | **Architecture:** TA-4.2#8–13, TA-9.1, TA-3.2 (`AppUser`, `AuthToken`)
**Milestone tasks:** T-021

---

## Description

Most users send files as guests; power users want **history without giving the product their whole life**. Accounts are deliberately light: **email + password** and a **magic link** (no OAuth in MVP), a cookie session, a small profile (display name, theme, plan), and a GDPR-grade delete that re-homes the user's transfers to "Anonymous". Sign-up **is** sign-in: the same form, unknown email → create.

**Actors:** new user, returning user, operator (sees plan state).
**Value:** attribution → My Files (F-TRF-009) → retention; GDPR hygiene from day one.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-008-1 | Auth: **email + password** (PBKDF2 via ASP.NET Identity `PasswordHasher`) and **magic link** (email a 10-min, single-use login token). No OAuth in MVP (ADR-007). |
| FR-008-2 | Session: httpOnly cookie `wa.session`, `SameSite=Lax`, Secure, **30-day rolling** (refreshed on use, TA-9.1). |
| FR-008-3 | **Sign-up = sign-in** (same form; unknown email → account created). Email is the identifier, stored lowercased. |
| FR-008-4 | Profile: display name (default "From" on all transfers), theme (system/light/dark), plan (Free by default). |
| FR-008-5 | Password rules: min 8 chars; no common-password check in MVP. |
| FR-008-6 | "Forgot password" → magic-link email (reuses the email worker; subject "Your ProtoDrop link"). |
| FR-008-7 | Transfers created while signed in are attributed to the account (`Transfer.OwnerAppUserId`) → appear in My Files (F-TRF-009); "From" defaults to the display name. |
| FR-008-8 | **Delete account (GDPR):** deletes the `AppUser` row (soft), re-homes active transfers to `SenderName`-only anonymous ownership (`OwnerAppUserId = NULL`, sender identity preserved), deletes profile-specific rows. Recipient pages are unaffected. |

## Acceptance criteria

```gherkin
AC-008-1: New user enters email + password
  Then account is created, session cookie set, and the profile screen shows

AC-008-2: Magic link clicked twice
  Then the second click is invalid (token single-use)

AC-008-3: Signed-in user creates a transfer
  Then the transfer is listed under their "My Files"
  And "From" defaults to their display name
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-008-1 | Email case-sensitivity | Stored lowercased; compare case-insensitive (`UQ_AppUser_Email` on the lowercase form) |
| EC-008-2 | Account deleted while a transfer is active | Transfer survives as anonymous (recipient page unaffected); `account_deleted` event emitted |
| EC-008-3 | Magic link expires (10 min) | "This link has expired" state + "Send a new link" button; token row still in `AuthToken` (not yet redeemed) |
| EC-008-4 | Sign-up with a different case of an existing email | Signs the user *in* to the existing account (no "email taken" error — sign-up is sign-in) |

## UI notes (UI-Reference §5.5)

- Single card with `Sign in` / `Sign up` tabs (same form; the tab only changes the headline copy and creates vs. finds).
- Fields: email, password (min 8, helper text), **Continue** primary; "Email me a magic link" ghost secondary.
- Forgot password: email field → "Link sent to {email}" state (no "or sign in as…" clutter).
- Profile: display name, theme select, plan line, danger zone **Delete account** (confirm modal with GDPR copy: "Your active transfers keep working as Anonymous.").
- Top bar: `Sign in` → avatar menu (profile, theme, sign out) when signed in (UI-Reference §5.1).

## Technical notes

- `AppUser` + `AuthToken` tables (TA-3.2, ADR-007: no ASP.NET Identity tables); `PasswordHash` column for both user and (separate) transfer passwords — same hasher, separate salts.
- Magic link: 256-bit random, stored hashed (`sha256`), `Purpose=1` (login) / `2` (forgot), 10-min TTL, single-use via `RedeemedAtUtc` (TA-9.1).
- Session cookie format: `Base64(userId|exp|sig)` HMAC-SHA256 with `Wa:Jwt:Secret` (TA-9.1); 30 d rolling.
- Endpoints 8–13 (TA-4.2): signup, login, magic-link/redeem, forgot-password, logout, me (GET/PATCH/DELETE).
- Rate limits: `/api/v1/auth/*` 10/min per IP (Front Door WAF, TA-9.5) + API-layer 5/min (second layer).
- Telemetry: `user_created`, `login_success`, `login_failed`, `account_deleted` (TA-10.2).

## Test plan

- Unit: `PasswordHasher` round-trip; token single-use logic; email normalization; session claim parsing.
- Integration: signup → cookie → `GET /auth/me`; login wrong password → `login_failed` + 401; magic link redeem twice → second is `TOKEN_REDEEMED` (VALIDATION); forgot → token row created; DELETE me → user soft-deleted, transfer re-homed (`OwnerAppUserId=NULL`, `SenderName` kept).
- E2E: full account journey (T-022 My Files attribution depends on this).

## User stories

| ID | Story | File |
|---|---|---|
| US-008-01 | Sign up and sign in with email + password | `US-008-01-signup-password.md` |
| US-008-02 | Sign in with a magic link | `US-008-02-magic-link.md` |
| US-008-03 | Recover my account when I forgot the password | `US-008-03-forgot-password.md` |
| US-008-04 | Manage my profile | `US-008-04-profile.md` |
| US-008-05 | See my transfers in My Files | `US-008-05-attribution.md` |
| US-008-06 | Delete my account (GDPR) | `US-008-06-delete-account.md` |
