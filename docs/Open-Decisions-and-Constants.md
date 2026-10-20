# Open Decisions & Constants

**Last updated:** 2026-09-02
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
| SAS version token | `2024-08-04` | TA-3.6 "Version 2024" — see ADR in Part 3 |
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
No new packages; key from `Wa:Blob:AccountKey` (Key Vault unwrap = prod deploy concern); absolute URL form per TA-3.6. Unit-asserted in `BlobSasMinterTest` (sp/sr/sv/se/st, absolute URL, independent re-sign). | AI (T-007b) |
| 2026-09-03 | ADR — Azurite integration test helper (T-007c) | `Testcontainers.Azurite` **4.14.0** (`AzuriteBuilder` from the shared Testcontainers family, same Docker pattern as the SQL tests) — the helper **exists** at 4.14.0, so the compose-Azurite fallback allowed by the exit check was not needed. Test: `tests/wa.api.integration/BlobSasIntegrationTest.cs` (mint `cwr` SAS → browser-leg block PUT over plain HTTP → read back via `IBlobStore`). Image `…/azurite:latest` (dev throwaway). | Test-only package (not a prod dep; Central Package Management); `Build()` → container with `GetBlobEndpoint()`, `AccountName`/`AccountKey` constants — the two transport-local accommodations (account path segment + `spr=https,http` re-sign) are needed because Azurite reads the first path segment as the account and its `validateProtocol` rejects a plain-HTTP PUT against `spr=https`. | AI (T-007c) |
| 2026-10-08 | ADR — DB health probe (T-050-03) | `Microsoft.Data.SqlClient` **7.1.1** in wa.infrastructure (raw `SqlConnection`, no EF yet) | TA-2.6 lists `Microsoft.EntityFrameworkCore.SqlServer` 10.x for wa.infrastructure, but no `WaDbContext` exists at M0 — the health endpoint needs a real `SELECT 1` and DB auto-create only; raw SqlClient is the minimal surface and stays until T-049/T-004 migrations land | AI (T-050-03) |
| 2026-10-08 | ADR — Service Bus health probe (T-050-03) | `Azure.Messaging.ServiceBus` **7.21.0** in wa.infrastructure (`ServiceBusClient` + `$management` receiver peek) | TA-2.6 pins this package for wa.infrastructure; 7.21.0 is the current major on nuget.org (the "12.x" pin in TA-2.6 predates the Azure SDK versioning shift — 7.x is the line the SDK ships). Peek on `$management` verifies auth + connectivity without requiring a real queue | AI (T-050-03) |
| 2026-10-08 | ADR — Health failure logging (T-050-04) | `Microsoft.Extensions.Logging.Abstractions` **10.0.12** in wa.application (`ILogger<HealthQueryHandler>`; console via ASP.NET Core's built-in provider — Serilog lands in T-050-05) | The handler must log the failure reason (AC-050-3); abstractions-only keeps the layer rule clean (no full `Microsoft.Extensions.Logging` runtime in a use-case project). Version matches the 10.0.12 already pinned by wa.api's ASP.NET Core stack | AI (T-050-04) |
| 2026-10-09 | ADR — Serilog console logging (T-050-05) | `Serilog.AspNetCore` **10.0.0** + `Serilog.Sinks.Console` **6.1.1** in wa.api (`builder.Host.UseSerilog` + `UseSerilogRequestLogging`) | TA-2.6 allows Serilog for M0 tooling; console sink only locally (App Insights sink later per environment, TA-10.1). Levels per TA-10.5: Information default, `Microsoft.*` → Warning — request lines come from the Serilog middleware since Microsoft request logging is suppressed to Warning | AI (T-050-05) |
| 2026-10-12 | ADR — DomainEvents outbox table (T-054-02) | Migration `20261012164301_DomainEvents`: `dbo.DomainEvents` (EventId UNIQUE PK = envelope EventId / SB MessageId, EventType VARCHAR(64), EnvelopeJson NVARCHAR(MAX) — exact TA-5.2 serialized envelope as re-publish source of truth, CorrelationId, PartitionKey, OccurredAtUtc, IsPublished BIT, CreatedAtUtc) | Not one of TA-3.2's 15 tables → golden rule 4: new migration + this ADR row. Replaces the stale `EventOutbox` entry above (migration 20260901170311 does not exist in the current tree — superseded, no code references it). EnvelopeJson kept verbatim so re-publish of an unpublished row is byte-exact TA-5.2 | AI (T-054-02) |
| 2026-10-13 | ADR — Rate limiting (T-056-03) | `AspNetCoreRateLimit` **5.0.0** in wa.api (`AddInMemoryRateLimiting`, general rule `*:/api/v1/auth/*` = 5/min per IP → 429 Problem+JSON via the TA-4.1.3 closed list) | TA-9.5 second line: in-memory store is correct at M0 scale — per-instance counters, no distributed state; a distributed store would be an ADR change when that requirement appears. Transitive deps pulled in: `Newtonsoft.Json` 13.0.2 + `Microsoft.Extensions.{Caching.Abstractions,Logging.Abstractions,Options}` 6.x (nuspec of the frozen package) | AI (T-056-03) |
| 2026-10-14 | ADR — `jsdom` web test environment (T-001-03) | `jsdom` **30.1.2** as wa.web devDependency (`// @vitest-environment jsdom` per file; client-mount render via `createRoot` + `act`) | The 2026-08-31 row already lists jsdom in the planned test tooling but it was never installed — T-001-03's first store-driven component needs a DOM: `renderToStaticMarkup` captures the Zustand snapshot before commit (store updates between render passes are invisible to SSR), so StagingList tests mount for real. Test-only devDependency, no runtime deps | AI (T-001-03) |
| 2026-10-15 | ADR — `@azure/storage-blob` exact pin (T-001-07) | `@azure/storage-blob` **12.24.0** in wa.web (exact, not `^`) | Azurite 3.37.0 rejects API versions > `2024-08-04` with 400 InvalidHeaderValue; the JS SDK hardcodes `x-ms-version` as an isConstant spec parameter — a ctor `{ version }` option never touches the wire header (proven: both constructions emit 2024-08-04). 12.25.0 jumped SERVICE_VERSION to `2024-11-04`; 12.24.0 is the highest version at or under the Azurite ceiling. Exact pin because a caret range re-crosses the boundary on any patch release | AI (T-001-07) |
| 2026-10-15 | ADR — Playwright E2E tooling (T-001-09) | `@playwright/test` **1.64.1** as wa.web devDependency (`e2e/` dir + `playwright.config.ts`, Chromium headless shell v1248, one serial worker against the local stack) | AC-001-1…4 need a real browser leg: in-browser SDK transport (Blob bodies), Azurite CORS set at runtime by global-setup, and console-error capture proving the x-ms-version downgrade landed. Test-only devDependency; no webServer key — the stack is supervised externally per §4 gate | AI (T-001-09) |
| 2026-10-16 | ADR — Growth telemetry (T-003-07) | Client-side M0 sink for `growth_cta_shown` / `growth_cta_clicked` with source dimension (`post-download` vs `terminal-state`): new events not in TA-10.2's frozen list yet (TA-10.2 to be updated at F-TRF-012), emitted via feature-local record + console.debug (UploadEngine's M0 client-sink convention); no AI pipeline at M0 | FR-003-7 / US-003-07 growth loop: both CTAs need to feed the funnel dashboard. No runtime deps added; sink is test-overridable module state, formalized by F-TRF-012 which updates TA-10.2 with these two event names | AI (T-003-07) |
| 2026-10-19 | T-003-08 sticky-bar scope — terminal-state "Send something" excluded from mobile sticky bar | FR-003-10 + exit check name only Download/Download all; US-003-05 owns terminal screens, whose "sticky CTA 'Send something'" line in US-003-06 alt-flows is deferred — revisit at F-TRF-017 if the story text holds | FR-003-10/FR-017-5 scope; terminal screens keep the centered anchor (T-003-08) | supervisor ruling (T-003-08 plan) |
| 2026-10-20 | T-003-09 SQL seed flip | `mssql` **^7.3.5** as wa.web devDependency (e2e/ only): direct UPDATEs to Status=2 (Expired) / Status=3 (DownloadLimit) — no prod code sets those states yet (T-018 owns expiry); CJS interop via `createRequire` because e2e/ sits outside tsc -b (same convention as global-setup.ts) | Test-only devDependency; terminal-state E2E (AC-003-3/5) needs the raw UPDATE since the API cannot reach those states | AI (T-003-09 plan), ADR row per AGENT.md §7 |
| 2026-10-20 | T-003-09 e2e local-stack-only — no new CI job (D11) | recipient.spec.ts runs serially against the supervised local stack (workers: 1, fullyParallel: false — playwright.config.ts), like T-001-09's staging.spec.ts; "pass in CI" satisfied via the serial gate + documented run command | D11: no dedicated e2e CI job at M0. `transfer_not_found` proven at unit level (GetPublicTransferHandlerTests) + wire-level 404/410 body parity (AC-003-4); no stdout capture at M0 | AI (T-003-09 plan), ADR row per AGENT.md §7 |
| 2026-10-20 | T-003-09 StrictMode removed (dev-only) | `<StrictMode>` dropped from src/wa.web/src/main.tsx as a DEV-ONLY workaround | Singleton WaDbContext concurrency bug pre-existing since T-002-02 (Program.cs registers the context as singleton — no request scope in the current API surface); integration tests already isolate via xunit.runner.json + per-test app boot. StrictMode's dev-mode double-mount fires two overlapping GET /api/v1/public/transfers/{linkId} requests → EF concurrency 500 ("A second operation was started on this context instance") exposed in e2e. React strips StrictMode in production builds (no-op), so the built bundle and prod behavior are unchanged; restore once DI is scoped | AI (T-003-09), supervisor ruling 2026-10-20 |
| 2026-10-20 | T-003-08 sticky bar — `position: fixed` supersedes D1's in-flow `sticky` | FR-003-10 says "sticky at the bottom of **the viewport**" + "page scrolls under it": with in-flow `position: sticky; bottom: 0` the bar pins only when `.rp` overflows the viewport (probed: 2 small files → docH == 780 == viewport → bar sits mid-page at y≈423; 15 rows → overflows → pins exactly 780). `fixed; left/right/bottom: 0` pins to the exact viewport bottom for both short and long content; `.rp { padding-bottom: 96px }` still reserves the no-overlap gap | FR-003-10 exit check "sticky download button visible at the bottom"; Playwright probes (n=2 / n=15) against local stack | supervisor ruling 2026-10-20 |
| 2026-10-20 | T-003-08 recipient download — client-side re-address + protocol re-sign of the minted read SAS (local Azurite) | RecipientStore navigated the RAW minted URL; against IP-hosted Azurite that is a 404 (bare host drops `/devstoreaccount1`; `spr=https` → AuthorizationProtocolMismatch on same signature). Mirrors StagingList's T-055-05/TA-14.3 convention for uploads: re-address to canonical `/blob/devstoreaccount1{path}`, protocol line "https,http", HMAC-SHA256 over the 16-line blob-SAS string-to-sign (sp/st/se/sr + rscd carried over). Unconditional local-dev convention, same as staging; production host-style URL would be unchanged in a real deployment | T-055-05/TA-14.3 convention; StagingList precedent (T-001-09) | supervisor ruling 2026-10-20 |
| 2026-10-20 | T-003-09-fix — Vitest `test.alias` `'buffer': 'node:buffer'` (mssql polyfill shadows Node builtin) | mssql devDependency (T-003-09 D4 seed flip) pulls buffer@6.0.3 via @tediousjs/connection-string, hoisted to top-level node_modules — it shadows Node's builtin "buffer" under Vitest: PooledBuffer.js reads `buffer.constants.MAX_LENGTH` at module scope → TypeError at collection of every jsdom suite importing the real BlockBlobClient (proven: polyfill v6 lacks `.constants`; lockfile diff a092b51 adds `node_modules/buffer`) | Test-scoped alias in vite.config.ts `test.alias` only — Vite build + Playwright unaffected. 3 suites restored to collection; suite scale back to 142 tests | AI (T-003-08-fix gate), supervisor ruling 2026-10-20 |
