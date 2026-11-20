# US-022-02 — Know exactly what a scheduled send does (schedule semantics copy)

**Feature:** F-PRF-002 Scheduled Send (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-002/US-022-02-schedule-semantics.md`
**Scope:** FR-022-1 copy ("The link works early; emails go out on schedule."), FR-022-6 (recipient pre-due line, downloads allowed), AC-022-1 recipient-facing half ("the link is live now" is visible as a state), EC-022-6 display, story ACs (confirmation time; pre-due line + download available).
**Task id note:** story id = task id (US-022-01 convention).
**Out of scope:** window validation (US-022-03); timer flip Scheduled→Active, f-email skip, expiry of Scheduled rows (US-022-04). The feature.scheduling global gate stays seeded "false" — nothing here touches `FeatureFlagConfiguration.cs`.

## 1. Goal

The link screen states the schedule semantics in one sentence (schedule mode only), and the recipient page of a Status-5 transfer shows "Files are ready — email goes out at {when}" (browser-locale time from the UTC value) above a fully downloadable file list. **Exit check:** new handler/integration/vitest tests green and the AGENT.md §4 gate green.

## 2. Design & Decisions

### Grounded facts
- US-022-01 shipped (502467a): ScheduleControl renders the picker + helper "Between now and 14 days from now." only when `allowed && mode === 'schedule'` (`src/wa.web/src/features/sender/ScheduleControl.tsx:80-91`). The semantics sentence is **not** rendered anywhere and no locale has a key for it — nothing to dedupe.
- Confirmation screen (story AC 1): `link.ready.scheduledFor` = "Scheduled for {{date}}, {{time}} — the link is live, emails go out then." rendered at `src/wa.web/src/features/sender/LinkScreen.tsx:267-269` from the server echo, pinned by `src/wa.web/src/features/sender/LinkScreen.test.tsx:721-731`. Matches the F-PRF-002 UI note verbatim → **verified, not re-planned.**
- Endpoint 4 (TA-4.2#4) wire shape is built at one site, `src/wa.api/Program.cs:413-419`; it has no `scheduledSendAt` today. The handler already has the full `Transfer` row from `IPublicTransferStore.GetByLinkIdAsync` (`src/wa.application/UseCases/Transfers/IPublicTransferStore.cs:12`), so `ScheduledSendAtUtc` (`src/wa.domain/Transfer.cs:20`) is available with no store/port change. Status 5 already falls through to the Active branch (`src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs:86-127`) → file list + downloads work today (US-022-01 `ProUser_Scheduled_LinkIsLiveNow`).
- EF reads datetime2 back as Kind=Unspecified → System.Text.Json writes no trailing Z and the browser would parse local time. Precedent fix: `AsUtc` at `src/wa.api/Program.cs:1349` (used at `:744`, `:1353`).
- Stale fact: `tests/wa.api.integration/PublicTransferEndpointTests.cs:78-79` pins the exact endpoint-4 key list → must gain `scheduledSendAt`.
- The application layer has no clock abstraction (no TimeProvider/IClock anywhere in the wa.application project).
- Recipient muted small copy precedent: `.rp-cap-note` (`src/wa.web/src/features/recipient/recipient.css:134-139`, fg-muted + fs-small tokens; fg-muted is defined in `src/wa.web/src/styles/tokens.css:11`, fs-small in `src/wa.web/src/index.css:12`). Locale formatting: `formatDate(iso, opts)` merges opts into the Intl call with i18n.language (`src/wa.web/src/core/i18n/format.ts:13-18`). Pure-helper precedent: `src/wa.web/src/features/recipient/senderLine.ts` + its test.

### D-1 — Endpoint 4 gains `scheduledSendAt` (TA-4.2#4, onion rule)
`GetPublicTransferResult` gets trailing `DateTime? ScheduledSendAtUtc = null` (Active only). Handler fills it **only when `transfer.Status == wa.domain.TransferStatus.Scheduled`** (else null — a stale value on an Active row never leaks a "goes out at" line). The endpoint appends `scheduledSendAt = result.ScheduledSendAtUtc is { } due ? AsUtc(due) : (DateTime?)null` as the **last** property (JSON null when not scheduled, same style as `orgLogoUrl`). Terminal states unchanged (no byte leak). No new ProblemCode, no telemetry change (TA-10.2 untouched).
Rejected: a server-side "before due" comparison — needs a clock port in the handler for no gain; after due the row stays Scheduled for at most one timer period (US-022-04 flips it), and the client hides a past due time (D-3).

### D-2 — Link-screen semantics sentence (FR-022-1)
New key link.schedule.semantics = "The link works early; emails go out on schedule." rendered as a second `<p className="ls-recipients-helper">` directly after the window helper inside the existing `allowed && mode === 'schedule'` block (`ScheduleControl.tsx:91`). "Send now" mode, Free/guest (not allowed) and gate-off render none of it by construction. No new CSS.

### D-3 — Recipient pre-due line (FR-022-6, EC-022-6)
- Store: `scheduledSendAt: string | null` in RecipientState + LOADING_SHAPE (null), read as `body.scheduledSendAt ?? null` (optional in the body type so old servers/tests stay valid).
- CREATE `src/wa.web/src/features/recipient/scheduledLine.ts` (senderLine precedent, pure):
  - `isPreDue(iso: string | null, nowMs: number): boolean` — true only when iso parses and is strictly after nowMs.
  - `formatScheduledWhen(iso: string): string` — `formatDate(iso, { hour: '2-digit', minute: '2-digit', timeZoneName: 'short' })` → one locale-formatted date+time with the viewer's zone label (browser locale + browser time zone; no server localization).
- RecipientPage: on the **active, ungated** screen only, when `isPreDue(scheduledSendAt, Date.now())`, render `<p className="rp-scheduled-note">{i18n.t('recipient.active.scheduledNote', { when: formatScheduledWhen(scheduledSendAt) })}</p>` between the note blockquote (`RecipientPage.tsx:273-275`) and the file list (`:276`). Download buttons untouched → "download is available". The password gate shows nothing extra (FR-003-6: gate card only).
- New key `recipient.active.scheduledNote` = "Files are ready — email goes out at {{when}}" (exact spec copy, no trailing punctuation, no exclamation point — UI-Reference §7).
- CSS: `.rp-scheduled-note` = copy of `.rp-cap-note` (margin 0, color var(fg-muted), font-size var(fs-small), centered — the real declarations use the double-dash custom properties exactly as `.rp-cap-note` does). Existing tokens only → theme/contrast scripts unaffected but still run.

### D-4 — i18n (F-TRF-014 pattern)
Two keys, all 8 locales (key-set parity enforced by `src/wa.web/src/core/i18n/init.test.ts:52-55`). Insert link.schedule.semantics after link.schedule.helper (line 33 in every locale) and `recipient.active.scheduledNote` after `recipient.active.capNote` (line 96). Values:

| Locale | link.schedule.semantics | recipient.active.scheduledNote |
|---|---|---|
| en | The link works early; emails go out on schedule. | Files are ready — email goes out at {{when}} |
| de | Der Link funktioniert schon vorher; die E-Mails gehen zum geplanten Zeitpunkt raus. | Dateien sind bereit — die E-Mail geht am {{when}} raus |
| es | El enlace funciona antes; los correos se envían a la hora programada. | Los archivos están listos — el correo se envía el {{when}} |
| fr | Le lien fonctionne dès maintenant ; les e-mails partent à l'heure prévue. | Les fichiers sont prêts — l'e-mail part le {{when}} |
| it | Il link funziona già da ora; le email partono all'orario programmato. | I file sono pronti — l'email parte il {{when}} |
| nl | De link werkt al eerder; e-mails gaan op het geplande moment uit. | Bestanden staan klaar — e-mail gaat uit op {{when}} |
| pt | O link funciona antes; os e-mails são enviados no horário agendado. | Os arquivos estão prontos — o e-mail sai em {{when}} |
| tr | Bağlantı hemen çalışır; e-postalar planlanan zamanda gönderilir. | Dosyalar hazır — e-posta {{when}} tarihinde gönderilir |

## 3. Implementation Plan

1. **Handler** — MODIFY `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs`: append `DateTime? ScheduledSendAtUtc = null` to the result record (`:25-35`, comment "Active only — set only for Status Scheduled (F-PRF-002)"); in the Active construction (`:117-127`) add `ScheduledSendAtUtc: transfer.Status == wa.domain.TransferStatus.Scheduled ? transfer.ScheduledSendAtUtc : null`.
2. **Handler tests** — MODIFY `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs`: `SetupStatus` (`:520-539`) gains trailing `DateTime? scheduledSendAtUtc = null` → sets `ScheduledSendAtUtc`. Facts:
   - `Handle_Scheduled_ReturnsActiveWithFilesAndScheduledSendAt` — status 5, due now+2h, one file → State Active, Files present, ScheduledSendAtUtc == due (FR-022-6: downloads allowed; AC-022-1 link live).
   - `Handle_ActiveStatus_ScheduledSendAtNull_EvenWhenColumnSet` — status 1 with a due value → null (D-1).
   - `Handle_TerminalStatus_NoScheduledSendAt` — status 2 → Expired, ScheduledSendAtUtc null (no byte leak).
   Verify: dotnet test tests/wa.application.unit with test filter FullyQualifiedName~GetPublicTransferHandlerTests.
3. **Endpoint** — MODIFY `src/wa.api/Program.cs:413-419`: append `scheduledSendAt = result.ScheduledSendAtUtc is { } due ? AsUtc(due) : (DateTime?)null` after `orgLogoUrl = result.OrgLogoUrl`.
4. **Integration** — MODIFY `tests/wa.api.integration/PublicTransferEndpointTests.cs:78`: key list ends `..., "orgName", "orgLogoUrl", "scheduledSendAt"`; after `:101` assert `scheduledSendAt` ValueKind Null (status-1 seed). MODIFY `tests/wa.api.integration/ScheduledSendEndpointTests.cs` (gate-on class, after `ProUser_Scheduled_LinkIsLiveNow` `:205-218`):
   - `ProUser_Scheduled_PublicPage_CarriesUtcScheduledSendAt` — schedule now+2h; GET endpoint 4 → 200, files length 1, `scheduledSendAt` string; `GetDateTimeOffset()` has Offset zero and equals due ±1 s (EC-022-6: UTC on the wire).
   - `ProUser_NoSchedule_PublicPage_ScheduledSendAtNull` — `{"emails":[]}` send → `scheduledSendAt` JSON null.
   Verify (Docker running): dotnet test tests/wa.api.integration with test filter FullyQualifiedName~ScheduledSendEndpointTests, then FullyQualifiedName~PublicTransferEndpointTests.
5. **Link-screen copy** — MODIFY `src/wa.web/src/features/sender/ScheduleControl.tsx:91` (D-2). MODIFY `src/wa.web/src/features/sender/ScheduleControl.test.tsx`: in the plan-true case (`:80-97`) assert, before clicking Schedule, text does NOT contain "The link works early; emails go out on schedule." and after the click it does; in the plan-false case (`:54-68`) assert it is absent; new case: Schedule → Send now hides it again (story alt flow "No schedule").
6. **Helpers** — CREATE `src/wa.web/src/features/recipient/scheduledLine.ts` (D-3) and `src/wa.web/src/features/recipient/scheduledLine.test.ts`: isPreDue future → true, past → false, equal → false, null → false, garbage → false; formatScheduledWhen equals `new Intl.DateTimeFormat(i18n.language, { month: '2-digit', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZoneName: 'short' }).format(new Date(iso))` and is non-empty (client formatting, EC-022-6).
7. **Store** — MODIFY `src/wa.web/src/features/recipient/RecipientStore.ts`: state field after `orgLogoUrl` (`:147`); LOADING_SHAPE `scheduledSendAt: null as string | null` (after `:210`); body type `scheduledSendAt?: string | null` (`:281`); set `scheduledSendAt: body.scheduledSendAt ?? null` (`:292`). Terminal/reset paths reuse LOADING_SHAPE → cleared by construction.
8. **Page** — MODIFY `src/wa.web/src/features/recipient/RecipientPage.tsx`: selector next to `:48-49`; import `{ formatScheduledWhen, isPreDue } from './scheduledLine'`; render per D-3 between `:275` and `:276`. MODIFY `src/wa.web/src/features/recipient/recipient.css`: `.rp-scheduled-note` after `.rp-cap-note` (`:134-139`).
9. **Page tests** — MODIFY `src/wa.web/src/features/recipient/RecipientPage.test.tsx`: add `scheduledSendAt?: string | null` to `PublicTransferBody` (`:17-26`); new describe "F-PRF-002 US-022-02 — pre-due line" (same before/afterEach as `:1365-1378`):
   - future due (now+2h ISO) → `.rp-scheduled-note` textContent === "Files are ready — email goes out at " + formatScheduledWhen(iso); contains no "!"; every `.rp-download` button enabled and Download all present (story AC 2 / FR-022-6).
   - past due → no `.rp-scheduled-note`, file list still rendered.
   - scheduledSendAt null / absent → no line (regression).
   - gated (passwordRequired true) + future due → no line (gate card only).
   Verify: cd src/wa.web; npm run lint && npm run test:run && npm run build.
10. **i18n** — MODIFY the 8 locale files per D-4.
11. **Gate** — full AGENT.md §4 (section 6 below).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs` | MODIFY | Result field + Scheduled-only fill (D-1). |
| `src/wa.api/Program.cs` | MODIFY | Endpoint 4 appends `scheduledSendAt` via AsUtc (D-1). |
| `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs` | MODIFY | SetupStatus param + 3 facts (step 2). |
| `tests/wa.api.integration/PublicTransferEndpointTests.cs` | MODIFY | Stale key-list pin + null assert (step 4). |
| `tests/wa.api.integration/ScheduledSendEndpointTests.cs` | MODIFY | 2 facts (step 4). |
| `src/wa.web/src/features/sender/ScheduleControl.tsx` | MODIFY | Semantics sentence in schedule mode (D-2). |
| `src/wa.web/src/features/sender/ScheduleControl.test.tsx` | MODIFY | Shown/hidden assertions (step 5). |
| `src/wa.web/src/features/recipient/scheduledLine.ts` | CREATE | isPreDue, formatScheduledWhen (D-3). |
| `src/wa.web/src/features/recipient/scheduledLine.test.ts` | CREATE | Step 6. |
| `src/wa.web/src/features/recipient/RecipientStore.ts` | MODIFY | `scheduledSendAt` state/read (step 7). |
| `src/wa.web/src/features/recipient/RecipientPage.tsx` | MODIFY | Pre-due line (step 8). |
| `src/wa.web/src/features/recipient/recipient.css` | MODIFY | `.rp-scheduled-note`. |
| `src/wa.web/src/features/recipient/RecipientPage.test.tsx` | MODIFY | Body type + describe (step 9). |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | 2 keys (D-4). |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | 2 keys. |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | 2 keys. |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | 2 keys. |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | 2 keys. |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | 2 keys. |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | 2 keys. |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | 2 keys. |

**`GetPublicTransferQuery.cs`** — result record `:25-35` (last param today `string? OrgLogoUrl = null);`) and the Active return `:117-127` (last arg `OrgLogoUrl: logoUrl);`). The file imports `wa.domain.Telemetry` / `wa.domain.ValueObjects` but not `wa.domain` → write `wa.domain.TransferStatus.Scheduled` fully qualified. Only constructor of the result outside tests: this handler; the endpoint is the only reader.

**`Program.cs`** — current endpoint-4 Active body (`:413-419`):
```csharp
PublicTransferState.Active => Results.Json(new
{
    from = result.From, note = result.Note,
    files = result.Files!.Select(f => new { fileId = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes }),
    hasDownloadAll = result.HasDownloadAll, downloadsLeft = result.DownloadsLeft, passwordRequired = result.PasswordRequired,
    orgName = result.OrgName, orgLogoUrl = result.OrgLogoUrl
}),
```
`AsUtc` is a top-level local function (`:1349`), already called earlier in the file (`:744`) — callable here.

**`ScheduleControl.tsx`** — `:91` is the window-helper paragraph (class ls-recipients-helper, key link.schedule.helper) → add the semantics paragraph (same class, key link.schedule.semantics) right after it, inside the same fragment.

**`RecipientStore.ts`** — body cast `:273-282` and `set({...})` `:283-295` (add after `orgLogoUrl: body.orgLogoUrl ?? null,`); LOADING_SHAPE `:201-222`.

**`RecipientPage.tsx`** — selectors `:46-65`; active section `:264-276`:
```tsx
{note && note.trim() !== '' && (
  <blockquote className="rp-note">{note}</blockquote>
)}
<ul className="rp-list" aria-label={i18n.t('recipient.active.filesAria')}>
```
Insert the line between these two. Gate branch `:208-262` unchanged.

**Tests** — `GetPublicTransferHandlerTests.SetupStatus` signature at `:520`; existing callers pass named args, so a trailing optional param compiles unchanged. `ScheduledSendEndpointTests` helpers to reuse: `SeedUserAsync`, `SetupDraftTransferAsync`, `PostSendAsync`, `ScheduleBody`, `RowAsync`, `GetAsync`, `ReadJsonAsync` (see `:205-218`). `RecipientPage.test.tsx` helpers: `stubFetch` `:33`, `renderApp` `:58`, `isActive` `:75`, `activeBody` `:80`.

## 4. ACs / ECs → tests

| ID | Test |
|---|---|
| Story AC 1 (confirmation states time + emails go out then) | existing `LinkScreen.test.tsx:721` (US-022-01) — verified, unchanged |
| FR-022-1 semantics copy, hidden on Send now / no schedule | `ScheduleControl.test.tsx` step 5 cases |
| Story AC 2 / FR-022-6 pre-due line + download available | `RecipientPage.test.tsx` future-due case; `Handle_Scheduled_ReturnsActiveWithFilesAndScheduledSendAt`; `ProUser_Scheduled_PublicPage_CarriesUtcScheduledSendAt` |
| AC-022-1 recipient-facing part (link live now, visible as a state) | same three as above |
| EC-022-6 display (UTC on wire, client locale formatting) | `ProUser_Scheduled_PublicPage_CarriesUtcScheduledSendAt` (offset zero); `scheduledLine.test.ts` formatter case |
| EC i18n one key, no exclamation points | locale parity `init.test.ts`; RecipientPage "no !" assertion |
| No leak / regression | `Handle_ActiveStatus_…`, `Handle_TerminalStatus_…`, `ProUser_NoSchedule_PublicPage_ScheduledSendAtNull`, PublicTransferEndpointTests key list, RecipientPage null/past/gated cases |
| AC-022-1 email half, AC-022-2..6, EC-022-1..5 | US-022-03 / US-022-04 |

## 5. Open Questions / Risks

- **R-1 EC-022-6 wording.** The feature spec says the UI shows "the UTC offset of the sender's pick … no per-recipient localizing"; the story edge case (and this task's brief) says render in the recipient's browser locale from UTC. The sender's offset is not stored (only `ScheduledSendAtUtc`), so the spec reading would need a schema change (§7 escalation). This plan follows the story: browser locale + browser zone, with `timeZoneName: 'short'` so the zone is explicit. Orchestrator: align the EC-022-6 text at close-out. No DECISION raised (no §7 trigger in the chosen path).
- **R-2 between due and flip.** Until US-022-04 flips Status, a past-due Scheduled row still returns `scheduledSendAt`; the client hides the line (isPreDue). Before US-022-04, emails actually leave immediately (US-022-01 R-1) — contained by feature.scheduling seeded "false"; do not flip it in shared environments.
- **R-3 jsdom time zone.** The formatter test computes its expectation with the same Intl options, so it is TZ-independent.
- **R-4 window copy** ("14 days") stays hard-coded spec copy; US-022-03 owns it.
- verify_plan: test filters spelled in words (double-dash tokens are read as CSS tokens).

## 6. Test gate (AGENT.md §4) + docs close-out

```powershell
dotnet test tests/wa.domain.unit
dotnet test tests/wa.application.unit
dotnet test tests/wa.workers.unit
dotnet test tests/wa.api.integration     # per task: ScheduledSendEndpointTests + PublicTransferEndpointTests classes only; full suite at feature end
cd src/wa.web
npm run lint && npm run test:run && npm run build
node ../../scripts/check-theme-tokens.mjs
node ../../scripts/check-contrast.mjs
node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest
```
Close-out (orchestrator): TA-4.2 row 4 note "+ `scheduledSendAt` (F-PRF-002, null unless Scheduled)"; REPO-FACTS endpoint-4 shape; story status.
