# US-024-04 — Toggle ads without a deploy (feature.ads master switch)

**Feature:** F-PRF-004 Ads (Free Tier) (Phase 1-Pro) · **Story:** `docs/features/Phase 1-Pro/F-PRF-004/US-024-04-ads-flag.md`
**Scope:** FR-024-3 (global feature.ads gate AND plan `Features.ads`), AC-024-4 (flag off → no ads on any plan), EC-024-4 (per-request resolution, ≤ 30 s cache), F-TRF-011 flag editor validation + FR-011-5 audit (admin.action, TA-5.3).
**Task id note:** story id = task id (US-022-01 convention).
**Nature of this task: TEST-ONLY.** Every production behaviour the story asks for already exists (see §2). There is nothing to build in `src/`; FILES FOR CODER lists one new integration test class that proves the end-to-end chain (admin PUT → audit + admin.action → cache TTL → `adsEnabled` flips on the recipient endpoint).
**Out of scope:** the ad slot component (US-024-01), failure collapse (US-024-03), any `src/` change, any seed/migration change (Free row `ads:false`, see Q1), any wa.web change (flag editor UX already shipped and tested).

## 1. Goal

Prove, against the real host, that an operator flipping feature.ads through `PUT /api/v1/admin/flags/{key}` changes `adsEnabled` on `GET /api/v1/public/transfers/{linkId}` for free senders only, after the 30 s flag cache, with the change audit-logged — and that an invalid value is rejected with the old value staying live. **Exit check:** new `AdsFlagToggleTests` green with the class filter FullyQualifiedName~AdsFlagToggleTests, plus the AGENT.md §4 per-task gate (domain/application/workers unit) green; `src/` diff empty.

## 2. Design & Decisions

### Already implemented (grounded — no change)
| Story element | Where | Evidence |
|---|---|---|
| feature.ads seeded `"false"` at launch (D-14, AC-024-4) | `src/wa.infrastructure/Persistence/Migrations/20261011215315_SeedPlansAndFeatureFlags.cs:21` | seed row |
| Gate constant + fail-closed default | `src/wa.application/Limits/FeatureGates.cs:12`, `:20` | `Ads`, `DefaultWhenAbsent = false` |
| Per-request `adsEnabled` = gate AND sender's current plan `Features.ads` (FR-024-3, EC-024-4/6) | `src/wa.application/UseCases/Transfers/RecipientAds.cs:9-21` | gate read first (`:13`), plan row decides (`:21`) |
| Handler wiring (Active branch only) | `src/wa.application/UseCases/Transfers/GetPublicTransferQuery.cs:37`, `:125`, `:139` | `AdsEnabled` result field |
| Wire field | `src/wa.api/Program.cs:427` | `adsEnabled = result.AdsEnabled` |
| 30 s flag cache, no invalidation on write (TA-3.4) | `src/wa.api/Program.cs:67`, `:91`; `src/wa.application/Limits/Caches.cs:33-46` | `Wa:Flags:TtlSeconds` default 30 |
| Admin flag write — SuperAdmin-only, raw body, typed validation | `src/wa.api/Program.cs:868-897`; `src/wa.application/UseCases/Admin/SetFlagCommand.cs:55-71` | unknown → FLAG_UNKNOWN, invalid → FLAG_INVALID, no write |
| Boolean rule for feature.* keys (exactly `true`/`false`) | `src/wa.application/Limits/FlagValueRules.cs:54-59` | non-limit keys |
| Audit on successful save only (FR-011-5) | `src/wa.api/Program.cs:882-887` | action flag.set, entityType `flag`, details `{oldValue,newValue}` |
| One AuditLog row + one admin.action event (TA-5.3) | `src/wa.application/UseCases/Admin/AuditWriter.cs:38-73` | `EventType.AdminAction` at `:66`, payload entityId at `:58` |
| Flag editor UI: inline FLAG_INVALID, old value stays, "Changes are logged." (UI-Reference §5.6) | `src/wa.web/src/features/admin/AdminFlagsPage.tsx:258`; tests `src/wa.web/src/features/admin/AdminFlagsPage.test.tsx:162`, `:410` | EC-011-4 / EC-044-1 |

### Existing test coverage (pieces, not the chain)
- `tests/wa.application.unit/Transfers/RecipientAdsTests.cs` — flag × plan matrix against stubs (US-024-02).
- `tests/wa.api.integration/PublicTransferAdsTests.cs:44-53` — flag flipped in the DB **before** the host builds; never exercises the admin PUT or the cache TTL.
- `tests/wa.api.integration/PublicTransferEndpointTests.cs:107` — seeded false → `adsEnabled` false (free/guest only).
- `tests/wa.api.integration/AdminFlagsEndpointTests.cs:84-170` — PUT + audit + TTL, but for a `limits.*` key resolved through `ILimitsProvider`, not feature.ads through the public endpoint; no admin.action event assertion.
- `tests/wa.application.unit/Admin/AuditWriterTests.cs:50` — admin.action envelope shape (unit).
- `tests/wa.application.unit/Limits/FlagValueRulesTests.cs:41-43` — feature.ads rejects `True`, `1`, `"true"`.

### Gap
No test proves the story's operator chain end-to-end: admin PUT feature.ads → `adsEnabled` changes on the next render **after** the TTL (stale within it), Pro unaffected, audit row + exactly one admin.action envelope for feature.ads, kill-switch back to `false` collapses, and a bad value leaves `adsEnabled` and the stored value unchanged with no audit. That is the whole task.

### D-1 — One integration class, real host, real TTL (TA-3.4, TA-4.2#4, TA-4.2#23)
CREATE `tests/wa.api.integration/AdsFlagToggleTests.cs`, a merge of two existing idioms:
- Host/admin setup + forged TA-9.1 cookie + `SendAsync` helper verbatim from `AdminFlagsEndpointTests` (`:36-73`, `:182-196`) — `Wa__AdminEmails` set before host build (pitfalls: role derives per request).
- Owner/transfer seeding + `GetAsync` from `PublicTransferAdsTests` (`:110-157`).
- Free plan row patched to `"ads":true` in `InitializeAsync` as TEST DATA (same as `PublicTransferAdsTests.cs:52-53`); feature.ads left at its seed (`false`) unless a fact needs `true` before the first request.
- `TestFactory : WebApplicationFactory<Program>` overriding `ConfigureWebHost` → `services.RemoveAll<IEventPublisher>(); services.AddSingleton<IEventPublisher>(Publisher);` with a private `RecordingPublisher` (copy `tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs:151-160`; override idiom `tests/wa.api.integration/StripeWebhookEndpointTests.cs:364-377`). This also neutralises any process-wide `Wa__ServiceBus__ConnectionString` left by another class (`AdminFlagsEndpointTests.cs:50`).
- **Real 31 s wait** (`await Task.Delay(31_000)`), precedent `AdminFlagsEndpointTests.cs:166-167` ("do not shorten"). Rejected: setting `Wa__Flags__TtlSeconds` low — `Environment.SetEnvironmentVariable` is process-wide and would leak into every later test class's host, and the AC names the real 30 s cache.
- No new ProblemCode, telemetry name, package, migration or `src/` edit. Audit action/entity strings are asserted as literals copied from `src/wa.api/Program.cs:886` ("flag.set", `"flag"`), same as `AdminFlagsEndpointTests.cs:98`.

### Facts (each one encodes an AC/EC)
1. `FlagOff_NoAdsOnAnyPlan` — AC-024-4 / story AC 1: seeded false; free owner, Pro owner, Business owner, guest → all `adsEnabled == false`.
2. `OperatorSetsFlagTrue_AfterTtl_FreeShowsAds_ProStillNone_AuditLogged` — story AC 2 + 3 / EC-024-4:
   a. GET free transfer → false (warms FlagsCache).
   b. PUT feature.ads body `true` → 200, body `{key:"feature.ads", value:"true"}`.
   c. Immediately GET free → still false (cached; "resolved per request, ≤ 30 s").
   d. AuditLog: exactly one flag.set row, EntityId feature.ads, actor = admin email, details oldValue `false` / newValue `true`.
   e. `Publisher.Published`: exactly one envelope with `Type == EventType.AdminAction`; `PartitionKey` = feature.ads (`AuditWriter.cs:68`).
   f. `Task.Delay(31_000)`; GET free → true; GET Pro → false (AC-024-2 holds under the master switch).
3. `KillSwitch_SetFalse_AfterTtl_FreeAdsCollapse` — EC-024-4 kill switch: set feature.ads `true` in the DB **before** the first request (same as `PublicTransferAdsTests.cs:46-49`, but in the fact body before `CreateClient`/first GET — see Step 1 note); GET free → true; PUT `false` → 200; wait 31 s; GET free → false.
4. `InvalidValue_Rejected_OldValueStays_NoAudit_NoEvent_AdsUnchanged` — story EC "bad JSON rejected, old value stays" (server half; UI half already at `AdminFlagsPage.test.tsx:162`): `[Theory]` bodies `yes`, `True`, `{"on":true}` → 400 `code == "FLAG_INVALID"`; stored feature.ads still `false`; zero flag.set rows; zero `AdminAction` envelopes; GET free → false.

## 3. Implementation Plan

1. **CREATE** `tests/wa.api.integration/AdsFlagToggleTests.cs` (D-1).
   - `InitializeAsync`: Azurite + MsSql containers, env vars exactly as `AdminFlagsEndpointTests.cs:43-50` minus the ServiceBus line (publisher is replaced in DI anyway — keep or drop, either is safe), `MigrateAsync`, seed the admin `AppUser` on the free plan (`:58-68`), patch the free row FeaturesJson (`PublicTransferAdsTests.cs:52-53`). Do **not** create the factory/client here — fact 3 must change the DB before the host's first flag read. Expose a private `EnsureClient()` that lazily builds `_factory`/`_client`; facts 1, 2, 4 call it first. (The cache fills on first read, not at host build, so building the client early would also be safe; lazy keeps the intent explicit.)
   - `DisposeAsync`: dispose client/factory only if created, then db, sql, azurite.
   - Helpers: `SendAsync` (cookie forge, copy `AdminFlagsEndpointTests.cs:182-196`), `GetAdsEnabledAsync(linkId)` (200 assert + `adsEnabled` bool, from `PublicTransferAdsTests.cs:110-116`), `SeedOwnerAsync(planCode)` / `SeedTransferAsync(ownerId, linkId)` (`PublicTransferAdsTests.cs:118-157`), `StoredFlagAsync()`. Use distinct 8-char linkIds per transfer.
   - The four facts of §2. Use `FeatureGates.Ads` (`using wa.application.Limits;`) for the key in URLs/queries rather than a fresh literal; `EventType.AdminAction` from `wa.domain`.
   - Verify: dotnet test tests/wa.api.integration with filter FullyQualifiedName~AdsFlagToggleTests (Docker running; ~2–3 min incl. two 31 s waits).
2. **Gate** — `dotnet test tests/wa.domain.unit`, `dotnet test tests/wa.application.unit`, `dotnet test tests/wa.workers.unit`; `dotnet build tests/wa.api.integration`. No wa.web or `src/` change → npm gates and the node token/contrast/PII scripts are unaffected (run them only if the orchestrator's gate requires a full §4 pass).
3. **Confirm empty src diff**: git diff (stat) HEAD over src must print nothing.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `tests/wa.api.integration/AdsFlagToggleTests.cs` | CREATE | New Testcontainers class: admin PUT feature.ads → audit row + one admin.action envelope → after real 31 s TTL `adsEnabled` flips for free only; kill switch collapses; invalid value 400 FLAG_INVALID with no write/audit/event. Four facts per §2. |

No MODIFY files. **There is nothing to build in `src/`** — the only deliverable is the test class above.

Source idioms to copy (no edits to these files):
- `tests/wa.api.integration/AdminFlagsEndpointTests.cs:36-73` (host + admin seed), `:182-196` (`SendAsync` cookie forge), `:98-107` (audit row asserts).
- `tests/wa.api.integration/PublicTransferAdsTests.cs:46-53` (flag/plan patch), `:110-157` (GET + seed helpers).
- `tests/wa.api.integration/ApplyAtFinalizeIntegrationTests.cs:151-160` (`RecordingPublisher : IEventPublisher`, returns `DomainEventRecord { EventId, IsPublished = true }`).
- `tests/wa.api.integration/StripeWebhookEndpointTests.cs:364-377` (`ConfigureWebHost` → `RemoveAll<IEventPublisher>` + singleton). Usings needed: `Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.TestHost` (if `ConfigureTestServices` is used) or `Microsoft.Extensions.DependencyInjection` + `Microsoft.Extensions.DependencyInjection.Extensions` (`RemoveAll`), `wa.application.Ports` (`IEventPublisher`), `wa.domain` (`EventEnvelope`, `EventType`, `DomainEventRecord`, `AuditLog`, `FeatureFlag`, `Plan`, `AppUser`, `Transfer`).

Behaviour under test (read-only context):
- `src/wa.api/Program.cs:868-897` — PUT reads raw body, `SetFlagCommand`, audits flag.set only on success, returns `{key,value}`; `SetFlagException` → BusinessException (400/404 Problem+JSON with `code`).
- `src/wa.application/UseCases/Transfers/RecipientAds.cs:13` — gate read through `IFlagResolver` (FlagsCache, 30 s), so a PUT is invisible until the cached entry ages out.
- `src/wa.application/UseCases/Admin/AuditWriter.cs:66-68` — `Type = EventType.AdminAction`, `PartitionKey = entityId ?? entityType`.

## 4. Open Questions / Risks

- **Q1 (not a blocker, no DECISION raised):** every seeded plan row carries `"ads":false` (`src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs:31`, frozen constant `ADS` Free=false in Open-Decisions Part 2), while the story expects free pages to show the slot once the flag is on. With seeds as-is, flipping feature.ads alone shows no ad anywhere in production. Already logged as an open decision under US-024-02; this plan patches the Free row as test data and does not touch seeds/migrations. Resolve before launch-with-ads, owner: product.
- **R1 — runtime:** two real 31 s waits per run. Acceptable (precedent `AdminFlagsEndpointTests.cs:167`); full integration suite runs once at feature end.
- **R2 — process-wide env leakage:** this class sets `Wa__AdminEmails` like its sibling; the DI publisher override makes the class immune to a leaked ServiceBus connection string.
- **R3 — fact 2c (stale-within-TTL) timing:** asserted immediately after the PUT, < 1 s after the warming read, so well inside 30 s; not flaky on a slow CI unless a step stalls > 29 s.
- **R4 — staging verification** ("verified in staging") is a manual QA step for the orchestrator/PO, not code.
