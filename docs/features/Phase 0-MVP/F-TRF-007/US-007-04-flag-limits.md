# US-007-04 — Change limits without a deploy

**Feature:** F-TRF-007 — Free-Tier Limits | **Status:** pending

---

**Story:** As the operator, I want to raise or lower any limit from the admin Flags screen, so that pricing experiments and incident response don't need a release.
**Actor:** SuperAdmin in the admin dashboard.
**Goal:** Edit `limits.*` values → new resolutions use them within 30 s, no redeploy, audit-logged.

## Preconditions

- Flags live in `FeatureFlag` (keys `limits.free.*`, TA-13.2); `FlagsCache` 30 s TTL (TA-3.4).

## Happy path

1. SuperAdmin opens Admin → Flags, finds `limits.free.maxTransferSize`.
2. Edits the JSON value (`5368709120` → `10737418240`), saves.
3. Within 30 s, new `ILimitsProvider.Resolve("free")` calls return 10 GB.
4. The edit is audit-logged (`admin.action`, actor email, old/new values in `DetailsJson`).
5. Reverting works the same way (rollback = second edit, also audited).

## Alternative flows

- **Bad JSON:** save rejected inline ("Invalid JSON"), old value stays live (EC-011-4).
- **Mid-flight effect:** in-flight transfers keep their send-time values; only new resolutions pick up the change (EC-007-1, documented).
- **All replicas:** with no Redis (ADR-008), each replica caches up to 30 s — divergence is accepted and documented.

## Acceptance criteria

```gherkin
Given the flag limits.free.maxTransferSize is 5 GB
When a SuperAdmin sets it to 10 GB
Then within 30 seconds a new finalize of 6 GB succeeds
And an admin.action audit row records the change with the actor email

Given the admin saves invalid JSON for a flag
When the save is submitted
Then the error is shown inline and the previous value remains effective
```

## Edge cases

- The 30 s TTL means "without a deploy", not "instant" — the UI says "Changes take effect within 30 seconds."
- Flag keys are seeded by migration (TA-3.7); an unknown key in the UI is a bug, surfaced as 404.

## UI notes

- Flags screen: key/value editor + JSON preview (UI-Reference §5.6); save per key; "Changes are logged." hint.

## Technical notes

- `SetFlagCommand` (endpoint 23): validate JSON → upsert `FeatureFlag` → audit (TA-4.2a).
- Cache invalidation: TTL only (no pub/sub in MVP, ADR-008).

## Links

- Feature: `TRF-007-limits.md` (FR-007-5)
- Related: US-011-04 (the admin UX side)
- Architecture: TA-3.4, TA-13.2
- Milestone: T-005, T-024
