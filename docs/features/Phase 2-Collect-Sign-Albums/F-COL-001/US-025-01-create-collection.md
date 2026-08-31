# US-025-01 — Create a collection for incoming files

**Feature:** F-COL-001 — Create Collection | **Status:** pending

---

**Story:** As a collector, I want to create a collection with a title and description, so that I can ask many people to send me files in one place.
**Actor:** Pro/Business account user (plan-gated, D-21).
**Goal:** a one-screen create flow that stores a `Collection` row and makes the link live.

## Preconditions

- User signed in, on Pro/Business (`PlanContext.Features.collect`).

## Happy path

1. My Files → **New collection** → card with title, description, due date, field setup.
2. **Create** → `POST /collections` (endpoint 25) → `collection.created` event.
3. The collection is visible in My Collections with its link.

## Alternative flows

- **Free tier**: "Collect requires Pro" + plan-screen deep-link (AC-025-4).
- **Guest owner**: not allowed — sign-in required (FR-025-4).

## Acceptance criteria

```gherkin
Given I am on Pro
When I create a collection with a title
Then a Collection row exists
And GET /collect/{linkId} returns 200 with the title and description
```

## Edge cases

- Title ≤ 80, description ≤ 500 (validated inline, FR-025-1).
- line breaks stripped, emojis allowed (EC-025-3).

## UI notes

- Create card: title, description, due date (optional), field toggles, **Create** primary.
- My Collections section above Transfers in My Files (F-COL-001 §5.4 extension).

## Technical notes

- `CreateCollectionCommand` (MediatR, `Collections/` area, TA-4.2a extension).
- `collection.created { collectionId, linkId, ownerId }` (TA-5.3 extension).

## Links

- Feature: `COL-001-create-collection.md` (FR-025-1, FR-025-4, AC-025-1/4)
- Architecture: TA-3.2, TA-4.2, TA-5.3
- Related: F-BIL-001 (plan gate), F-COL-002 (field setup)
