# T-049 — Get the cached zip instantly on later requests

**Story:** US-004-02 | **Feature:** F-TRF-004 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-004/US-004-02-cached-zip.md`
**Coarse task (Milestone-Backlog.md):** T-017
**Status:** pending

---

## Scope

As a recipient (or a second recipient) of the same transfer, I want the zip to be ready the moment I click, because someone already paid the generation time.

**Actor:** Any recipient of a transfer whose zip has already been generated.

**Goal:** Instant "Download all" after the first generation, forever (within the transfer's life).

Happy path:

1. Recipient presses **Download all**.
2. The API finds `transfers/{id}/all.zip` and returns its SAS URL immediately (no 202).
3. The download starts within one round-trip.

## Acceptance criteria

```gherkin
Given the zip for a transfer already exists
When I press "Download all"
Then I receive the URL immediately (no preparing state)

Given two recipients press "Download all" simultaneously on a fresh transfer
When both requests race
Then exactly one all.zip blob exists afterwards
And both recipients end up with a downloadable zip
```

## Edge cases

- Caching is per `transferId` — a re-send (F-TRF-010) creates a *new* transferId and a new zip path (the old zip dies with the old transfer's grace, F-TRF-005).
- The cached zip is immutable: transfers are immutable in MVP, so no invalidation logic exists.

## Exit check

- [ ] Scenario 1: the zip for a transfer already exists
- [ ] Scenario 2: two recipients press "Download all" simultaneously on a fresh transfer
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-004/US-004-02-cached-zip.md`
- Feature: `TRF-004-download-zip.md` (FR-004-3)
- Plan AC: AC-004-1 (second recipient clause)
- Architecture: TA-6.6, TA-3.5
- Milestone: T-017
