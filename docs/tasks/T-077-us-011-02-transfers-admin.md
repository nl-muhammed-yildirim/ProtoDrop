# T-077 — Find and act on any transfer

**Story:** US-011-02 | **Feature:** F-TRF-011 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-011/US-011-02-transfers-admin.md`
**Coarse task (Milestone-Backlog.md):** T-024
**Status:** pending

---

## Scope

As the operator, I want to search any transfer by linkId, recipient email, status, size, or date range — and force-delete it — so that support tickets and abuse reports end in an action, not a shrug.

**Actor:** Operator (read) / SuperAdmin (delete).

**Goal:** From "user says link 3F9K2A7X is leaking" to "found + killed" in under a minute.

Happy path:

1. Filter bar: search (exact `linkId`, or recipient email substring), status select, size range, date range (created).
2. Results: table (UI-Reference §4.7) — linkId, owner email, status, files, size, created, downloads.
3. Row actions: **View** (recipient page, new tab), **Force-delete** (confirm modal: "Force-delete transfer {linkId}? 3 files, 1.2 GB." → same semantics as user delete, reason `admin`).
4. Every mutation audit-logged (US-011-04).

## Acceptance criteria

```gherkin
Given I search for linkId "3f9k2a7x"
When the search runs
Then exactly that transfer is returned (exact match, case-insensitive)

Given I search for recipient email "ada@x.com"
When the search runs
Then all transfers that include that address are listed

Given I force-delete a transfer
When the confirm is pressed
Then transfer.deleted is emitted with reason admin
And the audit log has a row with my email as actor
```

## Edge cases

- Force-delete while a recipient downloads: same semantics as user delete (EC-011-2, EC-009-1).
- Cursor pagination on results (TA-4.1.4).

## Exit check

- [ ] Scenario 1: I search for linkId "3f9k2a7x"
- [ ] Scenario 2: I search for recipient email "ada@x.com"
- [ ] Scenario 3: I force-delete a transfer
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-011/US-011-02-transfers-admin.md`
- Feature: `TRF-011-admin.md` (FR-011-3, FR-011-4)
- Architecture: TA-4.2#21
- Related: US-009-04 (same delete command, different reason)
- Milestone: T-024
