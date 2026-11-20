# F-PRF-001-US-021-02 — Org branding in the notification email

**Feature:** F-PRF-001 Custom "From" Branding (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-001/US-021-02-email-branding.md`
**Scope:** FR-021-1 / FR-021-2 (email half), FR-021-4, FR-021-6, AC-021-1 (email half), story ACs (pro → org in subject + body header; free → plain), story edge case "email keeps the send-time org". Builds on US-021-01 (commit 8048856): `ISenderBrandingStore`, `SenderBrandingRow`, `AppUser.OrgName`.
**Out of scope:** recipient page (done), logo in email (US-021-04), per-transfer-name precedence rules (US-021-03), telemetry names (US-021-01 §5 DECISION A, deferred).

## 1. Goal

When the transfer owner's plan has feature branding on and `AppUser.OrgName` is set, the f-email notification renders subject "Transfer from {orgName} — {senderName}" (org only when the sender name is blank) and the same line in the body header; every other case is byte-identical to today; From stays the configured no-reply address (D-04). **Exit check:** new unit + integration facts green; AGENT.md §4 gate green (integration filtered per pitfalls).

## 2. Design & Decisions

### Grounded facts
- f-email handler: `src/wa.application/UseCases/Transfers/SendNotificationEmails.cs:48-206`; ctor `:28-46`; template data built once at `:99-103` from `EmailTransferContext.SenderName`; From = config `Wa:Email:FromAddress` at `:106` (D-04, untouched).
- Context port/record: `src/wa.application/UseCases/Transfers/IEmailStore.cs:7-13` — carries no owner id today. Adapter `src/wa.infrastructure/Persistence/EmailStore.cs:18-38` already loads the whole `Transfer` row (`:20`), so `OwnerAppUserId` (`src/wa.domain/Transfer.cs`) is free to project.
- Renderer: `src/wa.infrastructure/Email/StubbleTemplateRenderer.cs:63-64` feeds `data.SenderName` into the model; subject `:82-83` is `string.Format(SubjectFormats[locale], data.SenderName)` (formats `:23-33`, en = "Transfer from {0}"). All 16 templates use `{{SenderName}}` only in `<title>` and the header sentence (e.g. `src/wa.workers/Emails/templates/transfer/en.html:6`, `:18`; `en.txt:3`).
- Org gate logic already exists, private: `GetPublicTransferHandler.ResolveOrgNameAsync` `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs:115-126` (owner null / row null / blank org → null; else `IFeaturesProvider.Resolve(row.PlanCode)` Branding).
- Workers DI: handler composed at `src/wa.workers/WorkerServices.cs:149-157`; `PlansCache`/`FlagsCache` registered at `:52-53`, but **no** `IFeaturesProvider` and **no** `ISenderBrandingStore` in the worker host (api registers them at `src/wa.api/Program.cs:76`, `:105`).
- Handler ctor callers: `WorkerServices.cs:149`, `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs:43` and `:524`, `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs:256`.
- `SendTransferHandler` always fills `Transfer.SenderName` for signed-in owners (display name / email local part, `src/wa.application/UseCases/Transfers/SendTransfer.cs:158`, `:243-251`), so the "org only" subject is reachable only with a blank name.

### D-1 — Snapshot = the rendered email; no new column, no migration (TA-6.7 step 2, TA-3.2 unchanged)
The org is resolved **once, at email dispatch** in f-email (seconds after send; there is no scheduled-send consumer — `ScheduledSendAtUtc` is only stored, `SendTransfer.cs:192`), and baked into the subject/body that leaves the system. A sent email is immutable, so a later profile edit can never reach it: that is the story's "email keeps the send-time org", while the page keeps EC-021-1 render-time behaviour. TA-6.7 step 2 already says template data comes "from payload + transfer row", so reading the owner's AppUser row at render is inside the documented pipeline. Rejected: a `Transfer.SenderOrgName` column (schema change not sanctioned by the spec — F-PRF-001 sanctions only the two AppUser columns) and an `orgName` field on the transfer.created payload (TA-5.3 payload shape is frozen, golden rule 3; would put more PII on the bus). Residual window: a profile edit between send and dispatch (or between SB redeliveries, ≤ 1 h) shows the newer org — accepted and recorded in §5 (non-blocking DECISION for the docs log).

### D-2 — One shared gate rule: static `SenderBranding` in application (codebase-design: one rule, one place)
CREATE `src/wa.application/UseCases/Transfers/SenderBranding.cs`:
```csharp
public static class SenderBranding
{
    public static async Task<string?> ResolveOrgNameAsync(
        ISenderBrandingStore store, IFeaturesProvider features, Guid? ownerAppUserId, CancellationToken ct) { /* body moved verbatim from GetPublicTransferQuery.cs:117-125 */ }

    /// <summary>Email "From" line (FR-021-2): org null/blank → senderName unchanged; senderName blank → org; else "{org} — {senderName}" (U+2014, single spaces).</summary>
    public static string ComposeEmailSenderLine(string senderName, string? orgName) { ... }
}
```
`GetPublicTransferHandler.ResolveOrgNameAsync` becomes a one-line delegate to it (body change only; ctor and its tests untouched). Pure function + one async read: trivially unit-testable, no new port, no DI.

### D-3 — Handler composes the line; renderer, `TemplateData` and the 16 templates stay unchanged (TA-6.7, ADR-018)
The composed line is passed as `TemplateData.SenderName`. Every locale's subject format and header sentence already interpolate the sender name, so the org appears in subject + `<title>` + body header in all 8 locales with zero template or i18n churn, and the Free path stays byte-identical (AC: free = plain). The em-dash join is locale-neutral (same choice as the page's "·" separator, US-021-01 D-6). Org resolution sits in step 7 (after the "nothing un-notified" early return at `:88-89`) so no extra query runs for no-op events. `PlanUnknownException` propagates → SB redelivery, same as any transient failure.

### D-4 — Owner id via `EmailTransferContext` (append, defaulted)
`EmailTransferContext` gains `Guid? OwnerAppUserId = null` as the **last** positional member, so the 16 positional `new EmailTransferContext(...)` calls in the unit tests compile unchanged. `EmailStore` passes `transfer.OwnerAppUserId`.

### D-5 — Handler ctor: append `ISenderBrandingStore branding, IFeaturesProvider features` after `logger`
Appending last keeps every existing argument position; the four callers each add two arguments. Worker host registers `IFeaturesProvider` (`new FeaturesProvider(PlansCache, FlagsCache)`, same as `Program.cs:105`) and `ISenderBrandingStore` (`new SenderBrandingStore(WaDbContext)`, same as `Program.cs:76`). No new config key, env var or package.

### D-6 — PII (FR-021-6, TA-9.4)
No org value is logged or put into telemetry/event payloads (email.sent payload `:155-161` unchanged). The org reaches only the email body/subject — which already carries the raw sender name. No hashing work is needed in this story because nothing new is emitted.

## 3. Implementation Plan

1. **Shared rule** — CREATE `src/wa.application/UseCases/Transfers/SenderBranding.cs` per D-2 (`using wa.application.Limits;`). MODIFY `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs:115-126`: body becomes `=> SenderBranding.ResolveOrgNameAsync(_branding, _features, ownerAppUserId, ct);` (keep the doc comment). Verify: dotnet test tests/wa.application.unit with filter on GetPublicTransferHandlerTests (unchanged tests stay green).
2. **Context** — MODIFY `src/wa.application/UseCases/Transfers/IEmailStore.cs:7-13`: append `Guid? OwnerAppUserId = null`. MODIFY `src/wa.infrastructure/Persistence/EmailStore.cs:31-37`: pass `transfer.OwnerAppUserId` as the 7th argument.
3. **Handler** — MODIFY `src/wa.application/UseCases/Transfers/SendNotificationEmails.cs`: fields + ctor per D-5 (`using wa.application.Limits;`); in step 7 before `:99`:
   `var orgName = await SenderBranding.ResolveOrgNameAsync(_branding, _features, context.OwnerAppUserId, ct);`
   and first `TemplateData` argument becomes `SenderBranding.ComposeEmailSenderLine(context.SenderName, orgName)`. Comment: F-PRF-001 US-021-02, D-1 snapshot-at-dispatch.
4. **Worker DI** — MODIFY `src/wa.workers/WorkerServices.cs`: after `:54` register `IFeaturesProvider` and `ISenderBrandingStore` (D-5); `:149-157` append the two `GetRequiredService` args after the logger.
5. **Unit tests** — CREATE `tests/wa.application.unit/Transfers/SenderBrandingTests.cs` (pure composer + resolver with tiny inline stubs):
   - `Compose_OrgAndName_JoinsWithEmDash` → "Studio Nova — Anna K." (AC-021-1 / FR-021-2)
   - `Compose_OrgOnly_BlankName_ReturnsOrg` → "Studio Nova" (story alt flow "no per-transfer sender name")
   - `Compose_NoOrg_ReturnsNameUnchanged` (null and whitespace org) → "Anna K." (free tier, FR-021-4)
   - `Resolve_FreePlan_ReturnsNull`, `Resolve_GuestOwner_ReturnsNull_StoreNotCalled`, `Resolve_ProWithOrg_ReturnsTrimmed`
   MODIFY `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs`: add `StubSenderBrandingStore` + `StubFeaturesProvider` (copy shapes `GetPublicTransferHandlerTests.cs:457-474`), pass them at `:43-51` and `:524-532`. New facts:
   - `Handle_ProOwnerWithOrg_SubjectAndBodyShowOrg` — AC-021-1 email half: context `OwnerAppUserId` set, row ("pro", "Studio Nova", null), SenderName "Anna K." → `_templateRenderer.DataInOrder[0].SenderName == "Studio Nova — Anna K."`, sent Subject "Transfer from Studio Nova — Anna K.", Html contains "Studio Nova", From "no-reply@protodrop.com" (D-04).
   - `Handle_FreeOwnerWithOrg_SubjectPlain` — story AC 2: row ("free", "Studio Nova", null) → Subject "Transfer from Anna K.", Html lacks "Studio Nova".
   - `Handle_GuestTransfer_BrandingStoreNotCalled` — owner null → Calls == 0, plain subject.
   - `Handle_OrgChangedAfterDispatch_SentEmailUnchanged` — story edge case: handle once, mutate stub row OrgName, assert the already-captured `Sent[0].Subject` still has "Studio Nova" (snapshot = rendered message, D-1).
   Verify: dotnet test tests/wa.application.unit with filter FullyQualifiedName~SendNotificationEmails or SenderBranding.
6. **Worker DI test** — MODIFY `tests/wa.workers.unit/WorkerServicesTests.cs`: fact `SendNotificationEmailsHandler_Resolves` (pattern of `DataExportHandler_Resolves` at `:62-64`; `Wa:Email:Sender` = "log" in settings so the sender selector resolves). Verify: dotnet test tests/wa.workers.unit.
7. **Integration** — MODIFY `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs:256-264`: append `new SenderBrandingStore(workerDb)` and a `FeaturesProvider` built like `tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs:39-42` (guest sends → unchanged assertions, `:131` stays "Transfer from Alice Example"). CREATE `tests/wa.api.integration/EmailBrandingIntegrationTests.cs`: MsSql container only (no Azurite, no WebApplicationFactory), migrate, flip feature branding flag to "true" (idiom `PublicTransferBrandingTests.cs:45-49`), seed owner (`:129-145`) + transfer (`:147-164`, SenderName "Anna K.") + one `EmailRecipient`; build the handler with real `EmailStore`, `SenderBrandingStore`, `FeaturesProvider` (TTL zero), real `StubbleTemplateRenderer`, a capturing `IEmailSender`, envelope payload with `transferId` + `emails`:
   - `ProOwnerWithOrg_EmailSubjectAndHeaderShowOrg` — AC-021-1 email half: Subject "Transfer from Studio Nova — Anna K."; Html and Text contain "Studio Nova" and "Anna K."; From = configured no-reply address (D-04).
   - `FreeOwnerWithOrg_EmailSubjectPlain` — story AC 2 / FR-021-4: Subject "Transfer from Anna K."; Html lacks "Studio Nova".
   Verify: dotnet test tests/wa.api.integration with filter FullyQualifiedName~EmailBranding, then ~EmailLogSenderE2E (Docker running).
8. **Gate** — AGENT.md §4 (integration filtered as step 7; full run at feature end per pitfalls). No wa.web change.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Transfers/SenderBranding.cs` | CREATE | Static `ResolveOrgNameAsync` + `ComposeEmailSenderLine` (D-2). |
| `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs` | MODIFY | `ResolveOrgNameAsync` body delegates to `SenderBranding` (`:115-126`). |
| `src/wa.application/UseCases/Transfers/IEmailStore.cs` | MODIFY | `EmailTransferContext` appends `Guid? OwnerAppUserId = null`. |
| `src/wa.infrastructure/Persistence/EmailStore.cs` | MODIFY | Pass `transfer.OwnerAppUserId`. |
| `src/wa.application/UseCases/Transfers/SendNotificationEmails.cs` | MODIFY | Ctor + fields; resolve org, compose sender line into `TemplateData`. |
| `src/wa.workers/WorkerServices.cs` | MODIFY | Register `IFeaturesProvider`, `ISenderBrandingStore`; pass to handler. |
| `tests/wa.application.unit/Transfers/SenderBrandingTests.cs` | CREATE | 6 pure facts (compose + resolve). |
| `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs` | MODIFY | Two stubs, ctor args at `:43`, `:524`, 4 branding facts. |
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | `SendNotificationEmailsHandler_Resolves`. |
| `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs` | MODIFY | Ctor args at `:256-264`. |
| `tests/wa.api.integration/EmailBrandingIntegrationTests.cs` | CREATE | AC-021-1 email half + free-tier case, real renderer. |

**`IEmailStore.cs:7-13`**
```csharp
public sealed record EmailTransferContext(
    Guid TransferId,
    string SenderName,
    string? SenderEmail,
    DateTime ExpiresAtUtc,
    string LinkId,
    IReadOnlyList<FileEntry> Files);
```
→ add `, Guid? OwnerAppUserId = null` after `Files` (doc: US-021-02 branding gate input).

**`EmailStore.cs:31-37`**
```csharp
        return new EmailTransferContext(
            transfer.Id,
            transfer.SenderName,
            transfer.SenderEmail,
            transfer.ExpiresAtUtc,
            transfer.LinkId,
            files);
```

**`SendNotificationEmails.cs`** — ctor `:28-46` (append two params after `ILogger<SendNotificationEmailsHandler> logger`, two readonly fields after `_logger` at `:26`); template data `:96-103`:
```csharp
        var expiryDays = (int)Math.Ceiling((context.ExpiresAtUtc - DateTime.UtcNow).TotalDays);
        var publicOrigin = _config["Wa:Url:Public"] ?? "http://localhost:5173";
        var ctaUrl = $"{publicOrigin}/t/{context.LinkId}";
        var templateData = new TemplateData(
            context.SenderName,
            context.Files,
            ctaUrl,
            expiryDays);
```
Do not touch `:106` (From) or the email.sent / email.failed payloads.

**`GetPublicTransferQuery.cs:115-126`** — current body (moves verbatim into `SenderBranding.ResolveOrgNameAsync`, replacing `_branding`/`_features` with parameters):
```csharp
    private async Task<string?> ResolveOrgNameAsync(Guid? ownerAppUserId, CancellationToken ct)
    {
        if (ownerAppUserId is null)
            return null;

        var row = await _branding.GetAsync(ownerAppUserId.Value, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.OrgName))
            return null;

        var features = await _features.Resolve(row.PlanCode, ct);
        return features.Branding ? row.OrgName.Trim() : null;
    }
```

**`WorkerServices.cs:52-54`** (anchor for the two registrations):
```csharp
        services.AddSingleton(sp => new PlansCache(sp.GetRequiredService<IPlansStore>(), limitsTtl, () => DateTimeOffset.UtcNow));
        services.AddSingleton(sp => new FlagsCache(sp.GetRequiredService<IFlagsStore>(), limitsTtl, () => DateTimeOffset.UtcNow));
        services.AddSingleton<ILimitsProvider>(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
```
Add `services.AddSingleton<IFeaturesProvider>(sp => new FeaturesProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));` and `services.AddSingleton<ISenderBrandingStore>(sp => new SenderBrandingStore(sp.GetRequiredService<WaDbContext>()));`. Handler factory `:149-157` ends with `sp.GetRequiredService<ILogger<SendNotificationEmailsHandler>>()` — append `sp.GetRequiredService<ISenderBrandingStore>(), sp.GetRequiredService<IFeaturesProvider>()`. All needed usings are already present (`:6`, `:12`, `:16`).

**`SendNotificationEmailsHandlerTests.cs`** — ctor `:43-51`, `CreateHandler` `:524-532`; stub section starts `:595`. `StubTemplateRenderer` (`:641-685`) already returns subject `Transfer from {data.SenderName}` and echoes `data.SenderName` into Html — the new facts assert through it. Add `using wa.application.Limits;`. Existing facts pass `OwnerAppUserId` implicitly null → branding store never called → unchanged behaviour.

**`EmailLogSenderE2ETests.cs:256-264`** — append after the logger arg: `new SenderBrandingStore(workerDb)`, `new FeaturesProvider(new PlansCache(new PlansStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow), new FlagsCache(new FlagsStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow))`; add `using wa.application.Limits;`.

## 4. ACs / ECs → tests

| ID | Test |
|---|---|
| AC-021-1 (email half) / story AC 1 | `Handle_ProOwnerWithOrg_SubjectAndBodyShowOrg`, `ProOwnerWithOrg_EmailSubjectAndHeaderShowOrg`, `Compose_OrgAndName_JoinsWithEmDash` |
| Story AC 2 (free = plain) / FR-021-4 | `Handle_FreeOwnerWithOrg_SubjectPlain`, `FreeOwnerWithOrg_EmailSubjectPlain`, `Compose_NoOrg_ReturnsNameUnchanged`, `Resolve_FreePlan_ReturnsNull` |
| Story alt flow (org only) | `Compose_OrgOnly_BlankName_ReturnsOrg` |
| Story edge: send-time org kept | `Handle_OrgChangedAfterDispatch_SentEmailUnchanged` (D-1) |
| D-04 From unchanged | From asserted in both AC-021-1 facts |
| Guest unchanged | `Handle_GuestTransfer_BrandingStoreNotCalled`, existing `EmailLogSenderE2ETests` subject assert |
| Worker composition | `SendNotificationEmailsHandler_Resolves` |

## 5. Open Questions / Risks

```
DECISION: Accept "org resolved at email dispatch" as the send-time snapshot for US-021-02 (no new column)?
CONTEXT: The story's technical note says the template reads org "from the transfer's snapshot at send"; storing it on Transfer is an unsanctioned schema change, putting it on transfer.created changes the frozen TA-5.3 payload. Dispatch runs seconds after send; a sent email is immutable.
OPTIONS:
  A) Resolve at dispatch in f-email (this plan) — no migration, no contract change; profile edits in the send→dispatch/redelivery window (<= ~1 h) show the newer org.
  B) Add Transfer.SenderOrgName NVARCHAR(60) NULL stamped in SendTransferHandler — exact send-time value; migration + TA-3.2 + ADR line, and the column must join the erasure/export PII paths.
RECOMMEND: A — the email itself is the snapshot; B buys a sub-hour precision gain for a new PII column.
BLOCKS: none (this plan implements A; log the choice in Open-Decisions Part 3 at docs close-out)
```

- **Risk — Stubble HTML-encoding.** The em dash / org characters go through `{{SenderName}}` escaping in Html; integration facts assert "Studio Nova" and "Anna K." separately rather than the joined string in Html (subject is unescaped, asserted exactly).
- **Risk — gate seeded off.** feature branding flag is off in every seed; the integration class must flip it before building `FlagsCache` (TTL zero makes order irrelevant there, but keep the idiom).
- **Risk — worker host DI.** Missing registration would only fail on first resolution in Azure; step 6 fact pins it.
- **Docs close-out (orchestrator):** TA-6.7 step 2 note "sender line = org — name when branding applies"; REPO-FACTS unchanged (no closed-set change).
