# F-ENT-004 — Data Residency

**Priority:** P1 (Phase 3) | **Phase:** 3 — Enterprise
**Spec source:** `02-feature-plan.md` §5 F-ENT-004 (outline → remapped below as FR-041-*) | **Architecture:** TA-3.5, TA-3.2, TA-4.2, TA-11.2
**Milestone tasks:** T-059 (M6)

---

## Description

Some organizations must have **all of their data in one region** — EU for GDPR minimization, US for cost/latency. Residency is chosen **at org creation** (F-ENT-003) and stamped on the org: all of the org's blobs live in that region's storage account, and the org's SQL rows are written only through that org's data path. In Phase 3 this is a **storage-region boundary** plus an audited "residency report" — a full per-org SQL shard is an explicit non-goal (documented below).

**Region set (frozen for Phase 3):** `westeurope` (default, D-08) and `eastus2`. New regions = config decision, not code.

**Actors:** org admin (chooses at creation, reads the report), operator (provisions the regional storage account), member (unaware — that's the point).
**Value:** "Where is my data?" gets a one-sentence answer backed by a report.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-041-1 | **Region at org creation:** `Organization.Region` (`westeurope` \| `eastus2`) selected in the create-org screen (F-ENT-003) with a plain-language note per region ("Data stored in the European Union" / "Data stored in the United States"). Default `westeurope`. |
| FR-041-2 | **Blob routing:** all `BlobRef` paths for an org-attributed transfer (and collection/document/album when org-scoped) are written to the **regional storage account** `wa-blob-{region}`. Staging uploads for org users get their SAS from the regional account (draft creation resolves the org from the session cookie). Non-org users → default `westeurope` account. |
| FR-041-3 | **Immutability rule:** the region is **not changeable** in Phase 3 (no migration UI). Changing it later = decision (D-24) + a migration job. `PATCH /orgs/{id}` rejects a `region` field with `409 RESIDENCY_IMMUTABLE`. |
| FR-041-4 | **Residency report:** `GET /api/v1/orgs/{orgId}/residency` → JSON: `{ region, blobAccount (masked: wa-blob-* + region), transfersCount, activeBytes, lastSyncAtUtc, anomalies[] }`. `anomalies` = org-attributed `BlobRef`s whose storage account ≠ regional (should be zero; surfaced by the nightly check FR-041-5). Report is read-only; viewing it is audit-logged (`residency.report_viewed`). |
| FR-041-5 | **Nightly integrity check:** `f-residency-check` (timer, 03:00, `JobRun` claim like TA-6.1) scans org-attributed `BlobRef` rows: any `StorageAccountId` ≠ org's regional account → `RESIDENCY_ANOMALY` row + alert. Index: `BlobRef (StorageAccountId)` + `BlobRef (OrgId)` (TA-3.2 addition: `StorageAccountId BIGINT NULL`, `OrgId UNIQUEIDENTIFIER NULL`). |
| FR-041-6 | **SQL co-location (Phase 3 scope, documented):** org rows live in the shared DB; residency is enforced at the **storage** layer plus a documented SQL note ("rows are logically partitioned by `OrganizationId`; physical per-org shard is a post-Phase-3 decision"). The org settings screen says so in one line. |

## Acceptance criteria

```gherkin
AC-041-1: An org is created in eastus2
  Then its members' uploads land in wa-blob-eastus2
  And a guest (no org) upload lands in the default westeurope account

AC-041-2: The residency report
  Given an org with 50 org-attributed transfers
  When the org admin opens the report
  Then region, account, counts and zero anomalies are shown
  And residency.report_viewed is audit-logged

AC-041-3: A blob lands in the wrong account (simulated)
  Then the nightly check flags a RESIDENCY_ANOMALY row
  And an operator alert fires

AC-041-4: PATCH /orgs/{id} with a region
  Then 409 RESIDENCY_IMMUTABLE
  And no partial update is applied
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-041-1 | Org user's **re-send** (F-TRF-010) | New transfer stays in the same regional account — `BlobRef.StorageAccountId` carries over (blobs are shared, not copied) |
| EC-041-2 | Org user sends a **personal** transfer (explicit "not on behalf of org" in the link screen, Phase 3 toggle) | Default account; documented in UI helper text |
| EC-041-3 | Regional account fails over / 404s | Uploads show F-TRF-013 degraded state; check job marks account `degraded` in telemetry, no per-row anomaly rows |
| EC-041-4 | Account deleted after org archive | Anomalies suppressed for archived orgs (`ArchiveReason`) — report says "archived, last verified {date}" |
| EC-041-5 | Two orgs, same region, same user | Independent `StorageAccountId` per `BlobRef`; no cross-org leakage |
| EC-041-6 | Clock/timezone in report | All `*AtUtc`; UI renders in viewer's locale (F-TRF-014) |

## UI notes (UI-Reference extension — Org settings → "Data")

- **Create-org screen** (F-ENT-003): region selector — two radio cards (flag-free, region name + one-line description), disabled after creation (`RESIDENCY_IMMUTABLE` copy in the tooltip).
- **Org settings → Data card:** region chip (static once created), storage account (masked), "Last verified {date} — no anomalies", **View full report** (modal with the JSON report in `--bg-subtle` monospace, `Ref:` style footer). One honest sentence: "Your SQL rows are in our shared database, partitioned by organization; your files are in the {region} storage account."
- No confetti, no exclamation points.

## Technical notes

- DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  ALTER TABLE dbo.BlobRef ADD
      StorageAccountId  BIGINT NULL,          -- maps to a regional account (lookup table below)
      OrgId             UNIQUEIDENTIFIER NULL CONSTRAINT FK_Blob_Org REFERENCES dbo.Organization(Id);
  CREATE INDEX IX_BlobRef_Org ON dbo.BlobRef (OrgId);
  CREATE INDEX IX_BlobRef_Storage ON dbo.BlobRef (StorageAccountId);

  CREATE TABLE dbo.StorageAccount (
      Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StorageAccount PRIMARY KEY,
      Name          VARCHAR(32) NOT NULL CONSTRAINT UQ_StorageAccount_Name UNIQUE, -- 'westeurope','eastus2'
      Region        VARCHAR(16) NOT NULL,
      AccountName   VARCHAR(64) NOT NULL
  );
  CREATE TABLE dbo.ResidencyAnomaly (
      Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ResidencyAnomaly PRIMARY KEY,
      BlobRefId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_RA_Blob REFERENCES dbo.BlobRef(Id),
      OrganizationId UNIQUEIDENTIFIER NULL,
      ExpectedStorageAccountId BIGINT NOT NULL,
      ActualStorageAccountId   BIGINT NULL,
      DetectedAtUtc DATETIME2 NOT NULL,
      ResolvedAtUtc DATETIME2 NULL
  );
  ```
- Endpoints (TA-4.2 extension, ADR note): `GET /api/v1/orgs/{orgId}/residency` (59), `POST /api/v1/admin/orgs/{orgId}/residency/verify` (60, operator re-run).
- MediatR: `GetResidencyReportQuery`, `RunResidencyCheckCommand` (used by the function).
- Function (TA-6.2 extension, ADR note): `f-residency-check` — timer `0 0 3 * * *`, concurrency 1, 30 min, `JobRun` claim key `residency:{orgId}` batched per org (orgs > 1 000 = decision).
- Events (TA-5.3 extension): `residency.anomaly { orgId, blobRefId, expected, actual }`, `residency.verified { orgId, checked, anomalies }`.
- Telemetry (TA-10.2 extension): `residency_check { orgId, blobCount, anomalies }`; alert: any unresolved `ResidencyAnomaly` older than 48 h.
- `BlobRef.StorageAccountId` is set at write time from `IBlobStore.AccountRegion()` — the port gains `Region` (TA-3.5/TA-3.6 ADR note).

## Test plan

- Unit: region enum parsing; `StorageAccountId` resolution per (org, account); anomaly row dedup (same blob flagged twice → one open row).
- Integration: AC-041-1…041-4 with two fake storage accounts (Azurite × 2, local); re-send keeps account (EC-041-1); nightly check picks up a deliberately misplaced blob.
- E2E: create org (eastus2) → upload as member → verify account + report (Playwright, dev with two Azurite ports).

## User stories

| ID | Story | File |
|---|---|---|
| US-041-01 | Choose where our data lives | `US-041-01-choose-region.md` |
| US-041-02 | Verify our files are in the right place | `US-041-02-residency-report.md` |
| US-041-03 | Trust the check runs without me | `US-041-03-nightly-check.md` |
