# US-053-02 — Change a limit and have it apply within 30 seconds

**Feature:** F-FND-005 — Limits Registry | **Status:** done (T-005, 2026-09-01)

---

**Story:** As an operator fixing a limit at 3 a.m. (e.g. "max transfer size is wrong"), I want to edit one flag and have every request see the new value within one TTL window, so that a limit fix never needs a deploy.
**Actor:** SuperAdmin (edits via F-XCT-001's admin Flags screen once it ships), ops (verifies).
**Goal:** A `limits.<plan>.<key>` flag override wins over the seeded value — for exactly one field, on exactly one plan.

## Preconditions

- Limits Registry in place (T-005); plans and flags seeded (F-FND-004 / US-052-02).
- A `FeatureFlag` row exists or can be edited: `limits.<plan>.<key>`.

## Happy path

1. SuperAdmin sets a flag, e.g. `limits.free.maxTransferSize` = 10 GB.
2. Within one TTL window (30 s — `Wa:Flags:TtlSeconds`, TA-3.4), the next resolution for that plan returns the overridden value.
3. Every other field of free, and both other plans, are unchanged — the override touches only its one field (AC-053-2).

## Alternative flows

- **Rollback:** delete or reset the flag row; after TTL expiry the seeded `LimitsJson` value is back — no code change either way.
- **Mid-request edit:** in-flight resolutions keep their values; only new resolutions after TTL expiry see the change (EC-053-2, matches F-XCT-001 EC-044-2).

## Acceptance criteria

```gherkin
Given limits.free.maxTransferSize is seeded at the Free value
When a FeatureFlag row sets it to 10 GB
Then within one TTL window Resolve("free").MaxTransferSize is the 10 GB value
And every other field of free and both other plans are unchanged

Given a cached entry younger than 30 s
When a second read arrives before expiry
Then no refetch happens (same instance/row)
And after 30 s the next read refetches
```

## Edge cases

- `feature.*` flags or unknown key segments in the flags table are ignored by limit resolution — only `limits.<plan>.<key>` keys apply (EC-053-1).
- DB down on cache miss → typed failure, no silent fallback to hardcoded defaults (EC-053-3).

## UI notes

- None at M0 — the admin Flags screen that edits these keys is F-XCT-001 / T-065. At M0 the override is exercised via direct flag rows in tests.

## Technical notes

- `FlagsCache` holds flag rows with a 30 s TTL and an injectable clock (`Func<DateTimeOffset>`) so TTL behavior is testable without waiting (FR-053-4).
- Unit tests use faked `IPlansStore`/`IFlagsStore` + fake clock — no Testcontainers needed for resolution logic (9/9 in T-005).

## Links

- Feature: `F-FND-005-limits-registry.md` (FR-053-2, FR-053-4, AC-053-2/3)
- Architecture: TA-3.4
- Milestone: T-005
