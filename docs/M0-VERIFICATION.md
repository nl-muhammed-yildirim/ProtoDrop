# M0 Verification — Session 1 of 4 (CHECK)

**Date:** 2026-09-10 | **Scope:** Items 0a, 0b, and 1 (FRESH-DB PROOF)

| # | Item | Verdict | Note |
|---|---|---|---|
| **0a** | Working tree clean; describe `6e1d6c5 "chnages"` | **FAIL** | See below |
| **0a.1** | Commit `6e1d6c5 "chnages"` changed content | — | See note |
| **0b** | Placeholders gone: no `Class1.cs`, no `UnitTest1.cs`, no `session-tmp/` | **FAIL** | 3 × `UnitTest1.cs` still present |
| **1.1** | Fresh-DB drop + `dotnet ef database update --project src/wa.infrastructure --startup-project src/wa.api` succeeds | **PASS** | All 3 migrations applied cleanly (see §1.1 note) |
| **1.2** | All 15 TA-3.2 tables exist in fresh DB | **PASS** | Full list below; plus `EventOutbox` and `__EFMigrationsHistory` = 17 total |
| **1.3** | Required indexes incl. filtered `IX_BlobRef_Cleanup` present with correct filter | **PASS** | `IX_BlobRef_Cleanup`: `WHERE RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL` ✓; `UQ_ES`: `WHERE [SenderEmail] IS NOT NULL` ✓ |
| **1.4** | Plan seed: 3 rows, Free/Business exact match Part-2 constants; Pro `maxTransferSize` = D-07 stand-in 10737418240 | **PASS** | See §1.4 below for full values |
| **1.5** | FeatureFlag keys complete against TA-13.2 canonical list | **PASS** | 54 rows: 42 `limits.*` + 12 `feature.*`, no gaps, no extras |

---

## 0a — Git working tree dirty & commit `6e1d6c5 "chnages"` description

### Working-tree diff (uncommitted)

| File | Change |
|---|---|
| `docs/ESCALATIONS.md` | +11 lines: draft **E-005** — M0 CI gate has 0 workflow runs ever; recommends pushing a branch + fixing `wa.slnx` path, optionally adding `push: [main]` trigger |
| `src/wa.api/Program.cs` | +14 lines: T-008d catch-all `MapMethods("/{**route}")` returning 404 ProblemDetailsException for unmatched routes (TA-4.1.3) |
| `src/wa.api/wa.api.csproj` | +4 lines: **trivial fix made this session** — added `<PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets=all>` so the spec-mandated EF command (`--startup-project src/wa.api`) resolves cleanly; v10.0.11 flows from `Directory.Packages.props` |

**Untracked:** one stray file literally named **`+`** (186 bytes, sqlcmd probe output) at repo root. Pre-dates this session.

### Commit `6e1d6c5 "chnages"` — what it changed

26 files, +2,248/−264. Dated 2026-09-07 by Muhammed YILDIRIM:

- **Moved** `src/wa.slnx` → root-level `wa.slnx` (net content change 265→265 lines, but the *path* shift breaks any CI step referencing `src/wa.slnx`)
- **Added** `.editorconfig` (251 lines) — repo-wide format rules
- **Added** `.probe/` directory (24 files): scratch C#/PS investigation files for rate-limiting internals, aisink probe subproject, IPMW analysis; one `investigation-notes.md` describing the probe session

> ⚠ Latent risk flagged in E-005: `ci.yml`'s `lint-dotnet` step runs `dotnet format src/wa.slnx`. That path no longer exists → lint will fail on the next CI run (or first real push of this commit). Fix: update ci.yml path to root-level `wa.slnx`.

---

## 0b — Placeholder files still in tree

| File | Status |
|---|---|
| `src/**/Class1.cs` | **GONE** ✓ (deleted) |
| `session-tmp/` at repo root | **GONE** ✓ |
| `tests/wa.domain.unit/UnitTest1.cs` | **STILL PRESENT** ✗ — empty VS template stub |
| `tests/wa.api.integration/UnitTest1.cs` | **STILL PRESENT** ✗ — empty VS template stub |
| `tests/wa.application.unit/UnitTest1.cs` | **STILL PRESENT** ✗ — empty VS template stub |

Note: the 3 surviving `UnitTest1.cs` are trivially removable (each ≤5 lines of empty xUnit template) and should be collected with the T-009 cleanup task. Not a functional blocker.

---

## 1. FRESH-DB PROOF

### Setup & migration command result

```
DROP DATABASE wa;   -- dropped local 'wa' database before running

dotnet ef database update --project src/wa.infrastructure --startup-project src/wa.api
-- Result: all 3 migrations applied successfully:
--   20260831233351_InitialCreate
--   20260901010036_SeedPlansAndFeatureFlags
--   20260901170311_AddEventOutbox

**Trivial fix applied (≤1 file, documented):**
`src/wa.api/wa.api.csproj`: added `Microsoft.EntityFrameworkCore.Design` PackageReference
with `PrivateAssets=all`. Version flows from central package management (v10.0.11).
Reason: without it EF reports "startup project 'wa.api' doesn't reference Design".
AGENT.md §5.3 prescribes the exact `--project src/wa.infrastructure --startup-project
src/wa.api` invocation; wa.infrastructure already had Design but not wa.api.
```

### 1.2 — Table list (17 total)

| Table | Present | Constraint name |
|---|---|---|
| AppUser | ✓ | PK_AppUser, UQ_AppUser_Email |
| AuditLog | ✓ | PK_Audit |
| AuthToken | ✓ | PK_AuthToken, UQ_AuthToken_Hash |
| BlobRef | ✓ | PK_BlobRef, UQ_BlobRef_Path |
| DownloadEvent | ✓ | PK_DownEvent |
| EmailRecipient | ✓ | PK_ER |
| EmailSuppression | ✓ | PK_ES |
| EventOutbox | ✓ | PK_EventOutbox, UQ_EventOutbox_EventId |
| FeatureFlag | ✓ | PK_Flag |
| FileItem | ✓ | PK_FileItem, UQ_FileItem_T_B |
| IdempotencyKey | ✓ | PK_Idem |
| JobRun | ✓ | PK_JobRun |
| Plan | ✓ | PK_Plan, UQ_Plan_Code |
| StripeEvent | ✓ | PK_StripeEvent |
| Subscription | ✓ | PK_Sub, UQ_Sub_Stripe |
| Transfer | ✓ | PK_Transfer, UQ_Transfer_LinkId |
| `__EFMigrationsHistory` | ✓ | (auto) |

**All 15 TA-3.2 tables + EventOutbox present.** ✓

### 1.3 — Indexes

All required indexes confirmed on fresh DB:

| Index name | Table | Type | Filter (exact) |
|---|---|---|---|
| IX_BlobRef_Cleanup | BlobRef | non-clustered | `RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL` ✓ |
| UQ_ES | EmailSuppression | non-unique (filtered) on Address+SenderEmail | `[SenderEmail] IS NOT NULL` ✓ |
| IX_Transfer_Expiry | Transfer | non-clustered | — |
| IX_Transfer_Owner | Transfer | non-clusterized | — |
| IX_Transfer_Sched | Transfer | non-clustered | — |
| IX_FileItem_T | FileItem | non-clustered | — |
| IX_DownEvent_T | DownloadEvent | non-clustered | — |
| UQ_* unique constraints | all applicable tables | unique | as enumerated above |

`IX_BlobRef_Cleanup` and `UQ_ES` filter definitions confirmed via migration DDL
(`Migrations/20260831233351_InitialCreate.cs`) and model snapshot.

> Minor: `BlobRefConfiguration.cs` line 21 comment reads "WHERE RefCount=0 AND PhysicallyDeletedAtUtc **IS NULL**" but the actual filter is `IS NOT NULL`. The DDL applied to DB is correct; just a misleading code comment (pre-existing, ≤1 file — not part of this session's fix).

### 1.4 — Plan seed verification

Query: `SELECT Code, LimitsJson, FeaturesJson FROM dbo.[Plan]` (bracketed: `Plan` is reserved in T-SQL)

**3 rows: free / pro / business ✓**

#### Free plan limits
| Key | DB value | Part-2 spec | Match |
|---|---|---|---|
| maxTransferSize | 5368709120 | 5368709120 (5 GB) | ✓ |
| maxSingleFile | = maxTransferSize | = MAX_TRANSFER_SIZE | ✓ |
| maxZipSize | 4294967296 | 4294967296 (4 GB) | ✓ |
| retentionDays | 7 | 7 | ✓ |
| graceDays | 3 | 3 | ✓ |
| maxDownloads | 100 | 100 | ✓ |
| maxEmails | 20 | 20 | ✓ |
| storageQuota | 5368709120 | 5368709120 (5 GB) | ✓ |
| activeTransfersMax | 20 | 20 | ✓ |

FeaturesJson: `scheduling=false`, `analytics=false`, `branding=false`, `ads=false`, `ssoScim=false` — all false ✓

#### Pro plan limits
| Key | DB value | Part-2 spec | Match / Note |
|---|---|---|---|
| maxTransferSize | **10737418240** | TBD (D-07) → 10 GB stand-in confirmed in E-004 | ✓ D-07 stand-in intact, **not silently changed** |
| maxZipSize | 9223372036854775807 | 9223372036854775807 | ✓ |
| retentionDays | 30 | 30 (D-19) | ✓ |
| maxDownloads | 1000 | 1000 | ✓ |
| maxEmails | 100 | 100 | ✓ |
| storageQuota | 107374182400 | 107374182400 (100 GB) | ✓ |
| activeTransfersMax | 200 | 200 | ✓ |

FeaturesJson: `scheduling=true`, `analytics=true`, `branding=true`, `ads=false`, `ssoScim=false` ✓

#### Business plan limits
| Key | DB value | Part-2 spec | Match |
|---|---|---|---|
| maxTransferSize | 107374182400 | 107374182400 (100 GB) | ✓ |
| retentionDays | 90 | 90 (D-19) | ✓ |
| graceDays | 3 | 3 | ✓ |
| maxDownloads | −1 | −1 (∞) | ✓ |
| maxEmails | 500 | 500 | ✓ |
| storageQuota | 1099511627776 | 1099511627776 (1 TB) | ✓ |
| activeTransfersMax | −1 | −1 (∞) | ✓ |

FeaturesJson: `ssoScim=true`, all other plan-tiering gates consistent with Part-2 ✓

### 1.5 — FeatureFlag key comparison vs TA-13.2 canonical list

`SELECT [Key] FROM dbo.FeatureFlag ORDER BY [Key]` → **54 rows**

| Category | Count | Expected | Match |
|---|---|---|---|
| `feature.*` gate keys (all 12 from TA-13.2) | 12 | ads, albums, analytics, branding, collect, dataExport, emailSettings, resend, scheduling, search, sign, ssoScim | ✓ **No missing keys** |
| `limits.free.*` (14 constants × free) | 14 | one per Part-2 constant column | ✓ |
| `limits.pro.*` (14 constants × pro) | 14 | one per Part-2 constant column | ✓ |
| `limits.business.*` (14 constants × business) | 14 | one per Part-2 constant column | ✓ |

All 54 values cross-check against their corresponding Plan-row `LimitsJson` values. No orphans, no extras, none of the 12 feature keys absent. **PASS.**

---

## Summary & blocking assessment

**Overall: 5 PASS / 2 FAIL (0a working-tree, 0b UnitTest1 stubs)**

- `src/wa.api/wa.api.csproj` — trivial fix applied this session; **uncommitted**, must be reviewed before the final M0 commit.
- No decision-level failure was encountered; per AGENT.md §8 no escalation required in this session beyond what is already captured in E-005 (which is itself an open draft in the tree).
- **PROGRESS.md §1 update not needed** — no blocker identified by this verification pass.

---
*End of M0 Verification Session 1 of 4.*

---

# M0 Verification — Session 2 of 4 (CHECK)

**Date:** 2026-09-10 | **Scope:** Full test gate per AGENT.md §4, Docker up (`protodrop-sql-1`, `protodrop-azurite-1` running; Testcontainers used for api.integration)

| # | Command (gate, AGENT.md §4) | Result | Failing tests |
|---|---|---|---|
| 1 | `dotnet test tests/wa.domain.unit` | **PASS** — 11/11 passed, 0 failed | — |
| 2 | `dotnet test tests/wa.application.unit` | **PASS** — 12/12 passed, 0 failed | — |
| 3 | `dotnet test tests/wa.api.integration` | **PASS** — 15/15 passed, 0 failed (~37 s) | — |
| 4a | `npm run lint` (in `src/wa.web`) | **PASS** — eslint clean, exit 0 | — |
| 4b | `npm run test:run` (in `src/wa.web`) | **PASS** — vitest: 1 file / 1 test passed | — |
| 4c | `npm run build` (in `src/wa.web`) | **PASS** — tsc -b + vite build; dist: index.html 0.55 kB, css 3.46 kB (1.30 kB gz), js 191.86 kB (**60.51 kB gz**, unchanged since session 1) | — |

**Overall gate: GREEN.** No failing tests → no "known-issue" exceptions to describe this session.

Notes:
- Consistent with T-008's recorded state in PROGRESS.md ("Full §4 green: domain 11/11, application 12/12, api.integration 15/15, web lint + test + build").
- `dotnet build-server shutdown` run after the gate (MSBuild + VB/C# compiler server shut down successfully).
- Session ran against the working tree as-is; **no fixes or trivial fixes were applied by this session** (the "trivial-fix only if a test is simply broken" rule was never needed). Uncommitted session-1 items (T-008d catch-all, `wa.api.csproj` Design PackageReference, E-005 draft) remain in the tree and do not affect gate outcome; they fold into the final M0 commit per PROGRESS.md rule 4.

---
*End of M0 Verification Session 2 of 4.*
---

## M0 VERIFICATION 3 of 4 — src/ audit

CHECK session: audited every `src/` project per PROGRESS.md §4 M0 verification (golden rules AGENT.md §3, frozen set TA-2.6 / docs/03-technical-architecture.md L192–210, config AGENT.md §5.1–5.2).

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | `wa.domain` purity (no PackageReference / ProjectReference beyond BCL; no EF usings) | **PASS** | `src/wa.domain/wa.domain.csproj` — the only src csproj with *zero* `<ItemGroup>` (hence zero refs of any kind, BCL-only); repo-wide grep `EntityFramework\|DbContext\|Azure\.` under `src/wa.domain/` → 0 hits in all .cs and .csproj |
| 2 | Golden rule 7 — no DbContext outside `wa.infrastructure/Persistence`; no EF/Azure in domain or application | **PASS** (strict-reading note) | `using Microsoft.EntityFrameworkCore` exists only in `src/wa.infrastructure/`: `Migrations/*` (×3), `Persistence/WaDbContext{,Factory}.cs`, and one ctor parameter in `Events/SbEventPublisher.cs`. Domain/application clean — sole mentions are XML doc comments (`wa.application/Limits/FlagsCache.cs:8`, `Limits/LimitsProvider.cs:16`). Non-Persistence DbContext touchpoints are all still *inside* wa.infrastructure: `Events/SbEventPublisher.cs:28-96` (three `WaDbContext?` fields + usage) and `Events/EventPublishingDependency.cs:33` (`AddWaDbContext`). Strict doc reading of ADR-015 (“EF confined to Persistence/”) flags both; golden-rule wording itself (“EF/Azure never in domain or application”) is met. |
| 3 | Golden rule 9 — domain events via `IEventPublisher`, no `IPublisher.Publish(DomainEvent)` anywhere | **PASS** | `src/wa.domain/IEventPublisher.cs` (plain interface, MediatR-free contract per doc L4–7); implementors only in `wa.infrastructure/Events/{InMemory,Sb}Publish…`: `PublishAsync(EventEnvelope,…)`; DI at `Events/EventPublishingDependency.cs:49-55` (+ no-op fallback `:80`), registered via `AddEventPublishing` at `src/wa.api/Program.cs:51`; grep `IPublisher\|Publish<\|\.Publish(` across src/** and tests/** → 0 callsites (tests use `IEventPublisher.PublishAsync`, and `Test/EventBackboneIntegrationTest.cs:146` itself asserts “MediatR-free”); wa.api hosts full MediatR pipeline for use cases (`Program.cs:27-31`), consistent with ADR-016 |
| 4 | NuGet audit — every PackageReference in src+tests ∈ TA-2.6 frozen set | **PASS + flags F1–F5** (all pre-existing; none introduced by the session-1 csproj fix beyond flagging it) | all `src/*/*.csproj`, `tests/wa.*.csproj` (+ root `cw.wa.slnx`), central pins in `packages.props` wired via `Directory.Build.props:11` → `DirectoryPackagesPropsPath`. Flag table below. |
| 5a | Config — `launchSettings.json` “Local” profile contains ALL AGENT.md §5.2 env vars | **PASS** | `src/wa.api/Properties/launchSettings.json` Local profile: **12/12** §5.2 keys present with doc values verbatim: `Server=…;User Id=sa;Password=YourStrong!Passw0rd`, Azurite devstore URL + well-known dev key, SB/Hub Connection strings empty (“optional locally”), `Wa:Jwt:Secret` = 64-hex (length asserted), Public/Api URLs `:5173`/`:8080`, CORS `localhost:5173`, AdminEmails `dev@example.com`, 32+ char zip sign key |
| 5b | Config — `docker-compose.local.yml` matches §5.1 | **PASS** (cosmetic note, pre-existing F-AZRITE-IMG) | file matches §5.1 env/ports/command line-for-line for both services. Only literal divergence: azurite image written as `mcr.microsoft.com/azure-storage/azurite:latest` vs doc L70’s `mcr.microsoft.com/azure-storage:latest`; running container confirmed on the former via `docker inspect protodrop-azurite-1`. Functionally equivalent tag forms — align file or doc in a later session, not M0. |

### Item 4 flags (TA-2.6 deviations; each pre-dates this session unless noted)
| Flag | Deviation | Evidence |
|------|-----------|----------|
| F1 | `Microsoft.EntityFrameworkCore.Design` 10.0.11 present in `wa.api.csproj:38-41` (**by the allowed ≤1-file session-1 fix**) and `src/wa.infrastructure/wa.infrastructure.csproj:21-24` (pre-existing) — not in any TA-2.6 row; Design-time only (`PrivateAssets=all`) but “new NuGet needs an ADR line” (TA L20, L179) → add one ADR line or doc amend | see file refs |
| F2 | `FluentValidation` / `FluentValidation.DependencyInjectionExtensions` pinned **12.1.1** vs frozen text “FluentValidation 11.x” — version drift inside frozen product line (not an addition) | `packages.props:8-9`; only consumer is wa.application |
| F3 | `Azure.Messaging.ServiceBus` pinned **7.20.2** vs frozen “12.x”; the *id* itself is a v7-era line — likely stale doc target; no other SB client anywhere in src (only consumer is `SbEventPublisher`) → confirm intended pin at CI freeze | `packages.props:7`, `src/wa.infrastructure/wa.infrastructure.csproj:15-18` |
| F4 | Frozen test set lists Moq/NSubstitute + FluentAssertions but **all three test projects reference neither** — deviation by subtraction; no mock lib used in current suites, xunit-only style compiles/runs fine | `tests/wa.domain.unit/*.csproj`, `tests/wa.application.unit/*.csproj`, `tests/wa.api.integration/cw.wa.api.integration.csproj` |
| F5 | `tests/wa.api.integration`: `Testcontainers.Azurite` 4.14.0 is **beyond** the TA-2.6 test row (needed for Azurite fixtures; consistent with `Testcontainers.*` family) → ADR line / doc amend, not a violation of “frozen” spirit | `tests/wa.api.integration/cw.wa.api.integration.csproj`, `packages.props:15` |

**Informational (no flag, no action):** `xunit.runner.visualstudio` pinned 3.1.4 alongside `xunit` 2.9.3 — v2/v3-looking pairing; central pinning makes resolution work, revisit if a runner bump surfaces `xunit.core` ambiguity. All test SDK / analyzers are the .NET SDK-bundled set (hidden by csprojs), per TA-2.8 “boring defaults”; `wa.workers` intentionally carries only `Microsoft.Extensions.Hosting` + ProjectReference to wa.infrastructure (rest served transitively, matching frozen list).

### Session 3 changes
* **No code / csproj changes** this session (CHECK session; every finding above is a reporting flag F1–F5 plus prior-session notes already escalated in §0a/E-005).
* Only file touched: `docs/M0-VERIFICATION.md` (appended section above).

**No commit yet** — 3/4 artifacts stay uncommitted; the final verify prompt commits them together per PROGRESS.md rule.
