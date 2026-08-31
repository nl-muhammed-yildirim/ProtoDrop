# F-PRF-001 — Custom "From" Branding

**Priority:** P1 | **Phase:** 1 — Pro
**Spec source:** `02-feature-plan.md` F-PRF-001 (outline FR-001-1…2 → remapped below as FR-021-*) | **Architecture:** TA-3.2 (`AppUser`), TA-4.2#13, TA-9.4
**Milestone tasks:** Phase 1 backlog (not yet created)

---

## Description

A Pro+ recipient should see the **organization**, not a stranger. Pro and Business users set an organization name and an optional logo; both appear in the recipient page header (in the "org logo slot" reserved in UI-Reference §5.3) and in the notification email's sender line. The per-transfer sender display name stays available and wins in the UI ("From: {personName} · {org}"). Free tier: no branding (org name falls back to the plain sender name).

**Actors:** Pro/Business user (sets branding), recipient (sees it), operator (validates logo uploads).
**Value:** the paid tier carries the sender's identity into the product — the single most visible Pro benefit, and the one recipients actually notice.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-021-1 | Pro+: set an **organization name** + **optional logo** (profile field, `PATCH /auth/me` extension), shown on the recipient page header and in the email "From:" line. |
| FR-021-2 | Per-transfer sender display name remains available per transfer; when both are set the recipient page shows "From: {personName} · {orgName}" and the email subject stays "Transfer from {orgName} — {personName}". |
| FR-021-3 | Logo: upload max 1 MB, PNG/JPEG/WebP/SVG; stored as `avatars/{userId}/logo` (TA-3.5, blob path pattern); served from the transfers container (read via 30-min SAS mint — same path as files, no public blob). |
| FR-021-4 | Gating: branding requires `PlanContext.Features.branding` (F-BIL-001 `FeaturesJson`); Free tier shows the plain sender name, no logo, no org slot. |
| FR-021-5 | Validation: organization name ≤ 60 chars, no line breaks; logo ≤ 1 MB with content sniffing (reject non-image binaries with `LOGO_TOO_LARGE` / `LOGO_TYPE_INVALID`, TA-4.1.3). |
| FR-021-6 | PII: org name is PII (TA-9.4) — hashed in telemetry; logo bytes are PII-free storage but the row link is PII. |

## Acceptance criteria

```gherkin
AC-021-1: Pro user sets org name "Studio Nova" and a logo
  Then a recipient opening the transfer sees "From: Anna K. · Studio Nova" with the logo in the header
  And the notification email header shows the org name

AC-021-2: Same user sends with the per-transfer sender name "Anna"
  Then the page shows "From: Anna · Studio Nova"

AC-021-3: Free user has no branding set
  Then the page shows only the plain sender name (no org slot, no logo)

AC-021-4: A Pro user uploads a 2 MB logo
  Then the upload is rejected with the size limit named and no partial state

AC-021-5: A Pro user deletes their logo
  Then the recipient page falls back to the org name without a logo (org slot stays)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-021-1 | Org name changes mid-life of a transfer | Transfers read branding **at page render time** (current values), not at send time — documented ("branding follows your profile") |
| EC-021-2 | Logo blob deleted by admin | Header renders org name without logo (graceful), telemetry `branding_logo_missing` |
| EC-021-3 | SVG logo with scripts | SVG served with `Content-Disposition: inline` + `<img>` rendering only (scripts don't run); documented as accepted risk in TA-9.6 |
| EC-021-4 | Org name only, no logo | Allowed — org slot renders name only |
| EC-021-5 | Downgrade to Free with branding set | Branding stops appearing (plan-gated, FR-021-4); profile values kept for re-upgrade |

## UI notes (UI-Reference §5.3, §5.5)

- Recipient header: logo (max 40 × 40 px, `--radius-sm`, contained) + "From: {personName} · {orgName}" — org slot was reserved in §5.3 for exactly this.
- Profile screen: "Organization" section — text input + logo upload (drag or click, 1 MB cap, preview) + **Remove logo** ghost button; disabled with tooltip "Pro plan required" on Free (plan screen CTA deep-link).
- Tone: no exclamation points; helper "Recipients see this on your transfers and in your emails."

## Technical notes

- Profile extension (endpoint 13, TA-4.2#13): `PATCH /auth/me` body gains `orgName?`, `orgLogo?` (separate `PUT /auth/me/logo` for the blob put — metadata in `AppUser` row: `OrgName`, `OrgLogoBlobPath?` — **schema change: migration required**, ADR note per AGENT.md §7).
- Blob: `avatars/{userId}/logo` in the `transfers` container (TA-3.5 pattern), lifecycle with user (deleted with account, F-TRF-008-6).
- Rendering: recipient page + email both fetch the logo URL via a short-lived SAS mint (30 min, TA-3.6) — email uses the same URL inside its 7-day life (TTL ≥ transfer retention? No: email renders the URL; if expired the client falls back — documented, acceptable because the email CTA is the primary action).
- Telemetry: `branding_applied` on recipient page view when org rendering occurred (PII-safe: plan + boolean only).

## Test plan

- Unit: logo validation (size/type), org-name rules, PII hashing in telemetry payload.
- Integration: AC-021-1/2 (recipient page + email header assertions with fake email sender), AC-021-4, AC-021-5; migration up/down for the two new columns.
- E2E: set branding → send → recipient sees header + email; Free plan shows no slot.

## User stories

| ID | Story | File |
|---|---|---|
| US-021-01 | Show my organization on my transfers | `US-021-01-org-branding.md` |
| US-021-02 | Brand my emails too | `US-021-02-email-branding.md` |
| US-021-03 | Keep my own name in front | `US-021-03-personal-name-wins.md` |
| US-021-04 | Manage my logo | `US-021-04-manage-logo.md` |
