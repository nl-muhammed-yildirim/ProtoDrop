# US-021-04 — Manage my logo

**Feature:** F-PRF-001 — Custom "From" Branding | **Status:** pending

---

**Story:** As a Pro user, I want to upload, preview, and remove my organization logo, so that I control exactly what recipients see.
**Actor:** Pro/Business user.
**Goal:** logo upload (≤1 MB, PNG/JPEG/WebP/SVG) with preview and a remove action; invalid uploads rejected with the limit named.

## Preconditions

- User on Pro/Business (branding gated).

## Happy path

1. User uploads a logo (≤1 MB, supported type).
2. Preview shown; `AppUser.OrgLogoBlobPath` set.
3. **Remove logo** → falls back to name-only branding.

## Alternative flows

- **Oversized (2 MB)**: rejected with `LOGO_TOO_LARGE` + the size named; no partial state (AC-021-4).
- **Wrong type (GIF)**: rejected with `LOGO_TYPE_INVALID` (content-sniffed, FR-021-5).

## Acceptance criteria

```gherkin
Given a pro user uploads a 2 MB logo
When the upload is validated
Then it is rejected with the size limit named and no partial state

Given a pro user removes their logo
When a recipient opens a transfer
Then the header shows the org name without a logo
```

## Edge cases

- SVG with scripts → `<img>` render only (scripts don't run); documented risk (EC-021-3).
- Downgrade to Free with logo → stops appearing, values kept for re-upgrade (EC-021-5).

## UI notes

- Profile "Organization" logo uploader: drag/click, 1 MB cap, preview, **Remove logo** ghost.
- Free tier: uploader disabled + tooltip "Pro plan required".

## Technical notes

- `PUT /auth/me/logo` (SAS mint for the put, `avatars/{userId}/logo`); metadata row `AppUser.OrgLogoBlobPath?` (migration).
- Validation: size ≤ 1 MB, content-sniff type in {png, jpeg, webp, svg} (FR-021-5).

## Links

- Feature: `PRF-001-from-branding.md` (FR-021-3, FR-021-5, AC-021-4/5)
- Architecture: TA-3.5, TA-3.6, TA-4.1.3
- Design: UI-Reference §5.5
