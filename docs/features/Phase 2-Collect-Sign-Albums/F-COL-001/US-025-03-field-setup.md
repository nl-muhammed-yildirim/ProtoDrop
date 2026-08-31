# US-025-03 — Decide what each sender must provide

**Feature:** F-COL-001 — Create Collection | **Status:** pending

---

**Story:** As a collector, I want to decide whether each sender must provide a name, an email, and files, so that I get exactly the information I need and no more friction than that.
**Actor:** collector.
**Goal:** three toggles (name required / email required / files required) with sensible defaults (name required, email optional, files required).

## Preconditions

- Create-collection screen open (US-025-01).

## Happy path

1. Toggles: name (on), email (off), files (on) — defaults.
2. Stored on the `Collection` row (field-setup JSON, FR-025-1).
3. The contributor page (F-COL-002) renders exactly the required fields.

## Alternative flows

- **Name off**: contributors submit as "Anonymous" (dashboard shows the name or Anonymous).
- **Files off**: a metadata-only submission (name/email only) — rare, allowed.

## Acceptance criteria

```gherkin
Given I created a collection with email required
When a contributor opens the link
Then the email field is shown and marked required

Given I created a collection with email optional
When a contributor submits without an email
Then the submission succeeds
```

## Edge cases

- Defaults apply when a collector creates without touching the toggles (documented).
- Changing field setup after creation: MVP applies to **new** submissions only (existing entries keep their data) — documented.

## UI notes

- Three labeled toggles in the create card; helper "Contributors will fill in only what you require."

## Technical notes

- Field setup stored on `Collection` (JSON column or three booleans — D-21; proposed: `RequireName BIT, RequireEmail BIT, RequireFiles BIT` — simpler than JSON, migration note).
- Rendering logic is a pure function (unit-tested, F-COL-002-2).

## Links

- Feature: `COL-001-create-collection.md` (FR-025-1, AC-025-2)
- Related: F-COL-002 (contributor rendering)
