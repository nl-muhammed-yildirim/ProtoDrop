# F-ALB-002 — Share Album

**Priority:** P1 (Phase 2c) | **Phase:** 2c — Albums
**Spec source:** `02-feature-plan.md` F-ALB-002 (outline → remapped below as FR-035-*) | **Architecture:** TA-3.6, TA-4.2
**Milestone tasks:** T-053 (M5)

---

## Description

Sharing an album is **link + email invite**, and — the key difference from a transfer — the link is **persistent**: it doesn't run on the 7-day clock. Anyone with the link (and the password, if set) can view and download. The email invite reuses the `f-email` pipeline (F-TRF-006) with an album template.

**Actors:** owner (shares), invitee/viewer (guest), operator.
**Value:** the album reaches people without them finding the link — and it keeps working long after the "transfer" world would have expired it.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-035-1 | Share: copy-link field (4.4 component) + **invite by email** (one or more addresses, comma-separated, normalized — F-TRF-002-5 pattern). |
| FR-035-2 | **Persistent link**: no `ExpiresAt` (F-ALB-001); the "expires in N days" footer is replaced by "This link doesn't expire." (the sentence that sells persistence, i18n string). |
| FR-035-3 | Email invite: subject "You're invited to {album}", one CTA button to the link, sender line = album owner's display name (F-PRF-001 branding applies if Pro). |
| FR-035-4 | Invitee gets no account; viewing/download is the same as any guest (F-ALB-004 mobile view). |
| FR-035-5 | Suppression: per (address, owner-email), F-TRF-006-5 — an invited address that unsubscribes isn't re-invited by *this* owner (other albums/owners unaffected). |

## Acceptance criteria

```gherkin
AC-035-1: An owner invites two addresses
  Then both get the invite email with a working link

AC-035-2: A recipient opens the link 30 days later
  Then the album still loads (no expiry)

AC-035-3: The "doesn't expire" copy is present
  Then the share screen and the email state persistence

AC-035-4: An invited address unsubscribes
  Then the owner's next invite to that address is not sent (suppression)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-035-1 | Invite the owner's own address | Allowed (self-notify, F-TRF-006-3 pattern — actually *sent*, unlike transfer self-skip; documented difference: album invites are opt-in) |
| EC-035-2 | Album closed after invite | Link shows "This album is closed." (F-ALB-003 close state); invitees with old links see it too |
| EC-035-3 | Password set *after* invites sent | Existing invitees get locked out at next load (they have the link, not the password) — documented; owner re-invites with the password in a new email (the owner's call, no auto-resend) |
| EC-035-4 | 100 invites at once | `MAX_EMAILS` per plan applies (F-TRF-007) — the share screen enforces per send |

## UI notes (UI-Reference §5.3/§5.4)

- Share screen (owner, album detail): copy field + email field + **Invite** button; helper "This link doesn't expire."
- Invite email: one button, calm; "You're invited to {album}" / "From {ownerName}".
- Persistence sentence is the hero of the share screen (the product's answer to "why isn't this a transfer?").

## Technical notes

- Endpoints (TA-4.2 extension, ADR note): `POST /api/v1/albums/{id}/invite` (41, `{ emails[] }`), share state is client-side (copy field).
- `f-email` template: `album_invite` (F-TRF-006 pipeline, suppression per FR-035-5).
- Events (TA-5.3 extension): `album.invited { albumId, emails[] }`.
- Telemetry (TA-10.2 extension): `album_shared`.

## Test plan

- Unit: invite normalization; suppression matching; plan MAX_EMAILS.
- Integration: AC-035-1…035-4 (fake sender: two invites, persistence = no expiry column, suppression honored).
- E2E: invite → email link → album loads (Playwright + dev log-sender visual).

## User stories

| ID | Story | File |
|---|---|---|
| US-035-01 | Invite people to my album by email | `US-035-01-invite-album.md` |
| US-035-02 | Share a link that never expires | `US-035-02-persistent-link.md` |
| US-035-03 | Have an invite that looks like me | `US-035-03-invite-branding.md` |
