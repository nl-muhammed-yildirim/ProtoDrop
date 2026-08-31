# US-018-01 — Plans are data, not code

**Feature:** F-BIL-001 — Plan Catalog | **Status:** pending

---

**Story:** As a product owner, I want plans (Free / Pro / Business) to live in a `Plan` table as rows — not in code branches — so that adding or retuning a plan never requires an engineering release.
**Actor:** operator (seeds/launches plans), developer (writes plan-agnostic code).
**Goal:** make "plans are data" true in the schema, the seed migration, and the application layer.

## Preconditions

- `Plan` DDL per TA-3.2 (Id, Code, Name, LimitsJson, FeaturesJson, SortOrder) exists.
- Free/Pro/Business codes are a seeded, closed set (D-05 default-accepted).

## Happy path

1. The seed migration creates exactly three `Plan` rows: `free`, `pro`, `business`, with `LimitsJson` matching Part 2 constants and `FeaturesJson` per Appendix A.
2. A new `AppUser` gets `PlanId = free` by default (FK, TA-3.2).
3. Code reads plan values only through `ILimitsProvider` (TA-3.4) — no `if (plan == "pro")` anywhere.

## Alternative flows

- **Launch a new plan** (e.g. "Pro+"): insert a row with validated JSON; the plan screen (UI-Reference §5.7) renders it with zero UI changes.
- **Retune a value**: operator edits the flag `limits.pro.*` (F-BIL-001-5) — still data, no deploy.

## Acceptance criteria

```gherkin
Given a fresh database after migrations
When I query the Plan table
Then it contains exactly free, pro, business
And a new AppUser row has PlanId = free

Given I add a new Plan row with valid JSON
When the plan screen loads
Then the new plan renders from data with no UI change
```

## Edge cases

- Invalid JSON in a row → schema-validated before commit (EC-018-2).
- Unknown plan code → `UNKNOWN_PLAN` Problem+JSON (EC-018-3).
- Deleting a plan with users → blocked (EC-018-4).

## UI notes

- Plan screen cards generated from data: name, price (monthly/annual toggle), feature bullets from `FeaturesJson`, limit summary line.

## Technical notes

- Seed in the T-004 migration; `LimitsRecord` deserialization (TA-3.4) validates the JSON shape.
- Pro/Business placeholders pending D-07 are marked in admin (EC-018-1) — code never branches on TBD.

## Links

- Feature: `BIL-001-plan-catalog.md` (FR-018-1, FR-018-2, AC-018-1)
- Architecture: TA-3.2, TA-3.4
- Decisions: D-05, D-07, D-19
