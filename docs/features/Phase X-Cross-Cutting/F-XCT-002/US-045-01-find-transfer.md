# US-045-01 — Find a transfer by file name or recipient

**Feature:** F-XCT-002 — Search (Transfers) | **Status:** pending

---

**Story:** As a user with dozens of transfers in My Files, I want to type a file name or a recipient's email and jump straight to that transfer, so that I never scroll through months of history to re-send the one deck I keep forwarding.
**Actor:** Signed-in user (and, in the admin area, an admin searching all transfers).
**Goal:** One search box in My Files; type "invoice" or "a@b.com" → the right transfer is on top.

## Preconditions

- `feature.search` is true (F-XCT-001 gate, US-044-02).
- The transfer exists and is visible to the user (My Files scoping, F-TRF-009).

## Happy path

1. User opens My Files and types `logo` in the search box (300 ms debounce).
2. The list is replaced by transfers whose file names, recipient addresses, or sender name contain the match, ordered by relevance.
3. The user clicks the result → transfer detail (F-TRF-009) or re-send (F-TRF-010) — no extra navigation invented by search.
4. Clearing the search restores the normal chronological list with its own cursor.

## Alternative flows

- **No match:** "No transfers match 'logo'." + **Clear search** action (EC-045-1).
- **Admin use:** same input in Admin → Transfers searches *all* transfers; rows show the owner (FR-045-4).
- **Flag off:** the box is absent; a stale `?q=` in the URL is ignored by the API (degrades to the plain list — US-044-02 contract).

## Acceptance criteria

```gherkin
Given a transfer whose file "q4-financial-report.pdf" was sent to a@b.com
When I search "financial" in My Files
Then that transfer appears first in the results
And a transfer named "holiday.mp4" sent to c@d.com does not appear

Given I search "a@b.com"
When results load
Then the transfer appears (match on recipient, not file name)

Given the feature.search flag is off
When I open My Files
Then the search box is not rendered
And a q parameter in the URL does not filter the list
```

## Edge cases

- Wildcard characters in the query (`*`, `~`, `"`) match literally — no full-text operator leakage (FR-045-6, AC-045-3).
- A transfer matching both a file name and a recipient appears **once** (EC-045-6).
- Search never crosses owners: user A's query can't surface user B's transfer (FR-045-7) — the test that matters most in the privacy review.

## UI notes

- Toolbar input, max 100 chars (400 `Q_TOO_LONG` on overflow, UI trims first), "×" clear button, debounce; cursor resets per query.
- No search suggestions in P1; that's a later refinement, not specced here.

## Technical notes

- Endpoint 14 (`GET /api/v1/transfers`) with `q`; full-text predicate on the `SearchText` column (FR-045-2), `CONTAINSTABLE` rank ordering, opaque search cursor.
- De-dup by transfer id; owner scoping in the predicate (`OwnerAppUserId = @me`) so the index does the work and SQL does the ownership check.

## Links

- Feature: `XCT-002-search.md` (FR-045-1…8)
- Plan AC: AC-045-1…045-3
- Related: US-044-02 (the gate), US-009-01/02 (the list this searches), US-011-02 (admin transfer list)
- Architecture: TA-4.2 (endpoint 14/21), TA-3.4 (flag)
- Milestone: T-066
