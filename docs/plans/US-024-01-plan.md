# US-024-01 — Fund the free tier with one ad (slot placement, Free seed, ad telemetry)

**Feature:** F-PRF-004 Ads (Free Tier) (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-004/US-024-01-ad-slot.md`
**Scope:** FR-024-1, FR-024-4, FR-024-5, FR-024-6, AC-024-1, AC-024-5, EC-024-1, EC-024-2, EC-024-6; UI-Reference §5.3; TA-8.4, TA-10.2, TA-13.2, TA-3.4.
**Task id note:** story id = task id (US-024-02/03/04 convention).
**Human decisions applied (2026-11-20):** D1 Free plan row ads ON by data migration (feature.ads kill-switch stays seeded false, D-14). D2 `ad_impression` + `ad_error` join the TA-10.2 closed list and are emitted from the placement.
**Out of scope:** the publisher-id config source (DECISION-1 below), the `ad_miss_rate` metric/alert (TA-10.3, ops), Playwright E2E (feature test plan — needs a real publisher id), the privacy-page copy (FR-024-5 consent note).

## 1. Goal

A recipient of a free transfer (ads enabled) sees exactly one ad slot below the file list and above the "Send something" panel, never on the password screen, with impression/error telemetry carrying no PII; the Free plan row ships ads:true. **Exit check:** new + updated vitest files green; `npm run lint && npm run test:run && npm run build` green in src/wa.web; PII guard + selftest clean; wa.domain.unit + wa.application.unit green; the five integration classes named in step 2 green (filtered run).

## 2. Design & Decisions

### Already in place (grounded — do not rebuild)
| Piece | Where |
|---|---|
| adsEnabled on the public transfer wire (flag AND sender's current plan, EC-024-6) | `src/wa.api/Program.cs:427`; `src/wa.application/UseCases/Transfers/RecipientAds.cs:9` |
| AdSlot port + state machine + 60 s collapse + isolation CSS | `src/wa.web/src/features/recipient/ads/AdSlot.tsx:15`, `src/wa.web/src/features/recipient/ads/useAdSlot.ts:20`, `src/wa.web/src/features/recipient/ads/adSlot.css` |
| De-duped defer/async script injector | `src/wa.web/src/features/recipient/ads/loadAdScript.ts:4` |
| Password gate returns early — nothing after it renders | `src/wa.web/src/features/recipient/RecipientPage.tsx:210` |
| Growth panel (conditional, after full download) | `src/wa.web/src/features/recipient/RecipientPage.tsx:333` |
| Plan features = row AND global gate; limits-prefixed ads keys ignored | `src/wa.application/Limits/FeaturesProvider.cs:30` |

### D1 — Free seed: change HasData, let EF generate the data migration (TA-2.3, golden rule 4)
Precedent `src/wa.infrastructure/Persistence/Migrations/20261119084457_AddMaxLogoSizeLimit.cs` (HasData edit → generated UpdateData on Plan + FeatureFlag rows, old migrations untouched). Edit `src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs:31` (Free FeaturesJson ads false→true) and, for consistency with the "values per Open-Decisions Part 2" description, `src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs:53` (limits.free.ads "false"→"true"; FeaturesProvider ignores limits-prefixed feature keys, so this row is informational only). Then run `dotnet ef migrations add AddFreeAdsEnabled` with the project/startup-project pair from AGENT §5.3. Expected generated Up(): exactly two UpdateData calls (Plan Id 11111111-0000-4000-8000-000000000001 column FeaturesJson; FeatureFlag Key limits.free.ads column Value); Down() reverts both. Snapshot regenerates. Pro/Business untouched. feature.ads stays "false" — no ad anywhere until an operator flips it (AC-024-4 unchanged).

### D2 — Telemetry names (TA-10.2, golden rule 6)
- Server closed set: add AdImpression = "ad_impression", AdError = "ad_error" to `src/wa.domain/Telemetry/TelemetryEvents.cs` (Events array too). 24→26 events, 31→33 names. Not emitted server-side (client-only events, like growth CTA in TA-10.2), but the closed contract and REPO-FACTS stay one list as the human asked.
- Client closed list: append both to the client closed list at `src/wa.web/src/core/telemetry/clientTelemetry.ts:16`; fix the header comment ("exactly the four names").
- PII guard: add slotId to the client prop allowlist at `scripts/check-telemetry-pii.mjs:61`. reason is already allowed. **plan is NOT added** — the client never receives the plan code (the DTO carries only adsEnabled), so there is nothing truthful to send; see Q1.
- Emit (new `adTelemetry.ts`): impression → trackClientEvent('ad_impression', { slotId }); error → trackClientEvent('ad_error', { slotId, reason }). Object literals so guard rule R5 can see the keys. slotId is the constant 'recipient-leaderboard' — never the linkId (FR-024-5).

### D3 — Config + loader: fail closed, one seam for DECISION-1
- New `adConfig.ts`: `interface AdConfig { client: string; unit: string }`, pure `parseAdConfig(client?: string, unit?: string): AdConfig | null` (client must match /^ca-pub-\d{10,20}$/, unit /^\d{6,20}$/, else null), `getAdConfig(): AdConfig | null`, `setAdConfigForTest(v: AdConfig | null | undefined)` (precedent setAiKeyForTest, `src/wa.web/src/core/telemetry/clientTelemetry.ts:51`). Until DECISION-1 is answered, getAdConfig returns the test override if set, else **null** — with a one-line comment naming the pending decision. Once decided, only this function changes (e.g. `parseAdConfig(env.X, env.Y)`).
- Null config ⇒ the placement renders **nothing** (no slot in DOM, no script, no 100 px reserve, no ad_error noise). Stronger than "collapsed" and still invisible; reviewers should read it as the fail-closed path.
- New `adsenseLoader.ts`: `createAdSenseLoader(config: AdConfig): AdLoader`. Returned loader: await loadAdScript('https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=' + encodeURIComponent(config.client)); create an ins element (class adsbygoogle, inline style display:block plus width/height 100%), data-ad-client = client, data-ad-slot = unit, data-ad-format absent (fixed leaderboard), **data-page-url = window.location.origin + '/'** (FR-024-5: AdSense must not see /t/{linkId}; see DECISION-2); append to container; push an empty object onto the window adsbygoogle queue (cast through `unknown`, no global d.ts); then resolve from the ins element's data-ad-status attribute via a MutationObserver: 'filled' → 'filled', 'unfilled' → 'empty' (check once synchronously first; disconnect on settle). A rejected script load propagates (hook maps it to script_error); never settling is covered by the hook's 60 s timeout.
- No new package (TA-17), no new env var in this task (AGENT §7).

### D4 — Placement (FR-024-4, AC-024-1, AC-024-5)
- Store: `RecipientStore.ts` gets adsEnabled: boolean (state interface, loading shape false, body type `adsEnabled?: boolean`, set `adsEnabled: body.adsEnabled === true`). Terminal/reset paths spread the loading shape → false automatically.
- New `RecipientAdPlacement.tsx` (in ads/, TA-8.2 self-contained): reads getAdConfig() once (useState initializer), returns null when absent; otherwise memoizes createAdSenseLoader(config) in the same initializer and renders `<AdSlot slotId={RECIPIENT_AD_SLOT_ID} load={loader} label={i18n.t('recipient.active.adLabel')} onOutcome={trackAdOutcome} />`. No CTA, no link, no "remove ads" copy (FR-024-6).
- `RecipientPage.tsx`: select adsEnabled; render `{adsEnabled && <RecipientAdPlacement />}` in the active (ungated) branch **after** the downloads-left line and **before** the growth panel. The gate branch returns at line 210 → no slot on the password screen by construction; after unlock, load() re-queries with ?t= and the active branch renders the slot (AC-024-5). Pro/Business: adsEnabled false → no element (AC-024-2 client half). Plan follows the sender at render time (EC-024-6) because adsEnabled is per request.
- TA-8.4: AdSlot starts idle (display none) and starts the loader one frame after mount, i.e. after the list painted; script is async/defer.

### D5 — "Ad" label (UI-Reference §5.3, transparency)
AdSlot gains optional `label?: string`; rendered as a span with class rp-ad-label **only while state is filled** (collapsed/loading stay text-free, so the existing silence tests hold). CSS in `adSlot.css`: position absolute, top 0, left 0, z-index 1, padding 0 4px, font-size var(--fs-tiny), line-height 1.4, color = the fg-muted token and background = the bg token from tokens.css, pointer-events none. Key recipient.active.adLabel in all 8 locales (en "Ad", nl "Advertentie", de "Anzeige", fr "Publicité", es "Anuncio", pt "Anúncio", it "Annuncio", tr "Reklam").

## 3. Implementation Plan

1. **Seed** — edit the two HasData lines (D1); run the EF add command from AGENT §5.3 with name AddFreeAdsEnabled; open the generated Up()/Down() and confirm exactly the two UpdateData calls (anything else = model drift → stop and report). Verify: `dotnet build src/wa.infrastructure`.
2. **Integration facts** (compile per task; run filtered by class: SeedDataIntegrationTest, PlanCatalogIntegrationTests, PublicTransferAdsTests, PublicTransferEndpointTests, AdsFlagToggleTests — Docker required):
   - `tests/wa.api.integration/SeedDataIntegrationTest.cs:69` → Free FeaturesJson ads:true (proves the migration on a fresh DB, AC-052-2 rule "fix the seed and this test together").
   - `tests/wa.api.integration/PlanCatalogIntegrationTests.cs:243` → with every gate on, Free resolves `new PlanFeatures { Ads = true }`; rename the fact to GatesOn_ProRowDrives_FreeOnlyAds_RowAndGateEditsApplyWithoutDeploy. Line 228 (gates off → all off) unchanged.
   - Remove the Free-row TEST DATA patches (now redundant — the seed carries ads:true) and their stale "seed carries false" comments: `tests/wa.api.integration/PublicTransferAdsTests.cs:50` (+ summary lines 14-15), `tests/wa.api.integration/PublicTransferEndpointTests.cs:46`, `tests/wa.api.integration/AdsFlagToggleTests.cs:74` (+ summary line 23). The story's plan × flag matrix then runs on real seed data: Free×on true (PublicTransferAdsTests FreeOwner/Guest), Pro/Business×on false, Free×off false (PublicTransferEndpointTests:107, AdsFlagToggleTests FlagOff).
3. **Server telemetry set** — TelemetryEvents.cs (D2; update the summary count and the AllNames comment to 33). Tests: `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs` expected set +2 names, count 31→33, rename the count fact to ConstMembers_AreExactlyTwentySixEventsPlusSevenMetrics, add both names to the IsEvent_TrueForClosedSetEvents theory; `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs:115` 31→33 (+ comment). Verify: `dotnet test tests/wa.domain.unit` and `dotnet test tests/wa.application.unit`.
4. **Client telemetry** — clientTelemetry.ts list + comment; check-telemetry-pii.mjs allowlist + slotId. CREATE `adTelemetry.ts` + `adTelemetry.test.ts`: impression → one clientTelemetryLog entry {event 'ad_impression', props {slotId}}; error → {event 'ad_error', props {slotId, reason}}; props keys exactly those (no linkId/plan/url); setIsDevForTest(true) and assert no throw (names are in the closed list). Verify: `npx vitest run src/features/recipient/ads/adTelemetry.test.ts`; `node ../../scripts/check-telemetry-pii.mjs && node ../../scripts/check-telemetry-pii.mjs selftest`.
5. **Config** — CREATE `adConfig.ts` + `adConfig.test.ts`: parseAdConfig valid pair → object; missing/blank/malformed client or unit → null; getAdConfig() with no override → null (fail closed, DECISION-1 pending); override round-trip; afterEach resets override to undefined.
6. **Loader** — CREATE `adsenseLoader.ts` + `adsenseLoader.test.ts` (jsdom; dispatch script load events like loadAdScript.test.ts; afterEach resetAdScriptsForTest + delete the window queue): (a) appends one script whose src is the AdSense URL with client param only; (b) after load, container holds one ins.adsbygoogle with data-ad-client/data-ad-slot from config and data-page-url = location origin + '/'; queue length 1; (c) FR-024-5: with location at /t/ab3def5g, neither script src nor any ins attribute contains 'ab3def5g'; (d) setting data-ad-status='filled' resolves 'filled', 'unfilled' resolves 'empty'; (e) script error event → rejects.
7. **Label** — MODIFY AdSlot.tsx (label prop, D5) + adSlot.css; extend `AdSlot.test.tsx`: with label, filled → one .rp-ad-label with the text; loading and collapsed → no label, textContent '' (silence holds); without label filled → textContent ''. Extend `adSlotCss.test.ts`: .rp-ad-label has position absolute, pointer-events none, font-size var(--fs-tiny).
8. **Store + placement + page** — MODIFY RecipientStore.ts, 8 locale files, RecipientPage.tsx; CREATE `RecipientAdPlacement.tsx` and `RecipientPage.ads.test.tsx` (own helpers copied from RecipientPage.test.tsx: renderApp, stubFetch, activeBody + adsEnabled; setAdConfigForTest({ client: 'ca-pub-1234567890123456', unit: '1234567890' }) in beforeEach, reset + resetAdScriptsForTest in afterEach). Cases:
   - AC-024-1: adsEnabled true → exactly one .rp-ad; `.rp-list` compareDocumentPosition(ad) has FOLLOWING; with growthPanelVisible set true via setState, ad precedes .rp-growth; slot contains no anchor/button and no text before fill (FR-024-6).
   - AC-024-2 client half: adsEnabled false, and adsEnabled absent → zero .rp-ad, no script[data-ad-loader].
   - Fail closed: adsEnabled true + setAdConfigForTest(null) → zero .rp-ad, no script appended.
   - AC-024-5: dispatching fetch (gated without ?t=, ungated with ?t=tok123 — copy stubDispatchingFetch from RecipientPage.test.tsx:531) with adsEnabled true on both → at the gate zero .rp-ad and no ad script; type password + submit; after the ungated re-query exactly one .rp-ad below .rp-list.
   - EC-024-2 smoke: with the slot loading, clicking a row's .rp-download still calls endpoint 6 (stub records the URL).
   Verify: `npx vitest run src/features/recipient` then full web gate (§4 AGENT): `npm run lint && npm run test:run && npm run build`, theme/contrast scripts not needed (no token change).
9. **Docs** (same commit — REPO-FACTS rule §5.1): TA-10.2 events line in `docs/03-technical-architecture.md` (+ "`ad_impression`, `ad_error` (F-PRF-004, client-only; props slotId, reason)"); `docs/REPO-FACTS.md` telemetry header 24→26 events / 31→33 names + list; `docs/Open-Decisions-and-Constants.md` Part 2 ADS row Free = "true (global feature.ads kill-switch false, D-14)", and append one Part 3 row at the end of the table: "2026-11-20 | F-PRF-004 Free ads + ad telemetry | Free FeaturesJson ads:true via migration AddFreeAdsEnabled (feature.ads stays false); ad_impression/ad_error added to TA-10.2 (props slotId, reason) | US-024-01 architect decisions D1/D2 | you". Then one `feat: US-024-01 …` commit with exactly the table's files.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs` | MODIFY | Line 31 Free FeaturesJson ads:true (D1) |
| `src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs` | MODIFY | Line 53 limits.free.ads → "true" (D1) |
| `src/wa.infrastructure/Persistence/Migrations/{ts}_AddFreeAdsEnabled.cs` | CREATE | Generated by dotnet ef — two UpdateData up, two down |
| `src/wa.infrastructure/Persistence/Migrations/{ts}_AddFreeAdsEnabled.Designer.cs` | CREATE | Generated |
| `src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs` | MODIFY | Regenerated by dotnet ef (never hand-edited) |
| `src/wa.domain/Telemetry/TelemetryEvents.cs` | MODIFY | + AdImpression, AdError consts + Events array; counts in comments (D2) |
| `tests/wa.domain.unit/Telemetry/TelemetryEventsTests.cs` | MODIFY | Expected set +2, count 33, rename, IsEvent theory rows |
| `tests/wa.application.unit/Telemetry/WaTelemetryReporterTests.cs` | MODIFY | Line 115 count 31→33 |
| `src/wa.web/src/core/telemetry/clientTelemetry.ts` | MODIFY | Closed list + 2 names; header comment |
| `scripts/check-telemetry-pii.mjs` | MODIFY | Client prop allowlist + slotId |
| `src/wa.web/src/features/recipient/ads/adTelemetry.ts` | CREATE | trackAdOutcome (D2) |
| `src/wa.web/src/features/recipient/ads/adTelemetry.test.ts` | CREATE | Step 4 |
| `src/wa.web/src/features/recipient/ads/adConfig.ts` | CREATE | parseAdConfig / getAdConfig (null until DECISION-1) / setAdConfigForTest (D3) |
| `src/wa.web/src/features/recipient/ads/adConfig.test.ts` | CREATE | Step 5 |
| `src/wa.web/src/features/recipient/ads/adsenseLoader.ts` | CREATE | createAdSenseLoader (D3) |
| `src/wa.web/src/features/recipient/ads/adsenseLoader.test.ts` | CREATE | Step 6 incl. FR-024-5 no-linkId |
| `src/wa.web/src/features/recipient/ads/AdSlot.tsx` | MODIFY | Optional label, filled-only (D5) |
| `src/wa.web/src/features/recipient/ads/adSlot.css` | MODIFY | .rp-ad-label rules (D5) |
| `src/wa.web/src/features/recipient/ads/AdSlot.test.tsx` | MODIFY | Label cases (step 7) |
| `src/wa.web/src/features/recipient/ads/adSlotCss.test.ts` | MODIFY | Label CSS contract |
| `src/wa.web/src/features/recipient/ads/RecipientAdPlacement.tsx` | CREATE | Config gate + loader + AdSlot + telemetry (D4) |
| `src/wa.web/src/features/recipient/RecipientStore.ts` | MODIFY | adsEnabled state/shape/body/set (D4) |
| `src/wa.web/src/features/recipient/RecipientPage.tsx` | MODIFY | Select adsEnabled; placement between downloads-left and growth panel |
| `src/wa.web/src/features/recipient/RecipientPage.ads.test.tsx` | CREATE | Step 8 (AC-024-1, AC-024-2 client, AC-024-5, fail closed, EC-024-2) |
| `src/wa.web/src/core/i18n/locales/en.json` | MODIFY | + recipient.active.adLabel |
| `src/wa.web/src/core/i18n/locales/nl.json` | MODIFY | same key |
| `src/wa.web/src/core/i18n/locales/de.json` | MODIFY | same key |
| `src/wa.web/src/core/i18n/locales/fr.json` | MODIFY | same key |
| `src/wa.web/src/core/i18n/locales/es.json` | MODIFY | same key |
| `src/wa.web/src/core/i18n/locales/pt.json` | MODIFY | same key |
| `src/wa.web/src/core/i18n/locales/it.json` | MODIFY | same key |
| `src/wa.web/src/core/i18n/locales/tr.json` | MODIFY | same key |
| `tests/wa.api.integration/SeedDataIntegrationTest.cs` | MODIFY | Line 69 Free ads:true |
| `tests/wa.api.integration/PlanCatalogIntegrationTests.cs` | MODIFY | Line 243 Free Ads=true under gates on; rename fact |
| `tests/wa.api.integration/PublicTransferAdsTests.cs` | MODIFY | Drop Free-row patch (50-52) + stale summary |
| `tests/wa.api.integration/PublicTransferEndpointTests.cs` | MODIFY | Drop Free-row patch (46-48) |
| `tests/wa.api.integration/AdsFlagToggleTests.cs` | MODIFY | Drop Free-row patch (74-76) + stale summary line 23 |
| `docs/03-technical-architecture.md` | MODIFY | TA-10.2 events + 2 names |
| `docs/REPO-FACTS.md` | MODIFY | Telemetry counts/list |
| `docs/Open-Decisions-and-Constants.md` | MODIFY | Part 2 ADS Free; Part 3 append row |

#### Existing code the Coder changes

`src/wa.web/src/features/recipient/RecipientPage.tsx:327-338` — insert the placement between these two blocks:
```tsx
      {downloadsLeft !== null && downloadsLeft > 0 && (
        <p className="rp-downloads-left">{i18n.t('recipient.active.downloadsLeft', { count: downloadsLeft })}</p>
      )}
      {/* US-024-01 (FR-024-4): one slot below the list, above the growth panel; the gate branch above never reaches here (AC-024-5). */}
      {adsEnabled && <RecipientAdPlacement />}
      {growthPanelVisible && (
```
Add `const adsEnabled = useRecipientStore((s) => s.adsEnabled)` next to line 57 (passwordRequired) and `import { RecipientAdPlacement } from './ads/RecipientAdPlacement'`.

`src/wa.web/src/features/recipient/RecipientStore.ts` — four touch points: interface field after scheduledSendAt (line 149, doc comment "F-PRF-004 (FR-024-3): server-resolved; false on every non-active shape"); loading shape line 213 add `adsEnabled: false,`; body type line 285 add `adsEnabled?: boolean`; set() line 297 add `adsEnabled: body.adsEnabled === true,`.

`src/wa.web/src/features/recipient/ads/AdSlot.tsx:8-26` — props gain `label?: string`; inside the root div, before the unit: `{state === 'filled' && label ? <span className="rp-ad-label">{label}</span> : null}`. Keep `.rp-ad-unit` as the loader's container (the label must stay outside what the provider fills).

`src/wa.web/src/core/telemetry/clientTelemetry.ts:16-22` — append `'ad_impression', 'ad_error'` after 'translation_missing' with a comment `// US-024-01 (F-PRF-004, TA-10.2): ad slot outcomes — props slotId, reason only`.

`scripts/check-telemetry-pii.mjs:61` — the Set becomes [..., 'source', 'slotId'].

`src/wa.domain/Telemetry/TelemetryEvents.cs:34` — after BrandingLogoMissing:
```csharp
    public const string AdImpression = "ad_impression"; // US-024-01 (F-PRF-004): client-only, props slotId
    public const string AdError = "ad_error";           // US-024-01 (F-PRF-004): client-only, props slotId + reason
```
and append `AdImpression, AdError` to the Events array (line 51).

Context: `src/features/recipient/**` is in the no-hardcoded-JSX lint scope (`src/wa.web/eslint.config.js:17`) — the label comes from i18n, the placement renders no literal text. RecipientPage.test.tsx bodies omit adsEnabled → false → that 1500-line suite is unaffected; do not edit it. The ads CSS is imported by AdSlot.tsx (not main.tsx), so it ships with the page now.

## 5. Open Questions / Risks

**DECISION-1 (escalated — blocks only real ads in production; code ships fail-closed):**
```
DECISION: Where does the AdSense publisher id (and the leaderboard ad-unit id) come from?
CONTEXT: The loader needs ca-pub-… and a data-ad-slot unit id; both are new config (AGENT §7: new env var/config key needs the human). Until answered, getAdConfig() returns null and no slot or script ever renders.
OPTIONS:
  A) Vite build-time client vars (VITE_WA_ADSENSE_CLIENT, VITE_WA_ADSENSE_SLOT) — precedent VITE_WA_AI_KEY (ADR 2026-11-06); public values anyway; per-env via build pipeline; no API change.
  B) Server config Wa:Ads:PublisherId / Wa:Ads:SlotId returned on the public transfer DTO — runtime-switchable without rebuild, but widens the TA-4.2#4 wire shape and adds Key Vault/app-setting entries.
RECOMMEND: A — the ids are public in the page anyway; the kill-switch (feature.ads) already gives runtime on/off, so build-time ids lose nothing.
BLOCKS: none for US-024-01 (fail-closed path); blocks the Playwright E2E and enabling ads in staging/prod.
```

**DECISION-2 (escalated — PII/security-shaped, FR-024-5):**
```
DECISION: Is data-page-url = site origin an acceptable FR-024-5 mitigation, given AdSense reads the page URL (/t/{linkId}) itself?
CONTEXT: Our request params carry no linkId (tested), but the AdSense script can read document location and send it as a url parameter; a linkId is a bearer secret for non-password transfers. The browser default referrer policy already strips the path from cross-origin requests.
OPTIONS:
  A) Ship data-page-url = origin + verify in staging (network log of the ad request shows no linkId) before flipping feature.ads — no code beyond this plan.
  B) Additionally render the ad in a same-origin sandboxed iframe served from a fixed path (e.g. /ad-frame.html) so the provider never sees /t/{linkId} — strongest, but a new static page + CSP review.
RECOMMEND: A now, B only if staging shows the linkId in the ad request.
BLOCKS: none for US-024-01 (feature.ads seeded false); blocks enabling feature.ads in prod.
```

- **Q1 — plan dimension on ad events.** The feature tech notes say "slot id + plan"; the client has no plan code (DTO carries adsEnabled only) and adding one would widen the public wire. Today adsEnabled ⇒ Free by construction (D1), so plan is implied. Left out of CLIENT_PROPS; add later only with a DTO decision.
- **R1 — migration drift.** If `dotnet ef migrations add` emits anything beyond the two UpdateData calls, the model snapshot is out of sync with an earlier migration — stop and report rather than commit it.
- **R2 — MutationObserver lifetime.** A never-reporting ad leaves the observer attached until unmount (the hook ignores the late settle). Acceptable: one observer per page view, disconnected on settle.
- **R3 — fail-closed hides the feature.** With DECISION-1 open, production renders no slot even with feature.ads on; this is intended and covered by the fail-closed test.
- **R4 — AC-024-3 page-level and E2E** (route.abort the ad host) stay with the feature test plan once DECISION-1 provides a real id.
