# US-026-03 — Submit again when I have more files

**Feature:** F-COL-002 — Submission | **Status:** pending

---

**Story:** As a contributor, I want to submit more files later — even from the same person — so that I'm not forced to fit everything into one upload.
**Actor:** contributor (guest), collector (separate entries per submission).
**Goal:** multiple submissions per person, each a separate `CollectionEntry` with its own status and files.

## Preconditions

- At least one prior submission to the collection (or none — the first is just a submission).

## Happy path

1. Contributor opens the collection link again (any time while open/in-grace).
2. Uploads more files → **new** `CollectionEntry` (never appends to an existing entry).
3. The collector sees one row per submission (F-COL-003).

## Alternative flows

- **"Send something else"** on the confirmation screen → straight to a fresh submission (no reload).
- **After a decline** (F-COL-005): the resubmit link is a new submission (EC-029-1).

## Acceptance criteria

```gherkin
Given I already submitted once
When I submit again
Then a second, separate entry exists
And the two entries have independent status and files
```

## Edge cases

- No (collection, person) uniqueness (FR-026-3, documented).
- Each entry counts separately against the per-submission limit (owner plan, FR-026-4).

## UI notes

- Confirmation screen: **Send something else** primary-ish (secondary style), **Done** ghost.
- No "merge" or "edit previous" in MVP (documented — each submission is immutable).

## Technical notes

- `CollectionEntry` has no uniqueness constraint on (collection, senderEmail) — deliberate (FR-026-3).
- Per-entry status lifecycle is independent (F-COL-005).

## Links

- Feature: `COL-002-submission.md` (FR-026-3, AC-026-2)
- Related: F-COL-003 (dashboard rows), F-COL-005 (decline/resubmit)
