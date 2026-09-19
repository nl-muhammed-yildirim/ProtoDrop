# T-062 — Change limits without a deploy

**Story:** US-007-04 | **Feature:** F-TRF-007 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-007/US-007-04-flag-limits.md`
**Coarse task (Milestone-Backlog.md):** T-005, T-024
**Status:** pending

---

## Scope

As the operator, I want to raise or lower any limit from the admin Flags screen, so that pricing experiments and incident response don't need a release.

**Actor:** SuperAdmin in the admin dashboard.

**Goal:** Edit `limits.*` values → new resolutions use them within 30 s, no redeploy, audit-logged.

Happy path:

1. SuperAdmin opens Admin → Flags, finds `limits.free.maxTransferSize`.
2. Edits the JSON value (`5368709120` → `10737418240`), saves.
3. Within 30 s, new `ILimitsProvider.Resolve("free")` calls return 10 GB.
4. The edit is audit-logged (`admin.action`, actor email, old/new values in `DetailsJson`).
5. Reverting works the same way (rollback = second edit, also audited).

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

## Exit check

- [ ] Scenario 1: the flag limits.free.maxTransferSize is 5 GB
- [ ] Scenario 2: the admin saves invalid JSON for a flag
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-007/US-007-04-flag-limits.md`
- Feature: `TRF-007-limits.md` (FR-007-5)
- Related: US-011-04 (the admin UX side)
- Architecture: TA-3.4, TA-13.2
- Milestone: T-005, T-024
