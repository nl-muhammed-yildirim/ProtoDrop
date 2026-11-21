# US-045-01 plan — Find a transfer by file name or recipient (F-XCT-002, milestone T-066)

> **STATUS: BLOCKED ON DECISION D-21 (search backend).** D-21 is `open` / owner "you" in `docs/Open-Decisions-and-Constants.md` (row D-21), and the T-066 backlog row says "D-21 backend confirmed before index shape freezes". AGENT.md §7 forbids settling an unconfirmed TBD-you decision alone. The steps below are written for the **recommended option B** of the DECISION in §5. Do **not** move this item to Ready until the user answers. If the user picks A or C, the Architect re-plans steps 4–5 and 9 (the store and its integration facts). Every other step (problem code, term rules, cursor, gate, endpoint, web) stays the same under all three options.

## 1. Goal

Add an owner-scoped `q` search to endpoint 14 (GET /api/v1/transfers) behind the feature.search gate, plus a debounced search box in My Files. It matches file name, recipient address or sender name, orders by relevance, uses an opaque search cursor and rejects an over-long q with 400 Q_TOO_LONG. The admin endpoint 21 gets the same widened match. **Exit check:** the three story ACs, AC-045-1/2/3, EC-045-1/3/6 and FR-045-5/7 are green at domain-unit, application-unit, integration (`TransferSearchEndpointTests`) and vitest (`search.test.ts`, the new FilesPage describe) level. With the flag off there is no search box, and `?q=` returns the plain list.

## 2. Design & Decisions

**Verified on disk:**
- Endpoint 14 lives at `src/wa.api/Program.cs:1141`. It decodes a plain cursor early (L1154-1164) and sends `ListMyTransfersQuery` (L1181). The wire projection (L1183-1199) is the single construction site and stays unchanged: search returns the same `ListMyTransfersResult`.
- The plain list query is `src/wa.infrastructure/Persistence/TransferStore.cs:59-100`. The row Name is the first `FileItem.OriginalName`. File names live in `FileItem.OriginalName` (`src/wa.domain/FileItem.cs:9`), not in a "TransferFile.FileName" column as the spec says. Recipients are in `EmailRecipient.Address` (`src/wa.domain/EmailRecipient.cs:8`) and the sender in `Transfer.SenderName` (`src/wa.domain/Transfer.cs:17`). No SearchText column and no full-text objects exist anywhere in src/.
- Admin endpoint 21 already has a `q` (FR-011-3: LinkId exact OR recipient substring). Code: `src/wa.infrastructure/Persistence/SearchTransfersStore.cs:40`, handler trim at `src/wa.application/UseCases/Admin/SearchTransfersQuery.cs:47-48`. Admin rows already carry `ownerEmail` and the admin UI already has the q box and an owner column, so **AC-045-2 needs no web change**.
- The gate primitive exists: `FeatureGates.Search` (`src/wa.application/Limits/FeatureGates.cs:9`), `IFlagResolver` registered at `src/wa.api/Program.cs:111` (30 s FlagsCache). The web gate store already includes the feature.search key (`src/wa.web/src/core/features/featureGates.ts:16`), and FilesPage already calls `loadFeatureGates()` on mount (`src/wa.web/src/features/files/FilesPage.tsx:149`).
- Q_TOO_LONG is spec-sanctioned (Open-Decisions Part 3 row 2026-08-28) but is not yet in `src/wa.domain/Errors/ProblemCodes.cs`.
- The test SQL image is the stock `mcr.microsoft.com/mssql/server:2022-latest` (every integration class, e.g. `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs:47`, and `docker-compose.local.yml`). The stock Linux image ships **without** the Full-Text Search component (the separate mssql-server-fts package). This is why D-21 has to be decided now and not at implementation time.

**D1 — Matching (option B, pending D-21):** case-insensitive substring match through EF `string.Contains(term)` on `FileItem.OriginalName`, `EmailRecipient.Address` and `Transfer.SenderName`. Case-insensitivity comes from the database's CI collation, the same idiom as the admin store at L40. EF Core 10 sends `Contains` with a parameter as an escaped LIKE (or CHARINDEX), so `*`, `~`, `"`, `%`, `_` and `[` are literal characters (FR-045-6 / AC-045-3). The integration facts prove this at the SQL level. There are no hand-built pattern strings, so there is no operator leakage. No schema change and no migration. TA-2.3: EF stays in wa.infrastructure/Persistence. Tradeoff: the query is not index-assisted (EXISTS scans), but the owner predicate (`OwnerAppUserId = @me`) bounds it to one user's rows. The admin scan is the same shape as FR-011-3 today. The contract (FR-045-1) does not depend on the backend, so a later move to full-text or Azure Search changes only `TransferSearchStore` (D-21 fallback wording).

**D2 — Relevance (FR-045-3, EC-045-6):** rank = the maximum over the matched fields, with file name = 3, recipient = 2, sender = 1. It is computed as one CASE over EXISTS subqueries per Transfer row, so each transfer appears exactly once (de-dup by construction). Order: rank DESC, CreatedAtUtc DESC, Id DESC. The rank constants live in the domain (`SearchTerm.RankFileName` etc.) so the store and the tests share them.

**D3 — Search cursor (FR-045-3, TA-4.1.4):** a new domain codec `SearchCursorCodec`: base64("{rank}|{createdAtUtc:O}|{id:N}|{term}"). The term goes last because it may contain "|", and Decode splits into at most 4 parts. Decode failure throws the existing `CursorFormatException`. A cursor whose term differs (ordinal) from the current term also throws `CursorFormatException`, and so does a plain list cursor sent with a q (it has no "|"). The endpoint maps all of these to 400 VALIDATION, an existing code. The plain cursor format (`src/wa.domain/Transfers/CursorCodec.cs:10`) is unchanged.

**D4 — Term rules (FR-045-1, EC-045-3):** a new domain `SearchTerm.Normalize(raw)`: trim, null/whitespace → null (no search), length after trim > `SearchTerm.MaxLength` (100) → `SearchTermTooLongException`. The endpoints map that exception to 400 Q_TOO_LONG. MaxLength is a spec-fixed input-width constant and not a plan limit. It sits next to its rule, following the ORG_NAME_MAX_LENGTH / `DisplayNameMaxLength` precedent (Open-Decisions Part 2), so it does not go in the Limits Registry (golden rule 2 is not engaged).

**D5 — Gate placement (FR-045-5, EC-045-5):** the gate is resolved **inside the use cases**, not with `.RequireFeature`. `RequireFeature` answers 404, but FR-045-5 says a stale `?q=` must degrade to the plain list. Putting the gate in the handler also makes "flag off → q ignored" unit-testable. New `SearchMyTransfersQuery` + handler (ctor: `ITransferStore`, `ITransferSearchStore`, `IFlagResolver`) works like this:
- Flag off, or the term normalizes to null: run exactly the plain list path, with the plain cursor and `ListByOwnerAsync`. Its length is not validated either, because q is ignored entirely.
- Flag on: decode the search cursor and call the new port.

The endpoint sends `SearchMyTransfersQuery` only when a `q` parameter is present. Otherwise it sends the untouched `ListMyTransfersQuery`, so the US-009 paths and tests stay byte-identical. The flag is read once per request.

**D6 — New narrow port (ISP):** `ITransferSearchStore` in wa.application/UseCases/Transfers, implemented by the new `TransferSearchStore` in wa.infrastructure/Persistence. I do not add a member to `ITransferStore`, because that would break 12 hand-rolled stubs across tests/ (grep `: ITransferStore`) for a method none of them call. The projection to `MyTransferRow` copies the anonymous projection in `TransferStore.cs:69-92` on purpose. It is a separate query shape, and the TA-4.2a single-construction-site rule is about the endpoint wire, which stays single.

**D7 — Admin endpoint 21 (FR-045-4, AC-045-2):** `SearchTransfersQueryHandler` gains `IFlagResolver`.
- Flag on: q goes through `SearchTerm.Normalize` (so the Q_TOO_LONG rule applies), and the store additionally matches file name and sender name (`matchFilesAndSender: true`).
- Flag off: the FR-011-3 behaviour stays exactly as it is (trim, LinkId-exact OR recipient substring). That pre-existing admin q **is not** ignored, because it predates F-XCT-002 (see Open Questions).

Admin ordering and cursor stay chronological ("admin cursor shape", FR-045-4).

**D8 — Web (UI notes, US-044-02 contract):**
- The search box renders only when `useFeatureGate('feature.search')` is true. When off it is absent, not disabled.
- Input state is debounced 300 ms into a committed, normalized query (trim plus slice to 100: "UI trims first").
- A committed-query change resets the cursors and fetches page 0. Next/Prev carry the q.
- "×" and the empty-state **Clear search** both clear immediately, with no debounce wait, which restores the chronological list with its own cursor.
- A stale response guard (`cancelled` flag in the effect cleanup) stops a slow earlier query from overwriting a newer one.
- No-match empty state shows "No transfers match '{query}'." plus **Clear search** (EC-045-1).
- Row actions (View / Re-send / …) are unchanged, so search adds no new navigation.
- URL building and normalization go in a small pure module `features/files/search.ts`, which is unit-tested.
- New i18n keys in all 8 bundles (parity test `src/wa.web/src/core/i18n/init.test.ts:51-57`, 3-segment scheme).
- New CSS meets the tap-target gate: 44 px height, 44 px width for the icon button, 16 px input font.

**D9 — Telemetry (FR-045-8):** no new event, log line or property. A searched list request looks like any other list request (TA-10.2 closed list untouched). Do not log q, because it may be an email address (TA-9.4).

Dependency rule (TA-0.2 #7):
- Domain: `SearchTerm` and `SearchCursorCodec` (pure).
- Application: port + handler, depending on domain + `IFlagResolver`.
- Infrastructure: EF store.
- Api: DI + mapping.

## 3. Implementation Plan

1. **Problem code.** In `src/wa.domain/Errors/ProblemCodes.cs`:
   - Add `public const string QTooLong = "Q_TOO_LONG";` after `ScheduleOutOfWindow` (L45), with a comment citing US-045-01 / EC-045-3.
   - Add the table row `[QTooLong] = (400, "Search query too long"), // US-045-01 (EC-045-3): q over SearchTerm.MaxLength after trim` after the `[ScheduleOutOfWindow]` row (L75).
   - In the L4 summary, replace the stale member count with the live count after your change. Count the `public const string` lines; do not guess.

   Test: create `tests/wa.domain.unit/Transfers/QTooLongProblemCodeTests.cs` with one fact pinning the wire string, status 400 and title (shape of `tests/wa.domain.unit/Flags/FlagProblemCodesTests.cs:9-14`). Verify: `dotnet test tests/wa.domain.unit`.
2. **Term + cursor (domain).** Create `src/wa.domain/Transfers/SearchTerm.cs` and `src/wa.domain/Transfers/SearchCursorCodec.cs` (code in §4).
   - `tests/wa.domain.unit/Transfers/SearchTermTests.cs` covers:
     - null, "", "   " → null;
     - "  financial  " → "financial";
     - 100 chars → returned;
     - 101 chars (and "  " + 100 chars + "  " → OK, since the trim happens first) → `SearchTermTooLongException`;
     - "report *" and `~"x"` → returned unchanged (literal, FR-045-6);
     - rank constants 3 > 2 > 1.
   - `tests/wa.domain.unit/Transfers/SearchCursorCodecTests.cs` covers:
     - round-trip with term "a|b c@d.com", rank 2, a UTC timestamp and a Guid;
     - not-base64 → `CursorFormatException`;
     - a plain `CursorCodec.Encode(...)` cursor → `CursorFormatException`;
     - rank "0" or "x" → exception;
     - empty term → exception;
     - bad Guid → exception.

   Verify: `dotnet test tests/wa.domain.unit`.
3. **Application port + handler.**
   - Create `src/wa.application/UseCases/Transfers/ITransferSearchStore.cs` and `src/wa.application/UseCases/Transfers/SearchMyTransfers.cs` (code in §4).
   - Tests: `tests/wa.application.unit/Transfers/SearchMyTransfersTests.cs`. Hand stubs, no Moq:
     - `StubFlagResolver`: copy `tests/wa.application.unit/Transfers/GetPublicTransferHandlerTests.cs:652-662`.
     - `StubTransferStore`: copy `tests/wa.application.unit/Transfers/ListMyTransfersTests.cs:208-253` and add `public int ListCalls` incremented in `ListByOwnerAsync`.
     - `StubTransferSearchStore`: records every argument and a call count, and returns a settable `Data`.
   - Facts:
     - `FlagOff_QIgnored_PlainListCalled_SearchNotCalled` (FR-045-5; story AC 3, API half);
     - `FlagOff_TooLongQ_IsIgnored_NoThrow`;
     - `FlagAbsent_DefaultsOff` (no Values entry);
     - `FlagOn_TrimmedTermAndStatusesAndLimitPassedToSearch`;
     - `FlagOn_WhitespaceQ_FallsBackToPlainList`;
     - `FlagOn_101Chars_ThrowsSearchTermTooLong` and `FlagOn_100Chars_Searches`;
     - `FlagOn_SearchCursor_DecodedIntoRankUtcId`;
     - `FlagOn_CursorFromOtherTerm_ThrowsCursorFormat`;
     - `FlagOn_PlainCursor_ThrowsCursorFormat`;
     - `ReadsFlagOncePerRequest` (Calls == 1).

   Verify: `dotnet test tests/wa.application.unit`, filtered to FullyQualifiedName~SearchMyTransfers.
4. **Infrastructure store (option B).** Create `src/wa.infrastructure/Persistence/TransferSearchStore.cs` (code in §4). In `src/wa.api/Program.cs`, after L71, register `ITransferSearchStore` → `TransferSearchStore` as a singleton, using the same idiom as L71. Verify: `dotnet build src/wa.api`.
5. **Admin widening.**
   - `src/wa.application/UseCases/Admin/ISearchTransfersStore.cs`: insert the parameter `bool matchFilesAndSender` right after `q` in `SearchAsync` (L19), with an xml param doc.
   - `src/wa.infrastructure/Persistence/SearchTransfersStore.cs`: same parameter in the signature (L25). Replace the L40 predicate as shown in §4.
   - `src/wa.application/UseCases/Admin/SearchTransfersQuery.cs`: use the primary ctor `(ISearchTransfersStore store, IFlagResolver flags)` and replace step (1) at L46-48 per §4. Pass `searchOn` as the second store argument at L70. Add `using wa.application.Limits;`.
   - `tests/wa.application.unit/Admin/SearchTransfersQueryTests.cs`:
     - L17 becomes `new SearchTransfersQueryHandler(_store, _flags)`, with a field `_flags = new StubFlagResolver()` (flag off by default, so every existing fact keeps the legacy behaviour).
     - Add the new parameter to the stub's `SearchAsync` (L187), plus `public bool CapturedMatchFilesAndSender`.
     - Add `using wa.application.Limits;`.
     - New facts: `FlagOff_LegacyTrim_NoFileOrSenderMatch_LongQAllowed` (a 101-char q is passed trimmed and `CapturedMatchFilesAndSender` is false); `FlagOn_MatchFilesAndSenderTrue`; `FlagOn_101CharQ_ThrowsSearchTermTooLong`.

   Verify: `dotnet test tests/wa.application.unit`, filtered to FullyQualifiedName~SearchTransfersQuery.
6. **Endpoints.** In `src/wa.api/Program.cs`:
   - (a) Endpoint 14: read `var rawQ = request.Query["q"].FirstOrDefault();` right after L1154. Guard the early plain-cursor decode (L1156) with `rawQ is null &&`. Replace L1181 with the try/dispatch block in §4.
   - (b) Endpoint 21: add a third catch after L695 mapping `wa.domain.Transfers.SearchTermTooLongException` to `ProblemCodes.QTooLong`.

   Verify: `dotnet build src/wa.api`.
7. **Integration (Testcontainers, Docker running).** Create `tests/wa.api.integration/TransferSearchEndpointTests.cs`. Abstract base `TransferSearchEndpointTestsBase(bool gateOn)`:
   - Copy the container, env, migrate, flag-flip and factory recipe from `tests/wa.api.integration/TransferAnalyticsEndpointTests.cs:25-78`, using `FeatureGates.Search` instead of Analytics.
   - Additionally, before the factory is built, set `Environment.SetEnvironmentVariable("Wa__AdminEmails", AdminEmail)` and seed that admin `AppUser` (recipe in `tests/wa.api.integration/AdminFlagsEndpointTests.cs:47-70`).
   - Copy the helpers `SeedUserAsync`, `SeedTransferAsync`, `SeedFilesAsync`, `CookieFor`, `GetAsync` and `ReadJsonAsync` from `TransferAnalyticsEndpointTests.cs:80-198`. Give `SeedTransferAsync` an extra `string senderName = "Anna K."` parameter.
   - Add `SeedRecipientAsync(Guid transferId, string address)` (new `EmailRecipient { Id = Guid.NewGuid(), TransferId, Address }`).
   - Row ids on the wire are 'n'-form (`Program.cs:1187`): compare with `id.ToString("N")`.

   `TransferSearchEndpointTests` (gate on) facts:
   - `FileName_Financial_MatchFirst_HolidayAbsent`: story AC 1 / AC-045-1. T1 has "q4-financial-report.pdf" → a@b.com; T2 has "holiday.mp4" → c@d.com. q=financial gives items[0] = T1, and T2 is absent.
   - `Recipient_ExactEmail_Matches`: story AC 2. q=a@b.com gives T1.
   - `Sender_Name_Matches`: sender "Zorro Ltd", q=zorro.
   - `Operators_AreLiteral`: AC-045-3 / FR-045-6.
     - Files "report *.pdf" (T3) and "report-final.pdf" (T4): q="report *" gives only T3.
     - Files "50%_off.png" (T5) and "500 off.png" (T6): q="50%_" gives only T5.
     - q="\"" (a lone double quote) gives 200 and an empty list.
     - Send q URL-encoded with Uri.EscapeDataString.
   - `FileAndRecipientMatch_SingleRow`: EC-045-6. Files "acme-1.pdf" and "acme-2.pdf" plus recipient acme@x.com, q=acme: exactly one row.
   - `FileMatch_OutranksRecipientMatch`: T7 has file "zeta.pdf" and was created earlier; T8 has file "a.pdf" and recipient zeta@y.com and was created later. q=zeta gives [T7, T8].
   - `NeverCrossesOwners`: FR-045-7. User B owns "secret-financial.pdf". User A's q=financial never contains B's id, and B's q=financial contains only B's.
   - `DeletedTransfer_NotFound`: deleted: true, file "gone-financial.pdf".
   - `NoMatch_EmptyList`: EC-045-1. 200, items [], nextCursor null.
   - `QTooLong_400` (EC-045-3): 101 × "a" gives 400 with JSON code "Q_TOO_LONG", while 100 × "a" gives 200.
   - `Paging_SearchCursor`: 26 transfers with file "deck-{i}.pdf".
     - q=deck: 25 rows plus a nextCursor.
     - Page 2 with the same q: 1 row, no id repeats.
     - That cursor with q=other gives 400 VALIDATION.
   - `Admin_FileName_CrossesOwners_ShowsOwner`: AC-045-2. The admin cookie's GET /api/v1/admin/transfers?q=financial returns A's and B's transfers, each with a non-null ownerEmail.

   `TransferSearchEndpointGateOffTests` (gate off) facts:
   - `FlagOff_QIgnored_PlainChronologicalList`: story AC 3, API half. q=financial returns both T1 and T2, newest first.
   - `FlagOff_LongQ_Ignored_200`.
   - `FlagOff_Admin_LegacyQ_NoFileNameMatch`: admin q=financial returns no items, because FR-011-3 is unchanged.

   Verify: `dotnet test tests/wa.api.integration`, filtered to FullyQualifiedName~TransferSearchEndpoint.
8. **Web module.** Create `src/wa.web/src/features/files/search.ts` (code in §4) and `src/wa.web/src/features/files/search.test.ts`.
   - `normalizeQuery`: "  logo " → "logo"; 150 chars → 100; "   " → "".
   - `buildListUrl`: no q and no cursor gives exactly `${base}/transfers?status=all`; q "a@b.com" gives `&q=a%40b.com`; q plus cursor puts the q before the cursor and encodes both.
9. **i18n.** In each of the 8 locale bundles (the eight locales/*.json files under src/wa.web/src/core/i18n), insert the four keys from §4 directly after the existing files.empty.cta line, keeping the JSON valid.
10. **FilesPage.** Modify `src/wa.web/src/features/files/FilesPage.tsx` per §4. Add CSS to `src/wa.web/src/index.css` after the `.mf-pill-active` rule (L1183-1187) per §4. Tests: a new describe `'FilesPage search (US-045-01)'` appended to `src/wa.web/src/features/files/FilesPage.test.tsx`.
    - Reuse `stubFetch` (L32-46), `renderAppAtFiles` (L72-85), `flush` and `makeRows`.
    - Responder pattern of L704-708: `/features` → `{ 'feature.search': true }`.
    - Helper `typeInto(input, value)`: native value setter + bubbling `input` event (see §4).
    - Facts:
      - `GateOff_NoSearchBox_NoQSent`: story AC 3, UI half. `.mf-search-input` is null and no captured URL contains "q=".
      - `GateOn_Debounce300_SendsQ_ReplacesList`. Call `vi.useFakeTimers()` before rendering. Type "financial". After `vi.advanceTimersByTime(299)` no URL contains "q=". After 1 more ms, flush: exactly one URL ending `status=all&q=financial`, and the rows equal the stubbed search rows.
      - `NoMatch_ShowsMessage_ClearSearchRestores`: EC-045-1. q=logo gives `{items:[],nextCursor:null}`. The text "No transfers match 'logo'." is shown. Clicking `.mf-empty .mf-pill` empties the input, the next URL has no "q=", and the rows come back.
      - `ClearX_ResetsCursor`: search, then Next (the URL carries both q and cursor), then click `.mf-search-clear`. The last URL is exactly `http://localhost:8080/api/v1/transfers?status=all`.
      - `LongInput_TrimmedTo100BeforeSubmit`: type 150 × "a". The decoded q param length is 100.
    - The existing facts (a)–(q) and the Downloads describe must still pass unchanged.

    Verify from the web project folder: `npm run lint`, `npm run test:run`, `npm run build`.
11. **Gate.**
    - `dotnet test tests/wa.domain.unit`
    - `dotnet test tests/wa.application.unit`
    - `dotnet test tests/wa.workers.unit` (compile check only; nothing there implements the touched ports)
    - The step-7 filtered integration run
    - The web gate (step 10)
    - From the repo root: `node scripts/check-theme-tokens.mjs`, `node scripts/check-contrast.mjs`, `node scripts/check-telemetry-pii.mjs` and its selftest

    The full integration suite runs at feature end (pitfalls).

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.domain/Errors/ProblemCodes.cs` | MODIFY | QTooLong const + (400, "Search query too long") row; fix summary count |
| `tests/wa.domain.unit/Transfers/QTooLongProblemCodeTests.cs` | CREATE | Pin wire string / 400 / title |
| `src/wa.domain/Transfers/SearchTerm.cs` | CREATE | MaxLength 100, rank consts, Normalize, SearchTermTooLongException |
| `src/wa.domain/Transfers/SearchCursorCodec.cs` | CREATE | base64("{rank}\|{createdAt:O}\|{id:N}\|{term}") encode/decode |
| `tests/wa.domain.unit/Transfers/SearchTermTests.cs` | CREATE | Trim/null/100/101/literal operators |
| `tests/wa.domain.unit/Transfers/SearchCursorCodecTests.cs` | CREATE | Round-trip, plain cursor rejected, malformed parts |
| `src/wa.application/UseCases/Transfers/ITransferSearchStore.cs` | CREATE | Owner-scoped search port |
| `src/wa.application/UseCases/Transfers/SearchMyTransfers.cs` | CREATE | SearchMyTransfersQuery + handler (gate, term, cursor) |
| `tests/wa.application.unit/Transfers/SearchMyTransfersTests.cs` | CREATE | Gate off/absent/on, trim, too long, cursor rules, one flag read |
| `src/wa.infrastructure/Persistence/TransferSearchStore.cs` | CREATE | EF query: owner scope, EXISTS match, CASE rank, rank/created/id order, search cursor |
| `src/wa.application/UseCases/Admin/ISearchTransfersStore.cs` | MODIFY | `matchFilesAndSender` parameter after q |
| `src/wa.infrastructure/Persistence/SearchTransfersStore.cs` | MODIFY | Same parameter; widened q predicate |
| `src/wa.application/UseCases/Admin/SearchTransfersQuery.cs` | MODIFY | IFlagResolver; flag-on Normalize + widened match |
| `tests/wa.application.unit/Admin/SearchTransfersQueryTests.cs` | MODIFY | Ctor + stub signature; three gate facts |
| `src/wa.api/Program.cs` | MODIFY | DI for ITransferSearchStore; endpoint 14 q dispatch + mapping; endpoint 21 Q_TOO_LONG catch |
| `tests/wa.api.integration/TransferSearchEndpointTests.cs` | CREATE | Gate-on + gate-off classes (ACs, ECs, owner scoping, admin) |
| `src/wa.web/src/features/files/search.ts` | CREATE | Debounce/max consts, normalizeQuery, buildListUrl |
| `src/wa.web/src/features/files/search.test.ts` | CREATE | normalize + URL building |
| `src/wa.web/src/features/files/FilesPage.tsx` | MODIFY | Gated search box, debounce, q in fetches, no-match state, clear |
| `src/wa.web/src/features/files/FilesPage.test.tsx` | MODIFY | New search describe (5 facts) |
| `src/wa.web/src/index.css` | MODIFY | .mf-search / .mf-search-input / .mf-search-clear |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | 4 files.search keys |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | 4 files.search keys |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | 4 files.search keys |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | 4 files.search keys |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | 4 files.search keys |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | 4 files.search keys |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | 4 files.search keys |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | 4 files.search keys |

**New domain code** (`src/wa.domain/Transfers/SearchTerm.cs`):
```csharp
namespace wa.domain.Transfers;

/// <summary>US-045-01 (FR-045-1, FR-045-6, EC-045-3): the transfer search term. Pure — no EF, no ASP.NET.</summary>
public static class SearchTerm
{
    /// <summary>FR-045-1: 1..100 chars after trim. Input-width constant (ORG_NAME_MAX_LENGTH precedent), not a plan limit.</summary>
    public const int MaxLength = 100;

    /// <summary>FR-045-3 / EC-045-6: relevance = max of the matched fields.</summary>
    public const int RankFileName = 3;
    public const int RankRecipient = 2;
    public const int RankSender = 1;

    /// <summary>Trim; null/whitespace → null (no search). Over MaxLength after trim → SearchTermTooLongException (→ 400 Q_TOO_LONG).
    /// Characters are never interpreted: '*', '~', '"' stay literal (FR-045-6).</summary>
    public static string? Normalize(string? raw)
    {
        var term = raw?.Trim();
        if (string.IsNullOrEmpty(term)) return null;
        if (term.Length > MaxLength)
            throw new SearchTermTooLongException($"q must be at most {MaxLength} characters.");
        return term;
    }
}

/// <summary>US-045-01 (EC-045-3): the endpoint maps this to 400 Q_TOO_LONG via BusinessException.</summary>
public sealed class SearchTermTooLongException : Exception
{
    public SearchTermTooLongException(string message) : base(message) { }
}
```

**`src/wa.domain/Transfers/SearchCursorCodec.cs`:**
```csharp
using System.Globalization;
using System.Text;

namespace wa.domain.Transfers;

/// <summary>US-045-01 (FR-045-3): opaque search cursor = base64("{rank}|{createdAtUtc:O}|{id:N}|{term}") of the last row.
/// The term is last (it may contain '|'). Any malformed input → CursorFormatException (endpoint → 400 VALIDATION).</summary>
public static class SearchCursorCodec
{
    public static string Encode(string term, int rank, DateTime createdAtUtc, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{rank.ToString(CultureInfo.InvariantCulture)}|{createdAtUtc:O}|{id:N}|{term}"));

    public static (string Term, int Rank, DateTime CreatedAtUtc, Guid Id) Decode(string cursor)
    {
        string raw;
        try { raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)); }
        catch (FormatException) { throw new CursorFormatException("cursor is not valid base64."); }

        var parts = raw.Split('|', 4);
        if (parts.Length != 4 || parts[3].Length == 0)
            throw new CursorFormatException("search cursor has the wrong shape.");
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rank) || rank < 1)
            throw new CursorFormatException("search cursor has an invalid rank.");
        if (!DateTime.TryParseExact(parts[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAtUtc))
            throw new CursorFormatException("search cursor has an unparseable timestamp.");
        if (!Guid.TryParseExact(parts[2], "N", out var id))
            throw new CursorFormatException("search cursor has an unparseable transfer id.");
        return (parts[3], rank, createdAtUtc, id);
    }
}
```

**`src/wa.application/UseCases/Transfers/ITransferSearchStore.cs`:**
```csharp
namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (FR-045-1/3/7): owner-scoped transfer search. Owner + not-deleted predicate in the query (search never crosses owners);
/// one row per transfer; order rank DESC, CreatedAtUtc DESC, Id DESC; Take(limit+1); NextCursor = SearchCursorCodec of the last returned row.</summary>
public interface ITransferSearchStore
{
    Task<(List<MyTransferRow> Items, string? NextCursor)> SearchByOwnerAsync(
        Guid ownerId, string term, IReadOnlyCollection<byte>? statuses,
        int? afterRank, DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct);
}
```

**`src/wa.application/UseCases/Transfers/SearchMyTransfers.cs`:**
```csharp
using MediatR;
using wa.application.Limits;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (TA-4.2#14 + q): endpoint 14 with a q parameter present. Same result type as the plain list (single wire site).</summary>
public sealed record SearchMyTransfersQuery(Guid OwnerId, string? Q, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;

/// <summary>FR-045-5: feature.search off (or absent) → q is ignored entirely (plain list, plain cursor, no length check).
/// On → SearchTerm.Normalize (Q_TOO_LONG), search cursor bound to the term, ITransferSearchStore. One flag read per request.</summary>
public sealed class SearchMyTransfersHandler(ITransferStore transfers, ITransferSearchStore search, IFlagResolver flags)
    : IRequestHandler<SearchMyTransfersQuery, ListMyTransfersResult>
{
    public async Task<ListMyTransfersResult> Handle(SearchMyTransfersQuery request, CancellationToken cancellationToken)
    {
        var searchOn = await flags.ResolveFlag(FeatureGates.Search, FeatureGates.DefaultWhenAbsent, cancellationToken);
        var term = searchOn ? SearchTerm.Normalize(request.Q) : null;

        if (term is null)
        {
            DateTime? afterUtc = null;
            Guid? afterIdExcluded = null;
            if (!string.IsNullOrEmpty(request.Cursor))
                (afterUtc, afterIdExcluded) = CursorCodec.Decode(request.Cursor);
            var (items, nextCursor) = await transfers.ListByOwnerAsync(request.OwnerId, afterUtc, afterIdExcluded, request.Limit, request.Statuses, cancellationToken);
            return new ListMyTransfersResult(items, nextCursor);
        }

        int? afterRank = null;
        DateTime? searchAfterUtc = null;
        Guid? searchAfterId = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            var decoded = SearchCursorCodec.Decode(request.Cursor);
            if (!string.Equals(decoded.Term, term, StringComparison.Ordinal))
                throw new CursorFormatException("cursor belongs to a different search.");
            afterRank = decoded.Rank;
            searchAfterUtc = decoded.CreatedAtUtc;
            searchAfterId = decoded.Id;
        }

        var (rows, next) = await search.SearchByOwnerAsync(request.OwnerId, term, request.Statuses, afterRank, searchAfterUtc, searchAfterId, request.Limit, cancellationToken);
        return new ListMyTransfersResult(rows, next);
    }
}
```

**`src/wa.infrastructure/Persistence/TransferSearchStore.cs`** (option B). The constructor and usings mirror `SearchTransfersStore.cs:1-16`; the class is `public sealed class TransferSearchStore : ITransferSearchStore`. Query body:
```csharp
var rows = await _db.Set<Transfer>()
    .AsNoTracking()
    .Where(t => t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null)   // FR-045-7: owner scope in the predicate
    .Where(t => statuses == null || statuses.Contains(t.Status))
    .Select(t => new
    {
        T = t,
        Rank = _db.Set<FileItem>().Any(f => f.TransferId == t.Id && f.OriginalName.Contains(term)) ? SearchTerm.RankFileName
            : _db.Set<EmailRecipient>().Any(r => r.TransferId == t.Id && r.Address.Contains(term)) ? SearchTerm.RankRecipient
            : t.SenderName.Contains(term) ? SearchTerm.RankSender
            : 0
    })
    .Where(x => x.Rank > 0)
    .Where(x => afterRank == null
        || x.Rank < afterRank.Value
        || (x.Rank == afterRank.Value && (x.T.CreatedAtUtc < afterUtc!.Value
            || (x.T.CreatedAtUtc == afterUtc.Value && x.T.Id < afterIdExcluded!.Value))))
    .OrderByDescending(x => x.Rank)
    .ThenByDescending(x => x.T.CreatedAtUtc)
    .ThenByDescending(x => x.T.Id)
    .Take(limit + 1)
    .Select(x => new
    {
        x.Rank,
        Id = x.T.Id,
        CreatedAtUtc = x.T.CreatedAtUtc,
        Name = _db.Set<FileItem>().Where(f => f.TransferId == x.T.Id).OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                   .Select(f => f.OriginalName).FirstOrDefault() ?? "Transfer",
        x.T.FileCount, x.T.TotalBytes, x.T.Status, x.T.ExpiresAtUtc, x.T.DownloadsCount, x.T.MaxDownloads,
        RecipientCount = _db.Set<EmailRecipient>().Count(r => r.TransferId == x.T.Id),
        x.T.LinkId
    })
    .ToListAsync(ct);

var items = rows.Take(limit)
    .Select(r => new MyTransferRow(r.Id, r.Name, r.FileCount, r.TotalBytes, r.Status, r.ExpiresAtUtc, r.DownloadsCount, r.MaxDownloads, r.RecipientCount, r.LinkId))
    .ToList();
var nextCursor = rows.Count > limit
    ? SearchCursorCodec.Encode(term, rows[limit - 1].Rank, rows[limit - 1].CreatedAtUtc, rows[limit - 1].Id)
    : null;
return (items, nextCursor);
```
If EF reports a translation error on the CASE-over-EXISTS projection, project three bools (`InFile`, `InRecipient`, `InSender`) in the first Select and compute Rank in a second Select with the same ternary. Do not switch to client evaluation.

**`src/wa.domain/Errors/ProblemCodes.cs`.** Current anchors:
```csharp
4   /// The closed TA-4.1.3 machine-code list — exactly these NN members, no others in MVP ...   (NN is stale; set it to the live count after adding QTooLong)
44      // US-022-03 (FR-022-2): scheduledSendAt outside now+5 min .. now+14 days (server time) — nothing written.
45      public const string ScheduleOutOfWindow = "SCHEDULE_OUT_OF_WINDOW";
75          [ScheduleOutOfWindow] = (422, "Schedule out of window"), // US-022-03 (FR-022-2): well-formed but out of range
```

**`src/wa.application/UseCases/Admin/SearchTransfersQuery.cs`.** L42 and L46-48 are currently:
```csharp
42 public sealed class SearchTransfersQueryHandler(ISearchTransfersStore store) : IRequestHandler<SearchTransfersQuery, SearchTransfersResult>
47         var q = request.Q?.Trim();
48         if (string.IsNullOrEmpty(q)) q = null;
```
Target:
```csharp
public sealed class SearchTransfersQueryHandler(ISearchTransfersStore store, IFlagResolver flags) : IRequestHandler<SearchTransfersQuery, SearchTransfersResult>
...
        // (1) q. US-045-01 (FR-045-4): with feature.search on, q follows the FR-045-1 term rules and also matches file + sender names.
        var searchOn = await flags.ResolveFlag(FeatureGates.Search, FeatureGates.DefaultWhenAbsent, cancellationToken);
        string? q;
        if (searchOn)
            q = SearchTerm.Normalize(request.Q);
        else
        {
            q = request.Q?.Trim();                     // FR-011-3 legacy behaviour, unchanged when the gate is off
            if (string.IsNullOrEmpty(q)) q = null;
        }
```
Then the store call at L70 becomes `store.SearchAsync(q, searchOn, statuses, ...)`. Update the class doc comment step (1). `SearchTerm` comes from the existing `using wa.domain.Transfers;` (L2).

**`src/wa.infrastructure/Persistence/SearchTransfersStore.cs`.** Current L40:
```csharp
.Where(t => q == null || t.LinkId == q || _db.Set<EmailRecipient>().Any(r => r.TransferId == t.Id && r.Address.Contains(q)))
```
Target:
```csharp
// T-011-04 + US-045-01 (FR-045-4): LinkId exact OR recipient substring; with feature.search on also file name / sender substring (literal, CI collation).
.Where(t => q == null || t.LinkId == q
    || _db.Set<EmailRecipient>().Any(r => r.TransferId == t.Id && r.Address.Contains(q))
    || (matchFilesAndSender && (t.SenderName.Contains(q)
        || _db.Set<FileItem>().Any(f => f.TransferId == t.Id && f.OriginalName.Contains(q)))))
```
`FileItem` is in `wa.domain` (already imported, L3).

**`src/wa.api/Program.cs`.** Current L71, L1154-1164 and L1181 are shown in §2; L686-695 holds the two admin catches. Steps:
- After L71 add:
  ```csharp
  builder.Services.AddSingleton<wa.application.UseCases.Transfers.ITransferSearchStore>(sp => new wa.infrastructure.Persistence.TransferSearchStore(sp.GetRequiredService<WaDbContext>())); // US-045-01
  ```
- Insert after L1154:
  ```csharp
  // US-045-01 (FR-045-1/5): a present q goes to SearchMyTransfersQuery, which owns the feature.search gate and the search cursor.
  var rawQ = request.Query["q"].FirstOrDefault();
  ```
- L1156 becomes `if (rawQ is null && !string.IsNullOrEmpty(cursor))`.
- L1181 is replaced by:
  ```csharp
  ListMyTransfersResult result;
  try
  {
      result = rawQ is null
          ? await sender.Send(new ListMyTransfersQuery(userId, cursor, parsedLimit, ResolveStatusFilter(request)), ct)
          : await sender.Send(new SearchMyTransfersQuery(userId, rawQ, cursor, parsedLimit, ResolveStatusFilter(request)), ct);
  }
  catch (wa.domain.Transfers.CursorFormatException ex)
  {
      throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);    // TA-4.1.4: malformed / foreign search cursor
  }
  catch (wa.domain.Transfers.SearchTermTooLongException ex)
  {
      throw new BusinessException(ProblemCodes.QTooLong, ex.Message, ex);      // US-045-01 (EC-045-3)
  }
  ```
  The L1182-1199 projection stays as is: it reads `result.Items` / `result.NextCursor`.
- Admin: add after L695:
  ```csharp
  catch (wa.domain.Transfers.SearchTermTooLongException ex)
  {
      throw new BusinessException(ProblemCodes.QTooLong, ex.Message, ex);      // US-045-01 (EC-045-3): flag-on admin q over 100 chars
  }
  ```
Do not log q anywhere (D9).

**`src/wa.web/src/features/files/search.ts`:**
```ts
// US-045-01 (FR-045-1, EC-045-3, UI notes): My Files search helpers — pure, unit-tested.
export const SEARCH_DEBOUNCE_MS = 300
/** Mirrors the server's SearchTerm.MaxLength — the UI trims before submit so Q_TOO_LONG is a server-side backstop only. */
export const SEARCH_MAX_LENGTH = 100

export function normalizeQuery(raw: string): string {
  return raw.trim().slice(0, SEARCH_MAX_LENGTH)
}

export function buildListUrl(base: string, status: string, q: string, cursor: string | null): string {
  let url = `${base}/transfers?status=${status}`
  if (q !== '') url += `&q=${encodeURIComponent(q)}`
  if (cursor !== null) url += `&cursor=${encodeURIComponent(cursor)}`
  return url
}
```

**`src/wa.web/src/features/files/FilesPage.tsx`.** The full current source is in this repo (L1-374). Changes:
1. Import `SEARCH_DEBOUNCE_MS, SEARCH_MAX_LENGTH, buildListUrl, normalizeQuery` from `./search`.
2. After L85 add:
   ```tsx
   const searchOn = useFeatureGate('feature.search')
   const [query, setQuery] = useState('')
   const [committedQ, setCommittedQ] = useState('')
   const activeQ = searchOn ? committedQ : ''
   ```
   Next to `filterRef` (L114) add `const qRef = useRef('')`.
3. Add a debounce effect:
   ```tsx
   useEffect(() => {
     const next = normalizeQuery(query)
     if (next === committedQ) return
     const id = window.setTimeout(() => setCommittedQ(next), SEARCH_DEBOUNCE_MS)
     return () => window.clearTimeout(id)
   }, [query, committedQ])
   ```
4. Fetch effect (L127-146):
   - set `qRef.current = activeQ`;
   - fetch `buildListUrl(API_BASE, statusFilter, activeQ, null)`;
   - add `let cancelled = false`, have the then/catch/finally bodies return early when `cancelled`, and `return () => { cancelled = true }`;
   - deps `[statusFilter, activeQ]`.
5. `goToPage` (L163-164) builds `buildListUrl(API_BASE, filterRef.current, qRef.current, cursor)`.
6. Add `function clearSearch(): void { setQuery(''); setCommittedQ('') }`.
7. Between the header (L275-278) and the pills (L280), add:
   ```tsx
   {searchOn && (
     <div className="mf-search" role="search">
       <input type="search" className="mf-search-input" value={query} maxLength={SEARCH_MAX_LENGTH}
         aria-label={i18n.t('files.search.label')} placeholder={i18n.t('files.search.placeholder')}
         onChange={(e) => setQuery(e.target.value)} />
       {query !== '' && (
         <button type="button" className="mf-search-clear" aria-label={i18n.t('files.search.clear')} onClick={clearSearch}>×</button>
       )}
     </div>
   )}
   ```
8. The existing empty state (L357) gets the extra condition `&& activeQ === ''`. Add a sibling block:
   ```tsx
   {!loading && !error && rows.length === 0 && activeQ !== '' && (
     <div className="mf-empty">
       <p className="mf-empty-text">{i18n.t('files.search.noMatch', { query: activeQ })}</p>
       <button type="button" className="mf-pill" onClick={clearSearch}>{i18n.t('files.search.clear')}</button>
     </div>
   )}
   ```
9. Doc comment (L60-72) gains one sentence on the US-045-01 search box.

**`src/wa.web/src/features/files/FilesPage.test.tsx`.** Add this helper next to `flush` (L88-91):
```ts
function typeInto(input: HTMLInputElement, value: string): void {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
  setter.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}
```
Wrap `typeInto` and timer advances in `act(...)`. `afterEach` (L99-104) already restores real timers.

**`src/wa.web/src/index.css`.** Insert after the `.mf-pill-active` rule (L1183-1187):
```css
/* US-045-01 (FR-045-5): My Files search toolbar — rendered only when feature.search is on. */
.mf-search {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
  max-width: 480px;
}

.mf-search-input {
  flex: 1;
  min-width: 0;
  min-height: 44px;
  padding: 8px 12px;
  font-size: var(--fs-body);
  border-radius: var(--radius-sm);
  background: transparent;
  color: inherit;
}

.mf-search-clear {
  min-height: 44px;
  min-width: 44px;
  background: transparent;
  color: inherit;
  border-radius: var(--radius-sm);
  cursor: pointer;
}
```
Give both `.mf-search-input` and `.mf-search-clear` the identical `border:` declaration copied from `.mf-pill` (L1177). The tap-target gate needs a 44 px height and width on the clear button and a 16 px font on the input, and both are met.

**i18n.** Insert these four keys after the files.empty.cta line in each bundle. Keep `{{query}}` verbatim.
```json
en: "files.search.label": "Search transfers", "files.search.placeholder": "File name or email", "files.search.clear": "Clear search", "files.search.noMatch": "No transfers match '{{query}}'."
nl: "Overdrachten zoeken" | "Bestandsnaam of e-mail" | "Zoekopdracht wissen" | "Geen overdrachten gevonden voor '{{query}}'."
de: "Transfers durchsuchen" | "Dateiname oder E-Mail" | "Suche löschen" | "Keine Transfers passend zu '{{query}}'."
fr: "Rechercher des transferts" | "Nom de fichier ou e-mail" | "Effacer la recherche" | "Aucun transfert ne correspond à '{{query}}'."
es: "Buscar transferencias" | "Nombre de archivo o correo" | "Borrar búsqueda" | "Ninguna transferencia coincide con '{{query}}'."
pt: "Pesquisar transferências" | "Nome do arquivo ou e-mail" | "Limpar pesquisa" | "Nenhuma transferência corresponde a '{{query}}'."
it: "Cerca trasferimenti" | "Nome file o email" | "Cancella ricerca" | "Nessun trasferimento corrisponde a '{{query}}'."
tr: "Aktarımlarda ara" | "Dosya adı veya e-posta" | "Aramayı temizle" | "'{{query}}' ile eşleşen aktarım yok."
```
The non-en lines list the values in the en key order: label, placeholder, clear, noMatch. Interpolation runs with escapeValue false (`src/wa.web/src/core/i18n/init.ts:87`), and React escapes the text.

**`tests/wa.application.unit/Admin/SearchTransfersQueryTests.cs`.** The handler is built at L17. The stub `StubSearchTransfersStore` (L174-212) has its `SearchAsync` at L187: add `bool matchFilesAndSender` after `string? q` and capture it.

## 5. Open Questions / Risks

**Escalation (blocks this plan's Ready state):**
```
DECISION: D-21 — which search backend does US-045-01 ship: SQL full-text (CONTAINSTABLE on a SearchText column) or literal substring matching over the existing columns?
CONTEXT: D-21 is open/"you"; T-066 requires it confirmed before the index shape freezes. The stock mssql/server:2022 Linux image used by all ~50 integration classes and docker-compose has no Full-Text component, full-text population is asynchronous (EC-045-2 → flaky facts), and CONTAINS is word/prefix-based: it cannot satisfy "contains the match" substring semantics or AC-045-3 ("report *" literal — FTS drops '*' as punctuation).
OPTIONS:
  A) SQL full-text as specced — new SearchText column maintained on every create/re-send/file write path + migration with FULLTEXT CATALOG/INDEX guarded by FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'); custom FTS-enabled SQL image for Testcontainers, compose and CI (new infra artifact); ranking from CONTAINSTABLE; word-breaker semantics replace substring/literal ACs.
  B) Literal case-insensitive substring match (EF Contains → escaped LIKE) over FileItem.OriginalName / EmailRecipient.Address / Transfer.SenderName, owner-scoped, CASE rank file>recipient>sender — no schema change, no new image, every AC deterministic; not index-assisted (bounded by the owner predicate); endpoint contract identical, so FTS/Azure Search can replace the store later via ADR.
  C) B plus a persisted, app-maintained Transfer.SearchText column (migration + backfill) queried with LIKE — one column to scan instead of three EXISTS, but every write path must keep it in sync and it still needs no FTS.
RECOMMEND: B — satisfies FR-045-1/3/5/6/7 and all story ACs with zero infra/schema change; D-21's own fallback rule ("ADR, endpoint contract unchanged") applies equally to adding FTS later.
BLOCKS: US-045-01 (T-066)
```

**Other open points (no escalation needed; the orchestrator records them at close-out):**
- **Admin flag-off semantics (D7).** FR-045-5 says "q ignored" on both list endpoints. Admin q predates F-XCT-002 (FR-011-3), though, and ignoring it with the gate seeded off would regress admin search. This plan keeps the FR-011-3 behaviour when the gate is off and widens it when on. If the PO wants admin q truly ignored when off, only `SearchTransfersQueryHandler` step (1) changes.
- **Status pills + q combine (AND).** The spec is silent. Combining them is the cheaper reading and keeps the pills meaningful.
- **Spec text drift to fix in docs at close-out:**
  - FR-045-2 names "TransferFile.FileName"; the real column is `FileItem.OriginalName`.
  - The T-066 row and the spec Technical notes mention a SearchText computed column plus a full-text index; update them per the D-21 answer.
  - TA-4.2 row 14 gains `q`.
  - TA-4.1.3 and REPO-FACTS §1 gain Q_TOO_LONG.
  - Log D-21 in Open-Decisions Part 3.
- **EC-045-4 (diacritics).** With option B, matching follows the database collation (CI_AS: case-insensitive, accent-sensitive). "cafe" does not find "café". This is a documented limitation, as the spec allows.
- **EF translation risk.** The CASE-over-EXISTS projection is expected to translate. The fallback (three bools, then a second Select) is spelled out in §4. Client evaluation is forbidden.
- **Integration env vars are process-global.** `Wa__AdminEmails` is set by this class, the same precedent as AdminFlagsEndpointTests. The runner is single-threaded.
- **Client constant 100.** It mirrors `SearchTerm.MaxLength` and the server stays authoritative (Q_TOO_LONG). Drift between the two would only change whether the UI trims or the server answers 400.
