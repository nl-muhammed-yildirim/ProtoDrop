# US-029-03 — Tell a contributor their submission was declined

**Feature:** F-COL-005 — Completion & Contributor Notification | **Status:** pending

---

**Story:** As a contributor, I want a clear "declined" email with a working resubmit link, so that I can fix and re-send my files without starting from zero.
**Actor:** contributor (with email), collector (triggers the decline).
**Goal:** "Your submission to {collection} was declined — you can resubmit." + a **Resubmit** link that opens a fresh submission.

## Preconditions

- An entry the collector marks Declined.

## Happy path

1. Declined transition → event → `collection_declined` template.
2. Email has one CTA: **Resubmit** → `{origin}/collect/{linkId}?resubmit`.
3. The link opens the contributor page pre-titled "Resubmit to {collection}" — a **new** submission (not an edit).

## Alternative flows

- **Accepted transition**: same pipeline, `collection_accepted` template, no CTA (informational only).
- **Decline then Accept**: the Accept email supersedes (no "you were previously declined" copy — the latest state is the truth, documented).

## Acceptance criteria

```gherkin
Given I marked an entry Declined
When the contributor opens the email
Then the Resubmit link opens a fresh submission page for the same collection
```

## Edge cases

- Resubmit creates a new entry (F-COL-002-3, EC-029-1) — the declined entry stays as history.
- Re-accept after a decline: the "accepted" email is sent (transition-into-only, no spam on re-accept of an already-accepted).
- Collection closed after decline: resubmit link shows the closed state (US-028-03) — still correct.

## UI notes

- Email: one CTA button, calm: "…was declined — you can resubmit."
- Resubmit page: normal contributor page + a one-line pre-title, no data pre-fill.

## Technical notes

- `collection_declined` / `collection_accepted` templates; transition-into-only rule unit-tested.
- Resubmit flag is client-side state (no token needed — the collection is the same).

## Links

- Feature: `COL-005-completion.md` (FR-029-3, FR-029-4, AC-029-2)
- Related: F-COL-002 (new submission), F-TRF-006 (email pipeline)
