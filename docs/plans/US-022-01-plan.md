# US-022-01 — Pick the moment my files arrive (schedule at create)

**Feature:** F-PRF-002 Scheduled Send (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-002/US-022-01-schedule-transfer.md`
**Scope:** FR-022-1 (link-screen "Send at"), FR-022-3 (gate), FR-022-4 create half (store ScheduledSendAtUtc, Status Scheduled, transfer.created now with scheduledSendAt), FR-022-7 (ExpiresAt from creation), AC-022-4, story AC "link is live now". New TransferStatus.Scheduled = 5 + migration + ADR note.
**Task id note:** F-PRF-002 has no backlog row / T-xxx files — the story id is the task id (same convention as US-020-*).
**Out of scope (seams only):** window 5 min..14 days + SCHEDULE_OUT_OF_WINDOW (US-022-03), link-screen semantics helper + recipient pre-due line (US-022-02), timer flip Scheduled→Active, f-email skip, expiry of Scheduled rows, schedule_lag_seconds (US-022-04).

## 1. Goal

A Pro+ sender (PlanContext.Features.Scheduling) can choose "Schedule" + a datetime on the link screen; POST send stores `Transfer.ScheduledSendAtUtc`, sets Status 5 (Scheduled), keeps the link live and emits transfer.created immediately with scheduledSendAt; Free/guest/gate-off cannot. **Exit check:** new unit + integration + vitest tests green, migration applies on a fresh container, full AGENT.md §4 gate green.

## 2. Design & Decisions

### Grounded facts
- `Transfer.Status` is a raw `byte` with literal comparisons everywhere — there is **no** C# `TransferStatus` type today (`src/wa.domain/Transfer.cs:10-11`; e.g. `src/wa.application/UseCases/Transfers/SendTransfer.cs:105`, `:147`). Column is TINYINT with no CHECK constraint (`src/wa.infrastructure/Persistence/EntityConfigurations/TransferConfiguration.cs:28-29`); value 5 needs no DDL change. `ScheduledSendAtUtc` and `IX_Transfer_Sched` already exist (`Transfer.cs:20`, `TransferConfiguration.cs:66-68`, TA-3.2).
- The handler already emits `scheduledSendAt = transfer.ScheduledSendAtUtc` (`SendTransfer.cs:192`) — it is just never set. `SendTimeLimits.Stamp` requires Status 0 and stamps ExpiresAt from now (`src/wa.domain/Transfers/SendTimeLimits.cs:13-21`) → FR-022-7 holds if Stamp runs before the status assignment (it already does).
- Recipient read paths treat any status other than 2/3/4 as live (`src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs:86-97`; same shape in `GetFileDownloadUrlQuery.cs:69-75`, `GetDownloadAllUrlQuery.cs:78-84`, `UnlockTransfer.cs:57-63`) → Status 5 is "live now" with zero change there.
- Account quota/count predicates are `Status == 1 || Status == 3` (`src/wa.infrastructure/Persistence/TransferStore.cs:29-51`), used by finalize, send and GET /billing/plan — a Status-5 row would silently escape ACTIVE_TRANSFERS_MAX and STORAGE_QUOTA (FR-020-2).
- Effective features = plan row AND global feature.* gate (`src/wa.application/Limits/FeaturesProvider.cs:30-45`); the feature.scheduling gate is seeded "false" (`src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs:33`, `:85-86`); plan values free=false, pro/business=true (`:49`, `:64`, `:80`).
- Precedent for a plan-feature 403: `OrgLogoRejectedException.ForPlan()` → FORBIDDEN "Pro plan required." (`src/wa.application/UseCases/Auth/OrgLogo.cs:62-64`, used at `src/wa.application/UseCases/Auth/MeProfile.cs:194`).
- f-email sends on transfer.created with no status check (`src/wa.infrastructure/Persistence/EmailStore.cs:18-38`, `src/wa.application/UseCases/Transfers/SendNotificationEmails.cs:55-96`). See R-1.
- Web: phase gates are a fail-closed zustand store (`src/wa.web/src/core/features/featureGates.ts:15-56`); the session user's plan is read from GET /api/v1/billing/plan (`src/wa.web/src/core/upload/loadPlanLimits.ts:5-24`, endpoint `src/wa.api/Program.cs:1427-1439`), which carries no features today.

### D-1 — `TransferStatus` byte constants in wa.domain (TA-3.2, onion rule)
CREATE `src/wa.domain/TransferStatus.cs`: `public static class TransferStatus` with `public const byte` Draft 0, Active 1, Expired 2, DownloadLimit 3, Deleted 4, Scheduled 5. Constants (not an enum) so they compare with the existing `byte` property without casts and without touching the ~40 existing literal sites (no drive-by refactor). New code in this story uses the constants; old literals stay.

### D-2 — Migration = documented closed mapping on the column (golden rule 4, TA-3.7)
No column/table change is needed, but the spec requires a migration for the domain value change. The migration records the mapping in the schema: `b.Property(t => t.Status).HasColumnType("TINYINT").HasComment("0 Draft,1 Active,2 Expired,3 DownloadLimit,4 Deleted,5 Scheduled")` → `dotnet ef migrations add AddTransferStatusScheduled` (emits an MS_Description extended property; Down drops it). Rejected: an empty marker migration (no content to review); a CHECK constraint (a new schema object beyond TA-3.2 → would need §7 escalation). ADR note: docs close-out adds TA-17 row ADR-019 and updates the TA-3.2 Status comment (orchestrator, §6).

### D-3 — API contract: optional `scheduledSendAt` on endpoint 3 (TA-4.2#3 "schedule")
Body field `scheduledSendAt`: ISO-8601 string with offset (the SPA sends `toISOString()`, "Z"). Absent or JSON null → send now (today's behavior, byte-for-byte). A string that JsonElement.TryGetDateTimeOffset rejects, or any other JSON kind → 400 VALIDATION before dispatch. Endpoint passes `UtcDateTime` into `SendTransferCommand.ScheduledSendAtUtc` (new trailing optional param — every existing positional caller compiles unchanged). Response gains `scheduledSendAt` and `status` becomes "Scheduled" when scheduled ("Active" otherwise).

### D-4 — Gate in the handler, before any mutation (FR-022-3, FR-020-1)
In `SendTransferHandler.Handle`, keep the whole PlanContext (one read, `SendTransfer.cs:109`). After the 3c active-transfers check: `if (request.ScheduledSendAtUtc is not null && !plan.Features.Scheduling) throw new ScheduleForbiddenException();` → endpoint maps to 403 FORBIDDEN "Pro plan required." (closed list unchanged; guests resolve Free → 403). Directly after it, a comment marks the US-022-03 seam (window check here, server time, before mutation). Then in step 5: `transfer.ScheduledSendAtUtc = request.ScheduledSendAtUtc; transfer.Status = request.ScheduledSendAtUtc is null ? TransferStatus.Active : TransferStatus.Scheduled;`. Payload (step 7) already reads the field; `SendTransferResult` gets `Status = transfer.Status` instead of the literal `1` (`:220`) and a trailing `DateTime? ScheduledSendAtUtc = null`. Telemetry: none new (TA-10.2 untouched).

### D-5 — Scheduled counts as active for quotas (FR-020-2, FR-007-4)
`TransferStore.GetActiveStorageBytesAsync` and `GetActiveTransferCountAsync` predicates become `Status == 1 || Status == 3 || Status == TransferStatus.Scheduled`. A scheduled transfer holds its blobs and a slot from creation (it is live). Admin overview/user counts (`OverviewStore.cs:24,79`, `AdminUserStore.cs:52,60`) are left as-is (R-3).

### D-6 — Web: the plan feature comes from GET /billing/plan (FR-022-3, AC-022-4)
`PlanUsageResult` gains trailing `bool Scheduling = false` (= `plan.Features.Scheduling`), the endpoint adds `features = new { scheduling = r.Scheduling }`. No literal plan codes in the SPA.
Link-screen rendering rules: global gate feature.scheduling off → no control at all (FR-044-7, absent). Gate on → toggle "Send now" / "Schedule"; if the plan read says false (Free) or fails / 401 (guest) → "Schedule" disabled (UI-Reference §4.5: opacity, `title` tooltip on a wrapper span) plus a line "Pro plan required" + "See plans" link to PLAN_PATH (`src/wa.web/src/features/billing/planRoute.ts:6`, same pattern as `src/wa.web/src/features/profile/OrgNameSection.tsx:86-106`). Plan read pending → toggle not rendered yet.
Schedule mode shows a labelled datetime-local input + helper "Between now and 14 days from now." (exact spec copy; US-022-03 adds min/max + window errors). Submitting "Send transfer" in schedule mode with an empty/unparseable value shows an inline error and sends nothing. "Copy link only" always sends now (zero recipients — nothing to schedule). Sent phase: when the response carries scheduledSendAt, the sub-line reads "Scheduled for {date}, {time} — the link is live, emails go out then." (formatDate/formatTime from `src/wa.web/src/core/i18n/format.ts:13-28`), else the existing shareLater line.
`LinkScreen.test.tsx` mocks `loadFeatureGates` to a no-op so existing call-count assertions (`LinkScreen.test.tsx:285-300`, `:331-356`) never see a GET /features; schedule tests set `useFeatureGateStore` directly.

## 3. Implementation Plan

1. **Status constants** — CREATE `src/wa.domain/TransferStatus.cs` (D-1); MODIFY `src/wa.domain/Transfer.cs:10` doc comment → add "5=Scheduled (F-PRF-002)". CREATE `tests/wa.domain.unit/Transfers/TransferStatusTests.cs`: pins all six values (0..5) and that Scheduled differs from every other value. Verify: dotnet test tests/wa.domain.unit with test filter FullyQualifiedName~TransferStatusTests.
2. **EF + migration** — MODIFY `TransferConfiguration.cs:28-29` (comment lists 5 Scheduled; add HasComment per D-2) and `:66` comment ("scheduler picks due Scheduled rows (US-022-04)"). Run the AGENT.md §5.3 migrations-add command with name AddTransferStatusScheduled (project src/wa.infrastructure, startup project src/wa.api). Inspect: Up only adds the Status column comment, Down removes it, snapshot updated, nothing else. Verify: dotnet build src/wa.api.
3. **Handler** — MODIFY `src/wa.application/UseCases/Transfers/SendTransfer.cs` per D-4: command param `DateTime? ScheduledSendAtUtc = null` (after OwnerAppUserId), result param `DateTime? ScheduledSendAtUtc = null`, gate + seam comment, status/field assignment, result Status from the row; append `public sealed class ScheduleForbiddenException : Exception` (message "Pro plan required.") next to `FilesGoneException`.
4. **Unit tests (handler)** — MODIFY `tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs`: add `public PlanFeatures Features { get; set; } = new();` to `StubLimitsProvider` and return it from GetAsync (`:847-862`). New facts:
   - `Send_Scheduled_ProPlan_StoresUtcAndStatusScheduled` — Features.Scheduling true, +2 h → row Status == TransferStatus.Scheduled, ScheduledSendAtUtc equals the input, result.Status 5 and result.ScheduledSendAtUtc set (story AC part "stored"; FR-022-4).
   - `Send_Scheduled_EmitsTransferCreatedNow_WithScheduledSendAt` — exactly one envelope published during Handle, Type TransferCreated, payload scheduledSendAt equals the input (FR-022-4 "immediately").
   - `Send_Scheduled_ExpiresAtFromCreation_NotFromDue` — ExpiresAtUtc ≈ now + RetentionDays (FR-022-7).
   - `Send_Scheduled_PlanWithoutScheduling_ThrowsScheduleForbidden_NoWrite` — Features default (false) → ScheduleForbiddenException, SaveSendAsync not called, nothing published (FR-022-3 server half; covers guest = Free and gate off since both reach the handler as Scheduling false).
   - `Send_NoSchedule_StaysActive_ScheduledSendAtNull` — regression: Status 1, payload scheduledSendAt null.
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~SendTransferHandlerTests.
5. **Endpoint** — MODIFY `src/wa.api/Program.cs:308-350` per D-3: parse after `password` (`:322`); invalid → `throw new BusinessException(ProblemCodes.Validation, "scheduledSendAt must be an ISO 8601 date-time with an offset.")`; pass `scheduledSendAt?.UtcDateTime` as the new command arg; response `status = result.Status == TransferStatus.Scheduled ? "Scheduled" : "Active"`, `scheduledSendAt = result.ScheduledSendAtUtc`; add `catch (ScheduleForbiddenException ex) { throw new BusinessException(ProblemCodes.Forbidden, ex.Message); }`.
6. **Quota predicates** — MODIFY `src/wa.infrastructure/Persistence/TransferStore.cs:31-34` and `:48-51` per D-5 (update the "(a)" comment).
7. **Plan read carries the feature** — MODIFY `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs:11` (trailing `bool Scheduling = false`) and `:32-39` (pass `plan.Features.Scheduling`); MODIFY `src/wa.api/Program.cs:1432-1438` (add features = new { scheduling = r.Scheduling }). MODIFY `tests/wa.application.unit/Billing/GetPlanUsageHandlerTests.cs`: one fact — Scheduling mirrors PlanContext.Features.Scheduling (extend its StubPlanContext with an optional PlanFeatures). Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~GetPlanUsageHandlerTests.
8. **Integration** — CREATE `tests/wa.api.integration/ScheduledSendEndpointTests.cs` (containers + `SetupDraftTransferAsync`/`PostSendAsync` shapes from `tests/wa.api.integration/SendEndpointTests.cs:252-300`; user seed + forged cookie from `tests/wa.api.integration/PlanUsageEndpointTests.cs:147-215`; flip the feature.scheduling FeatureFlag row to "true" BEFORE the factory's first request, as `tests/wa.api.integration/OrgLogoUploadTests.cs:57` does for branding; use FeatureGates.Scheduling, not a literal). Facts:
   - `ProUser_Schedule_201_StatusScheduled_RowAndOutbox` — response 201, status "Scheduled", scheduledSendAt round-trips; DB row Status 5 + ScheduledSendAtUtc; outbox transfer.created row exists now with payload scheduledSendAt (FR-022-4).
   - `ProUser_Scheduled_LinkIsLiveNow` — GET /api/v1/public/transfers/{linkId} → 200 with the file list (story AC "it is live").
   - `ProUser_Scheduled_CountsTowardActiveTransfers` — GET /api/v1/billing/plan → activeTransfers.used 1, features.scheduling true (D-5, D-6).
   - `FreeUser_Schedule_403Forbidden_DraftUntouched` and `Guest_Schedule_403Forbidden` — code FORBIDDEN, row still Status 0, no outbox row (FR-022-3).
   - `GateOff_ProUser_Schedule_403Forbidden` — leave the flag seeded false (separate class or set back before host build; FlagsCache is 30 s, so choose the flag value per test class instance before the first request).
   - `InvalidScheduledSendAt_400Validation` — "next tuesday" → VALIDATION, row untouched.
   Verify: dotnet test tests/wa.api.integration with test filter FullyQualifiedName~ScheduledSendEndpointTests (Docker running).
9. **Web helpers** — CREATE `src/wa.web/src/features/sender/schedule.ts`: `toUtcIso(local: string): string | null` (datetime-local value → `new Date(local).toISOString()`, null on empty/invalid); `loadSchedulingAllowed(): Promise<boolean>` (GET http://localhost:8080/api/v1/billing/plan, credentials include, traceparent header; true only when `features.scheduling === true`; any failure/401 → false). MODIFY `src/wa.web/src/features/billing/planUsage.ts:6-12` (add optional `features?: { scheduling: boolean }`). CREATE `src/wa.web/src/features/sender/schedule.test.ts` (toUtcIso valid/empty/garbage; loader true / false / 401 / network error).
10. **ScheduleControl** — CREATE `src/wa.web/src/features/sender/ScheduleControl.tsx`: controlled props `{ mode: 'now' | 'schedule'; onModeChange; value: string; onValueChange; showRequiredError: boolean }`; reads `useFeatureGate('feature.scheduling')` (does NOT call loadFeatureGates); when on, calls loadSchedulingAllowed once on mount (alive-ref guard); renders per D-6: two buttons class `ls-mode-btn` with aria-pressed, disabled Schedule wrapped in `ls-actions-wrap`-style span with `title`; schedule mode: label (htmlFor) + `<input type="datetime-local" className="ls-sender-input ls-schedule-input">` + helper `ls-recipients-helper` + error `<p role="alert" className="ls-sender-error">` linked by aria-describedby. When not allowed, force mode 'now' via onModeChange. CREATE `src/wa.web/src/features/sender/ScheduleControl.test.tsx`: gate off → renders nothing and no fetch (FR-044-7); gate on + plan false → Schedule disabled, title + text "Pro plan required", plan link href /account/plan (AC-022-4); gate on + plan true → toggling Schedule shows the picker and the exact helper copy (FR-022-1 UI note).
11. **LinkScreen wiring** — MODIFY `src/wa.web/src/features/sender/LinkScreen.tsx`: state `sendMode`, `scheduleLocal`, `scheduleRequiredError`, `scheduledFor: string | null`; render `<ScheduleControl …/>` between the password block (`:356-380`) and `.ls-actions` (`:382`); `sendTransfer(emails, scheduledSendAt?: string)` adds `scheduledSendAt` to the body only when given (`:192-199`); `handleSendTransfer` (`:233-236`): schedule mode → `toUtcIso`, null → set error and return; `handleCopyLinkOnly` unchanged (always now). On success read `scheduledSendAt` from the response (extend `SendResponse` `:23-31`; replace the `void response` line `:216`) into `scheduledFor`; sent phase `:245` shows link.ready.scheduledFor when set. MODIFY `src/wa.web/src/features/sender/LinkScreen.test.tsx`: top-level vi.mock of `../../core/features/featureGates` keeping the real module but `loadFeatureGates: () => Promise.resolve()`; reset `useFeatureGateStore` in afterEach; new describe "schedule": gate on + plan fetch true → pick Schedule, set datetime, Send transfer → exactly one POST to the send URL whose body.scheduledSendAt equals toUtcIso(value) (story AC client half); empty picker → no POST, inline error; Copy link only in schedule mode → body has no scheduledSendAt; 201 with scheduledSendAt → sent line contains "Scheduled for" and "the link is live, emails go out then."; no-schedule send body has no scheduledSendAt key (regression).
12. **i18n** — MODIFY all 8 locale files in the locales folder (`en.json`, `de.json`, `es.json`, `fr.json`, `it.json`, `nl.json`, `pt.json`, `tr.json`; key-set parity is enforced by `src/wa.web/src/core/i18n/init.test.ts:52-55`). en values: link.schedule.sendNow "Send now"; link.schedule.schedule "Schedule"; link.schedule.label "Send at"; link.schedule.helper "Between now and 14 days from now."; link.schedule.required "Pick a date and time."; link.schedule.proRequired "Pro plan required"; link.schedule.viewPlan "See plans"; link.ready.scheduledFor "Scheduled for {{date}}, {{time}} — the link is live, emails go out then." Other locales: faithful translations, no exclamation points (UI-Reference §7); reuse each locale's existing profile.logo.proRequired / profile.logo.viewPlan wording.
13. **CSS** — MODIFY `src/wa.web/src/index.css`: `.ls-schedule` (column, gap 4px, like `.ls-sender` `:467-471`), `.ls-mode` row, `.ls-mode-btn` (UI-Reference §4.5 secondary button — copy the declarations of the existing `.ls-copyonly-btn` rule at `:636-655`; the aria-pressed true state uses the same accent colour as `.ls-password-toggle` `:547-554`; disabled = opacity 0.55 + cursor default); in the ≤640 block (`:1528`) add `.ls-mode-btn { min-height: 44px; }` (tap-target gate). Existing tokens only. Verify: cd src/wa.web; npm run lint && npm run test:run && npm run build.
14. **Gate** — full AGENT.md §4 gate (§7).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/TransferStatus.cs` | CREATE | Byte constants 0..5 (D-1). |
| `src/wa.domain/Transfer.cs` | MODIFY | Status doc comment adds 5=Scheduled. |
| `src/wa.infrastructure/Persistence/EntityConfigurations/TransferConfiguration.cs` | MODIFY | HasComment on Status + comments (D-2). |
| `src/wa.infrastructure/Persistence/Migrations/(timestamp)_AddTransferStatusScheduled.cs` | CREATE | Generated by dotnet ef migrations add AddTransferStatusScheduled (with its .Designer.cs). |
| `src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs` | MODIFY | Regenerated by the same command. |
| `src/wa.application/UseCases/Transfers/SendTransfer.cs` | MODIFY | Command/result params, gate, status, ScheduleForbiddenException (D-4). |
| `src/wa.api/Program.cs` | MODIFY | Send endpoint parse/catch/response (D-3); billing/plan features (D-6). |
| `src/wa.infrastructure/Persistence/TransferStore.cs` | MODIFY | Quota predicates include Scheduled (D-5). |
| `src/wa.application/UseCases/Billing/GetPlanUsageQuery.cs` | MODIFY | `Scheduling` on PlanUsageResult. |
| `tests/wa.domain.unit/Transfers/TransferStatusTests.cs` | CREATE | Step 1. |
| `tests/wa.application.unit/Transfers/SendTransferHandlerTests.cs` | MODIFY | Stub Features + step 4 facts. |
| `tests/wa.application.unit/Billing/GetPlanUsageHandlerTests.cs` | MODIFY | Step 7 fact. |
| `tests/wa.api.integration/ScheduledSendEndpointTests.cs` | CREATE | Step 8. |
| `src/wa.web/src/features/sender/schedule.ts` | CREATE | toUtcIso, loadSchedulingAllowed. |
| `src/wa.web/src/features/sender/schedule.test.ts` | CREATE | Step 9 tests. |
| `src/wa.web/src/features/sender/ScheduleControl.tsx` | CREATE | Toggle + picker + Pro gate (D-6). |
| `src/wa.web/src/features/sender/ScheduleControl.test.tsx` | CREATE | Step 10 tests. |
| `src/wa.web/src/features/sender/LinkScreen.tsx` | MODIFY | Step 11 wiring. |
| `src/wa.web/src/features/sender/LinkScreen.test.tsx` | MODIFY | Gate-loader mock + schedule describe. |
| `src/wa.web/src/features/billing/planUsage.ts` | MODIFY | `features?` on PlanUsageDto. |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | 8 keys (step 12). |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | Same keys, translated. |
| `src/wa.web/src/index.css` | MODIFY | Step 13 rules. |

Generated, also in the boundary (path known only after generation): the migration's .Designer.cs next to the migration file above.

**`src/wa.application/UseCases/Transfers/SendTransfer.cs`** — change sites: command `:16-23` (append param after `Guid? OwnerAppUserId = null`), result `:26-33` (append param), plan read `:108-109` (`var plan = await _planContext.GetAsync(cancellationToken); var limits = plan.Limits;`), insert gate after the 3c block `:116-123`, step 5 `:144-147` (keep `SendTimeLimits.Stamp` first — it throws unless Status is 0), result construction `:217-224` (`transfer.Status` instead of `1`, plus `transfer.ScheduledSendAtUtc`). `using wa.domain;` already present (`:9`). Callers of the command/result: `src/wa.api/Program.cs:326` and the unit tests — all positional up to OwnerAppUserId, so the trailing defaults keep them compiling. ResendTransfer does not construct these types.

**`src/wa.api/Program.cs`** — send endpoint body parse (`:318-322`):
```csharp
var note = root.TryGetProperty("note", out var n) ? n.GetString() : null;
var password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null;
```
Add after it: `DateTimeOffset? scheduledSendAt = null;` then if the property exists and is not JsonValueKind.Null: require JsonValueKind.String and `sa.TryGetDateTimeOffset(out var parsed)`, else throw the VALIDATION BusinessException; set `scheduledSendAt = parsed`. Response line `:329` and the catch chain `:331-349` as in step 5. `TransferStatus` is in `wa.domain` (check the file's usings; write it fully qualified as wa.domain.TransferStatus if `wa.domain` is not imported). Billing plan anonymous object `:1432-1438`.

**`src/wa.infrastructure/Persistence/TransferStore.cs`** — the two predicates at `:33` and `:50`: `t.OwnerAppUserId == ownerId && (t.Status == 1 || t.Status == 3)`.

**`src/wa.web/src/features/sender/LinkScreen.tsx`** — whole file is 401 lines; touch only `:23-31` (SendResponse), `:76-79` (new state next to send state), `:177-222` (sendTransfer signature/body/response read), `:233-236` (handleSendTransfer), `:245` (sent sub-line), `:380-382` (insert ScheduleControl). Imports: `formatDate, formatTime` from `../../core/i18n/format`, `ScheduleControl`, `toUtcIso` from `./schedule`.

Reference shapes (no edits): `src/wa.web/src/features/profile/OrgNameSection.tsx` (Pro-required line + plan link), `src/wa.web/src/core/features/FeatureGate.tsx` (gate read), `tests/wa.api.integration/OrgLogoUploadTests.cs` (flag flip before first request).

## 4. ACs / ECs in scope → tests

| ID | Test |
|---|---|
| Story AC "link opened now → live" | `ProUser_Scheduled_LinkIsLiveNow` |
| FR-022-4 store + emit now with scheduledSendAt | `Send_Scheduled_ProPlan_StoresUtcAndStatusScheduled`, `Send_Scheduled_EmitsTransferCreatedNow_WithScheduledSendAt`, `ProUser_Schedule_201_StatusScheduled_RowAndOutbox` |
| FR-022-7 ExpiresAt from creation | `Send_Scheduled_ExpiresAtFromCreation_NotFromDue` |
| FR-022-3 server gate (Free, guest, gate off) | `Send_Scheduled_PlanWithoutScheduling_ThrowsScheduleForbidden_NoWrite`, `FreeUser_Schedule_403Forbidden_DraftUntouched`, `Guest_Schedule_403Forbidden`, `GateOff_ProUser_Schedule_403Forbidden` |
| AC-022-4 Free sees disabled control "Pro plan required" | `ScheduleControl.test.tsx` plan-false case |
| FR-022-1 toggle + picker + exact copy, body carries UTC | `ScheduleControl.test.tsx` plan-true case; LinkScreen "schedule" describe |
| Confirmation copy (story UI note) | LinkScreen "schedule" describe, sent-line case |
| Quota consistency for the new status | `ProUser_Scheduled_CountsTowardActiveTransfers` |
| Story AC "no emails before due / within one timer period", AC-022-1 email half, AC-022-5/6, EC-022-1..4 | **US-022-04** (R-1) |
| AC-022-2/3 window | **US-022-03** |
| EC-022-5 (no edit; re-send makes a new transfer with its own schedule) | holds by construction: the re-send flow reuses LinkScreen, so ScheduleControl is there; no extra code |
| EC-022-6 stored UTC | `toUtcIso` tests + handler stores the UTC value unchanged |

## 5. Open Questions / Risks

- **R-1 (conflict, flagged not resolved): emails before due.** f-email does not check status, so with this story alone a scheduled transfer's emails go out on the immediate transfer.created. The story's "no emails before the due time" is therefore NOT met until US-022-04 adds the f-email skip (Status Scheduled → skip, timer re-emits at due). Containment: the feature.scheduling global gate is seeded "false" (`FeatureFlagConfiguration.cs:33`), so the server returns 403 to everyone until an admin flips it. **Rule for the orchestrator: do not flip feature.scheduling on in any shared environment before US-022-04 is done.** No DECISION raised: the containment is a seeded default, nothing ships exposed.
- **R-2 seams for later stories.** US-022-03: window check at the marked spot in the handler + client min/max on the picker; it adds SCHEDULE_OUT_OF_WINDOW to the closed list (FR-022-2 names it; not in `src/wa.domain/Errors/ProblemCodes.cs` today). The helper copy hard-codes "14 days" as spec copy; US-022-03 may interpolate the configured window. US-022-04: due query `Status == TransferStatus.Scheduled && ScheduledSendAtUtc <= now` on IX_Transfer_Sched; expiry sweep (`ExpiryStore.cs:38`, `:49` use Status 1 only) must include Scheduled for AC-022-5; the download-cap flip (`PublicTransferStore.cs:60-61`, `WHERE Status = 1`) skips Scheduled rows — the cap itself is still enforced by the guarded increment (`:52-54`). US-022-02: recipient pre-due line from status + scheduledSendAt on endpoint 4.
- **R-3 status 5 in surfaces not touched here.** My Files chip shows the raw number for 5 (`src/wa.web/src/features/files/formatting.ts:37-44`, defensive default) and the "active" filter is {1}; admin overview/user counts ignore 5. All unreachable while the gate is off; suggest a "Scheduled" chip in US-022-02 or a follow-up.
- **R-4 migration content.** If the reviewer prefers no schema-side artifact, D-2 can drop HasComment and the migration — but the spec explicitly says "migration required". EF must report no pending model changes after generation.
- **verify_plan.py**: path argument used; per-step verify commands spell the test filter in words because the script reads double-dash tokens as CSS tokens.

## 6. Docs close-out (orchestrator, not Coder)

TA-17: add ADR-019 "Transfer.Status adds 5 = Scheduled (F-PRF-002); byte constants in wa.domain.TransferStatus; migration AddTransferStatusScheduled documents the mapping" (alternatives: C# enum refactor, CHECK constraint). TA-3.2 Status comment → "…,4 Deleted,5 Scheduled". REPO-FACTS: endpoint 3 body/response (`scheduledSendAt`, status "Scheduled"), GET /billing/plan features.scheduling. Story file status.

## 7. Test gate (AGENT.md §4)

```powershell
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
dotnet test tests/wa.api.integration     # Docker running; per-task only the ScheduledSendEndpointTests class, full suite at feature end
cd src/wa.web
npm run lint && npm run test:run && npm run build
node ../../scripts/check-theme-tokens.mjs
node ../../scripts/check-contrast.mjs
node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest
```
