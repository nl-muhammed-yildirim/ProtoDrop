# US-029-02 — Let the contributor know when their submission is finished

**Feature:** F-COL-005 — Completion & Contributor Notification | **Status:** pending

---

**Story:** As a contributor, I want an email when my submission is marked complete, so that I know my part is over without checking back.
**Actor:** contributor (with an email), collector (trigger).
**Goal:** "Your submission to {collection} is complete." — one email, via the `f-email` pipeline.

## Preconditions

- An entry with a contributor email; the collector marks it Done.

## Happy path

1. Done transition → `collection.entry.status_changed` event.
2. `f-email` consumes it → `collection_done` template, subject "Your submission to {collection} is complete."
3. Suppression + retry + DLQ exactly as F-TRF-006.

## Alternative flows

- **No email on the entry**: no send, no failure, no log-only noise (FR-029-6, AC-029-5).
- **Unsubscribed address**: suppressed, visible in admin (AC-029-4, F-TRF-006-5).

## Acceptance criteria

```gherkin
Given an entry with a contributor email
When the collector marks it Done
Then the contributor gets exactly one "complete" email

Given the same event is replayed
When both are consumed
Then the contributor still gets exactly one email (dedup)
```

## Edge cases

- Bounced address: `email.failed`, Done stands (state/delivery decoupled, EC-029-2).
- Unsubscribe link in the email (F-TRF-006-5 pattern).

## UI notes

- Email: one CTA button ("Open {collection}" — informational; the real action is "done"), calm tone.

## Technical notes

- Template `collection_done` (F-TRF-006 pipeline); dedup by (entry, status) — a replay with the same pair skips (F-TRF-006-8 pattern).
- Events per F-COL-003 (TA-5.3 extension); `email.sent` / `email.failed` as usual.

## Links

- Feature: `COL-005-completion.md` (FR-029-2, FR-029-6, AC-029-1/3/4/5)
- Related: F-TRF-006 (email pipeline), F-COL-003 (transition)
