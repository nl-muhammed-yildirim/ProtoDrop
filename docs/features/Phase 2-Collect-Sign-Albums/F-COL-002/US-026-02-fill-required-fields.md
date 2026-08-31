# US-026-02 — Fill in only what's required

**Feature:** F-COL-002 — Submission | **Status:** pending

---

**Story:** As a contributor, I want to fill in only the fields the collector required — name, and email if required — so that I'm not guessing what's optional.
**Actor:** contributor (guest).
**Goal:** the contributor page renders exactly the collection's field setup; required fields are marked, optional ones are clearly optional.

## Preconditions

- Collection with a field setup (F-COL-001, US-025-03); guest on the contributor page.

## Happy path

1. Name required → shown, marked required.
2. Email required → shown, validated (inline), required; optional → shown with "optional" hint, no validation if empty.
3. Submit → entry stored with the given values (or "Anonymous" if name was allowed-empty).

## Alternative flows

- **No email given (optional)**: no confirmation email to the contributor (F-COL-005, FR-026-5 / EC-026-1).
- **Invalid email (required)**: inline validation, submit blocked (EC-026-5).

## Acceptance criteria

```gherkin
Given the collection requires name and email
When I submit with a valid email
Then my entry is stored with both

Given the collection requires only name
When I submit without an email
Then the entry is stored and no confirmation email is attempted
```

## Edge cases

- Email normalization (trim, lowercase, dedup not applicable — one field).
- "Anonymous" fallback only when name was not required and not given.

## UI notes

- Fields card above the drop zone: one field per row, `--fs-small` helper for optional fields.
- Tone: "Your name" / "Your email (optional)" — no exclamation points.

## Technical notes

- Rendering = pure function of the field setup (unit-tested); validation in `FinalizeSubmissionCommand` (server) + client pre-check.

## Links

- Feature: `COL-002-submission.md` (FR-026-1, FR-026-4, AC-026-3)
- Related: F-COL-001 (field setup), F-COL-005 (emails)
