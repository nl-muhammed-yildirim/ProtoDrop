# US-035-03 — Have an invite that looks like me

**Feature:** F-ALB-002 — Share Album | **Status:** pending

---

**Story:** As a Pro album owner, I want the album invite email to come from my name (and my org, if I set one), so that recipients recognize the sender and don't treat it as platform noise.
**Actor:** album owner (Pro, with F-PRF-001 branding configured), invitees.
**Goal:** the `album_invite` template carries the owner's display name — and the org name/logo when F-PRF-001 branding is active (FR-035-3, US-021-02 pattern).

## Preconditions

- An album exists; the owner is on Pro (Free gets the platform default, documented).

## Happy path

1. Invite emails render the sender line from the owner's branding: "From {ownerName}" — and, when org branding is set (F-PRF-001), the org name + logo in the email header/footer (the US-021-02 template slot, reused verbatim).
2. The CTA button links to `{origin}/album/{linkId}`; the subject is "You're invited to {album}" (FR-035-3).

## Alternative flows

- **No org branding (personal Pro)**: just the personal display name (the US-021-03 precedence — personal name wins over nothing here; the org slot is simply empty).
- **Free owner**: the platform default from-line — consistent with F-PRF-001 gating ("branded email requires Pro" is *not* shown, the default is just used).

## Acceptance criteria

```gherkin
Given I have org branding set
When I invite someone
Then the email header shows my org name and logo and the body is "You're invited to {album}"

Given I have no org branding
When I invite someone
Then the email uses my personal display name only
```

## Edge cases

- Branding changed after an invite: later invites use the new branding; already-sent emails keep the old one (email is a snapshot — documented).
- Logo missing/broken: the template falls back to name-only (the F-PRF-001-4 managed-logo rules apply).

## UI notes

- Nothing in the web UI beyond the share screen — the branding lives in the email (F-PRF-001's surface).
- The template is the *only* album email in Phase 2 (contributor/invitee lifecycle emails are Phase 3 — documented).

## Technical notes

- Template `album_invite` reads the owner's `Brand` (F-PRF-001 entity) at render time — the snapshot rule above.
- Suppression/sender-infrastructure per F-TRF-006 (unchanged).

## Links

- Feature: `ALB-002-share-album.md` (FR-035-3)
- Related: F-PRF-001 (the branding this renders), US-021-02 (the template slot), US-035-01 (the send path)
