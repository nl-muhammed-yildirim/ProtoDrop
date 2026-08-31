# US-041-03 — Trust the check runs without me

**Feature:** F-ENT-004 — Data Residency | **Status:** pending

---

**Story:** As an operator (or the admin who never gets a chance to look), I want the residency check to run on its own, so that a misplaced blob is found in hours, not at audit time.
**Actor:** Operator / system.
**Goal:** `f-residency-check` nightly, idempotent, alerting — zero human involvement in the steady state.

## Preconditions

- Orgs with regions in production.

## Happy path

1. 03:00 daily: `f-residency-check` claims per-org `JobRun` keys (TA-6.1), scans org-attributed `BlobRef` rows, compares `StorageAccountId` to the org's regional account.
2. Mismatch → one open `ResidencyAnomaly` row per blob (deduped: same blob flagged twice → one open row, `ResolvedAtUtc` NULL) + `residency.anomaly` event + operator alert.
3. Clean run → `residency.verified { orgId, checked, anomalies: 0 }`.
4. Anomaly auto-resolves when the blob is re-checked and matches (e.g., a corrected copy) → `ResolvedAtUtc` set.

## Alternative flows

- **Regional account down** during the check → rows for that org skipped with `degraded` telemetry (no anomaly rows — EC-041-3).
- **Org archived** → check stops writing new anomalies (EC-041-4).

## Acceptance criteria

```gherkin
Given a blob was written to the wrong account
When the nightly check runs
Then a ResidencyAnomaly row exists and an alert fires
And the report (US-041-02) shows it as an anomaly

Given two consecutive runs with the same condition
Then there is still exactly one open anomaly row
```

## Edge cases

- Check runs twice (host scale) → `JobRun` claim makes the second a no-op (TA-6.1).
- Clock skew → detection uses "expected from org row", not timestamps.
- Anomaly older than 48 h unresolved → escalation alert (TA-10.3 addition).

## UI notes

- No primary UI: the Data card's "Last verified {date}" + anomaly count is the surface. Operator re-run: `POST /admin/orgs/{id}/residency/verify` (endpoint 60) from the admin console.

## Technical notes

- Function `f-residency-check` (TA-6.2 extension): timer `0 0 3 * * *`, concurrency 1, 30 min timeout, `JobRun` key `residency:{orgId}` (batched per org; orgs > 1 000 = decision).
- Query shape: `SELECT b.Id, b.StorageAccountId FROM BlobRef b JOIN Organization o ON b.OrgId = o.Id WHERE b.OrgId = @org AND b.StorageAccountId <> @expected` (index `IX_BlobRef_Org` + `IX_BlobRef_Storage`).
- Telemetry: `residency_check { orgId, blobCount, anomalies }`.

## Links

- Feature: `ENT-004-data-residency.md` (FR-041-5, AC-041-3)
- Architecture: TA-6.1/6.2, TA-10.3
- Related: US-041-02 (consumer of the findings)
