# US-041-02 — Verify our files are in the right place

**Feature:** F-ENT-004 — Data Residency | **Status:** pending

---

**Story:** As an org admin, I want a residency report, so that I can show a client or auditor that our files are actually in the region we chose.
**Actor:** OrgAdmin.
**Goal:** one screen: region, account, counts, anomalies — and the act of looking is itself logged.

## Preconditions

- Org exists with a region (US-041-01).

## Happy path

1. Org settings → **Data** → **View full report** → modal: region, storage account (masked), `transfersCount`, `activeBytes`, `lastSyncAtUtc`, `anomalies: []`.
2. The Data card always shows the short line: "Last verified {date} — no anomalies."
3. Opening the full report writes `residency.report_viewed` (the auditor can prove who looked, when — FR-041-4).

## Alternative flows

- **Anomalies present:** each row: blob path (partial, `{prefix}/files/{id}`), expected vs actual account, detected date → "Contact us" line; the check job can clear them (US-041-03).
- **Archived org:** report says "archived — last verified {date}", anomalies suppressed (EC-041-4).

## Acceptance criteria

```gherkin
Given an org with org-attributed transfers
When the admin opens the report
Then region, account, counts and the anomaly list are shown
And residency.report_viewed is in the audit log

Given one blob is misplaced
Then the report lists it with expected and actual accounts
```

## Edge cases

- Zero transfers yet → "No data yet — the report is meaningful once your team sends its first file."
- `activeBytes` is the sum over `Status in (Active, DownloadLimit)` — same definition as F-TRF-007-4 (no new math).
- Report is a live query (no cache beyond the 30 s plan-cache pattern) — "last verified" = the nightly check, not the report itself (two different timestamps, labeled distinctly).

## UI notes

- Modal, `--bg-subtle` monospace block for the JSON, `Ref:`-style footer. Tone: plain, no "certified!" energy.
- Data card line states the honest SQL note in one sentence (FR-041-6): "Your files are in the {region} storage account. Your account records are in our shared database, partitioned by organization."

## Technical notes

- `GetResidencyReportQuery`; endpoint 59.
- Anomalies read from `ResidencyAnomaly` (open rows) + live account comparison for the top-N most recent transfers (bounded scan, `TOP 50` for speed — documented).
- Audit: `residency.report_viewed`, `EntityType='org'`.

## Links

- Feature: `ENT-004-data-residency.md` (FR-041-4, AC-041-2)
- Architecture: TA-3.2 (ResidencyAnomaly), TA-4.2 #59
- Related: US-041-03 (who fills the anomalies), US-040-03 (the audit entry it writes)
