# US-030-02 — Choose who signs in what order

**Feature:** F-SGN-001 — Send Document | **Status:** pending

---

**Story:** As a sender, I want to set the signing order (1, 2, 3…), so that the person who needs to sign first is first — and later signers wait for their turn.
**Actor:** sender, signers.
**Goal:** an ordered signer list; signer *n* gets their email only when signer *n−1* has signed (sequential chain, F-SGN-002).

## Preconditions

- Document create screen (US-030-01) with ≥ 1 signer.

## Happy path

1. Signer rows are reorderable (↑↓) — the order is the contract.
2. After send: signer 1 is active; signers 2..n are "waiting" (their links exist but show "not your turn yet" — F-SGN-002-4).
3. Each signature advances the chain; the last signature completes the document (F-SGN-004).

## Alternative flows

- **Parallel signing**: not in MVP (D-22 decision; the spec freezes sequential).
- **One signer**: trivially immediate.

## Acceptance criteria

```gherkin
Given I ordered signers A then B
When I send the document
Then A is emailed now and B is not
And B's link says "not your turn yet" until A signs
```

## Edge cases

- Reordering after send: not allowed in MVP (the sent order is the order; documented — edit = void + re-send).
- A declines: the chain stops at A (B never gets their turn) — the sender re-sends or voids (F-SGN-002-6).

## UI notes

- Signer rows: order number (1, 2, 3), email, ↑↓ / delete; helper "They'll sign in this order."
- Tracking view: order numbers + status chips (pending / signed / declined).

## Technical notes

- `DocumentRecipient.Order` (1-based, unique per document — DB constraint); chain advance on `document.signed` (F-SGN-002).
- Sequential rule is a pure function over the recipient list (unit-tested).

## Links

- Feature: `SGN-001-send-document.md` (FR-030-1, FR-030-5, AC-030-1)
- Related: F-SGN-002 (turn state), F-SGN-004 (completion)
