# T-069 plan — PII policy gate (F-XCT-005, US-048-01/02/03)

**Branch:** `feature/F-XCT-005` (from origin/develop @ 5b6bf45) · **Backlog row:** T-069 (M7) · **Spec:** `docs/features/Phase X-Cross-Cutting/F-XCT-005/F-XCT-005-pii-policy.md` · **Architecture:** TA-9.4, TA-10.2, TA-10.5, TA-5.2/5.3, TA-14, TA-17

## 1. Goal

Make the TA-9.4 PII policy a build gate. Done when: (a) the pii-check script fails on a raw file name, IP or address in a TA-5.3 event payload; (b) a convention test fails on an unmarked PII-looking fixture column and passes once it is marked; (c) the `[Pii]`-marked columns of the real EF model exactly equal `ErasureScope.AllColumns`, and each entry's erasure disposition is checked against the model. All of this is green on today's tree.

## 2. Design & Decisions

**Ground truth found in code (it differs from the spec text, so the plan follows the code):**
- Event payloads are **anonymous objects**, not DTO types. There are 22 `Payload =` sites in src/wa.application (for example `src/wa.application/UseCases/Transfers/SendTransferHandler.cs:162`, `src/wa.application/UseCases/Transfers/SendNotificationEmailsHandler.cs:173`). Reflection has nothing typed to inspect, so the "event DTO convention test" goes into the source-scan gate, which the spec's FR-048-3 "CI grep on event-helper call sites" already asks for.
- The spec's `TransferFile.FileName` is really `FileItem.OriginalName` (`src/wa.domain/FileItem.cs:13`). `DownloadEvent` stores only `IpHash`, never a raw IP (`src/wa.domain/DownloadEvent.cs:13`).
- The real raw-PII columns are **14**, not 7. Besides the FR-048-2 list, these hold raw PII: `AuthToken.Email`, `AuditLog.ActorEmail`, `AuditLog.DetailsJson` (`src/wa.api/Endpoints/AdminEndpoints.cs:51` and `:320` write `new { email = ... }`), `DomainEvent.EnvelopeJson` and `DomainEvent.PartitionKey` (the outbox stores TA-5.3 payloads that carry addresses, and auth events partition by email: `src/wa.application/UseCases/Auth/DeleteAccountHandler.cs:60`), `Transfer.SenderEmail`, `EmailSuppression.SenderEmail`, `AppUser.DisplayName` and `AppUser.OrgName`.
- What the "erasure cascade" does today (`src/wa.infrastructure/Persistence/AppUserStore.cs:53-82`): it soft-deletes `AppUser` (the email stays), re-homes transfers (`SenderName` and `SenderEmail` stay), and writes the `Erasure` marker. f-delete-transfers (`src/wa.infrastructure/Persistence/ExpiryDeletionStore.cs:49-108`) hard-deletes `EmailRecipient`, `FileItem` and `DownloadEvent` rows, but only soft-deletes `Transfer`. `GdprSweepStore` only stamps `SweptAtUtc`. Nothing scrubs a column. → **DECISION D2**.
- Telemetry never carries a raw address today. `email_sent` is property-free (`src/wa.application/Telemetry/WaTelemetryReporter.cs:27-33`), and R2/R7 of `scripts/check-telemetry-pii.mjs` already block PII in reporter calls, ILogger calls and `[LoggerMessage]`. No Pii=true property, no Serilog enricher and no `{X:Pii}` slot exist. → **DECISION D1**.

**Decisions (each with its recommended option, which the plan assumes):**
1. **Marker attributes live in `wa.domain`, namespace `wa.domain.Privacy`**, next to `EmailMask`. They are pure BCL code with no references, so the onion rule holds (TA-0.2 / AGENT §3.7). `PiiAttribute` marks a raw-PII column. `NotPiiAttribute(string reason)` is the reviewed escape hatch (US-048-02 alternative flow). Both are `sealed` and property-only, and `NotPii` exposes `Reason` (CA1813/CA1018/CA1019). EF ignores unknown CLR attributes, so there is **no model change and no migration** (golden rule 4 is untouched). Step 2 proves this with a model-snapshot check.
2. **`ErasureScope.AllColumns`** (`wa.domain.Privacy`) is the single closed list of `ErasureColumn(Type Entity, string Property, ErasureDisposition Disposition, string Rationale)`. Entries use `typeof(...)` and `nameof(...)`, so a rename breaks the compile and not only the test. `ErasureDisposition` records **what really happens today**:
   - `AccountSoftDelete`: the column is on `AppUser` and DELETE /auth/me soft-deletes the row. The value stays until a hard delete exists (D2).
   - `TransferHardDelete`: the column is on a row with a model FK to `Transfer`, and f-delete-transfers removes that row.
   - `Retained`: the value outlives erasure, either on purpose or pending D2. `Rationale` says why.
3. **Convention tests live in tests/wa.application.unit/Privacy/.** This project already references wa.infrastructure and EF InMemory (`tests/wa.application.unit/wa.application.unit.csproj:12,28`, precedent `tests/wa.application.unit/Transfers/UnsubscribeServiceTests.cs:24-29`). The tests can therefore walk the **real EF model** (`WaDbContext.Model`): every mapped `string` column of every table, which is FR-048-2's "every other column of every table". The check logic is a test-side pure helper (`PiiConvention`) that takes `PropertyInfo`s plus a closed list. The same function runs on the real model and on fixture types, which proves "red on unmarked fixture, green after marking" (exit check).
4. **PII-looking name** = the camelCase words of the property name include `email`, `mail`, `name`, `ip` or `address`. This is FR-048-4's `*Email*|*Name*|*Ip*` plus `address`, because `EmailRecipient.Address` and `EmailSuppression.Address` must be caught. Matching is word-based, not substring, so `Recipient` and `Description` don't match.
5. **Event-payload gate = new rule R8 in `scripts/check-telemetry-pii.mjs`.** It extends the existing guard rather than adding a second script. It needs no new CI job (the pii-check job at `.github/workflows/ci.yml:119-127` already runs the scan and the selftest) and no new package (TA-17). R8 anchors on `Payload =` (the `EventEnvelope.Payload` initializer, `src/wa.domain/EventEnvelope.cs:36-37`). The right-hand side must be `new { ... }`, or an identifier bound in the same file by `<id> = new { ... }`. Anything else is a finding, because the guard can't see the keys. Per key:
   - The key is in the closed EVENT_PII_KEYS = {`emails`, `senderEmail`, `email`, `to`, `actor`}. These are the TA-5.3 members that carry an address by contract. This set is the event-side "PII-flagged property" (FR-048-3, AC-048-2). The value is free, but an email literal is still a finding.
   - The key names a file name, IP or UA (words `ip`/`ua`, or `file`+`name`). The value must be exactly one slot-wrapped call (`PiiTransforms.FileName/IpAddress/UserAgent`, `FileNameSlot`, `IpSlot`) (AC-048-1).
   - Any other key: the key and the value identifiers must not name PII (the `isLogPiiIdentifier` rule; bare `Name` is still allowed, so `reason = ex.GetType().Name` passes). No email literal is allowed.
   - `'original'` joins NAME_QUALIFIERS, so `OriginalName` (the real file-name column) is caught by R7 and R8. Step 1 proves the tree stays clean.
6. **D1 (recommended A): no Pii=true slot.** AC-048-2 is met by the stricter rule: a raw address reaches **no** telemetry property and no log template, and `{Email:Pii}` stays an R7 finding (new selftest pin). Raw addresses may travel only in Service Bus payloads under EVENT_PII_KEYS. FR-048-5's "enrichment mapping" unit test becomes the selftest proof that the `:Pii` format suffix doesn't bypass R7.
7. **D2 (recommended A): encode, don't change, erasure behavior.** T-069 states today's dispositions truthfully, and the tests prove they stay in sync with the marks and the EF model. There are **no store edits**, so nothing conflicts with PR #712. Scrubbing the `Retained`/`AccountSoftDelete` gaps becomes a follow-up task after #712.
8. **Commits:** one `feat:` commit per story, in order T-069-01 (US-048-01), T-069-02 (US-048-02), T-069-03 (US-048-03).

```
DECISION: Should T-069 add a Pii=true App Insights property / {Message:Pii} log slot so raw emails may reach telemetry when flagged?
CONTEXT: FR-048-3/5 and AC-048-2 assume such a slot. Today no telemetry or log line carries a raw address, and R2/R7 forbid it. Building it would add a new PII flow into App Insights (90 d retention).
OPTIONS:
  A) No slot: raw addresses stay out of telemetry/logs entirely; `{X:Pii}` remains an R7 finding (selftest-pinned); events carry addresses only under the closed TA-5.3 keys — strictest, zero new code paths
  B) Build an enricher that maps `:Pii` placeholders to a Pii=true custom property and relax R7 for them — matches the spec text literally, but adds PII to telemetry, needs sink wiring (TA-10.5 Serilog is not wired; possible package/ADR) and widens the GDPR sweep
RECOMMEND: A — no producer needs a raw address in telemetry; B is a PII-shaped expansion (AGENT §7)
BLOCKS: none (plan assumes A)
```

```
DECISION: The erasure cascade leaves raw PII in 12 of 14 columns (AppUser soft-delete only, AuthToken never purged, Transfer.SenderEmail kept on re-home, audit/outbox/suppression rows) — fix it in T-069 or encode it and follow up?
CONTEXT: US-048-03 says "the erasure cascade deletes each [Pii] column". In the code only EmailRecipient.Address and FileItem.OriginalName are deleted, and only at transfer expiry. Fixing it means editing AppUserStore/ExpiryDeletionStore, which PR #712 is rewriting, and changing PII behavior.
OPTIONS:
  A) T-069 encodes today's dispositions (AccountSoftDelete / TransferHardDelete / Retained + rationale) and tests their sync; a follow-up task (after #712) scrubs: purge AuthToken by email, null Transfer.SenderEmail on re-home, AppUser hard delete after the 30-day undo, outbox pruning; FR-048-2 doc updated to the 14 real columns — no store edits now, honest gate
  B) Extend SoftDeleteWithReHomeAsync in T-069 to scrub the gaps — closes the GDPR gap sooner, but conflicts with #712, goes beyond the T-069 row, and needs owner sign-off on what "erased" means for audit/suppression rows
RECOMMEND: A — the gate makes every gap visible in one reviewed array; behavior changes belong in their own PII-reviewed task
BLOCKS: none for T-069 (plan assumes A); the scrub follow-up needs this answer
```

## 3. Implementation Plan

### Step 1 — T-069-01 (US-048-01): event-payload rule R8 + file-name hash pin
1. `scripts/check-telemetry-pii.mjs`:
   - Header: add an R8 paragraph after R7. Change the line "T-069 owns the [Pii] attribute and event-DTO convention tests." so it says R8 is T-069's event-payload scan.
   - Constants near line 59: add EVENT_PII_KEYS and `PAYLOAD_RE = /\bPayload\s*=(?!=)/g`. Add `'original'` to NAME_QUALIFIERS (line 100).
   - Helpers: add `extractBraces(code, open)`, a brace-matching twin of `extractCall` (line 230). Add `csAnonMembers(code, start, end)`, which splits with `splitTopLevel` (line 243). A segment `key = expr` gives `key`. A projection `a.b.C` gives key `C`. A bare identifier gives itself.
   - Add `scanPayloads(file, text)` → `{ findings, payloads }`. Mask with `mask(text,'cs')`. For each PAYLOAD_RE match in `code`, resolve the right-hand side as Design §5 describes and apply the per-key rules. Use `stripSlots` (line 280) to decide "exactly one slot call". An identifier RHS resolves to the **last** `\b<id>\s*=\s*new\s*\{` before the match.
   - `scanTree()` (line 588): call `scanPayloads` for every `.cs` file except REPORTER_CS. Add `payloads` to `counts`. `runScan()`: add `'R8'` to the rule loop (line 634) and `counts.payloads === 0` to the vacuous-pass guard (line 638). Mention `payloads` in the scanned-summary line. Add R8 to the PASS/FAIL hint text.
   - `runSelfTest()` (line 653): add `payload: (c) => scanPayloads('selftest.cs', c).findings` to `scan`. Seeds: S33 `Payload = new { transferId = t.Id, fileName = file.OriginalName }` (R8); S34 `Payload = new { transferId = t.Id, recipient = recipient.Address }` (R8); S35 `Payload = new { ipHash = request.ClientIp }` (R8); S36 `Payload = BuildPayload(x)` (R8); S37 `Payload = new { to = "dev@example.com" }` (R8). Clean: C17 `Payload = new { transferId = context.TransferId, to = recipient.Address, attempt = attempt, locale }`; C18 `var payload = new { transferId = transfer.Id, emails = addresses, senderEmail = transfer.SenderEmail };\nvar e = new EventEnvelope { Payload = payload };`; C19 `Payload = new { to = email, attempt = maxDeliveryCount, reason = ex.GetType().Name, kind, step }`; C20 `Payload = new { transferId = t.Id, fileName = PiiTransforms.FileName(file.OriginalName) }`; C21 `Payload = new { actor = actorEmail, action, entityType, entityId, details }`.
2. `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs`: add `FileName_IsAFullHash_NotATruncation`. For the US-048-01 example "2026-salary-proposal-final_v3.pdf", the result is 64 lowercase hex chars, doesn't contain "salary", and differs from the transform of a one-character edit. This covers the US-048-01 edge case "hashed, not truncated" (AC-048-1).
3. Verify: `node scripts/check-telemetry-pii.mjs` (PASS, about 22 payloads, R8 0) and `node scripts/check-telemetry-pii.mjs selftest` (all seeds caught, all clean shapes passed). Then `dotnet test tests/wa.domain.unit`. If the tree scan finds a real R8 or R7 hit, stop and report it. Don't edit handlers; that's scope creep.
4. Commit `feat: T-069-01 US-048-01 event-payload PII rule R8 (AC-048-1/2)`.

### Step 2 — T-069-02 (US-048-02): `[Pii]` / `[NotPii]` markers + naming convention test
1. CREATE `src/wa.domain/Privacy/PiiAttribute.cs` and `src/wa.domain/Privacy/NotPiiAttribute.cs`:
   ```csharp
   namespace wa.domain.Privacy;
   /// <summary>Marks a property whose value is raw PII under TA-9.4 (email, person/org name, file name, IP). Every marked persisted column must be listed in <see cref="ErasureScope.AllColumns"/> (F-XCT-005 FR-048-2/4).</summary>
   [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
   public sealed class PiiAttribute : Attribute;

   /// <summary>Escape hatch for a PII-looking name that holds no personal data (US-048-02); every use is pinned in the convention test so it stays reviewable.</summary>
   /// <param name="reason">Why the value is not personal data, e.g. "HMAC of the IP (TA-9.4)".</param>
   [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
   public sealed class NotPiiAttribute(string reason) : Attribute
   {
       /// <summary>Why the value is not personal data.</summary>
       public string Reason { get; } = reason;
   }
   ```
   One type per file. The `<see cref="ErasureScope.AllColumns"/>` cref only resolves once Step 3 lands. In Step 2, write `<c>ErasureScope.AllColumns</c>` instead, and switch it to the cref in Step 3. An unresolved cref is CS1574, a build error.
2. MODIFY the entities. Add `using wa.domain.Privacy;` and one attribute line above each property:
   - `[Pii]`: `AppUser.Email` (`src/wa.domain/AppUser.cs:9`), `AppUser.DisplayName` (`:13`), `AppUser.OrgName` (`:25`); `AuditLog.ActorEmail` (`src/wa.domain/AuditLog.cs:9`), `AuditLog.DetailsJson` (`:17`); `AuthToken.Email` (`src/wa.domain/AuthToken.cs:9`); `DomainEvent.EnvelopeJson` (`src/wa.domain/DomainEvent.cs:13`), `DomainEvent.PartitionKey` (`:19`); `EmailRecipient.Address` (`src/wa.domain/EmailRecipient.cs:11`); `EmailSuppression.Address` (`src/wa.domain/EmailSuppression.cs:9`), `EmailSuppression.SenderEmail` (`:11`); `FileItem.OriginalName` (`src/wa.domain/FileItem.cs:13`); `Transfer.SenderName` (`src/wa.domain/Transfer.cs:23`), `Transfer.SenderEmail` (`:25`).
   - `[NotPii(...)]`: `DownloadEvent.IpHash` (`src/wa.domain/DownloadEvent.cs:13`, "HMAC-SHA256 of the IP keyed by Wa:Jwt:Secret (TA-9.4) — never the raw IP"); `Plan.Name` (`src/wa.domain/Plan.cs:14`, "product plan display name, not a person").
   - Fix the now-false `<summary>` of `AuditLog.DetailsJson` (it says "must not contain raw PII", but `src/wa.api/Endpoints/AdminEndpoints.cs:51,320` store an address). Fix `DomainEvent.PartitionKey` too (it can be an email for auth/billing events, TA-5.2). Doc text only.
3. CREATE the test helper `tests/wa.application.unit/Privacy/PiiConvention.cs` (`internal static class`):
   - `IsPiiLookingName(string name)`: camelCase word split (regex `[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+`, same as the script). True if any word is in {email, mail, name, ip, address}.
   - `IReadOnlyList<string> NamingViolations(IEnumerable<PropertyInfo> stringColumns)`. Report a PII-looking property with neither attribute as `"{Type}.{Prop}: PII-looking string column without a marker — mark it [Pii] (and add it to ErasureScope.AllColumns) or [NotPii(\"reason\")] (US-048-02)"`. Report a property with **both** attributes, and a `[NotPii]` with a blank reason.
   - `IReadOnlyList<PropertyInfo> ModelStringColumns()`: build `new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)`, then `Model.GetEntityTypes()` → `GetProperties()` where `ClrType == typeof(string)` and `PropertyInfo is not null`. Dispose the context.
4. CREATE the fixtures, one type per file, under tests/wa.application.unit/Privacy/Fixtures/: `UnmarkedEmailFixture` (`public string Email { get; set; } = "";`), `MarkedEmailFixture` (same, with `[Pii]`), `SurpriseNameFixture` (`public string ContactName`, unmarked), `NotPiiNameFixture` (`[NotPii("product label")] public string Name`).
5. CREATE `tests/wa.application.unit/Privacy/PiiNamingConventionTests.cs`:
   - `UnmarkedEmail_Fixture_FailsNamingTheProperty_AndTheFix` (red). Assert one violation that contains "UnmarkedEmailFixture.Email" and "[Pii]".
   - `MarkedEmail_Fixture_Passes` (green after marking). `SurpriseName_Fixture_Fails` (FR-048-2 "no Name in a surprise place"). `NotPii_Fixture_Passes`.
   - `EveryModelColumn_IsMarkedOrClean`: `NamingViolations(ModelStringColumns())` is empty.
   - `NotPiiEscapes_ArePinned`: the set of `[NotPii]` model columns equals exactly {"DownloadEvent.IpHash", "Plan.Name"} (US-048-02 edge case "every use visible in one file").
   - `LoggerPiiSlot_IsNotABypass` is **not** a C# test. It is selftest seed S38 `_logger.LogInformation("sent {Email:Pii}", email);` expecting R7, added to `scripts/check-telemetry-pii.mjs` in this step (D1-A, AC-048-2 / EC-048-2).
6. Verify: `dotnet build src/wa.slnx` (zero warnings), `dotnet test tests/wa.domain.unit`, `dotnet test tests/wa.application.unit`, both node script modes, and `dotnet format src/wa.slnx` in verify-no-changes mode. Check there's no model change: dotnet ef migrations has-pending-model-changes (project wa.infrastructure, startup project wa.api) must report no changes. If the tool needs a live DB, skip it and say so: EF ignores plain CLR attributes.
7. Commit `feat: T-069-02 US-048-02 [Pii]/[NotPii] markers + naming convention gate (AC-048-2/3)`.

### Step 3 — T-069-03 (US-048-03): `ErasureScope.AllColumns` + cascade sync tests
1. CREATE `src/wa.domain/Privacy/ErasureDisposition.cs` (enum, three members as in Design §2, each with a doc). CREATE `src/wa.domain/Privacy/ErasureColumn.cs`: `public sealed record ErasureColumn(Type Entity, string Property, ErasureDisposition Disposition, string Rationale);` with `<param>` docs. CREATE `src/wa.domain/Privacy/ErasureScope.cs`: `public static class ErasureScope` with `public static IReadOnlyList<ErasureColumn> AllColumns { get; } = [ ... ];` holding the 14 entries:
   - AccountSoftDelete: AppUser.Email / DisplayName / OrgName ("soft-deleted by DELETE /auth/me (AppUserStore.SoftDeleteWithReHomeAsync); no hard delete yet — D2").
   - TransferHardDelete: EmailRecipient.Address, FileItem.OriginalName ("row removed by f-delete-transfers, ExpiryDeletionStore.DeleteTransfersAsync").
   - Retained: AuthToken.Email ("magic-link rows are never purged — D2"), AuditLog.ActorEmail and AuditLog.DetailsJson ("admin audit trail"), DomainEvent.EnvelopeJson and DomainEvent.PartitionKey ("outbox rows are not pruned"), EmailSuppression.Address and EmailSuppression.SenderEmail ("an opt-out must outlive the account"), Transfer.SenderName ("EC-048-1: anonymized attribution kept on re-home; Transfer row is only soft-deleted"), Transfer.SenderEmail ("kept on re-home today — D2").
   - Update the `PiiAttribute` summary to the real `<see cref="ErasureScope.AllColumns"/>`.
2. CREATE `tests/wa.application.unit/Privacy/ErasureScopeTests.cs`, and add `ClosedListViolations(IEnumerable<PropertyInfo> props, IReadOnlyCollection<ErasureColumn> list)` to `PiiConvention.cs`. It reports a `[Pii]` property missing from the list, and a list entry with no `[Pii]` property behind it.
   - `MarkedColumns_EqualTheClosedList` (AC-048-3, US-048-03 AC1): run on the `[Pii]` properties of `ModelStringColumns()` plus every `[Pii]` property in `typeof(AppUser).Assembly`. Expect zero violations both ways.
   - `MarkedFixture_NotInClosedList_Fails` / `MarkedFixture_InClosedList_Passes`: `MarkedEmailFixture` against `ErasureScope.AllColumns` gives one violation; against `[.. ErasureScope.AllColumns, new(typeof(MarkedEmailFixture), "Email", ErasureDisposition.Retained, "fixture")]` it is clean. This is the "new Pii column passes only if listed" proof (AC-048-3, US-048-03 AC2 Phase 3 path).
   - `EveryEntry_IsAMappedStringColumn_Once`: each entry resolves to an EF entity type, the property exists with `ClrType == typeof(string)`, and there are no duplicates.
   - `Dispositions_MatchTheModel`: AccountSoftDelete ⇒ `Entity == typeof(AppUser)`; TransferHardDelete ⇒ the entity type has a FK whose principal CLR type is `Transfer`; every entry has a non-blank `Rationale`.
   - `ClosedList_HasFourteenColumns`: an `Assert.Equal(14, ...)` pin. Changing the list is a reviewed diff (the US-048-03 "triad" control).
3. Verify: the full local gate (below).
4. Commit `feat: T-069-03 US-048-03 ErasureScope closed list + erasure sync tests (AC-048-3)`.

### Local gate (Docker is down; integration tests run only in CI)
`dotnet test tests/wa.domain.unit`, `dotnet test tests/wa.application.unit`, `dotnet test tests/wa.workers.unit`, `dotnet build tests/wa.api.integration`, `dotnet format src/wa.slnx` in verify-no-changes mode, `node scripts/check-telemetry-pii.mjs` plus `selftest`, `node scripts/check-theme-tokens.mjs`, `node scripts/check-contrast.mjs`. wa.web isn't touched, so the npm gates are skipped.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `scripts/check-telemetry-pii.mjs` | MODIFY | Step 1: R8 payload scan + seeds S33–S37 / C17–C21, `'original'` qualifier; Step 2: seed S38 |
| `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs` | MODIFY | Step 1: `FileName_IsAFullHash_NotATruncation` |
| `src/wa.domain/Privacy/PiiAttribute.cs` | CREATE | Step 2 marker |
| `src/wa.domain/Privacy/NotPiiAttribute.cs` | CREATE | Step 2 escape hatch with `Reason` |
| `src/wa.domain/AppUser.cs` | MODIFY | `[Pii]` Email, DisplayName, OrgName |
| `src/wa.domain/AuditLog.cs` | MODIFY | `[Pii]` ActorEmail, DetailsJson; fix DetailsJson summary |
| `src/wa.domain/AuthToken.cs` | MODIFY | `[Pii]` Email |
| `src/wa.domain/DomainEvent.cs` | MODIFY | `[Pii]` EnvelopeJson, PartitionKey; PartitionKey summary |
| `src/wa.domain/EmailRecipient.cs` | MODIFY | `[Pii]` Address |
| `src/wa.domain/EmailSuppression.cs` | MODIFY | `[Pii]` Address, SenderEmail |
| `src/wa.domain/FileItem.cs` | MODIFY | `[Pii]` OriginalName |
| `src/wa.domain/Transfer.cs` | MODIFY | `[Pii]` SenderName, SenderEmail |
| `src/wa.domain/DownloadEvent.cs` | MODIFY | `[NotPii(...)]` IpHash |
| `src/wa.domain/Plan.cs` | MODIFY | `[NotPii(...)]` Name |
| `tests/wa.application.unit/Privacy/PiiConvention.cs` | CREATE | Test helper: name rule, NamingViolations, ModelStringColumns, ClosedListViolations (Step 3) |
| `tests/wa.application.unit/Privacy/Fixtures/UnmarkedEmailFixture.cs` | CREATE | Red fixture |
| `tests/wa.application.unit/Privacy/Fixtures/MarkedEmailFixture.cs` | CREATE | Green fixture |
| `tests/wa.application.unit/Privacy/Fixtures/SurpriseNameFixture.cs` | CREATE | Surprise-column fixture |
| `tests/wa.application.unit/Privacy/Fixtures/NotPiiNameFixture.cs` | CREATE | Escape-hatch fixture |
| `tests/wa.application.unit/Privacy/PiiNamingConventionTests.cs` | CREATE | Step 2 tests |
| `src/wa.domain/Privacy/ErasureDisposition.cs` | CREATE | Step 3 enum |
| `src/wa.domain/Privacy/ErasureColumn.cs` | CREATE | Step 3 record |
| `src/wa.domain/Privacy/ErasureScope.cs` | CREATE | Step 3 closed list (14) |
| `tests/wa.application.unit/Privacy/ErasureScopeTests.cs` | CREATE | Step 3 tests |

**Context for the MODIFY files (the Coder's discovery result):**

- **`scripts/check-telemetry-pii.mjs`** (735 lines). Lines 59-70 hold the closed sets (SERVER_PROPS, CLIENT_PROPS, STRING_SLOT_METHODS, SLOT_METHODS, PII_WORDS). Line 99 is LOG_PII_WORDS. Line 100 is `const NAME_QUALIFIERS = new Set(['file', 'sender', 'display', 'user', 'recipient', 'owner'])`; add `'original'` there.
- Reusable helpers in that file:
  - `mask(text, lang)` (114) returns `{ nc, code }`. `nc` keeps string contents; `code` blanks strings and comments, at the same indices.
  - `extractCall(code, open)` (230) is paren-only; R8 needs a brace twin.
  - `splitTopLevel(code, start, end)` (243) returns `{s, e}` segments.
  - `identifierWords` (261), `stripSlots` (280; it already strips `PiiTransforms.FileName|IpAddress`, `FileNameSlot`, `IpSlot`), `isLogPiiIdentifier` (337), EMAIL_RE (96), `lineOf` and `finding(file, line, rule, message)`.
  - Extend the slot regex in `stripSlots` with `|UserAgent` so `uaHash = PiiTransforms.UserAgent(..)` counts as wrapped.
- `scanTree()` (588-625) calls `scanLog`, `scanLoggerMessage` and then `scanReporter`/`scanCs` per `.cs` file. Add `scanPayloads` there for non-reporter files. `runScan()` (627-650) holds the rule list `['R0'..'R7']` and the vacuous-pass guard. `runSelfTest()` (653-727) holds the `scan` map, then `seeded` as `[id, kind, rule, code]` (last is S32) and `clean` as `[id, kind, code]` (last ids C16/C5).
- Real payload shapes the tree scan must pass (all under src/wa.application/UseCases/):
  - `Transfers/SendTransferHandler.cs:162-170` (`emails`, `senderEmail`), via `var payload` → `Payload = payload` (181).
  - `Transfers/ActivateScheduledTransfersHandler.cs:65-73`.
  - `Transfers/SendNotificationEmailsHandler.cs:173-178, 202-207` (`to = recipient.Address`).
  - `Admin/AuditWriter.cs:47-54` (`actor = actorEmail`).
  - `Auth/RequestMagicLinkHandler.cs:62-67` and `Auth/ForgotPasswordHandler.cs:71-77` (`email = email`, `token = rawToken`).
  - `Billing/SendBillingEmailHandler.cs:140,160` (`reason = ex.GetType().Name`).
  - `Billing/BillingEmailSchedule.cs:71` and `Billing/BillingGrace.cs:76` (projection `email`).
- Lowercase `payload` variables that are not envelopes must stay ignored: `src/wa.workers/JobAuth.cs:50`, `src/wa.workers/ZipFunction.cs:198,216`, `src/wa.api/Middleware/ProblemWriter.cs:26`. They have no `Payload = payload` partner. The comment "Payload = ..." in `src/wa.api/Endpoints/UnsubscribeEndpoints.cs:19` is masked, so it stays ignored too.
- **`tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs`**. Lines 54-89 are the "TA-9.4 PII transforms" section: `FileName_DeterministicAndMatchesKnownSha256`, the IP vector and key-sensitivity tests. Determinism and key-sensitivity are already covered, so add only the not-a-truncation fact at the end of the class.
- **Entity files** (`src/wa.domain/*.cs`) all start with `namespace wa.domain;` (line 1) and have no usings. Put `using wa.domain.Privacy;` above the namespace. Each marked property is a single `public string ... { get; set; }` line, at the line numbers given in Step 2.2. Today the only PII signal is the `<summary>` text "PII (TA-9.4)" (for example `src/wa.domain/AppUser.cs:8`); keep it.
  - `src/wa.domain/AuditLog.cs:16` reads `/// <summary>Optional JSON payload with action-specific details; must not contain raw PII.</summary>`. Replace it with an accurate summary, for example "may carry a raw address for suppression/user actions; PII (TA-9.4)".
  - `src/wa.domain/DomainEvent.cs:18` reads `/// <summary>Optional Service Bus partition/session key ... (for example the transfer id).</summary>`. Add "or an email for auth/billing events (TA-5.2); PII".
- PR #712 rewrites the persistence stores. This plan edits **no** file under src/wa.infrastructure/; the only infrastructure use is reading `WaDbContext.Model` from tests.

## 4. Open Questions / Risks

- **D1 / D2** above. The plan assumes A for both. If the owner picks D2-B, add a separate task after PR #712.
- **Orchestrator follow-ups (docs/CI, not the Coder):**
  1. In `docs/AGENT.md` §4, extend the `check-telemetry-pii.mjs` comment with "R8 (T-069): TA-5.3 payload keys". No new CI job is needed; pii-check already runs both modes.
  2. Rewrite the F-XCT-005 FR-048-2 closed list to the 14 real columns and their dispositions. Correct `TransferFile.FileName` to `FileItem.OriginalName`, and the "DownloadEvent IP" entry to HMAC only. Record D1/D2 in `Open-Decisions-and-Constants.md` Part 3.
  3. Add `ErasureScope.AllColumns` (14) and EVENT_PII_KEYS (5) to `docs/REPO-FACTS.md` §1.
  4. Open the D2 scrub follow-up task.
- **Name-based limits:** free-text and JSON columns are only as safe as their explicit marks. `Transfer.Note` and `IdempotencyKey.ResultJson` don't match the name rule and are left unmarked. Flag them for D2 review. R8, like R7, is a text scan: a payload built in a helper method and passed by a neutral name gets an "R8 hidden shape" finding, not a silent pass.
- **Magic-link token in payloads** (`token = rawToken`) is a secret by design (the worker renders it). R8 doesn't flag `token`; TA-5.3 spec covers this. Not changed here.
- **Integration tests:** not touched. They only need to compile locally (`dotnet build tests/wa.api.integration`); CI runs them.
- **Spec drift to watch:** if the tree scan or `EveryModelColumn_IsMarkedOrClean` finds a column or payload this plan didn't list, the Coder must stop and report it. The Coder doesn't widen EVENT_PII_KEYS or `[NotPii]` alone, because each is a reviewed closed set.
