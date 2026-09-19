# T-069 — Review my transfers

**Story:** US-009-01 | **Feature:** F-TRF-009 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-009/US-009-01-history-list.md`
**Coarse task (Milestone-Backlog.md):** T-022
**Status:** pending

---

## Scope

As a signed-in user, I want a list of all my transfers with their key facts, so that "what did I send, and is it still alive?" is answered in one look.

**Actor:** Signed-in user on `/files`.

**Goal:** A scannable, paginated list with the numbers that matter.

Happy path:

1. The list shows the user's transfers, most recent first.
2. Each row: name (first file or "Transfer"), file count, total size, recipient count, status chip, expiry countdown, download count ("87/100").
3. Page 1 = 25 rows (cursor pagination, TA-4.1.4); "Next / Prev" navigation.
4. **New transfer** primary button → `/`.

## Acceptance criteria

```gherkin
Given I have 30 transfers
When I open My Files
Then the first page shows 25 rows
And the oldest visible row is my 25th most recent transfer
And a Next control is available

Given I have a transfer expiring in 2 days
When the list renders
Then its row shows a live countdown ("expires in 2 days")

Given I have zero transfers
When I open My Files
Then the empty state shows with a CTA to the upload surface
```

## Edge cases

- Countdowns tick from `ExpiresAtUtc` client-side (recomputed on render; no per-second server calls).
- `DownloadLimit` rows: chip "Download limit reached" (amber) + countdown replaced by the cap note.
- Deleted rows are gone from the list (hard for the user; the jobs finish physically).

## Exit check

- [ ] Scenario 1: I have 30 transfers
- [ ] Scenario 2: I have a transfer expiring in 2 days
- [ ] Scenario 3: I have zero transfers
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-009/US-009-01-history-list.md`
- Feature: `TRF-009-my-files.md` (FR-009-1, FR-009-2, FR-009-5)
- Plan AC: AC-009-1
- Architecture: TA-4.2#14, TA-3.3
- Milestone: T-022
