# F-PRF-001-US-021-01 — Org branding on the recipient page

**Feature:** F-PRF-001 Custom "From" Branding (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-001/US-021-01-org-branding.md`
**Scope:** FR-021-1 (recipient-page half only), FR-021-4, AC-021-1 (page half), AC-021-3, EC-021-1, EC-021-4, EC-021-5.
**Task id note:** F-PRF-001 has no T-xxx task files yet; the story id is the task id (same convention as `US-020-*`).

## 1. Goal

Endpoint 4 (GET /api/v1/public/transfers/{linkId}) returns the transfer owner's current `orgName` when the owner's plan has `PlanFeatures.Branding` on, and the recipient header renders "From: {personName} · {orgName}" (plain "From: {personName}" otherwise), backed by two new nullable `AppUser` columns in migration `AddAppUserOrgBranding`. **Exit check:** new unit, integration and vitest cases green; AGENT.md §4 gate green (integration filtered per pitfalls).

**Out of scope (later stories):** `PATCH /auth/me` orgName field + validation FR-021-5 and the profile "Organization" UI (US-021-04 / profile story), `PUT /auth/me/logo` + SAS mint of the logo + EC-021-2/3 (US-021-04), email header (US-021-02, AC-021-1 email half), per-transfer-name precedence AC-021-2 (US-021-03), telemetry `branding_applied` / `branding_logo_missing` (§5 DECISION), export/erasure of the new columns (§5).

## 2. Design & Decisions

### Grounded facts
- Endpoint 4 handler: `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs:59-100`; result record `:23-31`; Active payload built at `:91-99`; wire shape pinned at `src/wa.api/Program.cs:390-395`; the exact property list is asserted at `tests/wa.api.integration/PublicTransferEndpointTests.cs:78` (stale fact — must be extended).
- The transfer row knows its owner: `Transfer.OwnerAppUserId` (`src/wa.domain/Transfer.cs:9`, null = guest send). Endpoint 4 is anonymous, so `IPlanContextAccessor` (bound to the *viewer's* session, `src/wa.application/Limits/PlanContext.cs:19`) is the wrong source — the branding gate belongs to the **sender's** plan.
- Feature gate: `IFeaturesProvider.Resolve(planCode)` (`src/wa.application/Limits/FeaturesProvider.cs:15`) returns `PlanFeatures.Branding` (`src/wa.domain/PlanFeatures.cs:17-18`) = plan row AND global gate feature.branding; seed: free false, pro/business true, global gate seeded **off** (`tests/wa.api.integration/PlanCatalogIntegrationTests.cs:220-230`) — integration tests must flip the gate.
- `AppUser` today: `src/wa.domain/AppUser.cs:4-18`; EF config `src/wa.infrastructure/Persistence/EntityConfigurations/AppUserConfiguration.cs:10-42`. No test asserts the AppUser column list.
- `GetPublicTransferHandler` is constructed in exactly one test (`tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs:28-33`); DI resolves it via MediatR in wa.api.
- Closed telemetry set has no branding names (`src/wa.domain/Telemetry/TelemetryEvents.cs:45-51`).

### D-1 — Schema: two nullable columns, migration `AddAppUserOrgBranding` (TA-3.2, TA-3.7, golden rule 4)
`AppUser.OrgName` → `NVARCHAR(60) NULL` (FR-021-5: ≤ 60 chars; NVARCHAR like `DisplayName`); `AppUser.OrgLogoBlobPath` → `VARCHAR(400) NULL` (same width as `BlobRef.BlobPath`, TA-3.2; value will be `avatars/{userId}/logo`, TA-3.5). Expand-only, nullable, no default, no index, no backfill. The spec mandates the change (F-PRF-001 technical notes) so it is not an AGENT.md §7 escalation; the ADR note + TA-3.2 DDL lines are docs close-out (§5).

### D-2 — New narrow read port `ISenderBrandingStore` (not a new member on `IPublicTransferStore` / `IAppUserStore`)
`Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct)` with `public sealed record SenderBrandingRow(string PlanCode, string? OrgName, string? OrgLogoBlobPath);` — one AsNoTracking AppUser⋈Plan query, **filtered `DeletedAtUtc == null`** (a soft-deleted sender shows no branding; TA-9.4 erase re-homes transfers to anonymous anyway). Changing `IPublicTransferStore.GetByLinkIdAsync`'s tuple would ripple into its 3 handlers and 3 hand-rolled stubs; a new port touches none of them (codebase-design: narrow seam, zero blast radius). Lives next to `IPublicTransferStore` in `wa.application/UseCases/Transfers` (onion: application port, infrastructure adapter).

### D-3 — Render-time resolution in the handler (EC-021-1, FR-021-4, EC-021-5)
Only in the Active branch, after the password logic: owner null → no org; row null / OrgName blank → no org; else `IFeaturesProvider.Resolve(row.PlanCode)`; `Branding` false → no org (Free, downgrade EC-021-5 — profile values untouched). Nothing is copied onto `Transfer` at send time, so a profile edit shows on the next page load (no cache on the AppUser read; plan/flag rows keep their 30 s cache, EC-020-1). `PlanUnknownException` propagates (500 INTERNAL, same as today's limits read). Terminal states never carry org data (no byte leak, FR-003-9).

### D-4 — Wire shape: append `orgName`, `orgLogoUrl` (TA-4.2#4)
Appended after `passwordRequired` so existing clients/tests keep property order. `orgLogoUrl` is always `null` in this story (D-5); the field reserves the contract so US-021-04 only fills it. Both are JSON null when absent — never omitted.

### D-5 — Logo URL stays null in this story
Minting a logo SAS (TA-3.6, 30 min) needs a purpose row in TA-3.6 and an `img-src` CSP widening for the blob account (TA-9.5 CSP is `img-src 'self' data:`, `docs/03-technical-architecture.md:797`) — both security-shaped (AGENT.md §7) and owned by US-021-04 with the upload. No write path for `OrgLogoBlobPath` exists yet, so null is the only reachable value. The web renders the logo `<img>` only when `orgLogoUrl` is non-null (40×40, `--radius-sm`, `object-fit: contain`, empty `alt` since the org name is adjacent) — EC-021-4 "name only" is the live path.

### D-6 — Web: one header line, store coerces missing fields to null
`RecipientStore` gains `orgName: string | null`, `orgLogoUrl: string | null` (LOADING_SHAPE nulls; `body.orgName ?? null` so older stubs/bodies stay valid). `RecipientPage` renders in both `.rp-heading` h1s (gate `:210`, active `:262`): optional logo img, then `fromHeading + from`, then `{orgName !== null && ` · ${orgName}`}` — the separator is an expression, not JSXText (lint rule `src/wa.web/eslint.config.js:28`), and is language-neutral, so no new i18n key. Existing `'From: Studio Nova'` textContent assertions stay green (no org in their bodies).

### D-7 — No telemetry, no PII in logs
`branding_applied` / `branding_logo_missing` are not in the closed TA-10.2 set (golden rule 6) → not emitted (§5 DECISION). The handler logs nothing about the org (FR-021-6, TA-9.4).

## 3. Implementation Plan

1. **Domain** — MODIFY `src/wa.domain/AppUser.cs`: add `public string? OrgName { get; set; }` (doc: F-PRF-001 FR-021-1, ≤ 60 chars, PII — TA-9.4) and `public string? OrgLogoBlobPath { get; set; }` (doc: TA-3.5 `avatars/{userId}/logo`, set by US-021-04).
2. **EF config** — MODIFY `src/wa.infrastructure/Persistence/EntityConfigurations/AppUserConfiguration.cs`: after the Theme block add `b.Property(u => u.OrgName).HasColumnType("NVARCHAR(60)");` and `b.Property(u => u.OrgLogoBlobPath).HasColumnType("VARCHAR(400)");` (nullable by CLR type).
3. **Migration** — run dotnet ef migrations add AddAppUserOrgBranding with project src/wa.infrastructure and startup project src/wa.api (AGENT.md §5.3 command). Inspect: Up = two `AddColumn` on table `AppUser`, `nullable: true`, types as D-1, nothing else; Down = two `DropColumn`. Regenerated snapshot is in the boundary. Verify: `dotnet build src/wa.api`.
4. **Port** — CREATE `src/wa.application/UseCases/Transfers/ISenderBrandingStore.cs`: interface + `SenderBrandingRow` record (D-2), doc comment naming the soft-delete filter.
5. **Adapter** — CREATE `src/wa.infrastructure/Persistence/SenderBrandingStore.cs`: `public sealed class SenderBrandingStore(WaDbContext db) : ISenderBrandingStore`; query `db.Set<AppUser>().AsNoTracking().Where(u => u.Id == ownerAppUserId && u.DeletedAtUtc == null).Join(db.Set<Plan>().AsNoTracking(), u => u.PlanId, p => p.Id, (u, p) => new SenderBrandingRow(p.Code, u.OrgName, u.OrgLogoBlobPath)).FirstOrDefaultAsync(ct)` (shape mirrors `UserPlanStore`).
6. **Handler** — MODIFY `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs`: result record gains `string? OrgName = null` and `string? OrgLogoUrl = null` (Active only; OrgLogoUrl always null until US-021-04); ctor gains `ISenderBrandingStore branding, IFeaturesProvider features` (append after `tokens`, before `logger`); private `ResolveOrgNameAsync(Guid? ownerAppUserId, CancellationToken)` per D-3; Active return sets `OrgName: await ResolveOrgNameAsync(transfer.OwnerAppUserId, cancellationToken)`. Return the trimmed org name.
7. **Wiring + wire shape** — MODIFY `src/wa.api/Program.cs`: after `:74` register `builder.Services.AddSingleton<wa.application.UseCases.Transfers.ISenderBrandingStore>(sp => new wa.infrastructure.Persistence.SenderBrandingStore(sp.GetRequiredService<WaDbContext>()));` (comment: US-021-01). In the Active JSON (`:390-395`) append `orgName = result.OrgName, orgLogoUrl = result.OrgLogoUrl` after `passwordRequired`.
8. **Unit tests** — MODIFY `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs`: `CreateHandler` passes a new `StubSenderBrandingStore` (settable `Row`, `Calls` counter) and a `StubFeaturesProvider` (plan code → `PlanFeatures`; pro/business Branding true, free false); `SetupStatus` gains an optional `Guid? ownerAppUserId` param. New facts:
   - `Branding_ProOwnerWithOrg_ReturnsOrgName` — AC-021-1 (OrgName "Studio Nova", From "Alice").
   - `Branding_FreeOwnerWithOrg_OrgNameNull` — AC-021-3 / EC-021-5.
   - `Branding_GuestTransfer_OrgNameNull_StoreNotCalled` — owner null.
   - `Branding_ProOwnerBlankOrgName_OrgNameNull`.
   - `Branding_ProfileChangedBetweenViews_SecondViewShowsCurrentName` — EC-021-1 (same handler, mutate stub row, second Handle returns the new name).
   - `Branding_LogoPathSet_OrgLogoUrlStillNull` — D-5.
   - `Branding_ExpiredTransfer_NoOrgName_StoreNotCalled` — terminal states carry no org.
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~GetPublicTransferHandlerTests.
9. **Integration tests** — MODIFY `tests/wa.api.integration/PublicTransferEndpointTests.cs:78`: expected list becomes `["from", "note", "files", "hasDownloadAll", "downloadsLeft", "passwordRequired", "orgName", "orgLogoUrl"]` and assert both are JSON null (guest-owned seed). CREATE `tests/wa.api.integration/PublicTransferBrandingTests.cs` (container + `WaDbContext` + `TestFactory` idiom of `PublicTransferEndpointTests.cs:25-47`; user seed idiom `PlanUsageEndpointTests.cs:147-162`; set feature.branding = "true" via the `SetGate` idiom `PlanCatalogIntegrationTests.cs:192-199` **before** the first request):
   - `ProOwnerWithOrg_GateOn_ReturnsOrgName` — AC-021-1 page half: orgName "Studio Nova", from "Alice", orgLogoUrl null.
   - `FreeOwnerWithOrg_ReturnsOrgNameNull` — AC-021-3.
   - `OrgNameChangedAfterSend_NextViewShowsCurrent` — EC-021-1 (update AppUser.OrgName in DB between two GETs).
   - `Migration_AddsNullableOrgColumns` — INFORMATION_SCHEMA.COLUMNS for AppUser: OrgName nvarchar(60) nullable, OrgLogoBlobPath varchar(400) nullable.
   Verify: dotnet test tests/wa.api.integration with test filter FullyQualifiedName~PublicTransfer (Docker running; covers both classes).
10. **Web store + page + css** — MODIFY `src/wa.web/src/features/recipient/RecipientStore.ts` (state fields, LOADING_SHAPE, body type, `set` with `?? null`), `src/wa.web/src/features/recipient/RecipientPage.tsx` (two selectors; both headings per D-6), `src/wa.web/src/features/recipient/recipient.css` (`.rp-org-logo { width: 40px; height: 40px; object-fit: contain; border-radius: var(--radius-sm); vertical-align: middle; margin-inline-end: 8px; }`).
11. **Web tests** — MODIFY `src/wa.web/src/features/recipient/RecipientPage.test.tsx`: `PublicTransferBody` gains `orgName?: string | null; orgLogoUrl?: string | null`. New cases: org set → `.rp-heading` textContent `'From: Anna K. · Studio Nova'` and no `.rp-org-logo` (EC-021-4); org null → `'From: Anna K.'` and no separator (AC-021-3); orgLogoUrl set → one `.rp-org-logo` img with that src inside the heading (D-5 slot); gated body with org → gate heading also shows the org. Verify: `cd src/wa.web; npm run lint; npm run test:run; npm run build`.
12. **Gate** — AGENT.md §4 (integration filtered as step 9; full suite runs at feature end per pitfalls).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/AppUser.cs` | MODIFY | Add nullable `OrgName`, `OrgLogoBlobPath` (D-1). |
| `src/wa.infrastructure/Persistence/EntityConfigurations/AppUserConfiguration.cs` | MODIFY | Column types NVARCHAR(60) / VARCHAR(400). |
| `src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs` | MODIFY | Regenerated by dotnet ef migrations add AddAppUserOrgBranding |
| `src/wa.application/UseCases/Transfers/ISenderBrandingStore.cs` | CREATE | Port + `SenderBrandingRow` (D-2). |
| `src/wa.infrastructure/Persistence/SenderBrandingStore.cs` | CREATE | AppUser⋈Plan AsNoTracking read, non-deleted only. |
| `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs` | MODIFY | Result + ctor + `ResolveOrgNameAsync` (D-3). |
| `src/wa.api/Program.cs` | MODIFY | Register port; append `orgName`, `orgLogoUrl` to endpoint 4 Active JSON. |
| `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs` | MODIFY | New stubs, ctor args, 7 branding facts. |
| `tests/wa.api.integration/PublicTransferEndpointTests.cs` | MODIFY | Line 78 property list + null asserts (stale fact). |
| `tests/wa.api.integration/PublicTransferBrandingTests.cs` | CREATE | AC-021-1, AC-021-3, EC-021-1, migration columns. |
| `src/wa.web/src/features/recipient/RecipientStore.ts` | MODIFY | `orgName`, `orgLogoUrl` state + body mapping. |
| `src/wa.web/src/features/recipient/RecipientPage.tsx` | MODIFY | Header "· {orgName}" + optional logo in both headings. |
| `src/wa.web/src/features/recipient/recipient.css` | MODIFY | `.rp-org-logo` rule. |
| `src/wa.web/src/features/recipient/RecipientPage.test.tsx` | MODIFY | Body type + 4 header cases. |

Generated, also in the boundary (path known only after generation): src/wa.infrastructure/Persistence/Migrations/(timestamp)_AddAppUserOrgBranding.cs and its .Designer.cs.

**`src/wa.domain/AppUser.cs`** (whole file, `:4-18`) — append the two properties after `DeletedAtUtc`. Adding nullable properties breaks no object initializer (seeds in tests set only existing members).

**`AppUserConfiguration.cs`** — anchor, `:37-42`:
```csharp
        // VARCHAR(16) NOT NULL CONSTRAINT DF_AppUser_Theme DEFAULT 'system'
        var theme = b.Property(u => u.Theme);
        theme.HasColumnType("VARCHAR(16)").HasDefaultValue("system").IsRequired();
        // TA-3.2 names the default constraint DF_AppUser_Theme — EF needs an explicit name per column to match verbatim (AC-052-4).
        theme.Metadata.SetAnnotation("Relational:DefaultConstraintName", "DF_AppUser_Theme");
    }
```

**`GetPublicTransferQuery.cs`** — result record `:23-31`, ctor `:45-57`, Active return `:89-99`:
```csharp
public sealed record GetPublicTransferResult(
    PublicTransferState State,
    string? From = null,            // Active only
    ...
    bool PasswordRequired = false,  // Active only
    string? SenderEmail = null);    // terminal bodies render it into Problem+JSON details (FR-003-8/9)
```
```csharp
        var limits = await _limits.Resolve(ILimitsProvider.GuestPlanCode, cancellationToken);
        return new GetPublicTransferResult(
            PublicTransferState.Active,
            From: transfer.SenderName,
            Note: transfer.Note,
            Files: files,
            HasDownloadAll: transfer.FileCount >= 2 && transfer.TotalBytes <= limits.MaxZipSize,
            DownloadsLeft: transfer.MaxDownloads < 0 ? null : transfer.MaxDownloads - transfer.DownloadsCount,
            PasswordRequired: passwordRequired);
```
Append the new record members **after** `SenderEmail` (named-argument callers only; terminal returns at `:74,78,81` stay unchanged). `using wa.application.Limits;` is already present (`:3`) for `IFeaturesProvider`. Only constructor caller outside DI: `GetPublicTransferHandlerTests.cs:28-33`.

**`src/wa.api/Program.cs`** — registration anchor `:74`:
```csharp
builder.Services.AddSingleton<wa.application.UseCases.Transfers.IPublicTransferStore>(sp => new wa.infrastructure.Persistence.PublicTransferStore(sp.GetRequiredService<WaDbContext>()));
```
Wire anchor `:390-395`:
```csharp
        PublicTransferState.Active => Results.Json(new
        {
            from = result.From, note = result.Note,
            files = result.Files!.Select(f => new { fileId = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes }),
            hasDownloadAll = result.HasDownloadAll, downloadsLeft = result.DownloadsLeft, passwordRequired = result.PasswordRequired
        }),
```
`IFeaturesProvider` is already a singleton (`:103`), so MediatR resolves the new ctor without further wiring.

**`GetPublicTransferHandlerTests.cs`** — `CreateHandler` `:28-33` (add the two stubs between `_minter` and the logger); `SetupStatus` `:344-362` (add `OwnerAppUserId = ownerAppUserId`); stub section `:318-390` — add the two private stubs there (`IFeaturesProvider` has one member, `Resolve`; `ISenderBrandingStore` one member, `GetAsync`).

**`PublicTransferEndpointTests.cs:78`**:
```csharp
        Assert.Equal(["from", "note", "files", "hasDownloadAll", "downloadsLeft", "passwordRequired"],
            root.EnumerateObject().Select(p => p.Name).ToArray());
```

**`RecipientStore.ts`** — state interface `:135-170` (add after `passwordRequired`), LOADING_SHAPE `:198-217`, success body + set `:268-286`:
```ts
        const body = (await res.json()) as {
          from: string
          ...
          passwordRequired: boolean
        }
        set({
          status: 'active',
          from: body.from,
          ...
          passwordRequired: body.passwordRequired,
```
Body type gains `orgName?: string | null; orgLogoUrl?: string | null`; set `orgName: body.orgName ?? null, orgLogoUrl: body.orgLogoUrl ?? null`.

**`RecipientPage.tsx`** — selectors next to `:46` (`const from = useRecipientStore((s) => s.from)`); headings `:210` (gate) and `:262` (active):
```tsx
        <h1 className="rp-heading">{i18n.t('recipient.gate.fromHeading')}{from}</h1>
      <h1 className="rp-heading">{i18n.t('recipient.active.fromHeading')}{from}</h1>
```
**`recipient.css`** — add the new rule after `.rp-heading` (`:17-22`); `--radius-sm` is defined at `src/wa.web/src/index.css:14`.

**`RecipientPage.test.tsx`** — `PublicTransferBody` `:17-24`, `activeBody` `:78-89` (leave its defaults; pass org via overrides; use `from: 'Anna K.'` in the new cases).

## 4. ACs / ECs → tests

| ID | Test |
|---|---|
| AC-021-1 (page half; logo deferred, D-5) | `Branding_ProOwnerWithOrg_ReturnsOrgName`, `ProOwnerWithOrg_GateOn_ReturnsOrgName`, vitest org-set header case |
| AC-021-3 | `Branding_FreeOwnerWithOrg_OrgNameNull`, `FreeOwnerWithOrg_ReturnsOrgNameNull`, vitest org-null case |
| EC-021-1 | `Branding_ProfileChangedBetweenViews_SecondViewShowsCurrentName`, `OrgNameChangedAfterSend_NextViewShowsCurrent` |
| EC-021-4 | vitest org-set case asserts no `.rp-org-logo` |
| EC-021-5 | covered by the Free-plan cases (gate is plan-read at render time) |
| Schema | `Migration_AddsNullableOrgColumns` |

## 5. Open Questions / Risks

```
DECISION: Add branding_applied and branding_logo_missing to the closed TA-10.2 telemetry set?
CONTEXT: F-PRF-001 technical notes and EC-021-2 name both events, but TelemetryEvents.cs is a closed set (golden rule 6, EC-056-4: add to TA-10.2 first). This plan emits neither.
OPTIONS:
  A) Defer — add both in US-021-04 (logo path, where branding_logo_missing becomes reachable) with the TA-10.2 + REPO-FACTS update.
  B) Add both now in this story — grows the closed set before the logo exists; branding_logo_missing would be unreachable.
RECOMMEND: A — only branding_applied is reachable now, and the closed-set change belongs with the event that makes it meaningful.
BLOCKS: none (this plan implements A)
```

```
DECISION: Record the AppUser OrgName / OrgLogoBlobPath columns as an ADR line + TA-3.2 DDL update?
CONTEXT: AGENT.md §7 / F-PRF-001 technical notes require an ADR note for the schema change; TA-3.2 is "authoritative; EF migrations must match" and lacks both columns.
OPTIONS:
  A) Orchestrator docs close-out adds TA-3.2 lines (OrgName NVARCHAR(60) NULL, OrgLogoBlobPath VARCHAR(400) NULL) + one ADR/Part-3 log line.
  B) Hold the migration until a separate ADR task lands.
RECOMMEND: A — the spec already sanctions the change; only the record is missing.
BLOCKS: none
```

- **Risk — CSP blocks the future logo.** TA-9.5 `img-src 'self' data:` will block a blob SAS logo URL; US-021-04 must escalate the CSP/TA-3.6 change (security-shaped). Not reachable here (D-5).
- **Risk — wire contract change.** TA-4.2#4 and `docs/REPO-FACTS.md` endpoint shape need the two new fields in docs close-out.
- **Open — PII lifecycle.** OrgName is PII (FR-021-6) but is not yet included in the data export (`DataExportStore.cs:99`) or erasure scrub; recommend folding into US-021-04 or an F-XCT follow-up, not here.
- **Risk — gate seeded off.** Branding is invisible in every environment until feature.branding is flipped (TA-13.2); intended, but QA must flip it.
- **verify_plan.py**: test filters are written in words (CSS-token misread quirk, `US-020-01-plan.md`).
