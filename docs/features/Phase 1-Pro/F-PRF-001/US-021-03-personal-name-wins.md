# US-021-03 — Keep my own name in front

**Feature:** F-PRF-001 — Custom "From" Branding | **Status:** pending

---

**Story:** As a Pro user, I want my per-transfer display name to stay available and appear **in front of** the org, so that recipients know the person, with the org as context.
**Actor:** Pro/Business user, recipient.
**Goal:** "From: {personName} · {orgName}" — person first, org second; per-transfer name still overridable.

## Preconditions

- User on Pro/Business with org + per-transfer sender name set.

## Happy path

1. Pro user sends with sender name "Anna" and org "Studio Nova".
2. Page shows "From: Anna · Studio Nova".
3. Email subject "Transfer from Studio Nova — Anna".

## Alternative flows

- **No per-transfer name (guest)**: "From: Studio Nova" (org only).
- **Org not set**: "From: Anna" (person only, free-tier style).

## Acceptance criteria

```gherkin
Given a pro user with org "Studio Nova" sends with name "Anna"
When a recipient opens the transfer
Then the header shows "From: Anna · Studio Nova"
```

## Edge cases

- Person name empty + org set → org only (no dangling "·").
- Both empty → "Anonymous" (existing F-TRF-002 default).

## UI notes

- Header order is fixed: person first, org second, logo left of both.

## Technical notes

- Rendering helper composes `{personName} · {orgName}` with empty-part handling (one unit-tested formatter).

## Links

- Feature: `PRF-001-from-branding.md` (FR-021-2, AC-021-2)
- Related: F-TRF-002 (sender name), F-PRF-001 US-021-01
