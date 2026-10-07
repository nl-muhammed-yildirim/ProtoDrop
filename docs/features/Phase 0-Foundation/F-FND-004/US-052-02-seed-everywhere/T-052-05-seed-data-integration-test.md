# T-052-05 — SeedDataIntegrationTest (Testcontainers SQL)

**Story:** US-052-02 | **Spec:** AC-052-2 (third Gherkin block), FR-052-4 | **Size:** M
**Depends on:** T-052-04 (seeds exist), T-051-03 (integration job shape — Testcontainers already the CI pattern)

---

## Context to read (only these)

- `US-052-02-seed-everywhere.md` → third Gherkin block + Technical notes
- `../../F-FND-004-domain-model-schema.md` → AC-052-2 + Test plan

## Instructions

1. Add `SeedDataIntegrationTest` to `wa.api.integration`: spin up a fresh SQL 2022 via Testcontainers (TA-14.3), run both migrations, then assert:
   - exactly 3 Plan rows with fixed GUIDs and Part-2 JSON values
   - all seeded FeatureFlag keys present (12 feature gates + 42 limit keys)
2. The assertions encode AC-052-2 — same values as local Docker SQL (identical fixtures).

## Exit check

- [ ] Test passes against Testcontainers SQL with the identical seed values as local
- [ ] The test is the T-004 exit evidence for seeds (spec Technical notes)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; migrations InitialCreate + SeedPlansAndFeatureFlags in place).
Task T-052-05 — prove the seeds end-to-end in CI shape.
Read first (only): docs/features/Phase 0-Foundation/F-FND-004/US-052-02-seed-everywhere/US-052-02-seed-everywhere.md (third Gherkin block + Technical notes) and F-FND-004-domain-model-schema.md (AC-052-2 + Test plan).
Do exactly:
1. Add SeedDataIntegrationTest to src/wa.api.integration that spins up a fresh SQL 2022 via Testcontainers (TA-14.3), runs both migrations, and asserts: exactly 3 Plan rows with fixed GUIDs and Part-2 LimitsJson/FeaturesJson values, and all seeded FeatureFlag keys present (12 feature gates + 42 limit keys).
2. Run it locally against Docker and confirm the values match local Docker SQL exactly — identical fixtures, no environment-specific data.
Done when: AC-052-2's third Gherkin block passes on a real Testcontainers run — this test is the T-004 exit evidence for seeds.
Constraints: assertions encode the frozen Part-2 constants (EC-052-3) — if a value differs, fix the seed, not the test.
```
