# US-036-04 — Close my album to new uploads

**Feature:** F-ALB-003 — Contribute to an Album | **Status:** pending

---

**Story:** As an album owner, I want to close my album to new uploads when I'm done collecting, so that the gallery freezes — viewable, but nobody adds to it.
**Actor:** album owner.
**Goal:** `Status = Closed` — contributors' links show "This album is closed.", the grid is read-only, a gray `Closed` chip; re-open allowed (FR-036-6).

## Preconditions

- An album exists (US-034-01), status `Active`.

## Happy path

1. Album detail (owner) → **Close album** → confirm modal ("Close this album? Contributors won't be able to upload.") → `PATCH` status.
2. Contributor upload links now render "This album is closed." (no drop zone) — the items already contributed remain in the grid (AC-036-4).
3. My Albums row shows the gray `Closed` chip; **Re-open** is available (sets `Active` again — unlimited re-opens, documented as for F-COL-004).

## Alternative flows

- **Re-open**: contributors' links work again; no notification on close/re-open in MVP (the contributor sees the state when they arrive — documented; a close-notification email is Phase 3).
- **Closed, then owner uploads**: the owner *can* still upload after close (the owner is not "done collecting" — documented owner exception; "close" is about *invited* contributors).

## Acceptance criteria

```gherkin
Given I closed the album
When a contributor opens their upload link
Then it shows "This album is closed." and the grid is read-only

Given I re-open the album
When the contributor opens the link again
Then they can upload
```

## Edge cases

- Invite sent, then album closed (F-ALB-002 EC-035-2): recipients of old invite links see the closed state too — persistence (US-035-02) + state (this story) coexist.
- Close with zero items: allowed (an album you changed your mind about) — no "at least one item" rule.

## UI notes

- Chip vocabulary (F-TRF-016 text rule): `Active` (green) · `Closed` (gray).
- **Close album** ghost button in album detail (owner); **Re-open** takes its place when closed.

## Technical notes

- `Album.Status TINYINT (0 Active, 1 Closed)` (F-ALB-001 DDL); `PATCH /api/v1/albums/{id}` (status) — owner auth only.
- Event `album.status_changed { albumId, from, to }` (TA-5.3 extension).
- Contributor token check at draft creation: token valid AND album `Active` (the two conditions, one 409 state screen).

## Links

- Feature: `ALB-003-contribute-album.md` (FR-036-6, AC-036-4)
- Related: US-036-01 (the contributor link this gates), F-ALB-002 (closed state + persistent link), F-COL-004 (the state vocabulary borrowed)
