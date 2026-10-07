# US-055-03 — Trust that every blob has exactly one canonical path

**Feature:** F-FND-007 — Blob Foundation (IBlobStore + SAS minting) | **Status:** done (T-007, 2026-09-03)

---

**Story:** As a developer adding the next blob consumer (finalize copy at T-010, zip writer at T-017), I want every path to come from the one helper that implements TA-3.5, so that no two features ever write the same file to different locations — or the same location with different names.
**Actor:** Developer (human or AI session), code review.
**Goal:** `/staging/{draftId}/f/{fileId}`, `transfers/{transferId}/files/{fileId}`, and `transfers/{transferId}/all.zip` are produced by one path helper — the table in TA-3.5 is enforced, not remembered.

## Preconditions

- Blob foundation in place (T-007).
- A use case that needs to locate a blob (staging read at finalize, zip write later).

## Happy path

1. Code asks the staging path helper for a file's location: `/staging/{draftId}/f/{fileId}`.
2. Later features ask for their rows in TA-3.5 the same way — no string interpolation of blob paths outside the helper.
3. Lifecycle rules (staging 24 h, transfers 30 d) apply to those exact prefixes — the safety net works because the paths are canonical (FR-055-4).

## Alternative flows

- **A feature "needs" a new path shape** → add a row to TA-3.5 + ADR note (TA-17), then extend the helper; never inline a new prefix in code (EC-052-2 pattern from F-FND-004).
- **Abandoned drafts:** staging blobs with no matching transfer are swept by the 24 h lifecycle rule — canonical paths make that sweep possible.

## Acceptance criteria

```gherkin
Given any staged file
When I ask the path helper for its blob location
Then it returns /staging/{draftId}/f/{fileId} — nothing else at M0

Given the two containers exist (staging, transfers)
When lifecycle rules are inspected
Then staging/* deletes at 24 h and transfers/* at 30 d — matching the canonical prefixes exactly
```

## Edge cases

- `transfers` container settings: versioning OFF, GRS in prod, soft-delete 14 d ON (TA-3.5) — set once at M0, not per feature.
- A path built outside the helper is a code-review blocker until T-017 adds zip paths to the same table.

## UI notes

- None.

## Technical notes

- The round-trip test (US-055-02) asserts the full staging path — this story's AC is what it encodes.
- Server-side blob copy (staging → transfers) at T-010 reads and writes through these same paths.

## Links

- Feature: `F-FND-007-blob-foundation.md` (FR-055-3, FR-055-4, AC-055-3)
- Architecture: TA-3.5
- Milestone: T-007
