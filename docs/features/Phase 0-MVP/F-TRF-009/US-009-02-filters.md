# US-009-02 — Filter my transfers

**Feature:** F-TRF-009 — My Files (History) | **Status:** pending

---

**Story:** As a user with many transfers, I want to see only the ones that matter right now (active) or only what already expired, so that the list stays useful as it grows.
**Actor:** Signed-in user on `/files`.
**Goal:** Three views of the same list: All / Active / Expired.

## Preconditions

- Signed in; at least one transfer (filters on an empty list are still rendered).

## Happy path

1. Filter pills: **All / Active / Expired** (UI-Reference §5.4, `--accent-soft` active pill).
2. **Active** = status 1 (+ 3: download-limit, still serving downloads — no, it's *not* serving; see mapping below) — canonical mapping: Active = `Status=1`; Expired = `Status IN (2,3)` (expired + download-limit); All = everything not physically gone.
3. Selecting a pill refetches with `?status=`; the cursor resets.
4. Default: All.

## Alternative flows

- **Pill counts (P1 niceness, MVP-allowed):** "(12)" next to All, "(3)" next to Active — from the list's metadata; optional.
- **Expired includes DownloadLimit** because both are "no longer serving" states for the recipient (US-005-03).

## Acceptance criteria

```gherkin
Given I have 5 active and 4 expired transfers
When I select the Active filter
Then only the 5 active transfers are listed

When I select the Expired filter
Then the 4 expired (or download-limit) transfers are listed

When I select All
Then all 9 are listed
```

## Edge cases

- Filter + cursor paging compose: `?status=active&cursor=…` (TA-4.1.4).
- A transfer that expires *while the list is open* stays in Active until the next render (staleness acceptable, no live refetch loop).

## UI notes

- Pills per UI-Reference §5.4; active pill `--accent-soft`, text `--accent`.

## Technical notes

- `?status=` values: `all` (default), `active` (1), `expired` (2,3).
- Query: owner scope + status predicate + existing `IX_Transfer_Owner`/`IX_Transfer_Expiry` coverage.

## Links

- Feature: `TRF-009-my-files.md` (FR-009-4)
- Architecture: TA-4.2#14, TA-4.1.4
- Milestone: T-022
