# F-PRF-003 — Download Analytics

**Priority:** P1 | **Phase:** 1 — Pro
**Spec source:** `02-feature-plan.md` F-PRF-003 (outline FR-003-1…3 → remapped below as FR-023-*) | **Architecture:** TA-3.2 (`DownloadEvent`, `IX_DownEvent_T`), TA-9.4, TA-4.2a
**Milestone tasks:** Phase 1 backlog (not yet created)

---

## Description

Pro+ users can answer "did they get my files?" — per transfer: **downloads over time** (daily buckets), **countries** (from first-download IP, privacy-noted), **browsers**; per-file download counts; and a "top downloads" list on the My Files detail page. The data is already collected in MVP (`DownloadEvent`, TA-3.2) — this feature **reads** it and presents it, and makes the collection PII-safe (hashed IP/UA, country only).

**Actors:** Pro/Business user (views analytics), operator (validates the PII rules), recipient (source of the data — consented via the transfer terms).
**Value:** the first "did it land" proof for a sender — the analytics tier sells itself the first time someone checks.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-023-1 | Per transfer: downloads over time (daily buckets, last 90 days capped at retention), countries (from first-download IP — privacy note shown), browsers (UA family, top 5). |
| FR-023-2 | Per-file download counts (per `FileId`, plus "download-all" as its own row). |
| FR-023-3 | "Top downloads" list on the My Files detail page (top 5 files by count, tie-broken by recency). |
| FR-023-4 | PII (TA-9.4): `IpHash` (HMAC, key in Key Vault), `UaHash`, `Country CHAR(2)` only. No raw IP/UA persisted after bucketing. The privacy note on the page states: "Country is estimated from your download's first IP; we keep no raw IP." |
| FR-023-5 | Gating: `PlanContext.Features.analytics`; Free tier collects (shared pool for future) but the UI shows "Pro plan required" (no data shown, no cost of the query). |
| FR-023-6 | Data lifetime: `DownloadEvent` rows are hard-deleted with the transfer (F-TRF-005 deletion job — **one added delete in `f-delete-transfers`**, no new table lifecycle). |

## Acceptance criteria

```gherkin
AC-023-1: Pro user opens analytics for a transfer
  Then they see the daily chart (daily buckets), top countries, top browsers, and per-file counts

AC-023-2: No raw IP or UA string is ever returned by the API
  Then the response contains only hashes + country + browser family
  And the privacy note text is present

AC-023-3: Free user opens a transfer detail
  Then the analytics section shows "Pro plan required" (no query, no partial data)

AC-023-4: A transfer with 3 files is fully deleted
  Then all its DownloadEvent rows are gone (verified in DB)

AC-023-5: Top downloads list
  Then it ranks by count, tie-break recency, max 5 rows, "download-all" is a separate entry
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-023-1 | Zero downloads | Section renders an empty state: "No downloads yet." — not an error |
| EC-023-2 | Country unavailable (no GeoIP on IP) | Counted under `??` bucket, shown as "Unknown" |
| EC-023-3 | UA unparseable | Bucket `Other`; never crashes the query |
| EC-023-4 | Very large transfer (10k downloads) | Query aggregates in SQL (no client-side paging of raw events); p95 < 500 ms on the index (TA-3.3 hot query) |
| EC-023-5 | Analytics page opened after expiry | Rows still exist (kept to transfer deletion, FR-023-6); page shows the expired badge alongside |
| EC-023-6 | Downgrade to Free mid-life | Data stops being shown but keeps being collected (future re-upgrade); documented |

## UI notes (UI-Reference §5.4)

- My Files detail: "Downloads" panel — daily line chart (simple, no chart library: SVG polyline per daily bucket, accent color), three stat chips (total, top country, top browser), per-file table (name, count), top-5 list.
- Privacy note: `--fs-tiny` `--fg-muted` under the country stat.
- Empty state per UI-Reference tone: icon + one line + nothing else.
- Free tier: dashed card + "Pro plan required" + CTA link (consistent with F-PRF-001).

## Technical notes

- Reads only: `DownloadEvent` (TA-3.2, index `IX_DownEvent_T`). New query `GetTransferAnalyticsQuery` (TA-4.2a `Transfers/` area) → endpoint `GET /api/v1/transfers/{id}/analytics` (new endpoint 24 — **catalog addition, ADR note**).
- Daily bucket: `GROUP BY CAST(CreatedAtUtc AS DATE)` in SQL; 90-day cap = `RETENTION_DAYS` + `GRACE_DAYS` of the plan at send time.
- Country/browser: derived at event write time (MVP `f-email`-independent — actually written by the API at SAS mint, TA-5.3 `download.completed`): add `Country` + `UaHash` columns are already in the DDL; **browser family** is derived from `UaHash` → needs a plain `UserAgentFamily VARCHAR(64)` column at write time (**schema addition: migration required**, ADR note) — stored at write, queried as `TOP 5` by count.
- PII: `IpHash` HMAC key in Key Vault (TA-9.4); hash in API at event write (no raw IP in the row).
- Telemetry: `analytics_viewed` (PII-safe: userId + transferId only).

## Test plan

- Unit: bucket computation (date boundaries, DST-irrelevant in UTC), tie-break logic, `??` bucketing, top-5 cut.
- Integration: AC-023-1/2 (assert no raw IP/UA in response), AC-023-4 (deletion job clears rows), AC-023-5; query plan uses `IX_DownEvent_T`.
- E2E: seed a transfer with synthetic DownloadEvents → panel renders with correct numbers; Free tier shows the CTA card.

## User stories

| ID | Story | File |
|---|---|---|
| US-023-01 | See who's downloading my transfer | `US-023-01-analytic-view.md` |
| US-023-02 | Know which files mattered | `US-023-02-file-counts.md` |
| US-023-03 | Trust that my recipients' data is handled | `US-023-03-privacy-note.md` |
| US-023-04 | Have analytics vanish with the transfer | `US-023-04-analytic-lifetime.md` |
