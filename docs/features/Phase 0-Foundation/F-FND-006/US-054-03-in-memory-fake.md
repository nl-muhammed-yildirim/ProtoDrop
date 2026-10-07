# US-054-03 — Test events end-to-end without a Service Bus connection

**Feature:** F-FND-006 — Event Backbone (outbox → Service Bus) | **Status:** done (T-006, 2026-09-02)

---

**Story:** As a developer running unit or integration tests on a machine without Azure access, I want the in-memory publisher fake to stand in for Service Bus behind the same contract, so that event logic is testable locally and in CI without a broker.
**Actor:** Developer (human or AI session), CI pipeline.
**Goal:** No SB connection string → `IEventPublisher` resolves to the in-memory fake; publish → consume round-trips work against it.

## Preconditions

- Event backbone in place (T-006).
- Local profile with no Service Bus connection string set (F-FND-002 §5.2 rule: SB is optional locally).

## Happy path

1. Test publishes an event through `IEventPublisher`.
2. The in-memory fake records the envelope (same TA-5.2 shape, same dedup-by-eventId behavior).
3. A test consumer reads it back from the fake — publish → consume round-trip passes without any broker.

## Alternative flows

- **CI:** integration tests use the fake or Testcontainers per TA-14.3 — either way no live Azure SB is required at M0.
- **Real broker later:** T-019+ points the same contract at the SB adapter; nothing in test code changes (FR-054-5).

## Acceptance criteria

```gherkin
Given no Service Bus connection string is set
When I publish through IEventPublisher in a unit or integration test
Then the in-memory fake records the event and tests pass without SB

Given an event published to the fake
When a consumer reads from it
Then the envelope arrives with its exact TA-5.2 fields (round-trip)
```

## Edge cases

- The fake is a **test double, not a second implementation**: production code never branches on "is this the fake?" — DI picks the adapter (TA-0.2 rule 7).
- No new packages: serialization uses existing `System.Text.Json` (T-006 note).

## UI notes

- None.

## Technical notes

- T-006 exit evidence: unit tests 7/7 — publish → store → retrieve by topic, TTL expiry, no cross-topic contamination; full §4 gate green (domain 11/11, application 9/9, api.integration 4/4).
- The fake keeps the F-FND-002 promise: unset SB connection string = in-memory publisher, not a broken pipeline.

## Links

- Feature: `F-FND-006-event-backbone.md` (FR-054-5, AC-054-4)
- Architecture: TA-5.1, TA-5.2, TA-0.2(7)
- Milestone: T-006
