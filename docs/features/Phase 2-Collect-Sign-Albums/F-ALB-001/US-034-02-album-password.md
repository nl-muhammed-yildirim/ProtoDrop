# US-034-02 — Protect my album with a password

**Feature:** F-ALB-001 — Create Album | **Status:** pending

---

**Story:** As an album owner, I want to gate my album with a password, so that only the people I told can see it — without asking them for an account.
**Actor:** album owner, guest viewer with the link.
**Goal:** an optional single-password gate on `{origin}/album/{linkId}` — same semantics as the transfer password (F-TRF-003-6): one password, all viewers, stored as a hash.

## Preconditions

- An album exists (US-034-01); password set or unset at creation.

## Happy path

1. Creation with a password: `PasswordHash` stored (PBKDF2-family hash — the transfer password hash, same constant, documented).
2. A guest opens the link: the single-password gate renders (F-TRF-003-6 pattern) — field + **Open** + "No account needed." helper.
3. Correct password → album loads; the gate is not shown again for the session (session cookie scoped to the album link — documented).
4. Wrong password → shake + "Try again." (no count of attempts in MVP, documented; rate limiting per TA-4.1).

## Alternative flows

- **Password added after sharing** (F-ALB-002 EC-035-3): existing invitees are locked out at next load — documented; the owner re-invites with the password (the owner's call, no auto-resend).
- **No password set**: the gate is absent — the link alone is the key (FR-035-2 persistence + this gate).

## Acceptance criteria

```gherkin
Given a password-protected album
When a guest opens the link
Then the password gate is shown (F-TRF-003-6 pattern)

Given the guest enters the correct password
When they press Open
Then the album loads and the gate is not shown again this session
```

## Edge cases

- Password change: owner can replace it (My Albums detail, P2 — MVP: set at creation only, documented against the FR-034-1 shape).
- Case-insensitive password check (the transfer rule), constant-time compare.

## UI notes

- Gate screen: single card — lock icon, "This album is password-protected.", field (type=password, show/hide), **Open** primary.
- The password field is `autocomplete=current-password` (the browser helps, not hinders).

## Technical notes

- `Album.PasswordHash VARCHAR(256) NULL` (F-ALB-001 DDL).
- Gate endpoint: `POST /api/v1/albums/{linkId}/unlock` → session cookie `album_unlocked_{linkId}` (HMAC, short TTL — 12 h, documented).
- Telemetry `album_unlock_failed { albumId }` (no address/PII).

## Links

- Feature: `ALB-001-create-album.md` (FR-034-1, AC-034-2)
- Related: F-TRF-003 (the gate pattern reused), F-ALB-002 (sharing the protected link), F-ALB-004 (mobile gate rendering)
