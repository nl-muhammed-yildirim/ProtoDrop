# US-035-02 — Share a link that never expires

**Feature:** F-ALB-002 — Share Album | **Status:** pending

---

**Story:** As an album owner, I want my album link to keep working long after I sent it, so that "the link is dead" is never the answer to "where are the photos?".
**Actor:** album owner, any recipient of the link.
**Goal:** persistence as a *stated* product promise — the album has no `ExpiresAt`, and the share screen and the email both say "This link doesn't expire." (FR-035-2).

## Preconditions

- An album exists (US-034-01 — note: the `Album` DDL deliberately has no `ExpiresAtUtc`, FR-034-2).

## Happy path

1. Share screen: the copy field + the helper sentence "This link doesn't expire." — the sentence that sells persistence (FR-035-2, AC-035-3).
2. Invite emails carry the same promise in the body (the template line, i18n string).
3. A recipient opens the link 30 days later → the album loads (AC-035-2). Nothing to refresh, nothing to re-request.

## Alternative flows

- **Album closed by the owner** (F-ALB-003): the link still *works* — it resolves to "This album is closed." Persistence ≠ always-open; the chip vocabulary is the story (FR-036-6, EC-035-2).
- **Owner's account deleted** (EC-034-2): the album re-homes guest-owned and the link still loads — persistence survives the owner.

## Acceptance criteria

```gherkin
Given I shared the album link 30 days ago
When a recipient opens it now
Then the album still loads

Given the share screen and the invite email
When both are read
Then both state that the link doesn't expire
```

## Edge cases

- "Never expires" is about the *clock*, not the *state*: closed/cap-hit states still apply (documented — the sentence is about time, not about anything going).
- The 7-day transfer clock never applies to an album link (the contrast is the feature — "why isn't this a transfer?" is answered in the helper text).

## UI notes

- The helper sentence is the hero of the share screen: `--fs-small` under the copy field, not a tooltip (discovered by reading, not hunting).
- Invite email: same sentence in the body, one place, canonical string.

## Technical notes

- Proof by schema: no `ExpiresAtUtc` column on `Album` (FR-034-2); the test asserts the column's absence (a migration test), so "persistent" can't silently regress.
- i18n key e.g. `album.share.persistent` — one string, two placements.
- Telemetry: none new (the persistence claim needs no metric; `album_opened` with age would be nice-to-have, P2).

## Links

- Feature: `ALB-002-share-album.md` (FR-035-2, AC-035-2/3)
- Related: US-034-01 (the schema that proves it), F-ALB-003 (closed state coexisting with persistence), F-TRF-005 (the 7-day clock this is *not*)
