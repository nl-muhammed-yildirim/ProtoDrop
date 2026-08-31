# US-047-02 — My telemetry is erased too

**Feature:** F-XCT-004 — GDPR: Data Export & Erasure Completeness | **Status:** pending

---

**Story:** As a data subject who deleted my account, I want the traces of me in *telemetry* to disappear too — not just the rows in SQL — so that "erasure" means what the DPO says it means.
**Actor:** Data subject (indirectly), DPO (verifies), `f-gdpr-sweep` (performs).
**Goal:** After account deletion, no App Insights custom event is queryable by my `userId` within one nightly sweep.

## Preconditions

- The user deleted their account (`DELETE /auth/me`, F-TRF-008-8, US-008-06) → `account_deleted` event emitted with the `userId`.

## Happy path

1. `account_deleted` (TA-10.2, already the contract event) carries the `userId` — that id is the erasure marker; no new event type (FR-047-8).
2. The next night, `f-gdpr-sweep` finds markers older than 24 h and scrubs App Insights custom events carrying that `userId` PII property within the 90-day retention (TA-11.2).
3. The erasures list (admin) flips the row to `swept` with the sweep time.
4. If App Insights was down, the row stays `pending` and the next run retries — idempotent per userId.

## Alternative flows

- **Re-registered user:** same email, new userId — the sweep keys on the *new* id, never the email (EC-047-5); the old id's telemetry is already swept or gets swept by its own marker.
- **Sweep vs. retention boundary:** events past the 90-day retention are already gone; the sweep only acts inside retention (the contract is "not queryable", not "physically deleted from cold storage").

## Acceptance criteria

```gherkin
Given I deleted my account yesterday
When f-gdpr-sweep runs tonight
Then no custom event with my userId PII property is queryable in App Insights
And the erasures list shows my row as swept with the run time

Given the sweep ran for my userId before
When it runs again the next night
Then it marks nothing new (idempotent) and my row keeps its swept time
```

## Edge cases

- The 24 h delay is a *grace*, not a promise: the UI copy says "within 24 hours" (FR-047-7), and the sweep may run once per night — the next run after the marker crosses 24 h is the bound.
- Telemetry events that never carried the userId (anonymous sessions) are untouched by the sweep — they were already anonymous (TA-9.4).
- Hard-delete of the account row (P1 admin action, US-008-06) does **not** re-trigger the sweep; the marker is what triggers it.

## UI notes

- User-visible: one line in the delete-account modal — "Your telemetry data is anonymized within 24 hours of deletion." (FR-047-7, the only new copy).
- DPO-visible: the erasures screen (US-047-03) is where this is *seen*; there is no per-user telemetry screen.

## Technical notes

- `f-gdpr-sweep`: nightly timer (TA-6 pattern), input = erasures rows with `SweptAtUtc` NULL and marker age > 24 h; App Insights query per userId; idempotent mark.
- `account_deleted` already names the userId in its payload (US-008-06 emits it) — the sweep consumes that, no envelope change.
- 90-day metrics retention (TA-11.2) is the outer boundary; sweep scope = that window.

## Links

- Feature: `XCT-004-gdpr-export-erasure.md` (FR-047-5/7/8)
- Plan AC: AC-047-2
- Related: US-008-06 (the deletion that starts this), US-047-03 (the report)
- Architecture: TA-5.3 (event reuse), TA-6, TA-9.4, TA-11.2
- Milestone: T-068
