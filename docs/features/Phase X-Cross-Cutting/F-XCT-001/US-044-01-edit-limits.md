# US-044-01 — Change a limit without a deploy

**Feature:** F-XCT-001 — Feature Flags & Remote Limits | **Status:** pending

---

**Story:** As the operator, I want to raise or lower any plan limit from the admin Flags screen, so that a pricing experiment or an incident (e.g., "free tier eats 100 GB today") doesn't need a release train.
**Actor:** SuperAdmin in the admin dashboard.
**Goal:** Edit a `limits.<plan>.<key>` value → new resolutions use it within 30 s, no redeploy, fully audit-logged.

## Preconditions

- Flags live in `dbo.FeatureFlag` with the seeded keys (TA-13.2, T-065); `FlagsCache` 30 s TTL (TA-3.4, ADR-008).
- `Plan.LimitsJson` seeds exist for free/pro/business (F-BIL-001) — flags only *override* them.

## Happy path

1. SuperAdmin opens Admin → Flags → **Limits** and finds `limits.free.maxTransferSize`.
2. Edits the value (`5368709120` → `10737418240`) and saves.
3. Validation passes (positive integer), the row is upserted, `UpdatedAtUtc`/`UpdatedBy` set.
4. Within 30 s, new `ResolveLimits("free")` calls return 10 GB; in-flight transfers keep their send-time values.
5. An `admin_action` audit row records actor email, key, and the exact old/new values.
6. Reverting is a second edit — same path, same audit trail.

## Alternative flows

- **Invalid value:** "abc" or `-1` → inline `400 FLAG_INVALID`, previous value stays live (EC-044-1).
- **Unknown key:** 404 `FLAG_UNKNOWN` — the key list is seeded and finite (FR-044-5).
- **Replica staleness:** other replicas observe the change within the 30 s TTL window (EC-044-4); the UI caption says so.

## Acceptance criteria

```gherkin
Given the flag limits.free.maxTransferSize is 5368709120
When a SuperAdmin sets it to 10737418240
Then within 30 seconds a new finalize of a 6 GB transfer succeeds on the free plan
And an admin.action audit row records the actor email and both values
And the Flags screen shows the effective value as overridden

Given the admin saves the string "abc" for limits.free.maxTransferSize
When the save is submitted
Then the inline error 400 FLAG_INVALID is shown
And the previous value remains effective
```

## Edge cases

- Two concurrent edits → last write wins; both appear in the audit trail (EC-044-3).
- A limit edit never mutates `Plan` rows — plan catalog integrity is untouched (FR-044-8).
- "Within 30 s" is the contract; the UI must not promise instant propagation.

## UI notes

- Limits group: per-plan table, one row per seeded key, effective value + "(overridden)" marker when a flag is set.
- Save per key (not a global save), so a mistake affects one number.

## Technical notes

- `SetFlagCommand` (endpoint 23, TA-4.2): validate per key type → upsert `FeatureFlag` → `IAdminAudit` (TA-4.2a).
- Read path: `FlagsCache` (30 s TTL) → `ResolveLimits` = plan seed ← flag overrides; no per-read SQL.
- Telemetry: `admin_action` only; flag *reads* emit nothing (TA-10.2 closed list).

## Links

- Feature: `XCT-001-feature-flags.md` (FR-044-1/2/4/5/6)
- Plan AC: AC-044-1, AC-044-3
- Related: US-007-04 (F-TRF-007 view of the same screen), US-011-04 (admin audit trail)
- Architecture: TA-3.4, TA-13.2, TA-4.2a
- Milestone: T-065, T-068
