# T-059 — Use the free tier within its limits

**Story:** US-007-01 | **Feature:** F-TRF-007 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-007/US-007-01-free-limits.md`
**Coarse task (Milestone-Backlog.md):** T-020
**Status:** pending

---

## Scope

As a free user, I want the free tier to work exactly as advertised (5 GB per transfer, 100 downloads, 7 days), so that "free" means something I can plan around.

**Actor:** Any account user or guest on the Free plan.

**Goal:** Hit no limit accidentally — and when I do, know exactly which one.

Happy path:

1. Guest finalizes a 4.8 GB transfer → accepted.
2. Sends to 20 recipients → accepted (cap is inclusive).
3. Recipients download 100 times → the 101st hits `DownloadLimit` (F-TRF-005, US-005-03).
4. The transfer expires 7 days after send.
5. Every one of these numbers comes from the Limits Registry, not from code.

## Acceptance criteria

```gherkin
Given a free user with no active transfers
When they finalize and send a 4.9 GB transfer
Then it is accepted
And it expires 7 days after send

Given a free account with 20 active transfers
When they try to send a 21st
Then the send is rejected with "You have 20 active transfers (limit 20)"
```

## Edge cases

- Inclusive caps: exactly at the limit = allowed; over = rejected.
- Guests: per-transfer limits only; account limits need an owner (EC-007-3, feature file).
- `-1` encodes ∞ (Business) — the UI hides "left" counters when ∞.

## Exit check

- [ ] Scenario 1: a free user with no active transfers
- [ ] Scenario 2: a free account with 20 active transfers
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-007/US-007-01-free-limits.md`
- Feature: `TRF-007-limits.md` (FR-007-1, FR-007-3)
- Related: US-001-03 (client pre-check), US-005-03 (download cap)
- Architecture: TA-3.4
- Milestone: T-020
