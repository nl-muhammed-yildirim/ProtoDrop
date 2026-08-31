# Technical Architecture Plan — ProtoDrop File Transfer Platform

**Version 0.2** | **Last updated:** 2026-08-23
**Companions:** Product Analysis v0.2, Feature Plan v0.2
**Stack:** .NET 10 (Minimal API) · **Clean (onion) architecture** · **CQRS via MediatR 12.5** · Azure · React 18 + TypeScript + Vite · **Development environment: Visual Studio 2026**

---

## TA-0 — How to Use This Document with AI

### TA-0.1 Section reference convention
Cite sections by ID in prompts: *"The expiry job is specified in TA-6.3; implement it in `wa.workers`."* IDs are stable — do not renumber when editing.

### TA-0.2 Golden rules (apply to every implementation prompt)

1. **No new NuGet/npm package** without adding a line to the ADR table (TA-17).
2. **No literal limit values** in code — always read from the Limits Registry (TA-3.4).
3. **Every background event** uses the envelope in TA-5.2 and one of the event types in TA-5.3.
4. **Every DB schema change** is an EF Core migration in `wa.infrastructure/Persistence` — never manual SQL in prod (TA-3.7).
5. **Error responses** always use the Problem+JSON shape in TA-4.1.3.
6. **Telemetry** uses only event/metric names from TA-10.2.
7. Dependency rule: `wa.domain → wa.application → (wa.infrastructure ← wa.api, wa.workers)`. The domain must never reference EF or Azure.
8. **CQRS via MediatR 12.5:** one `IRequest<T>` + handler per use case in `wa.application`; no `DbContext` in domain or use cases (TA-2.4).
9. Domain events are **MediatR-free** — they go through `IEventPublisher` (outbox → Service Bus).

### TA-0.3 Prompt template

```text
You are implementing {FEATURE_ID} on our ProtoDrop platform.
ARCHITECTURE: see Technical Architecture Plan v0.2, sections {TA_SECTIONS}.
REPO LAYOUT: follow TA-2 exactly. DEPENDENCY RULE: TA-0.2(7). CQRS: TA-0.2(8).
Implement {SCOPE}. Satisfy {AC_IDS}. Handle {EC_IDS}.
Emit telemetry exactly per TA-10.2. Add tests per TA-14.
Definition of Done: Feature Plan §0.3.
IDE: Visual Studio 2026, .NET 10 SDK via global.json.
```

---

## TA-1 — System Overview

### TA-1.1 Component diagram

```
                    ┌────────────────────────────────────────────────────────────┐
                    │                        azuredomain.com                      │
  Browser (guest)   │                                                             │
  ───────────┐      ▼                                                              │
  Drag&drop   │  www.example.com ──► Front Door (CDN/WAF) ──► SWA (React SPA)     │
  Files       │        api.example.com ──┐                                        │
              │                          ▼                                        │
              │                   wa-api (Container App, .NET 10)                │
              │                          │        │        │                      │
              │                          ▼        │        ▼                      │
              │            Azure SQL (VNet)       │   Service Bus (topic: core)   │
              │                          │       │         │          ▲           │
              │                          ▼       │         ▼          │           │
              │            Blob Storage ◄────────┼────── wa-workers  │           │
              │            (staging + transfers) │   (Functions:     │           │
              │            via short-lived SAS   │    email, expire, │           │
              │                                 │    delete, zip)   │           │
              │   Stripe ◄── webhooks ──────────┼───────────────────┘           │
              │   Communication Hub (email) ◄───┘                               │
              └───────────────────────────────────────────────────────────────────┘
```

### TA-1.2 Component inventory

| Component | Tech | Scales by | Responsibility (and ONLY that) |
|---|---|---|---|
| `wa.web` | React 18 + Vite, Azure Static Web Apps | static | All UI. Talks to `wa-api` + Blob SAS directly. |
| `wa-api` | .NET 10 Minimal API, Container App | CPU 60%, min 1 / max 10 | All HTTP endpoints, MediatR use cases, DB writes, SAS minting, event publishing. **Never proxies file bytes.** |
| `wa-workers` | Azure Functions (Flex Consumption) | per-function | Async: email, expiry, deletion, zip. No public HTTP except zip. |
| `wa-sql` | Azure SQL Database (B-Server, VNet + PIP) | vCores | System of record (TA-3). |
| `wa-blob` | Azure Blob Storage (LRS → GRS in prod) | n/a | File bytes: `staging`, `transfers` containers. |
| `wa-sb` | Azure Service Bus (Standard) | n/a | Durable async decoupling (TA-5). |
| `wa-ch` | Azure Communication Hub (Email) | n/a | All transactional email. |
| `wa-fd` | Front Door (Standard, WAF) | n/a | Routing, WAF rate limits, TLS, CDN for web assets. |
| `wa-ai` | Application Insights | n/a | Logs/metrics/traces/alerts (TA-10). |
| `wa-kv` | Key Vault | n/a | All secrets (TA-9.5). |
| `wa-acr` | Container Registry (Basic) | n/a | Container images. |
| Stripe | SaaS | n/a | Billing (P1). |

### TA-1.3 Traffic flows (summary; details in TA-7)

- **Upload:** browser → Blob SAS (bytes), `wa-api` (metadata only).
- **Download:** browser ← Blob SAS (bytes), `wa-api` (mint URLs).
- **Async:** `wa-api` → Service Bus → `wa-workers`.
- **File bytes NEVER flow through `wa-api`.** This is the single most important cost/latency rule.

### TA-1.4 Environments

| | dev | staging | prod |
|---|---|---|---|
| Subdomain | `*.dev.example.com` | `*.staging.example.com` | `example.com`, `www.`, `api.` |
| SQL | B-Server 1 vCore | B-Server 2 vCores | B-Server Auto 4 vCores |
| Container App | min 1 / max 2 | min 1 / max 4 | min 1 / max 10 |
| Functions | 1 | 1 | 1 (Flex Consumption, scale per function) |
| TLS | Front Door auto-cert | Front Door auto-cert | Custom domain + cert |
| Data | seeded test data | prod-shaped synthetic | live |

---

## TA-2 — Repository Structure (monorepo)

### TA-2.1 Tree

```
/
├─ src/
│  ├─ wa.domain/                     # NO external deps (no EF, no Azure SDK, no MediatR)
│  │  ├─ Entities/                   # Transfer, FileItem, BlobRef, AppUser, Plan, ...
│  │  ├─ ValueObjects/               # LinkId, EmailAddress, ...
│  │  └─ Domain/                     # domain logic: limits resolution, refcounting, expiry math
│  ├─ wa.application/                # references: wa.domain
│  │  ├─ UseCases/{Feature}/         # MediatR IRequest+handler per use case (CQRS)
│  │  │  ├─ Transfers/               # CreateDraft, FinalizeTransfer, SendTransfer, Resend, ...
│  │  │  ├─ Auth/                    # Signup, Login, RedeemMagicLink, ForgotPassword, ...
│  │  │  ├─ Admin/                   # GetOverview, SearchTransfers, SetFlag, ...
│  │  │  └─ Billing/                 # (P1) CreateCheckoutSession, HandleStripeWebhook, ...
│  │  ├─ Ports/                      # abstractions (outward-facing): ITransferRepository,
│  │  │                              # IBlobStore, ILimitsProvider, IEventPublisher, IEmailSender...
│  │  └─ Common/                     # Result, paging, validation behaviors (FluentValidation)
│  ├─ wa.infrastructure/             # references: wa.application
│  │  ├─ Persistence/                # WaDbContext, EF mappings, Migrations (EF's only home)
│  │  ├─ Blob/                       # BlobStoreAdapter, SAS minting
│  │  ├─ Events/                     # SbEventPublisher, Outbox
│  │  └─ Email/                      # CommunicationHubEmailSender
│  ├─ wa.api/                        # references: wa.infrastructure, wa.application
│  │  ├─ Endpoints/                  # TransferEndpoints.cs, AuthEndpoints.cs, AdminEndpoints.cs
│  │  ├─ Pipeline/                   # correlation, error mapping (Problem+JSON), validation
│  │  ├─ Caches/                     # PlansCache, FlagsCache (30 s TTL)
│  │  └─ Program.cs                  # host wiring: AddMediatR, DI, Serilog, CORS, rate limit
│  ├─ wa.workers/                    # references: wa.infrastructure (own host for jobs)
│     ├─ Functions/
│     │  ├─ EmailFunctions.cs
│     │  ├─ ExpiryFunctions.cs
│     │  ├─ DeletionFunctions.cs
│     │  └─ ZipFunctions.cs
│     └─ Adapters/                   # job-specific adapters (ChEmailSender reuse, JobLockStore)
│  └─ wa.web/                        # React SPA, in-solution via package.json (TA-8.2)
├─ infra/
│  ├─ bicep/
│  │  ├─ main/                       # per-env param files: dev.bicep, staging.bicep, prod.bicep
│  │  ├─ modules/{network, sql, storage, servicebus, insights, containerapp, functionapp,
│  │  │           frontdoor, staticwebapp, identity, keyvault}
│  │  └─ tests/                      # bicep tests
│  └─ domains/                       # DNS notes
├─ tests/
│  ├─ wa.domain.unit/                # xUnit — pure domain tests (no host)
│  ├─ wa.application.unit/           # xUnit — use case tests (ports faked)
│  ├─ wa.api.integration/            # WebApplicationFactory + Testcontainers SQL + Azurite
│  └─ e2e/                           # Playwright (runs vs staging)
├─ .github/workflows/{ci.yml, cd-staging.yml, cd-prod.yml}
├─ docs/adr/                         # mirrors TA-17, one file per ADR
└─ wa.sln
```

### TA-2.2 Project dependency rule

`wa.domain` → nothing. `wa.application` → `wa.domain`. `wa.infrastructure` → `wa.application`.
`wa.api` / `wa.workers` → `wa.infrastructure` (composition root). No cycles;
`wa.api` never references `wa.workers`. **EF Core never appears in `wa.domain` or `wa.application`.**

### TA-2.3 Clean (onion) architecture rule (new in v0.2)

- **`wa.domain`** — entities, value objects, invariants. POCOs only; zero external packages.
- **`wa.application`** — use cases (CQRS commands/queries + handlers), ports (interfaces). Depends on domain only.
- **`wa.infrastructure`** — adapters for every port: EF Core + `WaDbContext` (the *only* home of EF), Blob, Service Bus, Communication Hub.
- **`wa.api`** — HTTP edge: endpoint groups, pipeline (correlation, error mapping, validation), DI composition root.
- **`wa.workers`** — background edge: Functions that consume events/timers; owns job-specific adapters.
- **`wa.web`** — React SPA (Vite); in-solution via `package.json` (VS 2026 web project), runs via npm in a terminal (TA-8.2).
- **Dependency inversion:** every port in `wa.application` has exactly one adapter in `wa.infrastructure` (or `wa.workers` for job-local ports); registered at the composition root.
- **EF never leaks into core:** no `IQueryable<T>` in use cases; ports return entities/DTOs; repositories hide EF.

### TA-2.4 CQRS (MediatR 12.5) rule (new in v0.2)

- **One `IRequest<T>` / handler per use case** in `wa.application/UseCases/{Feature}/`. Naming: `{Verb}{Noun}Command` / `{Verb}{Noun}Query` (e.g. `FinalizeTransferCommand`, `GetTransferQuery`).
- **Version frozen at MediatR 12.5** (pinned in CI; upgrade = ADR).
- **Cross-cutting only in pipeline:** validation via a MediatR `Behavior` (FluentValidation), correlation + logging via behaviors; use-case handlers stay thin.
- **Domain events stay MediatR-free:** use cases call `IEventPublisher` (outbox row + publish) instead of `IPublisher.Publish(DomainEvent)`, so `wa.application` never depends on MediatR's eventing.
- **Commands** mutate (and may publish events); **Queries** return DTOs and never mutate (except idempotency-safe counters).
- Thin endpoints: an endpoint group maps HTTP → request → `ISender.Send` → result; no logic in endpoint bodies (TA-4.2a).

### TA-2.5 Development environment (new in v0.2)

- **Visual Studio 2026** is the primary IDE (workloads: ASP.NET and Web Development + .NET Desktop).
- **.NET 10 SDK** pinned via `global.json` (`{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" } }`).
- Local data tier via `docker-compose.local.yml` (SQL Server 2022 + Azurite) — see AGENT.md §5.
- Frontend (`src/wa.web`) is a VS 2026 web project in the .sln (via `package.json`); it runs via npm/Vite in a terminal from within VS.

### TA-2.6 Required packages (pinned in CI; new ones need an ADR)

**wa.domain** (NuGet): *(none — pure POCOs)*

**wa.application** (NuGet): `MediatR` 12.5 · `FluentValidation` 11.x · `FluentValidation.DependencyInjectionExtensions`

**wa.infrastructure** (NuGet): `Microsoft.EntityFrameworkCore.SqlServer` 10.x · `Azure.Storage.Blobs` 12.x · `Azure.Identity` 1.x · `Azure.Messaging.ServiceBus` 12.x · `Azure.Communication.Email` 1.x · `Stubble` (Mustache)

**wa.api** (NuGet): `MediatR` 12.5 · `Serilog.AspNetCore` · `Serilog.Sinks.ApplicationInsights` · `Swashbuckle.AspNetCore` · `AspNetCoreRateLimit`

**wa.workers** (NuGet): `Microsoft.Azure.Functions.Hosting` · `Microsoft.Extensions.Hosting` · `Azure.Storage.Blobs` · `Azure.Messaging.ServiceBus` · `Azure.Communication.Email` · `Stubble` · `Serilog*` (same set)

**Billing (P1):** `Stripe.net` 14.x (wa.api + wa.application ports)

Tests: `xUnit` · `Moq` (or NSubstitute) · `FluentAssertions` · `Testcontainers` · `Testcontainers.MsSql`

**wa-web** (npm)
`react` 18 · `@tanstack/react-query` 5.x · `zustand` 5.x · `i18next` · `react-i18next` · `@azure/storage-blob` 12.x · `tailwindcss` 3.x · `typescript` 5.x · `vite` 5.x · `vitest` · `eslint` · `playwright` (dev)
*Deliberately no* Redux, no UI component kit, no axios (use `fetch` wrapper in `core/api`).

---

## TA-3 — Data Architecture

### TA-3.1 Conventions

- All timestamps `DATETIME2` UTC, suffixed `...AtUtc`.
- IDs: `UNIQUEIDENTIFIER`, generated by **clients** (the API generates IDs before any insert; enables idempotency and event-first ordering).
- Soft-delete only for `AppUser` and `Transfer` (`DeletedAtUtc`); everything else is hard-deleted by jobs.
- Money never stored in DB (Stripe is truth — see TA-7.5).

### TA-3.2 SQL DDL (authoritative; EF migrations must match)

```sql
-- ============ PLANS ============
CREATE TABLE dbo.Plan (
    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Plan PRIMARY KEY,
    Code         VARCHAR(32)  NOT NULL CONSTRAINT UQ_Plan_Code UNIQUE,
    Name         VARCHAR(64)  NOT NULL,
    LimitsJson   NVARCHAR(MAX) NOT NULL,   -- LimitsRecord JSON, see TA-3.4
    FeaturesJson NVARCHAR(MAX) NOT NULL,   -- bools: ads, scheduling, analytics, branding
    SortOrder    INT          NOT NULL CONSTRAINT DF_Plan_Sort DEFAULT 0
);

-- ============ USERS ============
CREATE TABLE dbo.AppUser (
    Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AppUser PRIMARY KEY,
    Email          VARCHAR(320)   NOT NULL CONSTRAINT UQ_AppUser_Email UNIQUE, -- stored lowercased
    EmailConfirmed BIT            NOT NULL CONSTRAINT DF_AppUser_Conf DEFAULT 0,
    DisplayName    NVARCHAR(100)  NOT NULL,
    PasswordHash   VARCHAR(256)   NULL,  -- PBKDF2 (ASP.NET Identity PasswordHasher)
    PlanId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_AppUser_Plan REFERENCES dbo.Plan(Id),
    Theme          VARCHAR(16)    NOT NULL CONSTRAINT DF_AppUser_Theme DEFAULT 'system',
    CreatedAtUtc   DATETIME2      NOT NULL,
    DeletedAtUtc   DATETIME2      NULL
);

CREATE TABLE dbo.AuthToken (               -- magic links, single-use
    Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AuthToken PRIMARY KEY,
    Email          VARCHAR(320)   NOT NULL,
    TokenHash      CHAR(64)       NOT NULL CONSTRAINT UQ_AuthToken_Hash UNIQUE, -- sha256(token)
    Purpose        TINYINT        NOT NULL,  -- 0=signup,1=login,2=forgot
    ExpiresAtUtc   DATETIME2      NOT NULL,
    RedeemedAtUtc  DATETIME2      NULL,
    CreatedAtUtc   DATETIME2      NOT NULL
);

-- ============ TRANSFERS ============
CREATE TABLE dbo.Transfer (
    Id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Transfer PRIMARY KEY,
    LinkId             CHAR(8)     NOT NULL CONSTRAINT UQ_Transfer_LinkId UNIQUE,
    OwnerAppUserId     UNIQUEIDENTIFIER NULL CONSTRAINT FK_Transfer_Owner REFERENCES dbo.AppUser(Id),
    Status             TINYINT     NOT NULL,   -- 0 Draft,1 Active,2 Expired,3 DownloadLimit,4 Deleted
    ExpiresAtUtc       DATETIME2   NOT NULL,
    MaxDownloads       INT         NOT NULL,
    DownloadsCount     INT         NOT NULL CONSTRAINT DF_T_Downloads DEFAULT 0,
    PasswordHash       VARCHAR(256) NULL,
    SenderName         NVARCHAR(100) NOT NULL,
    SenderEmail        VARCHAR(320) NULL,
    Note               NVARCHAR(500) NULL,
    ScheduledSendAtUtc DATETIME2   NULL,
    TotalBytes         BIGINT      NOT NULL CONSTRAINT DF_T_Bytes DEFAULT 0,
    FileCount          INT         NOT NULL CONSTRAINT DF_T_FileCount DEFAULT 0,
    SupersededBy       UNIQUEIDENTIFIER NULL CONSTRAINT FK_T_Super REFERENCES dbo.Transfer(Id),
    CreatedAtUtc       DATETIME2   NOT NULL,
    ExpiredAtUtc       DATETIME2   NULL,
    DeletedAtUtc       DATETIME2   NULL
);
CREATE INDEX IX_Transfer_Expiry  ON dbo.Transfer (Status, ExpiresAtUtc) INCLUDE (Id);
CREATE INDEX IX_Transfer_Owner   ON dbo.Transfer (OwnerAppUserId, CreatedAtUtc DESC);
CREATE INDEX IX_Transfer_Sched   ON dbo.Transfer (Status, ScheduledSendAtUtc);

-- ============ STORAGE (ref-counted) ============
CREATE TABLE dbo.BlobRef (
    Id                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BlobRef PRIMARY KEY,
    BlobPath               VARCHAR(400) NOT NULL CONSTRAINT UQ_BlobRef_Path UNIQUE,
    SizeBytes              BIGINT       NOT NULL,
    RefCount               INT          NOT NULL CONSTRAINT DF_BlobRef_C DEFAULT 1,
    CreatedAtUtc           DATETIME2    NOT NULL,
    PhysicallyDeletedAtUtc DATETIME2    NULL
);
CREATE INDEX IX_BlobRef_Cleanup ON dbo.BlobRef (PhysicallyDeletedAtUtc)
    WHERE RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL;

CREATE TABLE dbo.FileItem (
    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_FileItem PRIMARY KEY,
    TransferId   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_FileItem_T  REFERENCES dbo.Transfer(Id),
    BlobRefId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_FileItem_B  REFERENCES dbo.BlobRef(Id),
    OriginalName NVARCHAR(500)    NOT NULL,
    SizeBytes    BIGINT           NOT NULL,
    ContentType  VARCHAR(200)     NULL,
    SortOrder    INT              NOT NULL,
    CONSTRAINT UQ_FileItem_T_B UNIQUE (TransferId, BlobRefId)
);
CREATE INDEX IX_FileItem_T ON dbo.FileItem (TransferId, SortOrder);

-- ============ EMAIL ============
CREATE TABLE dbo.EmailRecipient (
    Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ER PRIMARY KEY,
    TransferId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ER_T REFERENCES dbo.Transfer(Id),
    Address       VARCHAR(320)     NOT NULL,
    NotifiedAtUtc DATETIME2        NULL,
    CONSTRAINT UQ_ER UNIQUE (TransferId, Address)
);

CREATE TABLE dbo.EmailSuppression (
    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ES PRIMARY KEY,
    Address      VARCHAR(320)     NOT NULL,
    SenderEmail  VARCHAR(320)     NULL,
    CreatedAtUtc DATETIME2        NOT NULL,
    CONSTRAINT UQ_ES UNIQUE (Address, SenderEmail)
);

-- ============ ANALYTICS ============
CREATE TABLE dbo.DownloadEvent (
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DownEvent PRIMARY KEY,
    TransferId     UNIQUEIDENTIFIER NOT NULL,
    FileId         UNIQUEIDENTIFIER NULL,     -- NULL = download-all
    IpHash         VARCHAR(64)    NULL,       -- HMAC(ip) — PII-safe
    Country        CHAR(2)        NULL,
    UaHash         VARCHAR(64)    NULL,
    CreatedAtUtc   DATETIME2      NOT NULL
);
CREATE INDEX IX_DownEvent_T ON dbo.DownloadEvent (TransferId, CreatedAtUtc);

-- ============ BILLING MIRROR (P1) ============
CREATE TABLE dbo.Subscription (
    Id                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Sub PRIMARY KEY,
    AppUserId             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Sub_U REFERENCES dbo.AppUser(Id),
    StripeSubId           VARCHAR(100) NOT NULL CONSTRAINT UQ_Sub_Stripe UNIQUE,
    StripeCustomerId      VARCHAR(100) NULL,
    PlanCode              VARCHAR(32) NOT NULL,
    Status                VARCHAR(32) NOT NULL,   -- active|trialing|past_due|paused|canceled
    CurrentPeriodEndUtc   DATETIME2 NULL,
    GraceEndsAtUtc        DATETIME2 NULL,
    CreatedAtUtc          DATETIME2 NOT NULL,
    UpdatedAtUtc          DATETIME2 NOT NULL
);

CREATE TABLE dbo.StripeEvent (                     -- webhook idempotency
    StripeEventId VARCHAR(100)  NOT NULL CONSTRAINT PK_StripeEvent PRIMARY KEY,
    ProcessedAtUtc DATETIME2    NOT NULL
);

-- ============ OPERATIONAL ============
CREATE TABLE dbo.FeatureFlag (
    Key           VARCHAR(100)  NOT NULL CONSTRAINT PK_Flag PRIMARY KEY,
    Value         NVARCHAR(MAX) NOT NULL,
    Description   NVARCHAR(500) NULL,
    UpdatedAtUtc  DATETIME2     NOT NULL
);

CREATE TABLE dbo.AuditLog (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Audit PRIMARY KEY,
    ActorEmail    VARCHAR(320)  NULL,
    Action        VARCHAR(100)  NOT NULL,
    EntityType    VARCHAR(64)   NOT NULL,
    EntityId      VARCHAR(100)  NULL,
    DetailsJson   NVARCHAR(MAX) NULL,
    CreatedAtUtc  DATETIME2     NOT NULL
);

CREATE TABLE dbo.JobRun (                          -- timer de-dup (TA-6.1)
    JobKey        VARCHAR(64)   NOT NULL,
    RunAtUtc      DATETIME2     NOT NULL,
    LockUntilUtc  DATETIME2     NOT NULL,
    FinishedAtUtc DATETIME2     NULL,
    CONSTRAINT PK_JobRun PRIMARY KEY (JobKey, RunAtUtc)
);

CREATE TABLE dbo.IdempotencyKey (
    Key           VARCHAR(128)  NOT NULL CONSTRAINT PK_Idem PRIMARY KEY,
    ResultJson    NVARCHAR(MAX) NULL,
    TransferId    UNIQUEIDENTIFIER NULL,
    CreatedAtUtc  DATETIME2     NOT NULL,
    ExpiresAtUtc  DATETIME2     NOT NULL
);
```

### TA-3.3 Hot queries → required indexes (verify with `SET STATISTICS` during load test)

| Query (owner) | Index |
|---|---|
| `SELECT * FROM Transfer WHERE Status=1 AND ExpiresAtUtc < @now` (expiry job) | `IX_Transfer_Expiry` |
| `SELECT * FROM Transfer WHERE Status=2 AND ExpiredAtUtc + grace < @now` (delete job) | `IX_Transfer_Expiry` |
| `SELECT * FROM Transfer WHERE OwnerAppUserId=@id ORDER BY CreatedAtUtc DESC` (My Files) | `IX_Transfer_Owner` |
| `SELECT * FROM Transfer WHERE LinkId=@id` (recipient page) | `UQ_Transfer_LinkId` |
| `SELECT * FROM BlobRef WHERE RefCount=0 AND PhysicallyDeletedAtUtc<@now` | `IX_BlobRef_Cleanup` |
| `SELECT * FROM DownloadEvent WHERE TransferId=@id ORDER BY CreatedAtUtc DESC` (analytics) | `IX_DownEvent_T` |

### TA-3.4 Limits Registry (feature plan Appendix A)

- `LimitsRecord` (in `wa.domain` / DTOs in `wa.application`): all constants from Feature Plan Appendix A as one POCO.
- `Plan.LimitsJson` holds the per-plan values. `PlansCache` + `FlagsCache` (SQL `FeatureFlag`) override individual keys with a **30 s in-memory TTL**.
- API resolves limits per request via `ILimitsProvider.Resolve(planCode)` — never hardcoded.
- `FeatureFlag` keys: `limits.free.*`, `limits.pro.*`, `limits.business.*`, plus the feature-gate keys in TA-13.2 (`feature.ads`, `feature.scheduling`, `feature.analytics`, `feature.resend`, `feature.collect`, `feature.sign`, `feature.albums`, `feature.search`, `feature.dataExport`, `feature.emailSettings`, `feature.branding`, `feature.ssoScim`) — each JSON value. Full seed list: TA-13.2 (canonical).

### TA-3.5 Blob layout

| Path | Lifetime | Written by |
|---|---|---|
| `staging/{draftId}/f/{fileId}` | lifecycle: 24 h (safety net) | browser (block blobs) |
| `transfers/{transferId}/files/{fileId}` | with transfer (job) | API (server-side blob copy on finalize) |
| `transfers/{transferId}/all.zip` | with transfer (job) | zip function |
| `avatars/{userId}` (P1) | with user | API |

- One storage account, two containers: `staging`, `transfers`. Container-level **lifecycle rules**: `staging/*` delete 24 h; `transfers/*` delete 30 d (safety net only — jobs are primary, F-TRF-005-8).
- `transfers` container: versioning OFF, GRS in prod, soft-delete 14 d ON.

### TA-3.6 SAS strategy

| Purpose | Scope | Permissions | TTL |
|---|---|---|---|
| Draft upload (per file) | Blob resource (per file URL from API) | `cwr` (create, write, read) | 2 h |
| Recipient single file | Blob resource | `r` | 30 min |
| Recipient zip | Blob resource | `r` | 30 min |

- Version 2024 SAS, signed with **Key Vault–stored account key** (managed identity user-delegation is a P2 optimization).
- SAS URLs are always `https://{acct}.blob.core.windows.net/...?sig=...`; browser holds them only in memory + `sessionStorage` for the unlock session.

### TA-3.7 Migrations

- EF Core migrations owned by **`wa.infrastructure/Persistence`** (`WaDbContext`) — run by `wa.api` at deploy (TA-12.2).
- Migrations run in the CD pipeline **before** the new API image starts (pre-deploy step: `dotnet ef database update`).
- Expanding schema first (add columns, never drop in the same release as code that needs them).
- Seed data: `Plan` rows (free/pro/business) and default `FeatureFlag` rows ship as a migration.

---

## TA-4 — API Design

### TA-4.1 Conventions

**TA-4.1.1 Versioning** — URL prefix `/api/v1`. Breaking change = `/api/v2` (rare; prefer additive changes).

**TA-4.1.2 Auth schemes**
- Browser: httpOnly cookie `wa.session` (same site) — set by auth endpoints.
- Recipient unlock: query `?t={jwt}` (TA-9.2).
- Admin: same cookie + `Role=Admin` claim (seeded users, P0 admin is a flag list of emails in config).
- No API keys in MVP (P2: `wa_live_...` header).

**TA-4.1.3 Error format (RFC 7804, always)**

```json
{
  "type": "https://example.com/errors/transfer-size-exceeded",
  "title": "Transfer size exceeded",
  "status": 422,
  "code": "TRANSFER_SIZE_EXCEEDED",
  "details": "Total 5200000000 bytes exceeds limit 5368709120.",
  "errors": [ { "field": "files", "message": "Transfer too large" } ],
  "correlationId": "08a1f3c2"
}
```

Machine `code` values (all of them, no others in MVP):
`VALIDATION`, `UNAUTHENTICATED`, `FORBIDDEN`, `NOT_FOUND`, `TRANSFER_NOT_FINALIZED`, `TRANSFER_SIZE_EXCEEDED`, `STORAGE_QUOTA_EXCEEDED`, `MAX_DOWNLOADS_REACHED`, `WRONG_PASSWORD`, `TRANSFER_EXPIRED`, `TRANSFER_DELETED`, `FILES_GONE`, `IDEMPOTENCY_CONFLICT`, `RATE_LIMITED`, `STRIPE_WEBHOOK_MISMATCH`, `EMAIL_UNSUBSCRIBED`, `INTERNAL`.

**TA-4.1.4 Pagination** — cursor: `?cursor={base64(created_at:id)}&limit=25`; response includes `nextCursor` (null when done).

**TA-4.1.5 Idempotency** — `Idempotency-Key` header on `POST .../finalize` and `POST .../send`. Stored in `IdempotencyKey` (24 h TTL); replay returns stored `ResultJson`. Key is SHA-256 hex.

**TA-4.1.6 CORS** — allow only `https://www.{env-domain}` (config `Wa:Cors:AllowedOrigins`). Preflight cached 1 h.

**TA-4.1.7 Timeouts & retries** — API: no retries on POST except idempotent ones; Service Bus publish timeout 10 s (on failure: write to local outbox table + `PUBLISH_FAILED` alert — *MVP simplification: log + `email.*` events are only critical path, accept at-most-once for `transfer.created` metrics*).

### TA-4.2 Endpoint catalog (MVP)

| # | Method + Route | Auth | Purpose | Emits |
|---|---|---|---|---|
| 1 | `POST /api/v1/transfers/draft` | none (cookie ok) | create draft, get per-file SAS | `upload_started` |
| 2 | `POST /api/v1/transfers/draft/{draftId}/finalize` | same | commit staged blobs → Transfer | `transfer.created` (if sent) / metrics |
| 3 | `POST /api/v1/transfers/{id}/send` | cookie (or draft-owner token) | set emails/password/note/schedule | `transfer.created` |
| 4 | `GET /api/v1/public/transfers/{linkId}` | none | recipient metadata | `transfer_page_viewed` |
| 5 | `POST /api/v1/public/transfers/{linkId}/unlock` | none | password → JWT | `password_correct/wrong` |
| 6 | `GET /api/v1/public/transfers/{linkId}/files/{fileId}/download-url?t=` | JWT | mint 30-min SAS | `download_started` |
| 7 | `GET /api/v1/public/transfers/{linkId}/download-all-url?t=` | JWT | mint zip SAS (202 if generating) | `download_started` |
| 8 | `POST /api/v1/auth/signup` | none | create user (+magic link if no pw) | `user_created` |
| 9 | `POST /api/v1/auth/login` | none | password login | `login_success/failed` |
| 10 | `POST /api/v1/auth/magic-link/redeem` | none | token → session | `login_success` |
| 11 | `POST /api/v1/auth/forgot-password` | none | magic link email | metrics |
| 12 | `POST /api/v1/auth/logout` | cookie | clear session | metrics |
| 13 | `GET /api/v1/auth/me` · `PATCH /api/v1/auth/me` · `DELETE /api/v1/auth/me` | cookie | profile (name, theme; GDPR delete) | `account_deleted` |
| 14 | `GET /api/v1/transfers?status=&cursor=` | cookie | My Files | — |
| 15 | `GET /api/v1/transfers/{id}` | owner | detail | — |
| 16 | `POST /api/v1/transfers/{id}/resend` | owner | pre-filled new draft (BlobRef share) | metrics |
| 17 | `DELETE /api/v1/transfers/{id}` | owner/admin | hard-delete (de-dup blobs) | `transfer_deleted` |
| 18 | `POST /api/v1/billing/checkout` · `GET /api/v1/billing/portal` | cookie | Stripe flows | `plan.changed` |
| 19 | `POST /api/v1/billing/webhooks/stripe` | Stripe sig | idempotent mirror | `plan.changed` |
| 20 | `GET /api/v1/admin/overview` | admin | counts (5-min cached) | — |
| 21 | `GET /api/v1/admin/transfers?q=&status=&cursor=` · `DELETE .../{id}` | admin | search/kill | `admin.action` |
| 22 | `GET /api/v1/admin/users?q=&cursor=` · `PATCH .../{id}` | admin | list / set plan | `admin.action` |
| 23 | `GET /api/v1/admin/flags` · `PUT /api/v1/admin/flags/{key}` | admin | flag editing | `admin.action` |

### TA-4.2a CQRS mapping (new in v0.2)

Every endpoint maps to exactly **one MediatR request** in `wa.application/UseCases/`; endpoint bodies contain only HTTP ↔ request mapping.

| Area | Requests (MediatR) | Endpoints |
|---|---|---|
| `Transfers/` | `CreateDraftCommand`, `FinalizeTransferCommand`, `SendTransferCommand`, `GetPublicTransferQuery`, `UnlockTransferCommand`, `GetFileDownloadUrlQuery`, `GetDownloadAllUrlQuery`, `ListMyTransfersQuery`, `GetMyTransferQuery`, `ResendTransferCommand`, `DeleteTransferCommand` | 1–7, 14–17 |
| `Auth/` | `SignupCommand`, `LoginCommand`, `RedeemMagicLinkCommand`, `ForgotPasswordCommand`, `LogoutCommand`, `GetProfileQuery`, `UpdateProfileCommand`, `DeleteAccountCommand` | 8–13 |
| `Admin/` | `GetOverviewQuery`, `SearchTransfersQuery`, `AdminDeleteTransferCommand`, `SearchUsersQuery`, `SetUserPlanCommand`, `GetFlagsQuery`, `SetFlagCommand` | 20–23 |
| `Billing/` (P1) | `CreateCheckoutSessionCommand`, `GetPortalSessionQuery`, `HandleStripeWebhookCommand` | 18–19 |

**Thin-endpoint rule:** an endpoint file may reference only: its MediatR request, `ISender`, a small mapping helper, and pipeline attributes. Business logic in an endpoint body is a code-review blocker.

**Key request/response schemas**

`POST /transfers/draft`
```json
// req
{ "files": [ { "name": "render.mp4", "sizeBytes": 2147483648, "contentType": "video/mp4" } ] }
// res 201
{ "draftId": "…", "expiresInSec": 7200,
  "files": [ { "fileId": "…", "uploadUrl": "https://…?sv=…&sp=cwr&se=…" } ] }
```
`POST /transfers/draft/{draftId}/finalize` → `TransferDto`
```json
{ "id": "…", "linkId": "3F9K2A7X", "publicUrl": "https://w.example.com/t/3F9K2A7X",
  "status": "Draft", "expiresAt": "…", "totalBytes": 2147483648,
  "files": [ { "id": "…", "name": "render.mp4", "sizeBytes": 2147483648, "contentType": "video/mp4" } ] }
```
`GET /public/transfers/{linkId}` →
```json
{ "from": "Studio Nova", "note": "Final renders — please review",
  "files": [ { "fileId": "…", "name": "render.mp4", "sizeBytes": 2147483648, "hasDownloadAll": true } ],
  "status": "Active", "downloadsLeft": 87, "passwordRequired": true }
```
`POST /public/transfers/{linkId}/unlock` → `{ "token": "eyJ…" }`

### TA-4.3 Web (HTML) routes — served by the SPA

| Route | Page | Data source |
|---|---|---|
| `/` | Upload surface | — |
| `/t/{linkId}` | Recipient page | endpoint 4–7 |
| `/t/{linkId}/sent` | Sender confirmation | in-memory |
| `/files` | My Files | 14 |
| `/account/*` | auth + profile | 8–13 |
| `/admin/*` | Admin screens | 20–23 |
| `/404`, `/500` | Error pages | — |

SPA assets: `/assets/*` hashed, `Cache-Control: immutable, max-age=1y`; `index.html` `no-cache`.


---

## TA-5 — Event Architecture (Service Bus)

### TA-5.1 Topology

- Topic: `core` (Standard tier, 30-day message retention, **dedup by `MessageId` = eventId**).
- Subscriptions:

| Subscription | Consumer | MaxDeliveryCount | DLQ policy |
|---|---|---|---|
| `email` | `f-email` | 5 | DLQ 30 d, alert `DLQ_COUNT > 0` |
| `analytics` | (App Insights receiver fn or direct) | 3 | DLQ 7 d |
| `admin-alerts` | (future) | 3 | DLQ 7 d |

- Topic: `billing` (P1) — subscription `billing-mirror`, max delivery 10.

### TA-5.2 Envelope (every message)

```json
{
  "eventId": "6f1a…",          // = ServiceBus MessageId (dedup)
  "eventType": "transfer.created",
  "version": 1,
  "occurredAt": "2026-08-24T10:00:00Z",
  "correlationId": "08a1f3c2",
  "partitionKey": "transferId-or-email",
  "payload": { }
}
```

Producers: `wa-api` via the `IEventPublisher` adapter (in tests: in-memory fake). Consumers **must** be idempotent (dedup by eventId against a 15-min LRU).

### TA-5.3 Event types (complete list; no others in MVP)

| eventType | Partition key | Payload | Emitted by |
|---|---|---|---|
| `transfer.created` | transferId | `{ transferId, linkId, totalBytes, fileCount, emails:[], senderEmail?, scheduledSendAt? }` | `SendTransferCommand` |
| `transfer.expired` | transferId | `{ transferId }` | f-expire |
| `transfer.deleted` | transferId | `{ transferId, reason: "expiry-grace"\|"user"\|"admin"\|"download-limit" }` | f-delete / API |
| `email.sent` / `email.failed` | address | `{ transferId, to, attempt, reason? }` | f-email |
| `download.completed` | transferId | `{ transferId, fileId?, ipHash, country, uaHash }` | API (at SAS mint) |
| `zip.generated` | transferId | `{ transferId, sizeBytes, durationMs }` | f-zip |
| `user.created` / `account.deleted` | userId | `{ userId }` | API |
| `plan.changed` | userId | `{ userId, fromPlan, toPlan, reason }` | Stripe webhook handler |
| `admin.action` | actor | `{ actor, action, entityType, entityId, details }` | admin endpoints |

---

## TA-6 — Background Processing (wa.workers)

### TA-6.1 Timer de-dup (all timer functions)

Single-row claim in `JobRun`: `INSERT (JobKey, RunAtUtc=trunc(now,15m), LockUntilUtc=now+10m)` — unique violation ⇒ another instance already claims it. `FinishedAtUtc` set on exit.

### TA-6.2 Function inventory

| Function | Trigger | Concurrency | Timeout |
|---|---|---|---|
| `f-email` | SB `core/email` | 10 | 5 min |
| `f-expire` | timer `0 */15 * * * *` | 1 | 10 min |
| `f-delete-transfers` | timer `0 */15 * * * *` | 1 | 10 min |
| `f-delete-blobs` | timer `0 0 * * * *` (hourly) | 1 | 30 min |
| `f-zip` | HTTP POST | 4 | 60 min (streaming) |

### TA-6.3 `f-expire` (spec)

1. Claim job (`job=expire`, 15-min window).
2. `SELECT Id, LinkId, OwnerAppUserId FROM Transfer WHERE Status=1 AND ExpiresAtUtc < @now` batch 500.
3. Per batch (single transaction): `Status=2, ExpiredAtUtc=now`.
4. Emit `transfer.expired` per row.
5. If `Status` would be `DownloadLimit` (DownloadsCount ≥ MaxDownloads): set `Status=3` directly from `1` without waiting for expiry (checked in endpoint 6).

### TA-6.4 `f-delete-transfers` (spec)

1. Claim job.
2. Candidates: `Status IN (2,3) AND ExpiredAtUtc + @graceDays < @now`.
3. Per transfer (transaction):
   - `Status=4, DeletedAtUtc=now`
   - `UPDATE BlobRef SET RefCount = RefCount-1` per `FileItem`
   - delete `FileItem` rows, `EmailRecipient` rows, zip row `transfers/{id}/all.zip` (blob delete, swallow `BlobNotFound`)
   - emit `transfer.deleted`
4. Batch 100, continue until cursor exhausted; **always re-run-safe**.

### TA-6.5 `f-delete-blobs` (spec)

1. `SELECT Id, BlobPath FROM BlobRef WHERE RefCount=0 AND PhysicallyDeletedAtUtc < @now` (flagged with 24 h buffer by f-delete-transfers).
2. Delete blob, remove row.
3. Safety: skip paths not starting with `transfers/` (log `UNEXPECTED_BLOB_PATH`).

### TA-6.6 `f-zip` (spec — F-TRF-004)

1. `POST /zip/{transferId}` (Front Door `x-forwarded-for` + shared secret header `x-zip-sign`).
2. If `transfers/{id}/all.zip` exists → return its URL.
3. Else: stream files (ordered by `SortOrder`) through a `System.IO.Archive.ZipArchive` in **stream mode** into a new blob (`maxBufferSize=4MB`, `leaveOpen=true`), entry names = `OriginalName` with `_1`/`_2` de-dup.
4. On success: emit `zip.generated`; on failure: delete partial, 500 + `zip_failed` telemetry.
5. Enforce `MAX_ZIP_SIZE` (sum of files) before starting; else 413.

### TA-6.7 `f-email` (spec — F-TRF-006)

1. Consume from `core/email`.
2. Resolve template `templates/transfer/{lang}.html` + `.txt` (Stubble, data from payload + transfer row).
3. Send via Communication Hub (`Reply-To = SenderEmail`, from = `no-reply@{domain}`).
4. Success → mark `EmailRecipient.NotifiedAtUtc`, emit `email.sent`.
5. Failure → retry with backoff per SB MaxDeliveryCount; last attempt emits `email.failed` (alert).
6. Unsubscribe: link `/unsubscribe/{token}` (token = HMAC(suppressionId)) → insert `EmailSuppression`, redirect to `/` with success toast.

### TA-6.8 Blob staging cleanup

No function — **Blob lifecycle rule** on `staging/*` (24 h) is primary (TA-3.5). Function only if lifecycle proves too coarse (P2).

---

## TA-7 — Key Sequences

### TA-7.1 Upload → finalize → send

1. Browser: user selects files → `POST /transfers/draft` (metadata only) → gets per-file SAS URLs.
2. Browser: `BlockBlobClient` per file; 8 MiB blocks; `maxParallel=4` per file, files serialized; progress = uploaded bytes.
3. All files 100 % → `POST .../finalize` with `Idempotency-Key`.
4. API (transaction): create `Transfer(Status=0)` + `FileItem`s + `BlobRef(RefCount=1)`; **server-side blob copy** `staging/… → transfers/{id}/files/{fileId}` per file (metadata-only ops); `TotalBytes`, `FileCount` set.
5. UI shows link screen → `POST /transfers/{id}/send` → status `1`, `ExpiresAtUtc = now + RETENTION_DAYS`, `EmailRecipient` rows, emit `transfer.created`, redirect to `/t/{linkId}/sent`.

### TA-7.2 Recipient download

1. `GET /t/{linkId}` → metadata (files listed only if `Active`; else expired screen).
2. If `passwordRequired` → `POST .../unlock` → JWT (7 d) in `sessionStorage`.
3. Single file: `GET .../download-url?t=` → 30-min SAS → browser `GET`s blob directly; API increments `DownloadsCount` + inserts `DownloadEvent` (F-TRF-003 imprecision accepted).
4. If `DownloadsCount ≥ MaxDownloads` → set `Status=3` (idempotent) → response `MAX_DOWNLOADS_REACHED`.
5. Download-all: `GET .../download-all-url?t=` → if zip exists: URL; else `202 { "pollAfterSec": 2 }` → client polls; on 200 → browser downloads zip.

### TA-7.3 Re-send (F-TRF-010)

1. `POST /transfers/{id}/resend` (owner).
2. API: verify all `BlobRef`s exist (not physically deleted) — else `FILES_GONE`.
3. Create new draft pre-filled: new `Transfer(Status=0)`, new `FileItem`s → same `BlobRefId`s, `RefCount++` per shared blob.
4. UI pre-fills emails/password/note/schedule → user sends (normal flow). Original transfer untouched; `SupersededBy` set on original when new one is sent.

### TA-7.4 Expiry lifecycle

`Active --(f-expire)--> Expired --(grace, f-delete-transfers)--> Deleted --(refcount 0, f-delete-blobs)--> blob gone`
DownloadLimit: `Active --> DownloadLimit --> (grace) --> Deleted`.

### TA-7.5 Stripe (P1)

1. `POST /billing/checkout` → Stripe `session.mode=subscription`, `client_reference_id=userId`.
2. Webhook `customer.subscription.*` → verify sig → dedup in `StripeEvent` → upsert `Subscription`, set `AppUser.PlanId`.
3. `paused` → `GraceEndsAtUtc = now + 14 d`; a 15-min timer inside `f-expire`'s pass downgrades to free when grace ends.
4. Mirror rule: **Stripe is truth**; our rows are read models for limits/UI.

---

## TA-8 — Frontend Architecture (src/wa.web)

### TA-8.1 Stack

React 18 · TypeScript 5 (strict) · Vite 5 · Tailwind 3 · TanStack Query 5 (server state) · Zustand 5 (client state: upload progress, theme, session) · i18next (en, nl, fr, es, pt, it, de, tr) · `@azure/storage-blob` 12 (upload engine).

### TA-8.2 Structure

```
src/wa.web/src/
├─ main.tsx  App.tsx  routes.tsx
├─ core/
│  ├─ api/        # fetch wrapper: baseUrl, cookie, Problem+json → typed errors, retry for GETs
│  ├─ upload/     # UploadEngine (Zustand store + @azure/storage-blob)
│  ├─ auth/       # session store, <RequireAuth>
│  ├─ i18n/       # locales/{lang}.json (flat keys: features.upload.title, ...)
│  ├─ theme/      # design tokens, data-theme bootstrap (pre-paint)
│  └─ ui/         # Button, Modal, ProgressBar, Toast, FileRow, CopyField (small, no kit)
├─ features/
│  ├─ landing/    # F-TRF-001 upload surface
│  ├─ sender/     # link screen, send form, confirmation
│  ├─ recipient/  # F-TRF-003 page: file list, password, download, growth loop
│  ├─ account/    # signup/login/magic-link/profile
│  ├─ files/      # F-TRF-009 My Files
│  ├─ billing/    # plan screen, upgrade CTA (P1)
│  └─ admin/      # F-TRF-011 screens
└─ styles/        # tokens.css (colors, spacing, radii) + tailwind.config.ts
```

### TA-8.3 UploadEngine contract

- State: `{ files: [{id, name, size, status: queued|uploading|done|failed, progress}], overall: {sentBytes, totalBytes} }`.
- API: `start(draft)`, `retry(fileId)`, `remove(fileId)`, `reset()`.
- Block size 8 MiB, parallelism 4 per file, one file at a time (simplest correct behavior).
- On `failed`: stop after 5 block retries (F-TRF-001-6).

### TA-8.4 Performance budgets

- `index.html` + JS < 300 KB gz; TTI < 1 s on 4G for `/` and `/t/{linkId}`.
- No images on landing (logo = inline SVG). Fonts: system stack + one webfont (self-hosted, `font-display: swap`).

### TA-8.5 i18n rules

- Keys: `{area}.{screen}.{control}`. Missing key → English fallback + `translation_missing` console warn (dev).
- Dates/sizes: `Intl` formatters with locale; bytes via `formatBytes()` helper (1024-based, "GB").

---

## TA-9 — Security Architecture

### TA-9.1 AuthN

- **Session**: `wa.session` cookie — value is `Base64(userId|exp|sig)` (HMAC-SHA256 with `Wa:Jwt:Secret`); httpOnly, Secure, `SameSite=Lax`, 30 d rolling (refresh on use).
- **Magic link**: 256-bit random, email with `?ml={token}`; stored hashed (`AuthToken`); 10-min TTL; single-use (`RedeemedAtUtc`).
- **Transfer unlock JWT**: HS256, claims `{ sub: linkId, pwd: true, exp: now+7d, jti }`, passed as `?t=`.
- **Password**: ASP.NET Identity `PasswordHasher` (PBKDF2-SHA256) — users *and* transfer passwords use the same hasher (separate columns, separate salts — inherent).

### TA-9.2 AuthZ

Roles: `Guest` (cookieless), `User` (session), `Admin` (session + email in `Wa:AdminEmails` list, P0; Entra SSO P3).
Admin endpoints reject non-admins with `FORBIDDEN`.

### TA-9.3 Token & key inventory

| Item | Store | Rotation |
|---|---|---|
| `Wa:Jwt:Secret` (HMAC for session + unlock JWT) | Key Vault `wa-jwt-secret` | 90 d |
| Storage account key (SAS signing) | Key Vault `wa-blob-key` | per-Azure schedule |
| Stripe secret + webhook signing secret | Key Vault | manual |
| Communication Hub connection | Key Vault | managed identity preferred |
| SQL: managed identity primary + PIM password fallback | — | per-Azure |
| Zip internal sign header `x-zip-sign` | Key Vault | 90 d |

### TA-9.4 PII policy

- PII = email addresses, sender names, file names, IPs.
- Telemetry: file names → SHA-256; IP → HMAC(`Wa:Jwt:Secret`); raw PII only in `DownloadEvent` (IP) and `AppUser`/`EmailRecipient` rows, all marked `Pii=true` in App Insights custom properties.
- GDPR erase: `DELETE /auth/me` cascades `AppUser` → re-home active transfers to anonymous (keep `SenderName`), delete profile, suppress rows.

### TA-9.5 Network & transport

- TLS 1.2+ (Front Door auto-terminate).
- SQL: VNet + PIP (managed identity auth); firewall = Front Door service tag + VPN IP.
- Blob: **no public network access**; all via SAS (browser) or managed identity (workers).
- CORS per TA-4.1.6. CSP on `index.html`: `default-src 'self'; script-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; connect-src 'self' {api} {blobacct}.blob.core.windows.net`.
- Rate limits (Front Door WAF, by IP): `/api/v1/auth/*` 10/min; `/api/v1/transfers/draft` 10/min; `/api/v1/public/*` 30/min; admin 60/min. API in-memory `AspNetCoreRateLimit` as second layer for `/auth` (5/min per IP).

### TA-9.6 Threat notes (design mitigations)

- **SAS leak**: TTLs per TA-3.6; SAS never in logs (mask `sig=`).
- **LinkId enumeration**: 8-char Crockford = 40 bits; 404 vs 410 identical bodies (F-TRF-003-9).
- **Replay of finalize**: idempotency key (TA-4.1.5).
- **Zip DoS**: `MAX_ZIP_SIZE` cap + function timeout + 4-way concurrency.
- **Admin bootstrap**: seed via migration (email list in config), documented runbook.

---

## TA-10 — Observability

### TA-10.1 Resources

- `wa-{env}-ai` (Application Insights) per environment; log retention 30 d (prod: 90 d on metrics).
- Correlation: W3C `traceparent` in → out; `CorrelationId` = first 8 hex of trace-id; echoed in error JSON (`correlationId`) and logs.

### TA-10.2 Telemetry contract (only these; names are code)

**Events** (App Insights, custom): `upload_started`, `upload_completed` (bytes, durationMs, retries), `upload_failed`, `transfer_page_viewed`, `transfer_not_found`, `password_correct`, `password_wrong`, `download_started`, `download_completed`, `zip_generated`, `zip_failed`, `email_sent`, `email_failed`, `user_created`, `login_success`, `login_failed`, `account_deleted`, `plan_changed`, `translation_missing`, `admin_action`, `dlq_count`.

**Metrics** (custom): `upload.success_rate` (5-min), `link_to_download_seconds`, `expiry_job_lag_seconds`, `email_failures_1h`, `active_transfers`, `storage_bytes_active`, `api.requests{status}`.

### TA-10.3 Alerts (Azure Monitor)

| Name | Condition | Severity |
|---|---|---|
| Upload success low | `upload.success_rate` < 0.99 for 15 min | P1 |
| Expiry lag | `expiry_job_lag_seconds` > 1800 | P1 |
| Email failures | `email_failures_1h` > 50 | P2 |
| DLQ | `dlq_count` > 0 | P2 |
| 5xx spike | 5xx > 5% for 10 min | P1 |
| Blob 429s | storage 429 rate > 10/min | P2 |

### TA-10.4 Dashboards

1. **Upload funnel** started→completed→failed. 2. **Link funnel** viewed→download. 3. **Job health** (lag per job). 4. **Email health**. 5. **Business** (transfers/day, storage, plan mix).

### TA-10.5 Logging

- Serilog everywhere: `Serilog.AspNetCore` (API), `Serilog` (Functions).
- Levels: Information for request/transfer lifecycle, Warning for retries, Error for 5xx/DLQ.
- Rule: no PII in default level; `Log.Information("{Message:Pii}", ...)` for PII slots.
- Log streaming: Container Apps → App Insights; Functions → App Insights (host setting).

---

## TA-11 — Infrastructure & Environments

### TA-11.1 Naming convention

`wa-{env}-{purpose}`: `wa-prod-sql`, `wa-prod-blob`, `wa-prod-sb`, `wa-prod-ai`, `wa-prod-kv`, `wa-prod-acr`, `wa-prod-fd`, `wa-prod-ca` (container app env), `wa-prod-api` (app), `wa-prod-fa` (function app), `wa-prod-swa` (static web app). Resource groups: `rg-wa-{env}`.

### TA-11.2 Resource list (prod; dev/staging same shape, smaller sizes)

| Resource | Type | Settings |
|---|---|---|
| `wa-prod-sql` | SQL DB B-Server Auto | 4 vCore, 32 GB, zone-redundant, PIP on, M2I |
| `wa-prod-blob` | Storage `Standard_LRS` (→ GRS) | lifecycle per TA-3.5, soft-delete 14 d |
| `wa-prod-sb` | Service Bus Standard | topic retention 30 d |
| `wa-prod-ai` | App Insights | 90 d metrics |
| `wa-prod-kv` | Key Vault | premium not needed; soft-delete 90 d |
| `wa-prod-acr` | ACR Basic | — |
| `wa-prod-fd` | Front Door Standard + WAF | rules per TA-11.3 |
| `wa-prod-ca` | Container App env | VNet-injected, M2I |
| `wa-prod-api` | Container App | min 1 / max 10, CPU 60%, 500 m CPU / 1 GiB per instance |
| `wa-prod-fa` | Functions Flex Consumption | per-function scaling (TA-6.2) |
| `wa-prod-swa` | Static Web Apps | custom domain, per-PR preview (staging) |
| `wa-prod-vnet` | VNet | subnets: `sub-ca`, `sub-fa`, `sub-sql` (delegated) |

### TA-11.3 Front Door routing (all environments)

| Host/path | Origin |
|---|---|
| `www.{d}/` | SWA default host |
| `api.{d}/api/*` | `wa-{env}-api` (Container Apps internal FQDN) |
| `api.{d}/zip/*` | `wa-{env}-fa` (f-zip) |

TLS: auto certs for custom domains; WAF: `RateLimitRule` set (TA-9.5); logs → App Insights.

### TA-11.4 Bicep layout

`infra/bicep/main/{env}.bicep` (params: names, sizes, domain) → modules in `modules/` (one per resource group, TA-11.2 rows). `azd` not required; plain `az deployment` via CI.

---

## TA-12 — CI/CD

### TA-12.1 Pipeline stages (GitHub Actions; same shape for DevOps)

```
PR → ci.yml:
  1. lint (eslint+prettier; dotnet format)
  2. unit (wa.domain.unit, wa.application.unit)          # gate: 100% pass
  3. integration (wa.api.integration: Testcontainers SQL + Azurite)  # gate
  4. typecheck + build web, vitest
  5. docker build wa-api → push ACR (dev)

main → cd-staging.yml:
  0. IaC apply (staging)          # az deployment
  1. migrate: dotnet ef database update  (pre-deploy)
  2. deploy wa-workers → wa-api → wa-web (SWA)   # order matters: workers first
  3. smoke: GET /health, create+expire a test transfer via API
  4. playwright e2e vs staging   # gate for prod promotion

tag v* → cd-prod.yml: same as staging + manual approval + canary (20% → 100%)
```

### TA-12.2 Deployment rules

- Migrations **always** before API cutover (TA-3.7).
- Rollback = redeploy previous image tag; migrations are add-only in the release (TA-3.7).
- Secrets: CI reads Key Vault via OIDC federation (no long-lived secrets).
- Health: `GET /health` (DB ping + SB ping), Front Door health probe on it.

### TA-12.3 Branch model

`main` = staging; `release/*` → prod (tag); `feat/{id}-{slug}`; squash merge; PR must include: feature IDs touched, ACs covered, migration? flag changes?

---

## TA-13 — Configuration Reference

### TA-13.1 Environment variables (wa-api / wa-workers)

| Variable | Required | Example |
|---|---|---|
| `ConnectionStrings__WaDb` | ✓ | `Server=…;Database=wa;…;Encrypt=True;Authentication=…` |
| `Wa:Blob:AccountUrl` | ✓ | `https://waprodblob.blob.core.windows.net` |
| `Wa:Blob:AccountKey` | ✓ (KV ref) | |
| `Wa:ServiceBus:ConnectionString` | ✓ | |
| `Wa:CommunicationHub:Connection` | ✓ | |
| `Wa:Jwt:Secret` | ✓ (KV) | 64 hex |
| `Wa:Stripe:SecretKey` / `Wa:Stripe:WebhookSecret` | P1 | |
| `Wa:AppInsights:ConnectionString` | ✓ | |
| `Wa:Url:Public` / `Wa:Url:Api` | ✓ | `https://w.example.com` / `https://api.example.com` |
| `Wa:Cors:AllowedOrigins` | ✓ | `https://www.example.com` |
| `Wa:AdminEmails` | ✓ | `ops@example.com` |
| `Wa:Zip:SignKey` | ✓ (KV) | |
| `Wa:Flags:TtlSeconds` | – | `30` |

App settings: `appsettings.json` (env-neutral defaults only) + `appsettings.{Environment}.json` (never committed with secrets — CI injects KV refs).

### TA-13.2 Feature flag keys (seeded; TA-3.4)

Plan-limit keys: `limits.free.maxTransferSize`, `limits.free.retentionDays`, `limits.free.maxDownloads`, `limits.pro.*` (…), `limits.business.*` — one per Part-2 constant.

Feature-gate keys (seeded with default `false` unless noted): `feature.ads` (D-14, off at launch) · `feature.scheduling` · `feature.analytics` · `feature.resend` · `feature.collect` · `feature.sign` · `feature.albums` · `feature.search` · `feature.dataExport` · `feature.emailSettings` (F-XCT-001) · `feature.branding` · `feature.ssoScim` (Phase 3, per Part-2 `BRANDING`/`SSO_SCIM` row). Plan gating for collect/sign/albums uses the `FeaturesJson` extension on `Plan` (F-BIL-001 shape) read through the same registry — never literals.

---

## TA-14 — Test Strategy

### TA-14.1 Pyramid

- **Unit (wa.domain.unit, wa.application.unit; xUnit)**: pure domain — LinkId generation, refcounting, limits resolution, expiry math. Use-case tests fake ports with in-memory records. Target ≥ 90 % of `wa.domain` + `wa.application`.
- **Integration (wa.api.integration)**: `WebApplicationFactory` + **Testcontainers** (`mcr.microsoft.com/mssql/server:2022-latest`) + **Azurite**; Service Bus replaced by in-memory `IEventPublisher` fake. Every endpoint in TA-4.2 gets ≥ 1 happy + ≥ 1 error test.
- **E2E (Playwright, vs staging)**: scenarios: guest upload → link → download on a second context (guest, no cookie); password flow; expiry via admin flag override; My Files CRUD; i18n spot-check (de).
- **Load (k6 vs staging)**: 5 GB synthetic file; targets: upload completes in < 10 min at 100 Mbps; API p50 < 100 ms, p99 < 500 ms; recipient page TTFB < 500 ms global (from 2 regions via k6 zones).

### TA-14.2 Gates

- PR: lint + unit + integration must pass.
- Staging deploy: smoke + e2e.
- Prod promotion: load test green (per release train, not per PR).

### TA-14.3 Test data

- `Plans` seeded identically in all test scopes (same fixture as migration seed).
- No PII in tests (test-accounts@example.com).

---

## TA-15 — Performance Targets & Scaling

| SLO | Target |
|---|---|
| `api.requests` p50 / p99 | < 100 ms / < 500 ms |
| Recipient page TTFB (global) | < 500 ms |
| Expiry job lag | < 15 min |
| Email lag (send → inbox) | < 60 s p95 |
| Upload aggregate | 10 GB/min per tenant (100 Mbps uplink) |
| Throughput plan | 10 TB/day, 10 k concurrent active transfers |

**Scaling rules**: `wa-api` CPU 60 % (Container Apps scale-out). `f-zip` scale on queue depth (HTTP). SQL: read scale not needed until > 2 k rps (add read replica ADR then).

---

## TA-16 — Cost Model (monthly, prod, at 10 TB/day transfers)

| Line | Driver | Est. $ |
|---|---|---|
| Blob egress (direct SAS) | ~10 TB × 0.6 retention ≈ 6 TB egress × $0.0875 | ~$525 |
| Blob storage | ~10 TB × 0.02/GB avg | ~$200 |
| Container Apps | ~150 h × scale | ~$150 |
| Functions Flex | ~$0.10/M GB-s | ~$50 |
| SQL B-Server Auto | 4 vCore | ~$450 |
| Communication Hub | ~200 k emails × $1.50/1k | ~$300 |
| Front Door + WAF | requests + data | ~$40 |
| App Insights | ingestion | ~$60 |
| **Total** | | **~$1,800/mo** |

Top cost controls: (1) egress — P2 CDN download path (TA-17 ADR-006); (2) retention days = storage + egress directly (plan pricing lever); (3) `staging` lifecycle is free cleanup.

---

## TA-17 — Architecture Decision Records (summary — this table is canonical)

The table below is canonical. Full ADR files land in `docs/adr/` when an ADR is **amended or superseded** (one file per ADR, mirroring this row); until then, cite the row ID (`ADR-015`, …) and do not expect a file.

| ID | Decision | Alternatives rejected | Key consequence |
|---|---|---|---|
| ADR-001 | .NET 10 Minimal API, one `wa-api` host | Node/Express; multiple microservices | Fewer moving parts; team knows C# |
| ADR-002 | Browser↔Blob **direct SAS** upload/download (never proxy bytes in API) | API-mediated multipart; Upload API service | API stays cheap; blob egress is the cost line |
| ADR-003 | Container Apps (not AKS/App Service) | AKS; App Service | Zero k8s ops; M2I + VNet + scale in one service |
| ADR-004 | Functions Flex Consumption for all background work | Durable orchestrations; background tasks in API | No 10-min timeout surprise (flex), per-fn scale |
| ADR-005 | Service Bus Standard topic `core` | In-process `Channel`; Event Hubs | Durable handoff to email; dedup by MessageId |
| ADR-006 | MVP: direct SAS downloads; **P2: Front Door CDN token path** | All downloads via API now; CDN from day 1 | Accept $0.0875/GB egress until volume justifies |
| ADR-007 | Lightweight cookie-session auth + magic link (custom `AppUser`, no ASP.NET Identity tables) | ASP.NET Identity; Entra ID from day 1 | 4 tables vs 12; Entra added at P3 via `ExternalId` |
| ADR-008 | No Redis in MVP (SQL + in-memory caches, 30 s TTL) | Redis from day 1 | Fewer resources; document per-replica flag cache divergence |
| ADR-009 | **BlobRef ref-counted sharing** for re-send | Copy-on-resend; content-addressed dedup | Re-send is free + instant; dedup is a P2 option |
| ADR-010 | Stripe (Checkout + Customer Portal + webhooks) | Azure Billing (Revenue Cycle); manual invoices | Self-serve billing; webhooks are truth-mirror |
| ADR-011 | Azure Communication Hub for email | SendGrid; direct SMTP | Azure-native, per-recipient attrs for i18n |
| ADR-012 | Front Door (Standard+WAF) as single edge | Azure CDN + separate API Gateway | One routing/WAF/TLS surface |
| ADR-013 | Single SQL instance per env (no replica until > 2 k rps) | Read replica from day 1 | Cost; revisit with ADR |
| ADR-014 | Monorepo (`src/` + `web/` + `infra/` + `tests/`) | Polyrepo per service | One CI, atomic schema+API changes |
| ADR-015 | **Clean (onion) architecture**: `wa.domain → wa.application → wa.infrastructure → wa.api / wa.workers`; EF confined to `wa.infrastructure/Persistence` (new v0.2) | Flat `wa.core`; 2-project core | Ports/adapters make each layer testable; EF never leaks to domain/use cases |
| ADR-016 | **CQRS via MediatR 12.5**: one `IRequest`/handler per use case in `wa.application/UseCases/`; domain events via `IEventPublisher` (MediatR-free) (new v0.2) | MediatR `INotification` domain events; custom home-grown mediator | Frozen, known API; pipeline owns cross-cutting; 12.5 is the last stable line before 13.x breaking changes |

---

## TA-18 — Naming & Coding Conventions

- **C#**: namespaces mirror folders (`Wa.Api.Endpoints`, `Wa.Application.UseCases.Transfers`); endpoint files named `*Endpoints.cs` with one `MapGroup` per file; use-case request + handler per file (`FinalizeTransferCommand.cs` contains `record FinalizeTransferCommand : IRequest<TransferDto>` + `FinalizeTransferCommandHandler`); DTOs named `{Area}{Shape}Dto` and live with their use case (or shared `Common` if cross-use-case).
- **Status ints** are constants in `wa.domain/TransferStatus` — never raw ints in logic.
- **Time**: `DateTime` only UTC (suffix `AtUtc` on properties); `DateTimeOffset` in DTO JSON (ISO 8601 `Z`).
- **TS**: strict mode; features own their data fetching; `core/` is import-only (no feature-to-feature imports); file naming kebab-case, components PascalCase.
- **Branch/commit**: `feat/{FR-id}-slug`; Conventional Commits (`feat:`, `fix:`, `chore:`) with feature ID in body.
- **Error codes**: TA-4.1.3 list is closed for MVP; new codes need an ADR line.

---

## TA-19 — Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Blob egress cost overrun (biggest) | High | High | P2 CDN path (ADR-006); per-plan egress metering; lifecycle enforced |
| Zip function OOM/timeout on huge sets | Med | Med | `MAX_ZIP_SIZE` cap; streaming (TA-6.6); per-file fallback |
| SAS key leak | Low | High | Short TTLs; key rotation in KV; mask `sig=` in logs |
| Function timer duplication | Med | Med | `JobRun` claim (TA-6.1) |
| SQL connection exhaustion at scale | Low | Med | Pooling defaults reviewed at load test; Container App replicas fixed 1 GiB |
| Flag divergence across replicas (no Redis) | Med | Low | 30 s TTL accepted for limits; document (ADR-008) |
| iOS Safari zip download quirks | Med | Low | E2E mobile matrix in staging; per-file fallback always visible |
| Stripe webhook loss | Low | High | Idempotent replay endpoint; `plan.changed` audit; manual re-sync admin action |

---

## TA-20 — Traceability: Feature → Architecture

| Feature (Feature Plan) | Architecture sections | Primary resources |
|---|---|---|
| F-TRF-001 Upload | TA-4.2#1–2, TA-6.8, TA-7.1, TA-8.3 | blob `staging`, SAS `cwr` |
| F-TRF-002 Link | TA-3.2 `Transfer`, TA-4.2#2–3, TA-7.1 | sql |
| F-TRF-003 Recipient page | TA-4.2#4–6, TA-7.2, TA-8 | sql, SAS `r` |
| F-TRF-004 Zip | TA-6.6, TA-4.2#7 | `f-zip`, blob |
| F-TRF-005 Expiry | TA-6.3–6.5, TA-7.4, TA-3.5 | `f-expire`, `f-delete-*`, lifecycle |
| F-TRF-006 Email | TA-5.3, TA-6.7 | `f-email`, Communication Hub |
| F-TRF-007 Limits | TA-3.4, TA-13.2 | `FeatureFlag`, `Plan` |
| F-TRF-008 Accounts | TA-4.2#8–13, TA-9.1 | `AppUser`, `AuthToken` |
| F-TRF-009 My Files | TA-4.2#14–15, TA-3.2 indexes | sql |
| F-TRF-010 Re-send | TA-7.3, ADR-009 | `BlobRef` refcount |
| F-TRF-011 Admin | TA-4.2#20–23, TA-9.2 | `AuditLog` |
| F-TRF-012 Telemetry | TA-10 | App Insights |
| F-TRF-013 Errors | TA-4.1.3, TA-10.5 | — |
| F-TRF-014 i18n | TA-8.5, TA-6.7 (email i18n) | — |
| F-TRF-015 Dark mode | TA-8.2 `core/theme` | — |
| F-TRF-016 A11y | TA-8.2, TA-10.4 (audit) | — |
| F-TRF-017 Mobile | TA-8.4, TA-14.1 (E2E matrix) | — |
| F-BIL-001…003 | TA-7.5, TA-4.2#18–19, TA-3.2 `Subscription` | Stripe, KV |
| F-PRF-001…004 | TA-4.2, TA-3.4 flags | — |
| F-COL-001…005 | TA-3.2 (+ `Collection`, `CollectionEntry` tables — define in P2 DDL), TA-7 | sql, blob |
| F-SGN-001…004 | TA-3.2 (+ `Document`, `Signature`, `AuditEntry`), TA-5.3 | sql |
| F-ALB-001…004 | TA-3.2 (+ `Album`, `AlbumItem`), TA-8 | sql, blob |
| F-ENT-001…006 | TA-9.1 (`ExternalId`), TA-11.4 (per-org VNet/residency), ADR-007 addendum | Entra ID |

**Change log**

- **v0.2 (2026-08-23):** Clean (onion) projects (`wa.domain/wa.application/wa.infrastructure/wa.api/wa.workers`), CQRS via MediatR 12.5, .NET 10, VS 2026 as dev environment, ADR-015/016, TA-2.3–TA-2.5, TA-4.2a.
- **v0.1:** initial architecture.
