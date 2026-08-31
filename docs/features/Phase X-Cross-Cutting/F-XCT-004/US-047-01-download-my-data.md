# US-047-01 — Download all my data

**Feature:** F-XCT-004 — GDPR: Data Export & Erasure Completeness | **Status:** pending

---

**Story:** As a user switching services or just curious, I want a single download of everything ProtoDrop knows about me — profile plus all my transfer history — so that portability is one click, not a support ticket.
**Actor:** Signed-in user in Profile.
**Goal:** "Export my data" → a zip with `profile.json` + `transfers.json` (metadata, not file bytes), valid for 24 h.

## Preconditions

- Signed in; `feature.dataExport` is true (F-XCT-001 gate, US-044-02).

## Happy path

1. Profile → **My data** → **Export my data**.
2. `POST /api/v1/auth/me/export` → state `processing`; the UI polls `GET` once per 5 s.
3. When `ready`: "Download" appears with the 24-h SAS link; the file is a zip containing:
   - `profile.json` — email, name, theme, plan, `createdAtUtc`, `emailNotifications`.
   - `transfers.json` — every owned transfer: id, createdAt, status, retention, `passwordSet`, file list (name, size, sha256), recipient addresses, download counts.
4. The link stays valid 24 h; the stored export vanishes from storage after 7 days (lifecycle).
5. A second export while one is in flight → 409 `EXPORT_IN_FLIGHT` ("Your export is already preparing.").

## Alternative flows

- **Zero transfers:** valid zip, `transfers.json = []` (EC-047-1).
- **Failure:** state `failed` → the button becomes **Try again** (FR-047-2, EC-047-2).
- **Flag flipped off mid-job:** the in-flight job still completes — the gate stops *new* exports, not running ones (FR-047-4).

## Acceptance criteria

```gherkin
Given feature.dataExport is true and I own 5 transfers
When I request an export and it becomes ready
Then the zip contains profile.json with my email and plan
And transfers.json lists all 5 transfers with file names, sizes, and recipient addresses
And the download link expires after 24 h

Given an export is already processing
When I click Export my data again
Then the response is 409 EXPORT_IN_FLIGHT and no second job is created

Given a transfer was sent to me by someone else
When I export
Then it is not in my transfers.json (owned transfers only)
```

## Edge cases

- The export is a *snapshot*: transfers created after the read are not in the zip (documented as "as of").
- File **bytes** are not exported (the recipient's problem to ask for files; sizes + sha256 are the metadata proof) — stated in the button's helper text.
- Re-sends appear under both ids (FR-047-3) — dedup would be confusing.

## UI notes

- Profile → My data: **Export my data** button; status text per state; 5 s polling while processing; "The link stays valid for 24 hours." caption.
- Helper: "Includes your profile and transfer history — not the files themselves."

## Technical notes

- `DataExport` row per job; `f-data-export` timer builds the zip (TA-6 pattern, no new event type — FR-047-8).
- 24-h SAS on `exports/{userId}/`; 7-day lifecycle rule (TA-3.5).
- Problem codes: `EXPORT_IN_FLIGHT` (409); reuse `FEATURE_DISABLED` (404).

## Links

- Feature: `XCT-004-gdpr-export-erasure.md` (FR-047-1/2/3/4)
- Plan AC: AC-047-1, AC-047-3
- Related: US-008-04 (Profile), US-008-06 (the delete flow next door), US-044-02 (the gate)
- Architecture: TA-4.2 (endpoint 13 family), TA-6, TA-9.4
- Milestone: T-068
