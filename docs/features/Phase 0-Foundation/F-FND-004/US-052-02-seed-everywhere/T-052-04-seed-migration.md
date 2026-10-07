# T-052-04 — SeedPlansAndFeatureFlags migration (3 plans + full flag set)

**Story:** US-052-02 | **Spec:** FR-052-4, AC-052-2 | **Size:** M
**Depends on:** T-052-03 (InitialCreate applied cleanly)

---

## Context to read (only these)

- `US-052-02-seed-everywhere.md` → happy path + first two Gherkin blocks
- `../../F-FND-004-domain-model-schema.md` → FR-052-4 + AC-052-2 + Edge cases (EC-052-3)
- Open-Decisions-and-Constants.md Part 2 (docs) → limits/features JSON values only

## Instructions

1. Add the EF migration **`SeedPlansAndFeatureFlags`** with `HasData`:
   - three `Plan` rows — free/pro/business — with **fixed GUIDs** and `LimitsJson`/`FeaturesJson` exactly per Open-Decisions-and-Constants.md Part 2
   - the full seeded `FeatureFlag` set: 12 `feature.*` gates (default **false** per D-14) + 3 × 14 `limits.<plan>.*` keys (42 limit keys total)
2. Seeds are data, not schema — fixed GUIDs keep FK values stable across environments (test fixtures reuse them — TA-14.3).

## Exit check

- [ ] After migration: exactly 3 Plan rows with Part-2 JSON and the full flag set present (12 + 42 keys)
- [ ] Re-running the migration on an already-seeded DB is a no-op

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; InitialCreate migration in place).
Task T-052-04 — add the seed migration.
Read first (only): docs/features/Phase 0-Foundation/F-FND-004/US-052-02-seed-everywhere/US-052-02-seed-everywhere.md (happy path + first two Gherkin blocks), F-FND-004-domain-model-schema.md (FR-052-4 + AC-052-2 + EC-052-3), and Open-Decisions-and-Constants.md Part 2 for the limits/features JSON values only.
Do exactly:
1. Add an EF migration named SeedPlansAndFeatureFlags with HasData seeding: three Plan rows (free, pro, business) with fixed GUIDs and LimitsJson/FeaturesJson exactly per Part 2; plus the full FeatureFlag set — 12 feature.* gates defaulting false per D-14 and 3 x 14 limits.<plan>.* keys (42 limit keys).
2. Apply it to local Docker SQL and verify: exactly 3 plans, all seeded flag keys present.
Done when: AC-052-2 holds — 3 plans with Part-2 JSON and the complete flag set (12 + 42) exist after migration, with fixed GUIDs so FK values stay stable across environments.
Constraints: seeds ship via HasData in the migration only — no runtime seeding code; Part 2 constants are frozen, changing a value is a decision request (EC-052-3), not an edit.
```
