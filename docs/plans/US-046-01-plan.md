# US-046-01 plan — Turn off my notification emails (F-XCT-003, milestone T-067 slice)

## 1. Goal

A signed-in user with feature.emailSettings on can flip one Profile switch that PATCHes `emailNotifications` onto a new nullable `AppUser.EmailNotificationsEnabled` column (NULL = on), GET /auth/me returns the effective value, and the f-email worker gets a send-time `OwnerNotificationGate` that suppresses owner notifications (Information log `suppressedReason=email_setting`, no event) — **exit check:** gate + PATCH/GET tests green, toggle absent when the flag is off, full §4 gate green.

**In scope:** FR-046-1, FR-046-2, FR-046-3 (gate), FR-046-5, FR-046-6, FR-046-7 (by construction: column lives on the AppUser row); AC-046-1, AC-046-2 (at the gate — see DECISION 1); EC-046-1, EC-046-2, EC-046-4, EC-046-5. **Out:** US-046-02 (recipient/billing exemption proof through the real handlers, AC-046-3), FR-046-8 digest (P2), Playwright E2E.

## 2. Design & Decisions

**Verified on disk.** Gate key constant `FeatureGates.EmailSettings` exists (`src/wa.application/Limits/FeatureGates.cs:11`) and is seeded (FeatureFlag seed rows); client gate key is already in FEATURE_GATE_KEYS (`src/wa.web/src/core/features/featureGates.ts:18`). f-email dispatches only TransferCreated (recipient emails), MagicLinkRequested (auth) and BillingEmailRequested (billing) (`src/wa.workers/EmailFunction.cs:67`) — **no owner-addressed notification email exists yet**, and the recipient handler already never mails the sender (`src/wa.application/UseCases/Transfers/SendNotificationEmails.cs:131`, FR-006-3). Hence DECISION 1 (non-blocking, option A).

**D1 — Column + migration (golden rule 4, TA-2.3, TA-3.2).** `AppUser.EmailNotificationsEnabled` as `bool?` (BIT NULL, no default constraint): NULL = never touched = on, false = off, true = explicitly on (FR-046-1 three-state). The spec itself mandates the column (FR-046-1 + F-XCT-003 technical notes, T-067 row) so it is not an "unspecced schema change" (AGENT.md §7). Pure domain helper `AppUser.EmailNotificationsOn(bool? stored) => stored ?? true` — the single NULL→true rule used by GET, the gate and (as COALESCE in SQL) the data export. Account deletion soft-deletes the row and a re-signup inserts a fresh row → NULL → on (EC-046-2, FR-046-7); nothing extra to build.

**D2 — One narrow port, two adapters' worth of use (TA-2.3, codebase-design: deep + narrow).** `IEmailNotificationsStore` in new folder src/wa.application/UseCases/EmailSettings/ with `SetAsync(userId, bool enabled)` and `GetAsync(userId) → bool?`. Kept separate from `IAppUserStore` for the same reason `IOrgNameStore` exists (`src/wa.application/UseCases/Auth/IOrgNameStore.cs:7` — the many hand-rolled IAppUserStore stubs stay untouched). Adapter `EmailNotificationsStore` over `IDbContextFactory<WaDbContext>` (fresh context per call — safe under f-email's concurrency 10; both hosts already register the factory: `src/wa.api/Program.cs:43`, `src/wa.workers/WorkerServices.cs:67`). Writes use ExecuteUpdateAsync guarded by `DeletedAtUtc == null` (OrgNameStore idiom, `src/wa.infrastructure/Persistence/OrgNameStore.cs:14`). Reads return null for "unknown/deleted row" and for "column NULL" — both mean on.

**D3 — PATCH/GET /auth/me (TA-4.2 endpoint 13, TA-4.2a single construction site).** Additive field, extend-don't-fork: `UpdateProfileRequest` and `UpdateProfileCommand` gain trailing `bool? EmailNotifications = null` (absent = unchanged, FR-046-2 / EC-046-5 per-field last-write-wins); the empty-PATCH rule counts it as a field; `GetProfileResult` gains trailing `bool EmailNotifications = true` filled via `AppUser.EmailNotificationsOn`. GET body dictionary and PATCH anonymous body both add `emailNotifications`. No test asserts the exact /auth/me key set (grep of tests/wa.api.integration for EnumerateObject found none on /auth/me).

**D4 — Gate on the write path (FR-046-6).** In the PATCH endpoint (not the handler): when `req.EmailNotifications` is non-null and `IFlagResolver.ResolveFlag(FeatureGates.EmailSettings, FeatureGates.DefaultWhenAbsent)` is false → `BusinessException(ProblemCodes.FeatureDisabled, …)` → 404 `FEATURE_DISABLED`, exactly what `.RequireFeature` throws (`src/wa.api/Middleware/FeatureGateEndpointFilter.cs:24`). Endpoint-level keeps the gate where US-044-02 put it (API filter = authority) and leaves the handler's DI shape alone. A PATCH without the field is never gated (name/theme/orgName keep working with the flag off). GET always returns the effective column value (UI hides it when off).

**D5 — Send-time gate (FR-046-3/5/6, EC-046-1, EC-046-4).** `OwnerNotificationGate` (application, MediatR-free, no publisher/telemetry dependency — so it *cannot* emit `email_sent` or any event, keeping TA-10.2 / TA-5.3 closed). `MaySendAsync(Guid? ownerAppUserId)`: null owner (guest) → true without reads; flag off → true without a store read (column inert); else read the column **on every call** (no cache — EC-046-1 one-message window) and return `EmailNotificationsOn(stored)`; on false log once at Information with literal text `suppressedReason=email_setting` and the owner id (a Guid — TA-9.4 and the R7 log scan allow ids, never the email). Registered in f-email DI; worker also gets an `IFlagResolver` registration (the API maps it at `src/wa.api/Program.cs:113`, the worker does not yet). Future owner-email handlers call it before sending — see DECISION 1.

**D6 — Data export follows the column (FR-047-1 accuracy).** The US-047-01 architect left a contract note for T-067 (`docs/plans/US-047-01-plan.md`, Open Questions): `emailNotifications` in profile.json is a constant true (`src/wa.application/UseCases/DataExport/DataExportArchive.cs:41`). `ExportProfile` gains trailing `bool EmailNotifications = true`; the snapshot query projects `u.EmailNotificationsEnabled ?? true` (SQL COALESCE — the static helper is not translatable); the archive writes the field. Three lines + one test; no shape change.

**D7 — Profile UI (UI-Reference §5.5, US-044-02 absent-not-disabled).** New `EmailNotificationsSection` mounted inside `<FeatureGate gate="feature.emailSettings">` in ProfilePage (DataExportSection precedent, `src/wa.web/src/features/profile/ProfilePage.tsx:132`). Own mount GET /auth/me (OrgNameSection precedent — StoredProfile carries no such field), renders nothing until loaded / on read failure. Native `input type="checkbox" role="switch"` (AdminFlagsPage precedent), immediate PATCH on change (optimistic, reverted on failure), success toast `.dz-toast` with `toastRole('success')` (`src/wa.web/src/core/ui/toastRole.ts:9`), inline `.pf-error` on failure. Helper copy names both exemptions (FR-046-4). New CSS `.pf-switch` / `.pf-switch-input` — the input class declares min 44 px both ways so `scripts/check-tap-targets.mjs` (profile is a P0 surface) passes at all widths. i18n: 5 new keys in all 8 bundles (key-set parity test, `src/wa.web/src/core/i18n/init.test.ts:51`).

## 3. Implementation Plan

1. **Domain + EF config + migration.**
   - `src/wa.domain/AppUser.cs`: add property + static helper (snippet below).
   - `src/wa.infrastructure/Persistence/EntityConfigurations/AppUserConfiguration.cs`: map the column as BIT, nullable, no default.
   - Generate: `dotnet ef migrations add AddAppUserEmailNotificationsEnabled` with the project/startup-project flags from AGENT.md §5.3. Verify the generated Up() contains exactly one AddColumn (table AppUser, type bit, nullable: true, no defaultValue) and Down() one DropColumn; nothing else may appear (if it does, stop — model drift).
   - Test: CREATE `tests/wa.domain.unit/Auth/AppUserEmailNotificationsTests.cs` — Theory: null→true, false→false, true→true (FR-046-1/2).
   - Verify: `dotnet build src/wa.api` and `dotnet test tests/wa.domain.unit`.
2. **Port + adapter + gate (application/infrastructure).**
   - CREATE `src/wa.application/UseCases/EmailSettings/IEmailNotificationsStore.cs`, `src/wa.application/UseCases/EmailSettings/OwnerNotificationGate.cs`, `src/wa.infrastructure/Persistence/EmailNotificationsStore.cs` (snippets below).
   - CREATE `tests/wa.application.unit/EmailSettings/StubEmailNotificationsStore.cs` (public sealed, namespace wa.application.unit.EmailSettings): `Dictionary<Guid, bool?> Stored`, `List<(Guid UserId, bool Enabled)> Writes`, `int Reads`, optional `Action<Guid, bool>? OnSet`; SetAsync records + sets Stored + invokes OnSet; GetAsync increments Reads and returns Stored value or null.
   - CREATE `tests/wa.application.unit/EmailSettings/OwnerNotificationGateTests.cs` (use `CapturingLogger<OwnerNotificationGate>`, `tests/wa.application.unit/CapturingLogger.cs:9`; a private StubFlagResolver copied from `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs:652`):
     - Guest_NullOwner_MaySend_NoStoreRead (FR-046-5).
     - FlagOff_StoredFalse_MaySend_NoStoreRead (FR-046-6, EC-046-4); also flag key absent → same (DefaultWhenAbsent).
     - FlagOn_StoredNull_MaySend and FlagOn_StoredTrue_MaySend (AC-046-1 second scenario: turned back on → sent).
     - FlagOn_StoredFalse_Suppressed_LogsInformationOnce: result false; exactly one entry, level Information, message contains `suppressedReason=email_setting` and the owner Guid, contains no "@" (AC-046-2, TA-9.4).
     - ReadsSettingOnEveryCall: Stored false → false; flip Stored to true → true; Reads == 2 (EC-046-1 send-time read).
   - Verify: `dotnet test tests/wa.application.unit` (filter FullyQualifiedName~EmailSettings is enough while iterating).
3. **PATCH/GET use cases.** Modify `src/wa.application/UseCases/Auth/MeProfile.cs` per the snippets below (command, request, result, empty-patch message, handler ctor + persist, both result constructions). Update the four existing UpdateProfileHandler construction sites to pass `new StubEmailNotificationsStore()` (`tests/wa.application.unit/Auth/MeProfileTests.cs:78`, `:99`, `:112`; `tests/wa.application.unit/Auth/MeProfileOrgNameTests.cs:25`) and add `using wa.application.unit.EmailSettings;` to both files.
   - CREATE `tests/wa.application.unit/Auth/MeProfileEmailNotificationsTests.cs` (reuse the Users/Plans/StubPlanContext pattern of MeProfileOrgNameTests — copy its private stubs; wire `OnSet = (id, v) => user.EmailNotificationsEnabled = v` so ResolveResult reflects the write):
     - Patch_False_WritesOnce_ResultFalse (AC-046-1 step 1).
     - Patch_True_WritesTrue_ResultTrue (AC-046-1 scenario 2).
     - Patch_ThemeOnly_DoesNotWriteSetting (FR-046-2 additive; Writes empty, result reflects stored value).
     - Patch_OnlyEmailNotifications_IsNotEmptyPatch (no MeProfileException).
     - Patch_AllFieldsAbsent_StillEmptyPatchValidation.
     - GetProfile_NullColumn_ReturnsTrue / GetProfile_False_ReturnsFalse (FR-046-2 effective value, EC-046-2).
   - Verify: `dotnet test tests/wa.application.unit`.
4. **API wiring.** `src/wa.api/Program.cs`: register the store after L86; GET body adds the key; PATCH lambda takes `IFlagResolver flags`, applies the D4 gate before `try`, passes `req.EmailNotifications`, response adds the key (snippets below). Verify: `dotnet build src/wa.api`.
5. **Worker wiring.** `src/wa.workers/WorkerServices.cs`: register IFlagResolver, the store and the gate (snippet below). Add fact `OwnerNotificationGate_Resolves` to `tests/wa.workers.unit/WorkerServicesTests.cs` (copy the GdprSweepHandler_Resolves shape at L89). Verify: `dotnet test tests/wa.workers.unit`.
6. **Data export (D6).** Modify `src/wa.application/UseCases/DataExport/IDataExportStore.cs:47`, `src/wa.infrastructure/Persistence/DataExportStore.cs:99`, `src/wa.application/UseCases/DataExport/DataExportArchive.cs:41`. Add fact `Profile_EmailNotificationsFollowsSnapshot` to `tests/wa.application.unit/DataExport/DataExportArchiveTests.cs`: `Profile() with { EmailNotifications = false }` → profile.json emailNotifications false; existing L42 assertion (default true) and key count 7 stay valid.
7. **Integration test.** CREATE `tests/wa.api.integration/EmailNotificationsSettingTests.cs` — clone the harness of `tests/wa.api.integration/DataExportEndpointTests.cs` (Azurite + SQL per fact, seed one AppUser, `StartAsync(flagOn)` sets the `FeatureGates.EmailSettings` row then builds the factory, forged wa.session cookie; ≤ 4 requests per fact for the 5/min auth rate rule). Request helper takes an optional JSON body (StringContent, application/json) and targets /api/v1/auth/me. Facts:
   - NewUser_ColumnIsNullableBitAndNull_GetReturnsTrue (EC-046-2): INFORMATION_SCHEMA.COLUMNS for AppUser.EmailNotificationsEnabled → DATA_TYPE bit, IS_NULLABLE YES (raw `SqlQueryRaw<string>` with column alias Value); seeded row reads null; GET 200 emailNotifications true.
   - Patch_FlagOn_False_PersistsFalse_GetReturnsFalse (AC-046-1): PATCH emailNotifications false → 200 body emailNotifications false; DB row false (fresh AsNoTracking read); GET false; PATCH true → DB true.
   - Patch_ThemeOnly_LeavesSettingUnchanged (FR-046-2, EC-046-5): seed false; PATCH theme dark → 200, body emailNotifications false, DB still false.
   - Patch_FlagOff_Returns404FeatureDisabled_NoWrite (FR-046-6): status 404, code `FEATURE_DISABLED`, DB still null; and a theme-only PATCH with the flag off → 200.
   - Gate_RealStore_ReadsAtSendTime (AC-046-2, EC-046-1, no host): `new OwnerNotificationGate(new EmailNotificationsStore(new AppDbContextFactory(options)), <inline flag stub returning true>, NullLogger<OwnerNotificationGate>.Instance)`; row false → false; update row to true → true; unknown Guid → true.
   - Verify: Docker running; `dotnet test tests/wa.api.integration` with filter FullyQualifiedName~EmailNotificationsSettingTests (full suite runs once at feature end — pitfalls.md).
8. **Web.** CREATE `src/wa.web/src/features/profile/EmailNotificationsSection.tsx` (snippet below); mount it in ProfilePage inside a FeatureGate after the DataExportSection block; add the CSS; add the 5 keys to all 8 bundles directly after the `"profile.orgName.invalid"` line (L247 in every bundle).
   - CREATE `src/wa.web/src/features/profile/EmailNotificationsSection.test.tsx` (harness of `src/wa.web/src/features/profile/OrgNameSection.test.tsx:16`): renders checked for true / unchecked for false / checked when the field is missing; renders nothing on GET 404; toggle off → one PATCH to ME_BASE with method PATCH, credentials include, body exactly {"emailNotifications":false}, switch unchecked, role=status toast text "Notification preferences saved." (AC-046-1); toggle back on → body {"emailNotifications":true} (scenario 2); PATCH 500 → switch reverts to checked, role=alert error, no toast; helper text contains "Recipients" and "billing" (FR-046-4).
   - `src/wa.web/src/features/profile/ProfilePage.test.tsx`: in a new describe next to L391, "gate off (default): the notification switch is absent" → no `#pf-notify-switch` (US-044-02 contract).
   - Verify (in src/wa.web): `npm run lint && npm run test:run && npm run build`.
9. **Full §4 gate** (AGENT.md §4): domain/application/workers unit, integration (filtered as above), web lint/test/build, `node ../../scripts/check-theme-tokens.mjs`, `node ../../scripts/check-contrast.mjs`, `node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest` (the new log line must pass R7). One `feat: US-046-01 …` commit with exactly the files below + the two generated migration files.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/AppUser.cs` | MODIFY | Add `bool? EmailNotificationsEnabled` + static `EmailNotificationsOn(bool?)` |
| `src/wa.infrastructure/Persistence/EntityConfigurations/AppUserConfiguration.cs` | MODIFY | Map EmailNotificationsEnabled as BIT NULL, no default |
| `src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs` | MODIFY | Regenerated by dotnet ef migrations add AddAppUserEmailNotificationsEnabled (never hand-edit) |
| `src/wa.application/UseCases/EmailSettings/IEmailNotificationsStore.cs` | CREATE | Narrow port: SetAsync / GetAsync |
| `src/wa.application/UseCases/EmailSettings/OwnerNotificationGate.cs` | CREATE | Send-time owner-notification gate (guest/flag/column, Information log) |
| `src/wa.infrastructure/Persistence/EmailNotificationsStore.cs` | CREATE | Adapter over IDbContextFactory (ExecuteUpdate write, untracked read) |
| `src/wa.application/UseCases/Auth/MeProfile.cs` | MODIFY | Command/request/result field, empty-patch rule, handler ctor + persist, result mapping |
| `src/wa.api/Program.cs` | MODIFY | Register store; GET/PATCH body key; PATCH flag gate (404 FEATURE_DISABLED) |
| `src/wa.workers/WorkerServices.cs` | MODIFY | Register IFlagResolver, store, OwnerNotificationGate |
| `src/wa.application/UseCases/DataExport/IDataExportStore.cs` | MODIFY | ExportProfile trailing `bool EmailNotifications = true` |
| `src/wa.infrastructure/Persistence/DataExportStore.cs` | MODIFY | Project `u.EmailNotificationsEnabled ?? true` into ExportProfile |
| `src/wa.application/UseCases/DataExport/DataExportArchive.cs` | MODIFY | Write snapshot value instead of the constant |
| `src/wa.web/src/features/profile/EmailNotificationsSection.tsx` | CREATE | Profile switch, immediate PATCH, toast/error |
| `src/wa.web/src/features/profile/ProfilePage.tsx` | MODIFY | Import + mount section inside FeatureGate feature.emailSettings |
| `src/wa.web/src/index.css` | MODIFY | `.pf-switch`, `.pf-switch-input` (44 px floor) after `.pf-error` |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | 5 profile.notifications keys after L247 |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | same 5 keys (German) |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | same 5 keys (Spanish) |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | same 5 keys (French) |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | same 5 keys (Italian) |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | same 5 keys (Dutch) |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | same 5 keys (Portuguese) |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | same 5 keys (Turkish) |
| `tests/wa.domain.unit/Auth/AppUserEmailNotificationsTests.cs` | CREATE | NULL→true / false / true theory |
| `tests/wa.application.unit/EmailSettings/StubEmailNotificationsStore.cs` | CREATE | Shared hand-rolled port stub |
| `tests/wa.application.unit/EmailSettings/OwnerNotificationGateTests.cs` | CREATE | Gate facts (AC-046-2, EC-046-1/4, FR-046-5) |
| `tests/wa.application.unit/Auth/MeProfileEmailNotificationsTests.cs` | CREATE | PATCH/GET facts (AC-046-1, FR-046-2) |
| `tests/wa.application.unit/Auth/MeProfileTests.cs` | MODIFY | Pass StubEmailNotificationsStore at the 3 ctor sites |
| `tests/wa.application.unit/Auth/MeProfileOrgNameTests.cs` | MODIFY | Pass StubEmailNotificationsStore at the ctor site |
| `tests/wa.application.unit/DataExport/DataExportArchiveTests.cs` | MODIFY | Add emailNotifications-false fact |
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | Add OwnerNotificationGate_Resolves |
| `tests/wa.api.integration/EmailNotificationsSettingTests.cs` | CREATE | Migration/PATCH/GET/flag-off/real-store gate facts |
| `src/wa.web/src/features/profile/EmailNotificationsSection.test.tsx` | CREATE | Section behavior facts |
| `src/wa.web/src/features/profile/ProfilePage.test.tsx` | MODIFY | Gate-off absence fact |

Generated, also in the commit boundary (path known only after generation): src/wa.infrastructure/Persistence/Migrations/(timestamp)_AddAppUserEmailNotificationsEnabled.cs and its .Designer.cs.

**`src/wa.domain/AppUser.cs`** — append after L23 (`OrgLogoBlobPath`):
```csharp
    /// <summary>F-XCT-003 FR-046-1: owner notification emails. Null = never touched = on; false = off; true = explicitly on.</summary>
    public bool? EmailNotificationsEnabled { get; set; }

    /// <summary>FR-046-2: the effective setting — null means on. The single NULL→true rule (GET, f-email gate).</summary>
    public static bool EmailNotificationsOn(bool? stored) => stored ?? true;
```

**`src/wa.infrastructure/Persistence/EntityConfigurations/AppUserConfiguration.cs`** — after L45 (`OrgLogoBlobPath` mapping):
```csharp
        // F-XCT-003 (FR-046-1): BIT NULL, no default — null = on; the three states stay distinguishable.
        b.Property(u => u.EmailNotificationsEnabled).HasColumnType("BIT");
```

**New port / gate / adapter:**
```csharp
// src/wa.application/UseCases/EmailSettings/IEmailNotificationsStore.cs
namespace wa.application.UseCases.EmailSettings;

/// <summary>F-XCT-003 (FR-046-1/2/3, TA-2.3): narrow port for AppUser.EmailNotificationsEnabled — separate from IAppUserStore (IOrgNameStore precedent).</summary>
public interface IEmailNotificationsStore
{
    /// <summary>Store the explicit choice for a non-deleted user. A missing row is a no-op.</summary>
    Task SetAsync(Guid userId, bool enabled, CancellationToken ct);

    /// <summary>Stored value of a non-deleted user; null when the column is null OR the row is unknown/deleted (both mean on).</summary>
    Task<bool?> GetAsync(Guid userId, CancellationToken ct);
}
```
```csharp
// src/wa.application/UseCases/EmailSettings/OwnerNotificationGate.cs
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.domain;

namespace wa.application.UseCases.EmailSettings;

/// <summary>
/// F-XCT-003 (FR-046-3/5/6, EC-046-1): f-email's send-time check for OWNER notification emails only — recipient, auth and
/// billing emails never call it (FR-046-4). No publisher or telemetry dependency: a skipped send emits nothing (TA-10.2 closed list).
/// </summary>
public sealed class OwnerNotificationGate
{
    private readonly IEmailNotificationsStore _store;
    private readonly IFlagResolver _flags;
    private readonly ILogger<OwnerNotificationGate> _logger;

    public OwnerNotificationGate(IEmailNotificationsStore store, IFlagResolver flags, ILogger<OwnerNotificationGate> logger)
    {
        _store = store;
        _flags = flags;
        _logger = logger;
    }

    /// <summary>True when the owner notification may be sent. Reads the column on every call (send time, no cache).</summary>
    public async Task<bool> MaySendAsync(Guid? ownerAppUserId, CancellationToken ct)
    {
        if (ownerAppUserId is not { } ownerId)
            return true; // FR-046-5: guest transfers have no setting
        if (!await _flags.ResolveFlag(FeatureGates.EmailSettings, FeatureGates.DefaultWhenAbsent, ct))
            return true; // FR-046-6 / EC-046-4: flag off = column inert
        if (AppUser.EmailNotificationsOn(await _store.GetAsync(ownerId, ct)))
            return true;

        _logger.LogInformation("f-email: owner notification skipped for {OwnerAppUserId}, suppressedReason=email_setting", ownerId);
        return false;
    }
}
```
```csharp
// src/wa.infrastructure/Persistence/EmailNotificationsStore.cs
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.EmailSettings;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>F-XCT-003: AppUser.EmailNotificationsEnabled read/write — fresh context per call (safe under f-email concurrency). Singleton in both hosts.</summary>
public sealed class EmailNotificationsStore : IEmailNotificationsStore
{
    private readonly IDbContextFactory<WaDbContext> _factory;

    public EmailNotificationsStore(IDbContextFactory<WaDbContext> factory) => _factory = factory;

    public async Task SetAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<AppUser>()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailNotificationsEnabled, (bool?)enabled), ct);
    }

    public async Task<bool?> GetAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .Select(u => u.EmailNotificationsEnabled)
            .FirstOrDefaultAsync(ct);
    }
}
```

**`src/wa.application/UseCases/Auth/MeProfile.cs`** — current code at L19, L58, L62-68, L96-98, L129, L139-156, L166, L197-201, L217 (shown verbatim in the Architect's grounding; the Coder reads the file once). Changes:
- add `using wa.application.UseCases.EmailSettings;`
- L19 → `public sealed record UpdateProfileCommand(Guid UserId, string? DisplayName, string? Theme, OrgNamePatch OrgName = default, bool? EmailNotifications = null) : IRequest<GetProfileResult>;`
- L58 → `public sealed record UpdateProfileRequest(string? DisplayName, string? Theme, OrgNamePatch OrgName = default, bool? EmailNotifications = null);` (update the L57 doc comment's construction-site text to include req.EmailNotifications)
- L62-68 `GetProfileResult`: add trailing parameter `bool EmailNotifications = true` after `string? OrgName = null`.
- L98 message → `"Provide at least one of displayName, theme, orgName or emailNotifications."` (no test pins the old text).
- L129 and L217 → `new GetProfileResult(user.Id, user.Email, user.DisplayName, user.Theme, plan.Code, user.OrgName, AppUser.EmailNotificationsOn(user.EmailNotificationsEnabled))`.
- Handler: field `private readonly IEmailNotificationsStore _emailNotifications;`, ctor becomes `UpdateProfileHandler(IAppUserStore users, IPlansStore plans, IOrgNameStore orgNames, IPlanContextAccessor plan, IEmailNotificationsStore emailNotifications)`.
- L166 → `if (request.DisplayName is null && request.Theme is null && !request.OrgName.IsSet && request.EmailNotifications is null)`.
- After L201 add:
```csharp
        // F-XCT-003 (FR-046-2): additive — absent leaves the column alone; the flag gate lives at the endpoint (FR-046-6).
        if (request.EmailNotifications is { } emailNotifications)
            await _emailNotifications.SetAsync(request.UserId, emailNotifications, cancellationToken);
```

**`src/wa.api/Program.cs`** — existing code:
```csharp
86: builder.Services.AddSingleton<wa.application.UseCases.Auth.IOrgNameStore>(sp => new wa.infrastructure.Persistence.OrgNameStore(sp.GetRequiredService<WaDbContext>()));
...
1085:         ["orgName"] = result.OrgName! // F-PRF-001 (FR-021-1): null when unset; the profile page reads it
1086:     };
...
1099: app.MapPatch("/api/v1/auth/me", async (UpdateProfileRequest req, HttpContext ctx, ISender sender, CancellationToken ct) =>
1100: {
1101:     if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
1102:         return Results.Json(ProblemWriter.Build(401, ...), statusCode: 401);
1103:
1104:     try
1105:     {
1107:         var result = await sender.Send(new UpdateProfileCommand(userId, req.DisplayName, req.Theme, req.OrgName), ct);
1108:         return Results.Json(new
1109:         {
...
1115:             orgName = result.OrgName
1116:         }, statusCode: 200);
```
Changes: after L86 register `builder.Services.AddSingleton<wa.application.UseCases.EmailSettings.IEmailNotificationsStore>(sp => new wa.infrastructure.Persistence.EmailNotificationsStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));` (comment `// F-XCT-003: AppUser.EmailNotificationsEnabled port (PATCH /auth/me emailNotifications).`). L1085: add a comma and a new line `["emailNotifications"] = result.EmailNotifications // F-XCT-003 (FR-046-2): effective value, null → true`. L1099: lambda parameters become `(UpdateProfileRequest req, HttpContext ctx, ISender sender, IFlagResolver flags, CancellationToken ct)`; between L1102 and L1104 insert:
```csharp
    // F-XCT-003 (FR-046-6): the setting is editable only while feature.emailSettings is on — same 404 as .RequireFeature.
    if (req.EmailNotifications is not null && !await flags.ResolveFlag(FeatureGates.EmailSettings, FeatureGates.DefaultWhenAbsent, ct))
        throw new BusinessException(ProblemCodes.FeatureDisabled, ProblemCodes.TitleOf(ProblemCodes.FeatureDisabled));
```
L1107 passes `req.EmailNotifications` as 5th argument; L1115 → `orgName = result.OrgName,` plus `emailNotifications = result.EmailNotifications`. IFlagResolver/FeatureGates (wa.application.Limits) are already used unqualified in Program.cs (L113, L1395) — no new using.

**`src/wa.workers/WorkerServices.cs`** — existing:
```csharp
54:         services.AddSingleton<ILimitsProvider>(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
...
147:         services.AddSingleton<IEmailStore>(sp => new EmailStore(sp.GetRequiredService<WaDbContext>()));
```
After L54 add `services.AddSingleton<IFlagResolver>(sp => (IFlagResolver)sp.GetRequiredService<ILimitsProvider>()); // LimitsProvider implements both (API precedent Program.cs L113)`. After L147 add (the factory is registered at L67; add `using wa.application.UseCases.EmailSettings;`):
```csharp
        // F-XCT-003 (FR-046-3): send-time owner-notification gate — every owner email handler calls MaySendAsync before sending.
        services.AddSingleton<IEmailNotificationsStore>(sp => new EmailNotificationsStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        services.AddSingleton<OwnerNotificationGate>(sp => new OwnerNotificationGate(
            sp.GetRequiredService<IEmailNotificationsStore>(),
            sp.GetRequiredService<IFlagResolver>(),
            sp.GetRequiredService<ILogger<OwnerNotificationGate>>()));
```

**Data export** — `src/wa.application/UseCases/DataExport/IDataExportStore.cs:47` → `public sealed record ExportProfile(string Email, string DisplayName, string Theme, string PlanCode, DateTime CreatedAtUtc, string? OrgName = null, bool EmailNotifications = true);` · `src/wa.infrastructure/Persistence/DataExportStore.cs:99` → `select new ExportProfile(u.Email, u.DisplayName, u.Theme, p.Code, u.CreatedAtUtc, u.OrgName, u.EmailNotificationsEnabled ?? true))` · `src/wa.application/UseCases/DataExport/DataExportArchive.cs:41` → `emailNotifications = snapshot.Profile.EmailNotifications, // F-XCT-003 (FR-046-1): effective column value`.

**`src/wa.web/src/features/profile/EmailNotificationsSection.tsx`:**
```tsx
import { useEffect, useRef, useState } from 'react'

import i18n from '../../core/i18n/init'
import { getTraceParent } from '../../core/traceparent'
import { toastRole } from '../../core/ui/toastRole'
import { ME_BASE } from './ProfilePage'

interface MeBody {
  emailNotifications?: boolean
}

/**
 * F-XCT-003 (FR-046-2/4, US-046-01): Notification preferences — one switch, saved on change (PATCH /auth/me { emailNotifications }).
 * Rendered inside FeatureGate feature.emailSettings (absent when off). Renders nothing until the GET /auth/me read succeeds.
 */
export function EmailNotificationsSection() {
  const [loaded, setLoaded] = useState(false)
  const [on, setOn] = useState(true)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [failed, setFailed] = useState(false)
  const alive = useRef(true)

  useEffect(() => {
    alive.current = true
    void (async () => {
      try {
        const res = await fetch(ME_BASE, { credentials: 'include', headers: { traceparent: getTraceParent() } })
        if (!alive.current || !res.ok) return
        const body = (await res.json()) as MeBody
        if (!alive.current) return
        setOn(body.emailNotifications !== false) // effective value; absent = on (FR-046-1)
        setLoaded(true)
      } catch {
        // Render nothing; a reload retries.
      }
    })()
    return () => { alive.current = false }
  }, [])

  async function toggle(next: boolean) {
    const previous = on
    setOn(next)
    setSaving(true)
    setSaved(false)
    setFailed(false)
    try {
      const res = await fetch(ME_BASE, {
        method: 'PATCH',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', traceparent: getTraceParent() },
        body: JSON.stringify({ emailNotifications: next }),
      })
      if (!alive.current) return
      if (!res.ok) {
        setOn(previous)
        setFailed(true)
        return
      }
      const body = (await res.json()) as MeBody
      if (!alive.current) return
      setOn(body.emailNotifications !== false) // server-wins
      setSaved(true)
    } catch {
      if (alive.current) {
        setOn(previous)
        setFailed(true)
      }
    } finally {
      if (alive.current) setSaving(false)
    }
  }

  if (!loaded) return null

  return (
    <section className="pf-notify">
      <h2 className="pf-label">{i18n.t('profile.notifications.heading')}</h2>
      <label className="pf-switch" htmlFor="pf-notify-switch">
        <input
          id="pf-notify-switch"
          className="pf-switch-input"
          type="checkbox"
          role="switch"
          checked={on}
          disabled={saving}
          onChange={(e) => void toggle(e.target.checked)}
        />
        <span>{i18n.t('profile.notifications.label')}</span>
      </label>
      <p className="pf-plan">{i18n.t('profile.notifications.helper')}</p>
      {saved && <p role={toastRole('success')} className="dz-toast">{i18n.t('profile.notifications.saved')}</p>}
      {failed && <p role={toastRole('error')} className="pf-error">{i18n.t('profile.notifications.error')}</p>}
    </section>
  )
}

export default EmailNotificationsSection
```

**`src/wa.web/src/features/profile/ProfilePage.tsx`** — add `import EmailNotificationsSection from './EmailNotificationsSection'` beside the L13 DataExportSection import; after the L132-134 FeatureGate block insert:
```tsx
      {/* US-046-01 (F-XCT-003): Notification preferences — absent unless feature.emailSettings is on (AC-044-2 contract). */}
      <FeatureGate gate="feature.emailSettings">
        <EmailNotificationsSection />
      </FeatureGate>
```
Note for tests: ProfilePage.test.tsx's stubFetch answers GET /auth/me with 404 (L73), so the section renders nothing there even with the gate on — presence is covered by the section's own test file.

**`src/wa.web/src/index.css`** — after the `.pf-error` rule (L1016-1024):
```css
/* US-046-01: notification preferences switch — the input carries the 44 px tap floor (T-017-03 check). */
.pf-switch {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: var(--fs-body);
  text-align: left;
}

.pf-switch-input {
  min-width: 44px;
  min-height: 44px;
  margin: 0;
}
```

**Locale bundles** — insert after the `"profile.orgName.invalid"` line (L247) in each file, keeping valid JSON (each line ends with a comma; L247 already does):
```json
  "profile.notifications.heading": "Notification preferences",
  "profile.notifications.label": "Email me about my transfers and accounts",
  "profile.notifications.helper": "Recipients always get their download emails; billing emails always arrive.",
  "profile.notifications.saved": "Notification preferences saved.",
  "profile.notifications.error": "Could not save your notification preferences. Try again.",
```
Translations (heading / label / helper / saved / error), same keys:
- de: "Benachrichtigungseinstellungen" / "E-Mails zu meinen Übertragungen und meinem Konto erhalten" / "Empfänger erhalten ihre Download-E-Mails immer; Abrechnungs-E-Mails kommen immer an." / "Benachrichtigungseinstellungen gespeichert." / "Benachrichtigungseinstellungen konnten nicht gespeichert werden. Bitte erneut versuchen."
- es: "Preferencias de notificación" / "Enviarme correos sobre mis transferencias y mi cuenta" / "Los destinatarios siempre reciben sus correos de descarga; los correos de facturación siempre llegan." / "Preferencias de notificación guardadas." / "No se pudieron guardar tus preferencias de notificación. Inténtalo de nuevo."
- fr: "Préférences de notification" / "M’envoyer des e-mails sur mes transferts et mon compte" / "Les destinataires reçoivent toujours leurs e-mails de téléchargement ; les e-mails de facturation arrivent toujours." / "Préférences de notification enregistrées." / "Impossible d’enregistrer vos préférences de notification. Réessayez."
- it: "Preferenze di notifica" / "Inviami email sui miei trasferimenti e sul mio account" / "I destinatari ricevono sempre le email di download; le email di fatturazione arrivano sempre." / "Preferenze di notifica salvate." / "Impossibile salvare le preferenze di notifica. Riprova."
- nl: "Meldingsvoorkeuren" / "Mail me over mijn overdrachten en account" / "Ontvangers krijgen altijd hun downloadmails; factuurmails komen altijd aan." / "Meldingsvoorkeuren opgeslagen." / "Je meldingsvoorkeuren konden niet worden opgeslagen. Probeer het opnieuw."
- pt: "Preferências de notificação" / "Enviar-me emails sobre as minhas transferências e conta" / "Os destinatários recebem sempre os emails de download; os emails de faturação chegam sempre." / "Preferências de notificação guardadas." / "Não foi possível guardar as preferências de notificação. Tente novamente."
- tr: "Bildirim tercihleri" / "Aktarımlarım ve hesabım hakkında bana e-posta gönder" / "Alıcılar indirme e-postalarını her zaman alır; fatura e-postaları her zaman gelir." / "Bildirim tercihleri kaydedildi." / "Bildirim tercihleri kaydedilemedi. Tekrar deneyin."

**Tests to update** — `tests/wa.application.unit/Auth/MeProfileTests.cs:78`, `:99`, `:112`: `new UpdateProfileHandler(_users, _plans, new StubOrgNames(), new StubPlanContext(true), new StubEmailNotificationsStore())`. `tests/wa.application.unit/Auth/MeProfileOrgNameTests.cs:25`: `new UpdateProfileHandler(_users, new Plans(plan), _orgNames, new StubPlanContext(branding), new StubEmailNotificationsStore())`. MediatR resolves the handler from DI in the API — the Program.cs registration is the only production wiring needed. `tests/wa.workers.unit/WorkerServicesTests.cs:89-94` is the shape to copy for the new resolve fact.

## 4. DECISIONs (non-blocking — the plan proceeds with option A)

```
DECISION: f-email sends no owner-addressed notification email yet — where does the FR-046-3 send-time check get wired?
CONTEXT: f-email routes only transfer.created (recipient emails), auth and billing (src/wa.workers/EmailFunction.cs:67); none of FR-046-4's owner notifications (my transfer expired / download limit approaching / re-send sent) exist or are specced. Adding one needs a template, 8-locale copy and routing that no spec defines.
OPTIONS:
  A) Ship column + PATCH/GET + Profile toggle + a reusable OwnerNotificationGate composed in f-email DI (unit + real-DB tested), no call site yet; every future owner email (e.g. F-COL-005 collector notification) must call MaySendAsync — AC-046-2 proven at the gate, not end-to-end; T-067's "toggle off stops owner notifications in dev" is vacuously true today.
  B) Also add a first owner notification (e.g. owner "transfer expired" on transfer.expired) through f-email — unspecced email type, copy, subscription routing; clear scope creep for US-046-01.
RECOMMEND: A — bounded to the story; the gate is the contract later owner emails plug into (record in PROGRESS + F-XCT-003 spec at close-out).
BLOCKS: none
```

## 5. Open Questions / Risks

- **PATCH with emailNotifications while the flag is off → 404 FEATURE_DISABLED** (D4) — chosen from FR-046-6 "only editable when true". EC-046-4's "column may be edited only via API in tests" is read as "tests may write the column directly". If product wants the API to accept writes while off, delete the three-line gate; no other change.
- **Recipient proof is US-046-02's AC.** The recipient handler has no dependency on the gate by construction (D5); the end-to-end "owner off, recipient still mailed" + billing-sent facts (AC-046-3) belong to US-046-02's plan.
- **Docs close-out (orchestrator, not Coder):** TA-3.2 AppUser DDL (`docs/03-technical-architecture.md` CREATE TABLE dbo.AppUser) gains `EmailNotificationsEnabled BIT NULL`; TA-4.2 row 13 PATCH body/GET response gain `emailNotifications`; `docs/REPO-FACTS.md` API notes; F-XCT-003 status + DECISION 1 outcome; PROGRESS §1.
- **Migration drift risk:** if dotnet ef emits anything beyond the one AddColumn, the snapshot was stale — stop and report, do not hand-edit.
- **EF 10 ExecuteUpdateAsync** lambda form `s => s.SetProperty(...)` matches existing callers (`src/wa.infrastructure/Persistence/OrgNameStore.cs:18`); keep that shape.
- **Toast persistence:** the success toast stays until the next toggle (AdminSuppressionsPage precedent, no auto-dismiss); acceptable per UI-Reference §4.3 "component-local until core/ui/Toast exists".
