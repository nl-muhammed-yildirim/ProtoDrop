# T-007-06 — Integration test: flag override changes a limit without deploy (FR-007-5)

**Story:** US-007-04 | **Spec:** FR-007-5, EC-007-1, TA-3.4/TA-13.2 | **Size:** M
**Depends on:** T-007-01 (Limits Registry + 30 s cache), T-007-02 (draft enforcement reads the registry)

---

## Context to read (only these)

- `US-007-04-flag-limits.md` → happy path + Alternative flows
- `../../F-TRF-007-limits.md` → FR-007-5 + EC-007-1 + Test plan (integration line)

## Instructions

1. Add an integration test proving the flag-driven limit flow end-to-end against a real database:
   - Seed `limits.free.maxTransferSize` at 5 GB; a 6 GB draft is rejected with `TRANSFER_SIZE_EXCEEDED`.
   - Set the flag to 10 GB (upsert the `FeatureFlag` row — the admin UI/endpoint 23 arrives later with F-TRF-011/T-024).
   - Wait out the 30 s cache TTL (fake clock or real wait per test harness convention); a new 6 GB draft is accepted.
2. Assert in-flight behavior per EC-007-1: an already-finalized transfer keeps its send-time values — only **new** resolutions pick up the change.
3. This is T-020's flag-driven exit check ("flag override mid-test: set flag → new draft uses new value").

## Exit check

- [ ] 5 GB flag: 6 GB draft rejected; after flag edit + TTL, same draft accepted — no deploy in between
- [ ] In-flight transfers keep their send-time values (EC-007-1)
- [ ] The test asserts via the API surface (draft endpoint), not by poking the provider directly

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; Limits Registry + draft enforcement in place).
Task T-007-06 — add the flag-override integration test.
Read first (only): docs/features/Phase 0-MVP/F-TRF-007/US-007-04-flag-limits/US-007-04-flag-limits.md (happy path + Alternative flows) and F-TRF-007-limits.md (FR-007-5).
Do exactly:
1. Add an integration test against a real database proving the flag-driven limit flow end-to-end:
   - Seed limits.free.maxTransferSize at 5 GB; a 6 GB draft is rejected with TRANSFER_SIZE_EXCEEDED;
   - Set the flag to 10 GB (upsert the FeatureFlag row — admin UI/endpoint 23 arrives later with F-TRF-011/T-024);
   - Wait out the 30 s cache TTL (fake clock or real wait per test harness convention); a new 6 GB draft is accepted.
2. Assert in-flight behavior per EC-007-1: an already-finalized transfer keeps its send-time values — only new resolutions pick up the change.
3. Treat this as T-020's flag-driven exit check ("flag override mid-test: set flag → new draft uses new value").
Done when: both Gherkin blocks of US-007-04 hold through the API surface with no deploy in between.
Constraints: assert via the draft endpoint, not by calling ILimitsProvider directly; cache TTL is 30 s (TA-3.4) — do not shorten it to make the test pass.
```
