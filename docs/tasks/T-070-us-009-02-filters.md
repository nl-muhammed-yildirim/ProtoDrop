# T-070 — Filter my transfers

**Story:** US-009-02 | **Feature:** F-TRF-009 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-009/US-009-02-filters.md`
**Coarse task (Milestone-Backlog.md):** T-022
**Status:** pending

---

## Scope

As a user with many transfers, I want to see only the ones that matter right now (active) or only what already expired, so that the list stays useful as it grows.

**Actor:** Signed-in user on `/files`.

**Goal:** Three views of the same list: All / Active / Expired.

Happy path:

1. Filter pills: **All / Active / Expired** (UI-Reference §5.4, `--accent-soft` active pill).
2. **Active** = status 1 (+ 3: download-limit, still serving downloads — no, it's *not* serving; see mapping below) — canonical mapping: Active = `Status=1`; Expired = `Status IN (2,3)` (expired + download-limit); All = everything not physically gone.
3. Selecting a pill refetches with `?status=`; the cursor resets.
4. Default: All.

## Acceptance criteria

```gherkin
Given I have 5 active and 4 expired transfers
When I select the Active filter
Then only the 5 active transfers are listed

When I select the Expired filter
Then the 4 expired (or download-limit) transfers are listed

When I select All
Then all 9 are listed
```

## Edge cases

- Filter + cursor paging compose: `?status=active&cursor=…` (TA-4.1.4).
- A transfer that expires *while the list is open* stays in Active until the next render (staleness acceptable, no live refetch loop).

## Exit check

- [ ] Scenario 1: I have 5 active and 4 expired transfers
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-009/US-009-02-filters.md`
- Feature: `TRF-009-my-files.md` (FR-009-4)
- Architecture: TA-4.2#14, TA-4.1.4
- Milestone: T-022
