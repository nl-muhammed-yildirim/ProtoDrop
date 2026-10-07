# T-055-01 — Canonical blob path helper (TA-3.5 table)

**Story:** US-055-03 | **Spec:** FR-055-3, AC-055-3 (first block) | **Size:** S (one pure helper)
**Depends on:** T-049-02 (wa.application project exists)

---

## Context to read (only these)

- `US-055-03-canonical-paths.md` → happy path + Gherkin blocks + Edge cases
- `../../F-FND-007-blob-foundation.md` → FR-055-3/4 + AC-055-3

## Instructions

1. Add the blob path helper to `wa.application` producing exactly:
   - `/staging/{draftId}/f/{fileId}` (staging — the only one at M0)
   - `transfers/{transferId}/files/{fileId}` and `transfers/{transferId}/all.zip` (same table, used by T-010/T-017)
2. No feature invents its own blob paths — string interpolation of blob paths outside the helper is a code-review blocker.

## Exit check

- [ ] The helper returns `/staging/{draftId}/f/{fileId}` for any staged file — nothing else at M0 (AC-055-3)
- [ ] No blob path strings exist anywhere outside this helper (grep audit)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.application exists).
Task T-055-01 — add the canonical blob path helper.
Read first (only): docs/features/Phase 0-Foundation/F-FND-007/US-055-03-canonical-paths/US-055-03-canonical-paths.md (happy path + Gherkin blocks + Edge cases) and F-FND-007-blob-foundation.md (FR-055-3/4).
Do exactly:
1. Add the blob path helper to src/wa.application producing exactly: /staging/{draftId}/f/{fileId} (staging — the only one exercised at M0), transfers/{transferId}/files/{fileId}, and transfers/{transferId}/all.zip (same TA-3.5 table, used later by T-010/T-017).
2. Keep it pure — no Azure SDK, no configuration; just the path strings from the TA-3.5 table.
Done when: AC-055-3 holds — any staged file's blob location comes back as /staging/{draftId}/f/{fileId} and no blob path strings exist outside this helper (grep audit).
Constraints: a new path shape is an architecture change (TA-3.5 row + ADR note in TA-17) — do not add speculative rows at M0.
```
