# US-035-01 — Invite people to my album by email

**Feature:** F-ALB-002 — Share Album | **Status:** pending

---

**Story:** As an album owner, I want to email people the album link, so that they get the album in their inbox instead of hunting for it in my chat history.
**Actor:** album owner; invitees (guests, no account).
**Goal:** the share screen — copy field + email field + **Invite** — sending an `album_invite` email through the `f-email` pipeline (FR-035-1/3).

## Preconditions

- An album exists (US-034-01).

## Happy path

1. Album detail → **Share**: copy field (4.4 component) with `{origin}/album/{linkId}` + email field (one or more addresses, comma-separated, normalized — F-TRF-002-5 pattern) + **Invite**.
2. `POST /api/v1/albums/{id}/invite` (endpoint 41, `{ emails[] }`) → per-address `album.invited` event → `f-email` sends `album_invite`: subject "You're invited to {album}", one CTA button to the link, sender line = the owner's display name (F-PRF-001 branding applies if Pro, US-035-03).
3. Invitee opens the link → the album (or its password gate) — no account needed (FR-035-4).

## Alternative flows

- **Invite the owner's own address** (EC-035-1): *sent* — album invites are opt-in, unlike transfer self-skip (documented difference).
- **Per-plan email cap**: `MAX_EMAILS` applies per send (EC-035-4, F-TRF-007) — the share screen enforces per send.
- **Suppressed address**: an invited address that unsubscribed isn't re-invited by *this* owner (FR-035-5, F-TRF-006-5) — other albums/owners unaffected.

## Acceptance criteria

```gherkin
Given I invite two addresses
When the emails go out
Then both recipients get the invite email with a working link

Given an invited address unsubscribed
When I invite the same address again
Then the invite is not sent (suppression per (address, owner))
```

## Edge cases

- Album closed after invite: old links show "This album is closed." (F-ALB-003 close state, EC-035-2).
- Duplicate addresses in one send: normalized + de-duped (F-TRF-002-5), one email each.

## UI notes

- Share screen: copy field (4.4) + email field + **Invite** button; helper "This link doesn't expire." (US-035-02).
- Email: one button, calm; "You're invited to {album}" / "From {ownerName}".

## Technical notes

- `POST /api/v1/albums/{id}/invite` (endpoint 41); `album.invited { albumId, emails[] }` event; telemetry `album_shared`.
- Template `album_invite` (F-TRF-006 pipeline — retries, suppression, dead-letter, localization all inherited).
- Suppression key: (address, owner-email) per F-TRF-006-5.

## Links

- Feature: `ALB-002-share-album.md` (FR-035-1/3/4/5, AC-035-1/4, EC-035-1/2/4)
- Related: US-035-02 (the link), US-035-03 (the sender line), F-TRF-006 (pipeline)
