# US-021-01 — Show my organization on my transfers

**Feature:** F-PRF-001 — Custom "From" Branding | **Status:** pending

---

**Story:** As a Pro user, I want my organization name (and logo) shown on my transfers' recipient page, so that recipients see **my company**, not a stranger.
**Actor:** Pro/Business user (sets branding), recipient (sees it).
**Goal:** recipient page header renders "From: {personName} · {orgName}" with the logo in the reserved org slot.

## Preconditions

- User on Pro/Business (branding = `PlanContext.Features.branding`).
- Org name (+ optional logo) set on the profile.

## Happy path

1. User sets organization name "Studio Nova" (+ logo).
2. Recipient opens a transfer → header shows "From: Anna K. · Studio Nova" + logo.
3. The org slot (UI-Reference §5.3) renders the logo (max 40×40, `--radius-sm`).

## Alternative flows

- **Org name only, no logo**: slot renders name only (EC-021-4).
- **Free tier**: no org slot, no logo — plain sender name (FR-021-4, AC-021-3).

## Acceptance criteria

```gherkin
Given a pro user set org "Studio Nova" and a logo
When a recipient opens a transfer
Then the header shows "From: {personName} · Studio Nova" with the logo

Given a free user
When a recipient opens a transfer
Then only the plain sender name shows (no org slot, no logo)
```

## Edge cases

- Branding read at **page render time** (current profile values), not send time (EC-021-1, documented).
- Logo blob deleted by admin → name renders, logo slot falls back (EC-021-2, `branding_logo_missing`).

## UI notes

- Recipient header: logo + "From: {personName} · {orgName}" (UI-Reference §5.3 org slot).
- Profile "Organization" section: text input + logo upload (1 MB) + preview + **Remove logo**.

## Technical notes

- `AppUser.OrgName`, `AppUser.OrgLogoBlobPath?` (migration required, ADR note); `PUT /auth/me/logo`.
- Logo in `avatars/{userId}/logo` (TA-3.5), served via 30-min SAS mint (TA-3.6).
- PII: org name hashed in telemetry (TA-9.4); `branding_applied` event.

## Links

- Feature: `PRF-001-from-branding.md` (FR-021-1, FR-021-4, AC-021-1/3)
- Architecture: TA-3.2, TA-3.5, TA-3.6, TA-9.4
- Design: UI-Reference §5.3, §5.5
