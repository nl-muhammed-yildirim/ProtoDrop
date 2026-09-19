# T-079 — Edit feature flags and see the audit trail

**Story:** US-011-04 | **Feature:** F-TRF-011 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-011/US-011-04-flags-audit.md`
**Coarse task (Milestone-Backlog.md):** T-024
**Status:** pending

---

## Scope

As the operator, I want to edit feature flags and limits live, and to be able to prove who changed what and when, so that "it worked yesterday" has an answer.

**Actor:** SuperAdmin (edit), Operator (read the trail).

**Goal:** Flags as data: editable, validated, audited.

Happy path:

1. Flags screen: list of keys with current JSON values + `--fs-small` preview.
2. SuperAdmin edits a value (e.g. `limits.free.maxTransferSize`) → **Save** → validated (JSON shape) → upserted.
3. Within 30 s (cache TTL, TA-3.4) new resolutions use the value (US-007-04).
4. Every save writes `AuditLog` + emits `admin.action` (`{actor, action:"flag.set", entityType:"flag", entityId:key, details:{old,new}}`).
5. Audit view: a per-entity trail (who, when, old→new) — MVP shows the last 50 actions in a small "Recent actions" panel (full CSV export is P1).

## Acceptance criteria

```gherkin
Given I edit limits.free.maxTransferSize from 5 GB to 10 GB
When I save
Then the new value is effective within 30 seconds
And an audit row exists with my email, the key, and both values

Given I save invalid JSON
When validation runs
Then the inline error shows and the previous value remains live
And no audit row is created
```

## Edge cases

- Unknown key → 404 (the seeded set is the contract, TA-13.2).
- The Suppressions screen (FR-011-2) is the same pattern: list + **Remove** (each removal audited, `admin.action`).

## Exit check

- [ ] Scenario 1: I edit limits.free.maxTransferSize from 5 GB to 10 GB
- [ ] Scenario 2: I save invalid JSON
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-011/US-011-04-flags-audit.md`
- Feature: `TRF-011-admin.md` (FR-011-2, FR-011-5)
- Plan AC: AC-011-2, AC-011-3
- Related: US-007-04 (same change from the limits feature's side)
- Architecture: TA-4.2#23, TA-3.2 `AuditLog`
- Milestone: T-024
