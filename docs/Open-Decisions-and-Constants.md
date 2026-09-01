# Open Decisions & Constants

**Last updated:** 2026-08-23
Part 1: decisions awaiting the human (**TBD-you**). Part 2: frozen constants code reads. Part 3: append-only decision log (AI + human).

---

## Part 1 — Open decisions (TBD-you)

Status values: `open` → `confirmed` (you) / `default-accepted` (you said "proceed with defaults" — code may use the default).

| ID | Item | Current placeholder | Recommended default | Owner | Status |
|---|---|---|---|---|---|
| D-01 | Product name | ProtoDrop | **ProtoDrop** | you | default-accepted |
| D-02 | Main domain | protodrop.com | **protodrop.com** | you | default-accepted |
| D-03 | API domain | api.protodrop.com | **api.protodrop.com** | you | default-accepted |
| D-04 | Email `From` address | no-reply@protodrop.com | **no-reply@protodrop.com** | you | default-accepted |
| D-05 | Plan names | Free / Pro / Business | **Free / Pro / Business** | you | default-accepted |
| D-06 | Free tier limits | 5 GB / 7 d / 100 downloads / 5 GB storage / 20 active | per Feature Plan Appendix A | you | default-accepted |
| D-07 | Pro/Business limits & prices | 20/50 GB & 100 GB tiers; $9 / $19 / $49 (TBD) | confirm before billing (F-BIL-001) | you | open |
| D-08 | Azure region (prod) | westeurope | **westeurope** (GDPR-friendly) | you | default-accepted |
| D-09 | Cost ceiling | ~$1,800/mo at 10 TB/day (TA-16) | review at CG-1/CG-2 gates | you | open |
| D-10 | Brand primary color | `#4185F4` (ProtoDrop brand blue) | **`#4185F4`** | you | default-accepted |
| D-11 | CI provider | GitHub Actions | **GitHub Actions** | you | default-accepted |
| D-12 | Stripe account | test mode until launch | you create (Preflight P-08) | you | open |
| D-13 | Communication Hub resource | dev resource created with IaC; prod resource manual | you create (Preflight P-09) | you | open |
| D-14 | Free-tier ads on/off | off at MVP launch (F-PRF-004 later) | off | you | default-accepted |
| D-15 | Magic link as default auth UX | email+password primary, magic link secondary | as specced | you | default-accepted |
| D-16 | i18n language set at launch | en, nl, fr, es, pt, it, de, tr | en+nl+de+tr at launch, rest follow | you | open |
| D-17 | GDPR legal texts (ToS/Privacy) | drafted by you | needed at launch (Preflight P-14) | you | open |
| D-18 | Admin emails (seed) | dev: dev@example.com; prod: you | set in KV/config at deploy | you | open |
| D-19 | Retention days for Pro/Business | 30 / 90 | confirm with D-07 | you | open |
| D-20 | Tracking pixel in emails | OFF by default | keep OFF (privacy) | you | default-accepted |
| D-21 | Search backend for F-XCT-002 | SQL full-text `CONTAINS` first | **SQL full-text first** — Azure Search only if relevance/scale demands (ADR at that point, endpoint contract unchanged) | you | open |
| D-22 | Data export retention (F-XCT-004) | link 24 h / stored zip 7 days | **24 h link, 7-day blob lifecycle** — confirm before launch (privacy text D-17 must match) | you | open |

## Part 2 — Frozen constants (code reads these, never literals)

These are the *current* values of the Limits Registry (TA-3.4 / Feature Plan Appendix A). Code reads them from `Plan.LimitsJson` + `FeatureFlag` rows seeded by migration; changing a value here is a **decision** (escalate per AGENT.md §7).

| Constant | Free | Pro | Business |
|---|---|---|---|
| `MAX_TRANSFER_SIZE` | 5368709120 (5 GB) | TBD (D-07) | 107374182400 (100 GB) |
| `MAX_SINGLE_FILE` | = `MAX_TRANSFER_SIZE` | same | same |
| `MAX_ZIP_SIZE` | 4294967296 (4 GB) | 9223372036854775807 | 9223372036854775807 |
| `RETENTION_DAYS` | 7 | 30 (D-19) | 90 (D-19) |
| `GRACE_DAYS` | 3 | 3 | 3 |
| `MAX_DOWNLOADS` | 100 | 1000 | -1 (∞) |
| `MAX_EMAILS` | 20 | 100 | 500 |
| `STORAGE_QUOTA` | 5368709120 (5 GB) | 107374182400 (100 GB) | 1099511627776 (1 TB) |
| `ACTIVE_TRANSFERS_MAX` | 20 | 200 | -1 (∞) |
| `SCHEDULING` | false | true | true |
| `BRANDING` | false | true | true |
| `ANALYTICS` | false | true | true |
| `ADS` | false (D-14) | false | false |
| `SSO_SCIM` | false | false | true |

**Technical constants (code):**

| Constant | Value | Notes |
|---|---|---|
| Link ID alphabet | Crockford base32, 8 chars | `0O1I` excluded |
| SAS TTL draft upload | 7200 s | TA-3.6 |
| SAS TTL download | 1800 s | TA-3.6 |
| Unlock JWT TTL | 7 d | TA-9.1 |
| Magic link TTL | 600 s, single-use | TA-9.1 |
| Session TTL | 30 d rolling | TA-9.1 |
| Block size (browser upload) | 8 MiB | TA-8.3 |
| Block parallelism | 4 per file, files serialized | TA-8.3 |
| Block retries | 5 with backoff | F-TRF-001-6 |
| Flags/Plans cache TTL | 30 s | TA-3.4 |
| Expiry/delete job period | 15 min | TA-6.2 |
| Blob delete buffer | 24 h after RefCount=0 | TA-6.5 |
| Staging lifecycle | 24 h | TA-3.5 |
| Transfers lifecycle safety net | 30 d | TA-3.5 |

## Part 3 — Decision log (append-only)

| Date | Decision | Chosen | Justification | By |
|---|---|---|---|---|
| 2026-08-23 | ADR-015 | Clean (onion) projects `wa.domain/wa.application/wa.infrastructure/wa.api/wa.workers` | Ports/adapters keep EF out of domain/use cases | you (doc update request) |
| 2026-08-23 | ADR-016 | CQRS via MediatR 12.5, frozen version | One request/handler per use case; known API surface | you (doc update request) |
| 2026-08-23 | Toolchain | .NET 10 + Visual Studio 2026 | your dev environment | you (doc update request) |
| 2026-08-28 | Phase X speccing | F-XCT-001…005 fully specced (FR/AC/EC + US-044…048); new flag keys `feature.search`/`feature.dataExport`/`feature.emailSettings`; new Problem codes `FEATURE_DISABLED`, `FLAG_UNKNOWN`, `FLAG_INVALID`, `Q_TOO_LONG`, `EXPORT_IN_FLIGHT` | cross-cutting features need gates + audit before any phase feature ships | AI (cross-cutting spec request) |
| 2026-08-31 | Frontend test tooling | `vitest` 4 + `jsdom` + `@testing-library/react` as wa.web devDependencies; scripts `test` (watch) + `test:run` (one-shot gate); `wa.web.esproj` `JavaScriptTestFramework = Vitest` | TA-8.1 lists `vitest` as the web test tooling and AGENT.md §4 gate includes `npm run test`; versions pinned at install, no runtime deps added | AI (T-001 web stub) |
| 2026-09-01 | Outbox table added per T-006/TA-5.2 | Migration `20260901170311_AddEventOutbox`: `dbo.EventOutbox` (Id, EventId UNIQUE, EventType, PayloadJson, PartitionKey, CorrelationId, SentAtUtc NULL) | Known gap: TA-3.2 DDL has no outbox table, but T-006/TA-5.2 require "outbox row" + TA-4.1.7 "write to local outbox table" on publish failure — TA-3.2 needs the row added | AI (T-006b) |
