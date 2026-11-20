# F-PRF-001-US-021-03 — Keep my own name in front

**Feature:** F-PRF-001 Custom "From" Branding (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-001/US-021-03-personal-name-wins.md`
**Scope:** FR-021-2, AC-021-2, story alt flows (guest / org not set) and edge cases (blank person + org → org only; both blank → "Anonymous"). Builds on US-021-01 (8048856, page header) and US-021-02 (50635df, email line via SenderBranding.ComposeEmailSenderLine).
**Out of scope:** logo (US-021-04), profile org field (US-021-04 / profile story), telemetry names, server-side normalisation of blank senderName input.

## 1. Goal

The recipient header is composed by one unit-tested formatter that renders "From: {personName} · {orgName}" with empty-part handling (AC-021-2: "Anna" + "Studio Nova" → "From: Anna · Studio Nova"), and the existing per-transfer-name-wins behaviour on the wire and in the email subject is pinned by tests. **Exit check:** new vitest + unit + integration facts green; AGENT.md §4 gate green (integration filtered per pitfalls).

## 2. Design & Decisions

### Gap analysis (current behaviour, verified in source)
| Requirement | Today | Verdict |
|---|---|---|
| Per-transfer name wins over profile display name | `SendTransfer.cs:158` — `request.SenderName ?? ResolveSenderName(...)`; snapshot stored in Transfer.SenderName, returned as `from` (`GetPublicTransferQuery.cs:101`) | Works — pin with one integration fact |
| Page "From: Anna · Studio Nova" | `RecipientPage.tsx:214` (gate) and `:269` (active): fromHeading + from + (orgName !== null && " · " + orgName) | Works for the happy path — covered by AC-021-2 vitest case |
| Guest / no org → "From: Anna" | orgName null → no suffix (test `RecipientPage.test.tsx:1390`) | Works |
| Person blank + org → "From: Studio Nova", no dangling "·" | from "" renders "From:  · Studio Nova" | **GAP** |
| Both blank → "Anonymous" | from "" renders "From: " | **GAP** (server defaults only null senderName to "Anonymous", `SendTransfer.cs:252`; the API passes "" / "  " through, `Program.cs:312`) |
| "One unit-tested formatter" (story tech note) | inline JSX expression, duplicated in two headings | **GAP** |
| Email subject "Transfer from Studio Nova — Anna", per-transfer name wins | `SenderBranding.ComposeEmailSenderLine` (`SenderBranding.cs:27-34`) composes org — Transfer.SenderName; blank name → org only | Works — pin with one handler fact using "Anna" |

### D-1 — Pure web formatter `formatSenderLine` (TA-8.2 feature-local module; codebase-design: one rule, one place)
CREATE `src/wa.web/src/features/recipient/senderLine.ts`:
```ts
/** Server default for an unnamed sender (F-TRF-002, SendTransfer.cs ResolveSenderName) — mirrored for blank wire values. */
export const ANONYMOUS_SENDER = 'Anonymous'

/** F-PRF-001 FR-021-2 / US-021-03: person first, org second; blank parts dropped, never a dangling separator. */
export function formatSenderLine(personName: string, orgName: string | null): string {
  const person = personName.trim()
  const org = orgName?.trim() ?? ''
  if (person !== '' && org !== '') return `${person} · ${org}`
  return person || org || ANONYMOUS_SENDER
}
```
Both headings render the fromHeading i18n prefix followed by the formatter result. The string lives in a .ts module, so the JSXText lint rule (`src/wa.web/eslint.config.js:28`) is not touched. "Anonymous" is a data value the server already stores untranslated for every guest send, so mirroring it (instead of a new i18n key in 8 locales) keeps the page consistent with what non-blank guest transfers already show. Logo slot and aria-labels (which use the raw `from`) are unchanged.

### D-2 — No server code change (TA-4.2#4 wire shape unchanged, TA-6.7 email pipeline unchanged)
Precedence is already decided at send time (Transfer.SenderName snapshot) and the email composer already orders org — person and handles a blank person. US-021-03 only adds tests on the server side. No schema, migration, telemetry event, package or gate change; no allow-list edit in PlanFeaturesConsumerGuardTests.cs (the web reads no plan features; the server gate rule is untouched).

## 3. Implementation Plan

1. **Formatter** — CREATE `src/wa.web/src/features/recipient/senderLine.ts` exactly per D-1.
2. **Formatter tests** — CREATE `src/wa.web/src/features/recipient/senderLine.test.ts` (vitest, node env):
   - "Anna" + "Studio Nova" → "Anna · Studio Nova" (AC-021-2)
   - "Anna" + null → "Anna" (alt flow: org not set)
   - "" + "Studio Nova" and "   " + "Studio Nova" → "Studio Nova" (edge: no dangling "·"; alt flow: no per-transfer name)
   - "" + null and "" + "  " → "Anonymous" (edge: both empty)
   - "  Anna " + " Studio Nova " → "Anna · Studio Nova" (trim)
3. **Page** — MODIFY `src/wa.web/src/features/recipient/RecipientPage.tsx`: import formatSenderLine from './senderLine'; at `:214` and `:269` replace `{from}{orgName !== null && ` · ${orgName}`}` with `{formatSenderLine(from, orgName)}`. Nothing else in the file changes.
4. **Page tests** — MODIFY `src/wa.web/src/features/recipient/RecipientPage.test.tsx`, append to the US-021-01 branding describe block (`:1380-1419`, same activeBody / renderApp / isActive / isGatedActive helpers):
   - "shows person first, org second (AC-021-2)": from "Anna", orgName "Studio Nova" → heading textContent "From: Anna · Studio Nova".
   - "drops the separator when the person name is blank": from "", orgName "Studio Nova" → "From: Studio Nova", not containing "·".
   - "falls back to Anonymous when both parts are blank": from "", orgName null → "From: Anonymous".
   - "gate heading uses the same composition": passwordRequired true, from "", orgName "Studio Nova" → "From: Studio Nova".
   Existing cases at `:1385`, `:1394`, `:1416` and every "From: Studio Nova" assertion stay green unchanged.
   Verify: `cd src/wa.web; npm run lint; npm run test:run; npm run build`.
5. **Wire pin (AC-021-2 server half)** — MODIFY `tests/wa.api.integration/PublicTransferBrandingTests.cs`: SeedActiveTransferAsync gains `string senderName = "Alice"` (assign to SenderName; existing callers unchanged). New fact `PerTransferNameWinsOverDisplayName_ReturnsPersonAndOrg` — owner seeded via SeedOwnerAsync("pro", "Studio Nova") (DisplayName "Alice"), transfer senderName "Anna" → `from` "Anna", `orgName` "Studio Nova". Verify: dotnet test tests/wa.api.integration with filter FullyQualifiedName~PublicTransferBranding (Docker running).
6. **Email pin (FR-021-2 email half)** — MODIFY `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs`: ArrangeBrandingTransfer (`:600-611`) gains `string senderName = "Anna K."` passed as the context's SenderName. New fact `Handle_PerTransferNameWithOrg_SubjectOrgThenPerson` — ArrangeBrandingTransfer(Guid.NewGuid(), "Anna"), row ("pro", "Studio Nova", null) → Subject "Transfer from Studio Nova — Anna". Verify: dotnet test tests/wa.application.unit with filter FullyQualifiedName~SendNotificationEmails.
7. **Gate** — AGENT.md §4 (integration filtered as step 5; full run at feature end per pitfalls). Theme/contrast scripts unaffected (no CSS change).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.web/src/features/recipient/senderLine.ts` | CREATE | formatSenderLine + ANONYMOUS_SENDER (D-1). |
| `src/wa.web/src/features/recipient/senderLine.test.ts` | CREATE | 5 formatter cases (step 2). |
| `src/wa.web/src/features/recipient/RecipientPage.tsx` | MODIFY | Import formatter; use it in both headings (`:214`, `:269`). |
| `src/wa.web/src/features/recipient/RecipientPage.test.tsx` | MODIFY | 4 header cases (step 4). |
| `tests/wa.api.integration/PublicTransferBrandingTests.cs` | MODIFY | senderName param on seed helper + AC-021-2 wire fact. |
| `tests/wa.application.unit/Transfers/SendNotificationEmailsHandlerTests.cs` | MODIFY | senderName param on ArrangeBrandingTransfer + subject fact. |

**`RecipientPage.tsx`** — gate heading `:212-215`:
```tsx
        <h1 className="rp-heading">
          {orgLogoUrl !== null && <img className="rp-org-logo" src={orgLogoUrl} alt="" />}
          {i18n.t('recipient.gate.fromHeading')}{from}{orgName !== null && ` · ${orgName}`}
        </h1>
```
active heading `:267-270`:
```tsx
      <h1 className="rp-heading">
        {orgLogoUrl !== null && <img className="rp-org-logo" src={orgLogoUrl} alt="" />}
        {i18n.t('recipient.active.fromHeading')}{from}{orgName !== null && ` · ${orgName}`}
      </h1>
```
Selectors `from` / `orgName` are at `:46-47` (store types: from string, orgName string or null — `RecipientStore.ts:137`). Keep the `from` selector: the two aria-labels (`:211`, `:264`) still use it.

**`RecipientPage.test.tsx`** — body type `:16-25` already has optional orgName / orgLogoUrl; existing branding cases `:1380-1419` are the template (stubFetch(200, activeBody({...})), renderApp(isActive | isGatedActive), host.remove()).

**`PublicTransferBrandingTests.cs`** — seed helper `:147-164`:
```csharp
    private async Task SeedActiveTransferAsync(Guid ownerId, string linkId)
    {
        _db.Set<Transfer>().Add(new Transfer
        {
            ...
            SenderName = "Alice",
            OwnerAppUserId = ownerId,
            ...
        });
        await _db.SaveChangesAsync();
    }
```
Use a fresh 8-char linkId (e.g. "e1b2c3d4"; existing facts use b1/c1/d1). GetAsync helper `:121-127`.

**`SendNotificationEmailsHandlerTests.cs`** — `:600-611`:
```csharp
    private EventEnvelope ArrangeBrandingTransfer(Guid? ownerId)
    {
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId, "Anna K.", null, DateTime.UtcNow.AddDays(30), "ABCD1234", [new FileEntry("file.txt", 1024)], ownerId);
        ...
        return CreateEnvelope(transferId, ["a@x.com"]);
    }
```
Its four callers (`:616-670`) pass only ownerId and keep "Anna K." via the default. Model the new fact on Handle_ProOwnerWithOrg_SubjectAndBodyShowOrg (`:616-628`; `_branding.Row`, `_handler.HandleAsync(envelope, 1, 5, CancellationToken.None)`, `_emailSender.Sent`).

## 4. Open Questions / Risks

- **"Anonymous" is English-only** on the page for blank wire values — same as the stored server default for every guest send today; localising it would be a separate F-TRF-002/i18n change (not this story).
- **Email with both parts blank** still renders "Transfer from " (ComposeEmailSenderLine returns the blank name). Reachable only by an API client sending a whitespace senderName for a non-branded send; the story's edge cases target the header. Not changed here; candidate for a server-side senderName normalisation follow-up.
- No DECISION required: no schema, package, limit, env var, telemetry or guard allow-list change.
