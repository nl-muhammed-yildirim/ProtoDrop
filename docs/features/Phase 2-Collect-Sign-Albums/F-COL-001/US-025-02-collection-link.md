# US-025-02 — Get a link I can send to people

**Feature:** F-COL-001 — Create Collection | **Status:** pending

---

**Story:** As a collector, I want a short link I can send anywhere, so that the people who need to send me files don't need to search for anything.
**Actor:** collector, their contributors.
**Goal:** `{origin}/collect/{linkId}` — 8-char Crockford, same generator and collision rule as transfers.

## Preconditions

- A collection exists.

## Happy path

1. Creating the collection also mints its `linkId` (unique, `UQ_Collection_LinkId`).
2. The link screen shows a copy field (UI-Reference §4.4) with the collection URL.
3. Anyone opening the link gets the collection page — **no account required** (AC-025-2).

## Alternative flows

- **Collision** (astronomically unlikely): regenerate once, then 500 + telemetry (EC-025-1, F-TRF-002 rule).
- **Two collections**: different linkIds, each resolves to its own page (AC-025-3).

## Acceptance criteria

```gherkin
Given I created a collection
When I open its link
Then the collection page renders for a guest (no account)

Given I create a second collection
When I check both links
Then they differ and each resolves to its own page
```

## Edge cases

- Link shown on the create confirmation screen (immediate copy) and in My Collections.
- The link is the *only* identifier — no slug, no search (documented).

## UI notes

- Copy field pattern (4.4) verbatim from transfers — consistency across products.

## Technical notes

- LinkId generator shared with F-TRF-002 (Crockford base32, 8 chars, no `0/O/1/I`).
- Guest GET on the public endpoint (no cookie needed, TA-4.2).

## Links

- Feature: `COL-001-create-collection.md` (FR-025-2, AC-025-2/3)
- Related: F-TRF-002 (generator), F-COL-002 (contributor page)
