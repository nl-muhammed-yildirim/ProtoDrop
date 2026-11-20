# US-023-03 — Trust that my recipients' data is handled (privacy note + PII proof)

**Feature:** F-PRF-003 Download Analytics (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-003/US-023-03-privacy-note.md`
**Scope:** FR-023-4 (PII: IpHash HMAC / UaHash / Country CHAR(2) / browser family only; privacy note text), AC-023-2 (no raw IP/UA in the API response + privacy note present), story AC, story edge cases (note says "we keep no raw IP"; analytics_viewed PII-safe — not emitted, see US-023-01 DECISION-2).
**Baseline:** US-023-01 (eca99bc) + US-023-02 (4182262). Same endpoint 24, same panel. **No schema change, no new package, no new config / env var, no backend production code change.**
**Out of scope:** GeoIP country source (US-023-01 DECISION-1, still open), HMAC key rotation (story edge case: "later decision"), US-023-04 lifetime/deletion.

## 1. Goal

The Downloads panel shows the one-line privacy note under the country stat in all 8 locales, and tests prove that both download write paths persist only hashes + browser family (never the raw IP/UA) and that endpoint 24 returns only aggregates. **Exit check:** the new vitest + integration facts pass and the AGENT.md §4 gate is green.

## 2. Design & Decisions

### Verified current state (source is the truth)
- **Write path, single file:** endpoint passes `RemoteIpAddress` + `User-Agent` into the query (`src/wa.api/Program.cs:498-499`); the handler hashes before the store call: `IpHash` = `_telemetry.IpSlot(ip)` = HMAC-SHA256 hex (`src/wa.application/UseCases/Transfers/GetFileDownloadUrlQuery.cs:93-94,110-115`, `src/wa.domain/Telemetry/PiiTransforms.cs:17-21`), `UaHash` = SHA-256 hex, `UserAgentFamily` = `BrowserFamily.FromUserAgent(ua)`. Store writes only those (`src/wa.infrastructure/Persistence/PublicTransferStore.cs:72-81`).
- **Write path, download-all:** same transforms (`src/wa.api/Program.cs:522-523`, `src/wa.application/UseCases/Transfers/GetDownloadAllUrlQuery.cs:109-110,161-167`); store `RecordZipDownloadAsync` writes IpHash/UaHash/UserAgentFamily only.
- **Schema:** `DownloadEvent` has no raw IP/UA column (`src/wa.domain/DownloadEvent.cs:4-18`); `IpHash`/`UaHash`/`UserAgentFamily` VARCHAR(64), `Country` CHAR(2) (`src/wa.infrastructure/Persistence/EntityConfigurations/DownloadEventConfiguration.cs:21-24`). `Country` is never written (US-023-01 DECISION-1).
- **HMAC key:** `Wa:Jwt:Secret`, which TA-9.3 already maps to Key Vault `wa-jwt-secret` (`docs/03-technical-architecture.md:783`) — FR-023-4 "key in Key Vault" holds with no new secret / env var.
- **Read path:** endpoint 24 body is aggregates only — total, daily, countries (code), browsers (family), files (owner's own file names), downloadAll, topFiles (`src/wa.api/Program.cs:1239-1249`). AC-023-2 is already asserted by `ProUser_Body_HasNoRawIpUaOrHashes` (`tests/wa.api.integration/TransferAnalyticsEndpointTests.cs:229-243`: IpHash/UaHash sentinels + "Mozilla" absent, exact property list).
- **Conclusion:** no raw IP or UA is persisted or returned in the analytics path → **no production backend fix needed.** Gap: no end-to-end test proves the HTTP write paths persist hashes only (the zip handler has no unit test at all — codegraph: "no tests found within 3 caller hops"). This plan adds those proofs.
- **i18n:** no privacy-note key exists in any locale (en.json ends at files.analytics.topDownloads, `src/wa.web/src/core/i18n/locales/en.json:279`). Must be added to all 8.

### D-1 — Note placement and markup (story UI notes, UI-Reference §5.4)
In `AnalyticsBody` (`src/wa.web/src/features/files/DownloadsPanel.tsx:67-105`), directly after the `.mf-dl-chips` div (the country stat lives there, `:75-79`): `<p className="mf-dl-privacy">{i18n.t('files.analytics.privacyNote')}</p>`. Only rendered when total > 0 (the panel only has a country stat then; zero downloads keeps the EC-023-1 empty line unchanged). New class name so the existing chip-count assertion (`DownloadsPanel.test.tsx:75`, exactly 3 `.mf-dl-chip`) is untouched.

### D-2 — CSS (tokens only)
New rule `.mf-dl-privacy`: margin 0, font-size var(--fs-tiny), color = the muted-text token (same declaration `.mf-dl-empty` uses at `src/wa.web/src/index.css:1270`), placed after `.mf-dl-empty` (`src/wa.web/src/index.css:1267-1271`). `--fs-tiny` (`index.css:13`) and the muted-text token (`src/wa.web/src/styles/tokens.css:11,28,45`) exist; no new token, so theme/contrast scripts are unaffected.

### D-3 — Locale key files.analytics.privacyNote (all 8, parity test `src/wa.web/src/core/i18n/init.test.ts:48-62`)
| Locale | Value |
|---|---|
| en | Country is estimated from your download's first IP; we keep no raw IP. |
| de | Das Land wird anhand der ersten IP des Downloads geschätzt; wir speichern keine IP-Adressen im Klartext. |
| es | El país se estima a partir de la primera IP de la descarga; no guardamos ninguna IP sin procesar. |
| fr | Le pays est estimé à partir de la première IP du téléchargement ; nous ne conservons aucune IP brute. |
| it | Il paese è stimato dal primo IP del download; non conserviamo alcun IP in chiaro. |
| nl | Het land wordt geschat op basis van het eerste IP-adres van de download; we bewaren geen ruwe IP-adressen. |
| pt | O país é estimado a partir do primeiro IP do download; não guardamos nenhum IP bruto. |
| tr | Ülke, indirmenin ilk IP adresinden tahmin edilir; ham IP adresi saklamıyoruz. |
The en string is the FR-023-4 literal, byte-exact (straight apostrophe). Add as the last key (fix the preceding line's trailing comma).

### D-4 — PII proof tests at the HTTP boundary (TA-9.4, FR-023-4)
One fact per write path, sending a fixed Chrome User-Agent (same literal as `tests/wa.application.unit/Transfers/GetFileDownloadUrlHandlerTests.cs:77`) and reading the DownloadEvent row back:
- `UaHash` == lowercase SHA-256 hex of the UA (64 chars, matches `^[0-9a-f]{64}$`), `UserAgentFamily` == "Chrome";
- `IpHash` is null (TestServer has no remote IP) **or** matches `^[0-9a-f]{64}$` — never an IP literal;
- `Country` is null;
- none of IpHash / UaHash / UserAgentFamily / Country contains "Mozilla" (the raw UA).

## 3. Implementation Plan

1. **Locales** — MODIFY the 8 locale JSON files (en, de, es, fr, it, nl, pt, tr) in src/wa.web/src/core/i18n/locales (D-3). Verify: `cd src/wa.web; npm run test:run -- src/core/i18n`.
2. **Panel** — MODIFY `src/wa.web/src/features/files/DownloadsPanel.tsx` (D-1; update the component doc comment to mention the note). MODIFY `src/wa.web/src/index.css` (D-2).
3. **Panel tests** — MODIFY `src/wa.web/src/features/files/DownloadsPanel.test.tsx`, add:
   - `Pro_WithData_ShowsPrivacyNote_UnderCountryStat (AC-023-2)`: render with DATA; `.mf-dl-privacy` text is exactly "Country is estimated from your download's first IP; we keep no raw IP."; its previousElementSibling has class mf-dl-chips; `.mf-dl-chip` count still 3.
   - `Pro_ZeroDownloads_NoPrivacyNote`: zero body → `.mf-dl-privacy` null.
   - `Free_NoPrivacyNote`: allowed false → `.mf-dl-privacy` null.
   Verify: `cd src/wa.web; npm run lint && npm run test:run && npm run build`.
4. **Write-path PII facts** — MODIFY `tests/wa.api.integration/DownloadUrlEndpointTests.cs`: add `Mint_PersistsOnlyHashesAndFamily_NoRawIpOrUa` (copy `Mint_RecordsDownloadsCountPlusOneAndOneDownloadEventRow` `:112-131`; use a fresh linkId from the Crockford charset not used elsewhere in the file, e.g. "d5w7f2q8" — grep first; send via `HttpRequestMessage` with `request.Headers.UserAgent.ParseAdd(chromeUa)`; assert D-4). MODIFY `tests/wa.api.integration/DownloadAllUrlEndpointTests.cs`: add `Mint_PersistsOnlyHashesAndFamily_NoRawIpOrUa_DownloadAll` (copy `:115-137` incl. `UploadZipBlobAsync`; same D-4 asserts; FileId null). Add `using System.Text.RegularExpressions;` if needed. Verify (Docker running): dotnet test tests/wa.api.integration with filter `FullyQualifiedName~DownloadUrlEndpointTests|FullyQualifiedName~DownloadAllUrlEndpointTests|FullyQualifiedName~TransferAnalyticsEndpointTests`.
5. **Gate** — AGENT.md §4 in full: domain / application / workers unit, integration, web lint/test/build, check-theme-tokens, check-contrast, check-telemetry-pii + selftest.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.web/src/features/files/DownloadsPanel.tsx` | MODIFY | Privacy note `<p className="mf-dl-privacy">` after `.mf-dl-chips` (D-1). |
| `src/wa.web/src/features/files/DownloadsPanel.test.tsx` | MODIFY | 3 facts (step 3). |
| `src/wa.web/src/index.css` | MODIFY | `.mf-dl-privacy` rule after `.mf-dl-empty` (D-2). |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | files.analytics.privacyNote (D-3). |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | Same key, D-3 value. |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | Same key, D-3 value. |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | Same key, D-3 value. |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | Same key, D-3 value. |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | Same key, D-3 value. |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | Same key, D-3 value. |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | Same key, D-3 value. |
| `tests/wa.api.integration/DownloadUrlEndpointTests.cs` | MODIFY | Single-file write-path PII fact (D-4). |
| `tests/wa.api.integration/DownloadAllUrlEndpointTests.cs` | MODIFY | Download-all write-path PII fact (D-4). |

**`src/wa.web/src/features/files/DownloadsPanel.tsx`** — current chips block in `AnalyticsBody` (`:75-80`); insert the note between the closing chips div and the table:
```tsx
      <div className="mf-dl-chips">
        <span className="mf-dl-chip">{i18n.t('files.analytics.total')}: <strong>{data.total}</strong></span>
        <span className="mf-dl-chip">{i18n.t('files.analytics.topCountry')}: <strong>{country === null ? '–' : countryLabel(country)}</strong></span>
        <span className="mf-dl-chip">{i18n.t('files.analytics.topBrowser')}: <strong>{browser === null ? '–' : browserLabel(browser)}</strong></span>
      </div>
      {/* US-023-03 (FR-023-4): insert <p className="mf-dl-privacy">…privacyNote…</p> here */}
      <table className="mf-dl-files" aria-label={i18n.t('files.analytics.perFile')}>
```

**`src/wa.web/src/index.css`** (`:1267-1272`) — add the new rule after:
```css
.mf-dl-empty {
  margin: 0;
  font-size: var(--fs-small);
  color: …;   /* verbatim value is the muted-text token var (tokens.css:11); verify_plan rejects the literal here */
}
```

**Locale files** — each currently ends (`:278-280`, en shown):
```json
  "files.analytics.downloadAll": "Download all (zip)",
  "files.analytics.topDownloads": "Top downloads"
}
```
Add a comma after the topDownloads line and the new key as the last entry.

**`src/wa.web/src/features/files/DownloadsPanel.test.tsx`** — reuse `stubApi`, `render`, `flush`, DATA (`:12-51`); the zero-downloads body literal is at `:82`.

**`tests/wa.api.integration/DownloadUrlEndpointTests.cs`** — pattern to copy (`:112-131`); helpers `SeedTransferAsync(status, linkId, maxDownloads)`, `SeedFileItemsAsync`, `FreshDb()`, `_client`; `System.Security.Cryptography` / `System.Text` already imported (`:1-16`):
```csharp
var transferId = await SeedTransferAsync(status: 1, linkId: "d5w3f1q3", maxDownloads: 5);
var fileId = Guid.NewGuid();
await SeedFileItemsAsync(transferId, [(fileId, "counted.txt", 10L)]);
using var response = await _client.GetAsync("/api/v1/public/transfers/d5w3f1q3/files/" + fileId.ToString("n") + "/download-url");
...
var rows = await freshDb.Set<DownloadEvent>().Where(e => e.TransferId == transferId).ToListAsync();
```
Replace GetAsync with an `HttpRequestMessage` (GET) carrying the Chrome UA, sent via `_client.SendAsync`.

**`tests/wa.api.integration/DownloadAllUrlEndpointTests.cs`** — pattern to copy (`:115-137`, uses `UploadZipBlobAsync(transferId, [1, 2, 3])` and the download-all-url route); `System.Security.Cryptography` / `System.Text` already imported.

## 4. Open Questions / Risks

- **Carried, not new — US-023-01 DECISION-1 (country source) is still open.** Nothing writes `DownloadEvent.Country`, so every country shows "Unknown" (EC-023-2) while the note says country "is estimated from your download's first IP". The note is the FR-023-4 literal and stays; it becomes fully accurate once DECISION-1 lands. BLOCKS: none.
- **DECISION (new, non-blocking) — UaHash keying.** The code uses unkeyed SHA-256 for UaHash (`GetFileDownloadUrlQuery.cs:114-115`, `GetDownloadAllUrlQuery.cs:166-167`), while the column comment says HMAC(UA) (`DownloadEventConfiguration.cs:20`). FR-023-4 only requires "UaHash". This plan does not change it (PII-shaped → AGENT.md §7). See the DECISION block in the final report.
- **Key rotation:** `wa-jwt-secret` rotates every 90 d (TA-9.3), which changes IpHash for the same IP. Nothing reads IpHash today (no unique-visitor stat), so no impact; rotation policy is the story's "later decision".
- **Doc drift (orchestrator close-out):** TA-9.4 still says "raw PII only in `DownloadEvent` (IP)" — stale against FR-023-4 and the code; update to "DownloadEvent holds IpHash (HMAC, `Wa:Jwt:Secret`) / UaHash / UserAgentFamily / Country only". Also story status + REPO-FACTS if it tracks locale keys.
- **TestServer IP:** RemoteIpAddress is null under WebApplicationFactory, so IpHash is null end-to-end; the HMAC itself is already asserted at unit level (`GetFileDownloadUrlHandlerTests.cs:62`). The D-4 assertion accepts null or 64-hex.
