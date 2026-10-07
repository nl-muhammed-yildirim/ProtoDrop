# T-053-01 — LimitsRecord POCO in wa.domain (BCL-only)

**Story:** US-053-01 | **Spec:** FR-053-1, AC-052-3 (stays true) | **Size:** S (one file)
**Depends on:** T-052-01 (wa.domain POCO conventions established)

---

## Context to read (only these)

- `../../F-FND-005-limits-registry.md` → FR-053-1 + Description (first paragraph only)
- `US-053-01-resolve-limits.md` → Technical notes (first bullet)
- Feature Plan Appendix A (docs) → the limit constants list

## Instructions

1. Add `LimitsRecord` to `wa.domain` as a **BCL-only POCO** holding every constant from Feature Plan Appendix A.
2. JSON property names must be **camelCase and match the seeded `LimitsJson`** exactly (the deserialization contract with F-FND-004 seeds).

## Exit check

- [ ] `LimitsRecord` compiles in wa.domain with zero external references
- [ ] Its camelCase JSON property names are 1:1 with the seeded `LimitsJson` keys (free/pro/business rows from T-052-04)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.domain is BCL-only per T-052-01).
Task T-053-01 — add the LimitsRecord POCO.
Read first (only): docs/features/Phase 0-Foundation/F-FND-005/F-FND-005-limits-registry.md (FR-053-1 + Description first paragraph), US-053-01-resolve-limits.md (Technical notes first bullet), and Feature Plan Appendix A for the limit constants list.
Do exactly:
1. Add LimitsRecord to src/wa.domain as a BCL-only POCO holding every constant from Feature Plan Appendix A — no behavior, no external references.
2. Give it camelCase JSON property names that match the seeded LimitsJson keys exactly (the seeds came from T-052-04; verify against one real seeded row).
Done when: LimitsRecord compiles in wa.domain and its JSON shape is 1:1 with the seeded LimitsJson (AC-053-1's deserialization contract).
Constraints: BCL only — no System.Text.Json attributes from packages beyond what the BCL provides; no behavior yet (resolution logic lands in T-053-04/T-053-05).
```
