# F-PRF-001-US-021-04 — Manage my logo

**Feature:** F-PRF-001 Custom "From" Branding (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-001/US-021-04-manage-logo.md`
**Scope:** FR-021-3, FR-021-5, AC-021-4, AC-021-5, EC-021-2, EC-021-3, EC-021-5. Builds on US-021-01 (8048856: `AppUser.OrgLogoBlobPath` column, `orgLogoUrl` wire slot, always null), US-021-02, US-021-03.
**Status of this plan:** split. **Phase A** (below, in FILES FOR CODER) is not blocked and can ship now. **Phase B** (upload, size/type rejection, recipient logo URL, profile uploader) is blocked on five DECISION blocks returned with this plan; it is outlined in §2.3 and will be re-planned once they are answered.

## 1. Goal

Phase A: a signed-in user can remove their org logo (DELETE /api/v1/auth/me/logo → 204, blob gone, `OrgLogoBlobPath` cleared), the recipient page then shows the org name with no logo (AC-021-5), and the pure content sniffer that FR-021-5 needs exists with unit tests. **Exit check:** new unit + integration facts green; AGENT.md §4 gate green (integration filtered per pitfalls, full run at feature end).

## 2. Design & Decisions

### 2.1 What exists today (verified in source)
| Need | Today |
|---|---|
| Logo column | `src/wa.domain/AppUser.cs:20-21` (`OrgLogoBlobPath`, VARCHAR(400), migration AddAppUserOrgBranding). **No new migration in Phase A.** |
| Avatar/logo upload pattern to reuse | **None.** No avatar upload exists (grep: only TopBar's initial-letter avatar). Phase B creates the first server-side upload. |
| Blob port | `src/wa.application/Ports/BlobStore.cs:10-31` — UploadAsync / DeleteAsync (no-op when absent, `AzureBlobStore.cs:108-115`). Enough for Phase A and B; no port change. |
| Blob path helper | `src/wa.application/Blob/BlobPaths.cs:8-42` — no logo row yet; "a new row is an architecture change" (`:5-6`). Blocked (DECISION 4). |
| Logo error codes | `src/wa.domain/Errors/ProblemCodes.cs` has neither LOGO_TOO_LARGE nor LOGO_TYPE_INVALID (closed list, TA-4.1.3). Blocked (DECISION 1). |
| Logo size limit | `src/wa.domain/LimitsRecord.cs` and Open-Decisions Part 2 have **no** logo key. Blocked (DECISION 2). |
| Read SAS for logo | `src/wa.infrastructure/Blob/BlobSasMinter.cs:79-119` — recipient-file / zip / export rows only; TA-3.6 has no logo row; TA-9.5 CSP img-src is 'self' data: only. Blocked (DECISION 3). |
| SVG risk | TA-9.6 has no SVG note, although EC-021-3 says "documented as accepted risk in TA-9.6". Blocked (DECISION 5). |
| Plan gate for the viewer's own plan | `IPlanContextAccessor` → `PlanContext.Features.Branding` (`src/wa.application/Limits/PlanContext.cs:7-13`). Phase B uses it, so **no allow-list change** in `tests/wa.domain.unit/Plans/PlanFeaturesConsumerGuardTests.cs:17-26`. The recipient-side gate already lives in the allow-listed `GetPublicTransferQuery.cs` / `SenderBranding.cs`. |

### 2.2 Phase A decisions
- **D-A1 — Narrow write port `IOrgLogoStore`** (TA-2.3, TA-4.2a). One member: `SetPathAsync(Guid userId, string? blobPath, CancellationToken ct)`. Adding it to `IAppUserStore` would break about 10 hand-rolled stubs in `tests/` (e.g. `tests/wa.application.unit/Auth/MeProfileTests.cs:167`) for no gain. Reads go through the existing `IAppUserStore.GetByIdAsync` (`src/wa.application/UseCases/Auth/IAppUserStore.cs:20`). It returns the `AppUser` with `OrgLogoBlobPath` and already filters out soft-deleted rows (`src/wa.infrastructure/Persistence/AppUserStore.cs:36-37`). The EF adapter uses ExecuteUpdateAsync, like `src/wa.infrastructure/Persistence/AdminFlagStore.cs:35`. That keeps it safe on the singleton, shared `WaDbContext` (`src/wa.api/Program.cs:64`): no tracked entity is left behind.
- **D-A2 — `RemoveOrgLogoCommand(Guid UserId) : IRequest<Unit>`** (golden rule 8, one handler per use case), in a new `src/wa.application/UseCases/Auth/OrgLogo.cs` next to MeProfile. Order: existence gate (null → `MeProfileException.ForNotFound()`, `MeProfile.cs:45-46`). If the path is null, no-op (idempotent). Otherwise **clear the column first, then delete the blob**: the recipient page falls back right away (AC-021-5), and a failed blob delete only leaves an orphan that the next upload to the same canonical path overwrites. The handler deletes whatever path is stored and never builds one, so it does not depend on DECISION 4.
- **D-A3 — DELETE is not plan-gated.** A user can always remove their own PII-linked data. Downgrade keeps the values for re-upgrade (EC-021-5), but an explicit remove by the owner is always allowed. This mirrors GET /auth/me/export being ungated (`Program.cs:1249`). DELETE answers 401 for a guest, 404 when the user row is gone (same idiom as `Program.cs:1207-1226`), and 204 otherwise.
- **D-A4 — `LogoContentSniffer`** (FR-021-5 type half, pure, no I/O). `static LogoImageType? Sniff(ReadOnlySpan<byte> head)` returns Png / Jpeg / WebP / Svg or null. Magic bytes: PNG `89 50 4E 47 0D 0A 1A 0A`; JPEG `FF D8 FF`; WebP = ASCII "RIFF" + 4 bytes + ASCII "WEBP"; SVG = UTF-8 text (optional BOM, leading whitespace, optional `<?xml …?>` and comments) whose first element is `<svg`. Anything else (GIF `GIF8`, PDF, EXE, HTML) → null. It is placed in wa.application, not wa.domain: it is use-case validation with no entity invariant, and TA-2.3 keeps the domain to entities. The client Content-Type is never trusted.
- No telemetry, ProblemCode, limit, package, env var, CSP or schema change in Phase A.

### 2.3 Phase B outline (blocked; NOT for the Coder yet)
1. `BlobPaths.OrgLogo(Guid userId)` per DECISION 4 (+ BlobContainerBootstrap if a new container is chosen).
2. PUT /api/v1/auth/me/logo, **raw request body** read server-side up to cap+1 bytes. The bytes must be sniffed before anything is stored ("no partial state", AC-021-4). The story's "SAS mint for the put" would mean a new write-SAS purpose and storing before sniffing, so Phase B declines it (see Open Questions). Handler `UploadOrgLogoCommand(UserId, byte[] Bytes)`: PlanContext.Features.Branding false → 403 FORBIDDEN (existing code, "Pro plan required"); size > the DECISION-2 Limits Registry key → LOGO_TOO_LARGE naming the limit; Sniff null → LOGO_TYPE_INVALID; then UploadAsync (Content-Type from the sniffed kind) → `IOrgLogoStore.SetPathAsync`.
3. `IBlobSasMinter.MintOrgLogo(Guid userId)`: new TA-3.6 row, r, 30 min, signed inline Content-Disposition (DECISION 3 + 5). `GetPublicTransferHandler` (`GetPublicTransferQuery.cs:98-116`) fills `OrgLogoUrl` only when branding resolves true (the same SenderBranding rule, so EC-021-5 downgrade hides it) and the path is set. If the blob is missing (EC-021-2), the URL is still minted and the img onerror hides it; the branding_logo_missing telemetry name is not in TelemetryEvents, so it is a golden-rule-6 addition to raise with DECISION 1.
4. GET /auth/me gains an additive orgLogoUrl (owner preview). The web profile gets an Organization logo section (drag/click, preview, Remove logo ghost, disabled with "Pro plan required" tooltip on Free), next to `src/wa.web/src/features/profile/ProfilePage.tsx:124-126`.
5. Phase B tests: AC-021-4 (2 MB → LOGO_TOO_LARGE, column still null, no blob), FR-021-5 (GIF → LOGO_TYPE_INVALID), EC-021-5 (Free owner with path set → orgLogoUrl null on the public GET, column untouched), Free PUT → 403, SVG → inline Content-Disposition on the minted SAS.

## 3. Implementation Plan (Phase A)

1. **Sniffer.** CREATE `src/wa.application/UseCases/Auth/LogoContentSniffer.cs`: `public enum LogoImageType { Png, Jpeg, WebP, Svg }` + `public static class LogoContentSniffer { public static LogoImageType? Sniff(ReadOnlySpan<byte> head) }` per D-A4. SVG check: decode at most the first 1024 bytes as UTF-8, strip BOM/whitespace, skip `<?xml…?>` and `<!--…-->` prologue, then require `<svg` followed by whitespace, `>` or `/` (ordinal ignore-case).
2. **Sniffer tests** (FR-021-5). CREATE `tests/wa.application.unit/Auth/LogoContentSnifferTests.cs`, xUnit `[Theory]`: PNG, JPEG, WebP heads → their kind; `<svg …>`, BOM + `<?xml?>` + comment + `<svg` → Svg; GIF89a, `%PDF-`, `MZ`, `<html>`, empty, 3-byte truncated PNG, a RIFF…WAVE head → null; a PNG head with the wrong 8th byte → null.
3. **Port + adapter.** CREATE `src/wa.application/UseCases/Auth/IOrgLogoStore.cs` (D-A1). CREATE `src/wa.infrastructure/Persistence/OrgLogoStore.cs`: `_db.Set<AppUser>().Where(u => u.Id == userId && u.DeletedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(u => u.OrgLogoBlobPath, blobPath), ct)`.
4. **Use case.** CREATE `src/wa.application/UseCases/Auth/OrgLogo.cs`: `RemoveOrgLogoCommand` + `RemoveOrgLogoHandler(IAppUserStore users, IOrgLogoStore logos, IBlobStore blobs)` per D-A2.
5. **Handler tests** (AC-021-5 unit half). CREATE `tests/wa.application.unit/Auth/OrgLogoTests.cs` with hand-rolled stubs (IAppUserStore stub: copy the shape of `MeProfileTests.cs:167`; IOrgLogoStore and IBlobStore stubs that record calls into one shared ordered list):
   - `Remove_PathSet_ClearsColumnThenDeletesBlob`: user path "transfers/x/logo" → calls exactly [SetPath(null), Delete("transfers/x/logo")] in that order.
   - `Remove_NoPath_NoOp`: no store or blob calls.
   - `Remove_UnknownUser_ThrowsNotFound`: message == ProblemCodes.TitleOf(ProblemCodes.NotFound).
   Verify: dotnet test tests/wa.application.unit with filter "FullyQualifiedName~OrgLogo|FullyQualifiedName~LogoContentSniffer".
6. **Endpoint + DI.** MODIFY `src/wa.api/Program.cs`: register `IOrgLogoStore` next to `:76` (singleton over WaDbContext, same line style). Map DELETE /api/v1/auth/me/logo right after DELETE /auth/me (`:1226`) using that endpoint's exact idiom: session check → 401; send `RemoveOrgLogoCommand`; catch the NotFound-message MeProfileException → 404; return `Results.NoContent()`.
7. **Integration** (AC-021-5 end to end). MODIFY `tests/wa.api.integration/PublicTransferBrandingTests.cs` (it already runs Azurite + SQL with branding on, `:30-51`, and has the SeedOwnerAsync / SeedActiveTransferAsync / GetAsync helpers, `:133-176`):
   - Keep the Azurite account URL in a field (it is a local today, `:30`). Add a `SessionCookie(Guid userId)` helper copied from `tests/wa.api.integration/DataExportEndpointTests.cs:74-88` (secret "test-jwt-secret" matches `:34`). SeedOwnerAsync gains `string? orgLogoBlobPath = null`.
   - `DeleteLogo_RemovesBlobAndPath_RecipientShowsOrgWithoutLogo`: seed a pro owner "Studio Nova" with path `transfers/test-logo/{ownerId:N}`; create the container if missing (BlobContainerBootstrap is a racing background service) and upload 8 bytes through a BlobServiceClient built from the Azurite URL + AzuriteBuilder.AccountKey; seed transfer "f1b2c3d4". DELETE with the cookie → 204. Column now null (fresh AsNoTracking read). Blob no longer exists. GetAsync("f1b2c3d4") → orgName "Studio Nova", orgLogoUrl JSON null.
   - `DeleteLogo_Guest_Returns401` and `DeleteLogo_NoLogo_Returns204Idempotent`.
   Each fact makes at most 2 /auth requests (5/min auth rule). Verify: dotnet test tests/wa.api.integration with filter "FullyQualifiedName~PublicTransferBranding" (Docker running).
8. **Gate:** AGENT.md §4 (web gates unaffected: no web change in Phase A).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Auth/LogoContentSniffer.cs` | CREATE | LogoImageType enum + pure Sniff (D-A4). |
| `tests/wa.application.unit/Auth/LogoContentSnifferTests.cs` | CREATE | Magic-byte theory cases (step 2). |
| `src/wa.application/UseCases/Auth/IOrgLogoStore.cs` | CREATE | One-member write port (D-A1). |
| `src/wa.infrastructure/Persistence/OrgLogoStore.cs` | CREATE | ExecuteUpdateAsync adapter (step 3). |
| `src/wa.application/UseCases/Auth/OrgLogo.cs` | CREATE | RemoveOrgLogoCommand + handler (D-A2). |
| `tests/wa.application.unit/Auth/OrgLogoTests.cs` | CREATE | 3 handler facts (step 5). |
| `src/wa.api/Program.cs` | MODIFY | DI line near `:76`; DELETE /api/v1/auth/me/logo after `:1226`. |
| `tests/wa.api.integration/PublicTransferBrandingTests.cs` | MODIFY | Cookie helper, seed param, 3 DELETE facts (step 7). |

**`src/wa.api/Program.cs`**. DI anchor `:75-76`:
```csharp
// US-021-01: sender branding read port (org name beside the sender on the recipient page).
builder.Services.AddSingleton<wa.application.UseCases.Transfers.ISenderBrandingStore>(sp => new wa.infrastructure.Persistence.SenderBrandingStore(sp.GetRequiredService<WaDbContext>()));
```
Endpoint template `:1207-1226` (DELETE /api/v1/auth/me): copy the session check (`:1210-1211`) and the NotFound catch (`:1221-1225`) verbatim, but drop the cookie deletion and return `Results.NoContent()`. IBlobStore is already a singleton (`:56-57`). MediatR scans the application assembly (`:35`), so the handler needs no registration.

**`tests/wa.api.integration/PublicTransferBrandingTests.cs`**. SeedOwnerAsync `:141-157` (add the optional `orgLogoBlobPath` and assign `OrgLogoBlobPath = orgLogoBlobPath`; the existing callers `:64`, `:77`, `:89` stay unchanged). The Azurite URL is built at `:30` as a local `accountUrl`; promote it to a field. Cookie format (from `DataExportEndpointTests.cs:78-86`): `base64("{userId:N}|{exp}|{base64(HMACSHA256(secret, "{userId:N}|{exp}"))}")` in header `Cookie: wa.session=…`. Existing link ids b1/c1/d1/e1 are taken, so use f1, f2, f3.

**Context for the new application files.** `MeProfileException` (`src/wa.application/UseCases/Auth/MeProfile.cs:40-59`) is reused for not-found (no new exception type in Phase A). `IBlobStore.DeleteAsync` is a no-op when the blob is absent (`src/wa.application/Ports/BlobStore.cs:29-30`).

## 4. Open Questions / Risks

- **Phase B is blocked** on DECISIONS 1–5 (returned to the orchestrator). Without them the story's AC-021-4 and EC-021-5 logo behaviour cannot ship.
- **Story tech note "SAS mint for the put"** conflicts with "no partial state" + content sniffing. The plan proposes a server-side raw-body PUT (no new write SAS). If the PO wants browser-direct upload, that adds a cwr SAS row to TA-3.6 (security-shaped → another DECISION).
- **Transfers-container lifecycle:** TA-3.5 applies `transfers/*` delete after 30 d (safety net). A logo stored there per FR-021-3 would silently vanish after 30 days. This is the core of DECISION 4.
- **CSP is spec-only today:** no Content-Security-Policy header is emitted anywhere in `src/` (grep). DECISION 3 changes TA-9.5 text and the eventual hosting config, not running code.
- **REPO-FACTS ProblemCodes section is stale** (its list predates FLAG_INVALID, FLAG_UNKNOWN, FEATURE_DISABLED, EXPORT_IN_FLIGHT and BILLING_UNAVAILABLE). The orchestrator's docs close-out should refresh it. The new DELETE route also belongs in REPO-FACTS §2 / TA-4.2#13 at close-out.
- D-A3 (DELETE ungated on Free) is an architect call within §7 autonomy. Flip it to gated only if the PO disagrees.
