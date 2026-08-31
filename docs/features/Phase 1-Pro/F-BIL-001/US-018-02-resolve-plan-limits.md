# US-018-02 — Always resolve my plan's limits

**Feature:** F-BIL-001 — Plan Catalog | **Status:** pending

---

**Story:** As a user, I want every limit I hit to come from my plan's row — one resolver, one source — so that "what is my limit?" always has one answer.
**Actor:** any user (guests resolve as Free), developer (calls the resolver).
**Goal:** one `ILimitsProvider.Resolve(planCode)` call path for all limits; no second source.

## Preconditions

- `Plan` rows seeded (US-018-01); `LimitsRecord` + `PlansCache` + `FlagsCache` in place (T-005, TA-3.4).

## Happy path

1. A request resolves the user's plan (guest → `free`).
2. `Resolve` returns the plan's `LimitsRecord`: base from `LimitsJson`, individual keys overridable by `FeatureFlag` (`limits.{plan}.*`), 30 s cache TTL.
3. Every enforcement point (F-BIL-003-6) reads from that record.

## Alternative flows

- **Operator raises `limits.pro.maxTransferSize`**: after ≤ 30 s the new value resolves (new resolutions only — EC-007-1 semantics).
- **Unknown plan code**: `UNKNOWN_PLAN` (defensive; closed set).

## Acceptance criteria

```gherkin
Given I am on the pro plan
When a feature asks for maxTransferSize
Then the value equals the pro plan's LimitsJson value
And no code path reads the number from a literal

Given an operator sets flag limits.pro.maxTransferSize
When the 30 s cache expires
Then the new value is returned
```

## Edge cases

- `-1` encodes ∞ (Appendix A / Part 2) — handled in the resolver, not per feature.
- Cache TTL honored: a change mid-test is visible only after expiry (T-005 exit check).

## UI notes

- None directly — users see the effect at limit screens (F-TRF-007 wording).

## Technical notes

- TA-3.4: `PlansCache` + `FlagsCache` 30 s TTL; flag override wins over row value.
- The resolver is the only consumer of `Plan.LimitsJson` (code review rule, F-BIL-003-1).

## Links

- Feature: `BIL-001-plan-catalog.md` (FR-018-5, AC-018-2)
- Architecture: TA-3.4, TA-13.2
- Related: F-TRF-007 (enforcement points), F-BIL-003 (middleware)
