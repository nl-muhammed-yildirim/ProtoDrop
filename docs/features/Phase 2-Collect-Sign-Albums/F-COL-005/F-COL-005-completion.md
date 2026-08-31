# F-COL-005 — Completion & Contributor Notification

**Priority:** P1 (Phase 2a) | **Phase:** 2a — Collect
**Spec source:** `02-feature-plan.md` F-COL-005 (outline → remapped below as FR-029-*) | **Architecture:** TA-5.3, TA-6.7
**Milestone tasks:** T-046 (M5)

---

## Description

The end of a submission's life: the collector marks a submission **Done** (the terminal "I'm finished with this" state) and the contributor — if they left an email — gets a notification ("{CollectorName} marked your submission as complete."). **Accepted** and **Declined** are reversible working states that also email the contributor on change (declined → "you can resubmit" CTA). All emails go through the existing `f-email` pipeline (F-TRF-006): retries, suppression, dead-letter, localization.

**Actors:** collector (marks), contributor (gets notified), operator.
**Value:** closure — the contributor knows when their part is over, without polling.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-029-1 | **Done**: per-entry "Mark done" action (terminal). Confirmation modal: "Mark as done? The contributor will be notified." Sets `Status = Done`, `DoneAtUtc`. |
| FR-029-2 | On **Done**: contributor email "Your submission to {collection} is complete." (only if email was provided). |
| FR-029-3 | **Accepted**: reversible; email "Your submission to {collection} was accepted." — only on the transition *into* Accepted (no spam on re-accept). |
| FR-029-4 | **Declined**: reversible until Done; email "Your submission to {collection} was declined — you can resubmit." + a **Resubmit** link (new submission to the same collection, F-COL-002 — not an edit of the old entry). |
| FR-029-5 | Suppression: `EmailSuppression` respected per (address, collector-email) exactly as F-TRF-006-5; unsubscribe link in every collection email. |
| FR-029-6 | No email when the contributor gave none — no bounce risk, no account needed. |
| FR-029-7 | Collector notification per new submission: "New submission to {collection} from {name}" — one email per entry (dedup by event, TA-5.2), suppressed by the same mechanism. |

## Acceptance criteria

```gherkin
AC-029-1: The collector marks an entry Done
  Then the contributor (with email) gets the "complete" email

AC-029-2: The collector declines an entry
  Then the contributor gets the "declined — resubmit" email with a working resubmit link

AC-029-3: The same status transition is fired twice (event replay)
  Then the contributor gets exactly one email (dedup)

AC-029-4: A contributor who unsubscribed is marked Done
  Then no email is sent, and the suppression is visible in admin (F-TRF-011)

AC-029-5: An entry with no contributor email is marked Done
  Then no email is attempted (no log-only send, no failure)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-029-1 | Resubmit after decline | New entry (new row, F-COL-002-3); the declined entry stays visible (history), marked Declined |
| EC-029-2 | Done on an entry with a bounced address | `f-email` marks `email.failed`; the Done state still stands (state and delivery are decoupled) |
| EC-029-3 | Collection closed after Done | Done entries keep their emails; no retroactive sends |
| EC-029-4 | Two collectors? | MVP: single owner per collection (FR-025-3) — no shared collectors (Phase 3, documented) |

## UI notes (UI-Reference §5.4, §7)

- Email design: one CTA button (like F-TRF-006), calm tone: "Your submission to {collection} is complete." / "…was declined — you can resubmit." No exclamation points.
- Dashboard: Done rows dim slightly (opacity 0.75 on meta) — the chip says `Done` (text, not color-only).
- Resubmit link target: the collection link with a `?resubmit` flag → contributor page pre-titled "Resubmit to {collection}" (no data pre-fill — it's a new submission).

## Technical notes

- Transitions handled by `SetEntryStatusCommand` (F-COL-003) — this feature owns the **email fan-out**, not the state machine.
- Events (TA-5.3 extension, already listed under F-COL-003): `collection.entry.status_changed`; `f-email` consumes it (subscription `email`), templates `collection_done`, `collection_accepted`, `collection_declined`, `collection_new_submission`.
- Dedup: `NotifiedAtUtc`-style idempotency per (entry, status) — a second event with the same (entry, status) pair skips (TA-5.2 dedup + state check, F-TRF-006-8 pattern).
- Metrics: `email_failures_1h` shared (TA-10.3); no new alert line.

## Test plan

- Unit: transition→template mapping; dedup predicate; suppression matching.
- Integration: AC-029-1…029-5 (fake sender: one email per transition, replay-safe, suppression honored, no-email case silent).
- E2E: decline → resubmit link opens a fresh contributor page (Playwright).

## User stories

| ID | Story | File |
|---|---|---|
| US-029-01 | Mark a submission as done | `US-029-01-mark-done.md` |
| US-029-02 | Let the contributor know when their submission is finished | `US-029-02-contributor-notified.md` |
| US-029-03 | Tell a contributor their submission was declined | `US-029-03-declined-resubmit.md` |
