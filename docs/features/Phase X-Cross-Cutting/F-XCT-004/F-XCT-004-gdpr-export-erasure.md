# F-XCT-004 — GDPR: Data Export & Erasure Completeness

**Priority:** P1 | **Phase:** X — Cross-cutting
**Spec source:** `02-feature-plan.md` §6 (F-XCT-004) | **Architecture:** TA-9.4, TA-5.2
**Gate:** `feature.dataExport` (F-XCT-001) | **Milestone tasks:** T-068

---

## Description

GDPR gives users two rights beyond "delete my account": **portability** (give me my data) and **erasure** (when I leave, everything PII-ish goes — *including telemetry*, which is where PII hides longest). Account deletion itself is already F-TRF-008-8 (US-008-06); this feature adds the missing halves: a **data export** (zip of profile + transfer metadata) and the **telemetry PII sweep** that makes erasure complete, plus one auditable statement of what "erased" means.

**Actors:** signed-in user (export), data subject (erasure claim), operator/DPO (erasure report), `f-gdpr-sweep` function (telemetry scrub).
**Value:** the legal team can answer "where is that email still?" with a pointer instead of an investigation.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-047-1 | **Export:** `POST /api/v1/auth/me/export` creates an export job (one in-flight per user; a second `POST` while in-flight → 409 `EXPORT_IN_FLIGHT`). The job builds a zip containing: `profile.json` (email, name, theme, plan, `createdAtUtc`, `emailNotifications`), `transfers.json` (all the user's transfers: id, createdAt, status, retention, password set?, file list with names + sizes + sha256, recipient addresses, download counts — metadata, **not** file bytes). |
| FR-047-2 | **Export delivery:** the zip is written to a container `exports/{userId}/` with a 24 h SAS; `GET /api/v1/auth/me/export` returns the job state (`processing` / `ready` / `failed`) and, when ready, the download URL. Completed exports are lifecycle-deleted after 7 days. |
| FR-047-3 | **Scope:** export covers *owned* transfers only (`OwnerAppUserId = me`); transfers the user *received* as a recipient are not included (theirs, not theirs — documented). Re-sent transfers appear under both the original and the new id (same metadata, no dedup). |
| FR-047-4 | **Gate:** `feature.dataExport` (F-XCT-001). Off → Profile hides the **Export my data** action; `POST` returns 404 `FEATURE_DISABLED`; in-flight jobs at flip-off time still complete (the flag gates creation, not completion). |
| FR-047-5 | **Erasure includes telemetry:** on `DELETE /auth/me` (F-TRF-008-8), the `account_deleted` event (TA-10.2, already emitted) carries the `userId` as the erasure marker. A nightly function `f-gdpr-sweep` finds `account_deleted` markers older than 24 h and scrubs App Insights custom events with that `userId` PII-flagged property within the 90-day metrics retention (TA-11.2): either delete-by-query or expire-the-retention for those items — implementation per ADR, contract = "no raw PII queryable by userId after the sweep runs". |
| FR-047-6 | **Erasure report:** `GET /api/v1/admin/erasures?cursor=` (admin) lists deleted accounts with: delete time, transfer re-home count, sweep status (`pending` / `swept` + last sweep time). The DPO can answer "is that email fully gone?" from one screen. |
| FR-047-7 | **Erasure statement (one screen):** Profile → Delete account modal (US-008-06) gains one line: "Your telemetry data is anonymized within 24 hours of deletion." — the only user-visible GDPR copy this feature adds. |
| FR-047-8 | **No new event types:** the export and sweep use existing events only (`account_deleted` exists; export emits none — a `data_export_ready` would be TA-10.2 drift; export completion is a log line + the job row). |

## Acceptance criteria

```gherkin
AC-047-1: A user exports their data
  Given feature.dataExport is true and I have 5 transfers
  When I request an export and wait for ready
  Then the zip contains profile.json and transfers.json
  And transfers.json lists all 5 transfers with file names, sizes, and recipient addresses
  And the download link works for 24 h and the file is gone from storage after 7 days

AC-047-2: Erasure reaches telemetry
  Given I delete my account
  When f-gdpr-sweep runs the next night
  Then no App Insights custom event with my userId PII property is queryable
  And the erasures list shows my row as swept with the sweep time

AC-047-3: The export is mine alone
  Given I have transfers and a friend's transfer was also sent to me
  When I export
  Then only my owned transfers appear in transfers.json
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-047-1 | Export with zero transfers | Valid zip with `transfers.json = []` (profile still exported) |
| EC-047-2 | Export job fails (Blob unavailable) | State `failed` with a retryable `POST` (409 no longer applies); failure log line with correlation id; no telemetry event (FR-047-8) |
| EC-047-3 | User deletes their account while an export is in flight | The export completes from the snapshot it already read (soft-deleted `AppUser` keeps rows, US-008-06) — or fails cleanly; either way no crash, and the erasure report shows the account once |
| EC-047-4 | App Insights down during sweep | Sweep marks nothing; retries next night (idempotent by `userId`); the erasures screen shows `pending` until success |
| EC-047-5 | Re-registered user (same email, new id) | Their telemetry is *not* the old account's — the sweep keys on `userId` (new), never on email |
| EC-047-6 | Two `POST /export` in quick succession | Second is 409 `EXPORT_IN_FLIGHT` (one in-flight per user, FR-047-1) |

## UI notes

- Profile → **My data**: "Export my data" (button, not a link — it's an action). Modal/inline status: "Preparing your export…" → "Ready — download" (auto-refresh once per 5 s while processing; no polling faster than that).
- Copy for the expiry: "The link stays valid for 24 hours."
- Delete-account modal: add the single telemetry line (FR-047-7) to the existing US-008-06 copy.
- Admin → **Erasures** (read-only list + CSV export of the list): userId (masked email), deletedAt, re-homed transfer count, sweep status/time.

## Technical notes

- Export job: `ExportMyDataCommand` writes a job row (`DataExport` table: id, userId, state, blobPath, createdAtUtc, readyAtUtc). Because no new TA-5.3 event type is allowed (FR-047-8), the builder is a **timer function** `f-data-export` that scans due jobs — the same pattern as `f-expire` (TA-6) — and sets `ready` after writing the zip to a temp blob.
- `exports/{userId}/` container with lifecycle: 7-day blob expiry; 24-h SAS at `GET`.
- `f-gdpr-sweep`: nightly timer (TA-6 pattern), input = `account_deleted` markers from the erasures table joined against App Insights queries; idempotent per userId; marks `SweptAtUtc`.
- Masking: the erasures list shows `d****@x.com` to non-DPO admins (full email is PII, TA-9.4).
- New Problem+JSON codes: `EXPORT_IN_FLIGHT` (409), plus reuse `FEATURE_DISABLED` (404).

## Test plan

- Unit: export payload shape (fixture user + transfers → exact JSON), one-in-flight logic, sweep idempotency (same userId twice → one mark).
- Integration: `POST /export` happy (fake blob) + 409 + failed state; `GET` states; flag off → 404 `FEATURE_DISABLED`; erasures list rows appear on `DELETE /auth/me`.
- E2E: Playwright — request export in dev, poll to ready, download, unzip (fixture asserts presence of profile.json/transfers.json).
- Exit check (T-068): "Export zip verified in dev; after a test account deletion, the next sweep shows `swept` and no queryable PII by userId."

## User stories

| ID | Story | File |
|---|---|---|
| US-047-01 | Download all my data | `US-047-01-download-my-data.md` |
| US-047-02 | My telemetry is erased too | `US-047-02-erase-telemetry.md` |
| US-047-03 | Prove the erasure (DPO report) | `US-047-03-erasure-report.md` |
