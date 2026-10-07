# T-054-01 — EventEnvelope (TA-5.2 exact) + IEventPublisher port

**Story:** US-054-01 | **Spec:** FR-054-1/2, AC-054-2 | **Size:** M
**Depends on:** T-049-06 (CQRS convention established — use cases exist to call the publisher)

---

## Context to read (only these)

- `../../F-FND-006-event-backbone.md` → FR-054-1/2 + AC-054-2
- `US-054-01-publish-once.md` → happy path + second Gherkin block + Technical notes (first bullet)

## Instructions

1. Add the **EventEnvelope** type with exactly these fields — no more, no less: `eventId` (GUID), `eventType` (closed list from TA-5.3), `version` (1), `occurredAt` (UTC), `correlationId`, `partitionKey`, `payload`.
2. Add the **`IEventPublisher.Publish(envelope)`** port to `wa.application`.
3. Domain events stay **MediatR-free POCOs** (TA-0.2 rule 9): use cases call `IEventPublisher.Publish(envelope)`, never `IPublisher.Publish(DomainEvent)` — `wa.application` must not depend on MediatR's eventing.

## Exit check

- [ ] The envelope has exactly the seven TA-5.2 fields (serialized form inspected — AC-054-2)
- [ ] No MediatR eventing dependency in wa.application (grep: no `IPublisher` usage for domain events)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CQRS use cases exist per T-049-06).
Task T-054-01 — define the event contract.
Read first (only): docs/features/Phase 0-Foundation/F-FND-006/F-FND-006-event-backbone.md (FR-054-1/2 + AC-054-2) and US-054-01-publish-once.md (happy path + second Gherkin block).
Do exactly:
1. Add the EventEnvelope type with exactly these fields — no more, no less: eventId (GUID), eventType (closed list from TA-5.3), version (1), occurredAt (UTC), correlationId, partitionKey, payload.
2. Add the IEventPublisher port with Publish(envelope) to src/wa.application.
3. Keep domain events MediatR-free POCOs (TA-0.2 rule 9): use cases call IEventPublisher.Publish(envelope), never IPublisher.Publish(DomainEvent); wa.application must not depend on MediatR's eventing.
Done when: the envelope serializes to exactly the seven TA-5.2 fields and no MediatR eventing appears in wa.application (AC-054-2).
Constraints: closed eventType list from TA-5.3 only — no ad-hoc event types in MVP; version is 1 for everything at M0.
```
