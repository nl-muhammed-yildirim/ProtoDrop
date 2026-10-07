# US-052-02 — Trust that seeds are identical in every environment

**Feature:** F-FND-004 — Domain Model & Database Schema | **Status:** done (T-004 seeds, 2026-09-01)

---

**Story:** As a developer testing plan limits or feature gates, I want the Plan and FeatureFlag rows to be seeded identically in local, CI, dev, staging, and prod, so that "it works here" means "it works there".
**Actor:** Developer (human or AI session), ops (verifies seeds after deploy).
**Goal:** One seed migration (`SeedPlansAndFeatureFlags`) is the single source of plan/flag state at environment birth.

## Preconditions

- Migrations applied to a fresh database (US-052-01).

## Happy path

1. The seed migration inserts three `Plan` rows (free/pro/business) with fixed GUIDs and Part-2 limits JSON.
2. It inserts the full seeded `FeatureFlag` set: 12 `feature.*` gates (default false per D-14) + 3 × 14 `limits.<plan>.*` keys.
3. Any query for a plan's limits resolves from these rows — no environment-specific data.

## Alternative flows

- **Limits re-tuned later:** the change goes through the flag override (F-FND-005 / F-XCT-001) or a confirmed Part-2 decision + new seed migration — never an ad-hoc UPDATE in prod.
- **CI:** Testcontainers runs the same migrations, so integration tests see the identical seeds (TA-14.3).

## Acceptance criteria

```gherkin
Given a migrated database in any environment
When I query Plan
Then exactly 3 rows exist (free, pro, business) with fixed GUIDs and Part-2 LimitsJson/FeaturesJson

Given a migrated database
When I query FeatureFlag
Then all seeded keys are present: the 12 feature gates (false per D-14) and the limits.<plan>.* keys for each plan

Given an integration test running against Testcontainers SQL
When it asserts the seed rows
Then it passes with the same values as local Docker SQL (identical fixtures)
```

## Edge cases

- Part 2 constants are **frozen** (Open-Decisions-and-Constants.md): changing a seeded value is a decision request, not a code edit (EC-052-3).
- `FeatureFlag` rows are data, not schema — they can be edited at runtime via the admin Flags screen (F-XCT-001); seeds only define the initial state.

## UI notes

- None (seeds are invisible until the admin Flags screen renders them — F-TRF-011 / F-XCT-001).

## Technical notes

- Seeds ship via `HasData` in the migration with fixed GUIDs so FK values stay stable across environments.
- Integration test `SeedDataIntegrationTest` (T-004) asserts the seeds end-to-end — this story's ACs are what it encodes.

## Links

- Feature: `F-FND-004-domain-model-schema.md` (FR-052-4, AC-052-2)
- Architecture: TA-3.4, TA-13.2, TA-14.3
- Milestone: T-004 (seeds)
