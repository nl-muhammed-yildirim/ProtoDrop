# Milestone Backlog

**Last updated:** 2026-08-28
Ordered task list. **One `CURRENT` at a time — no skipping.** Each task: scope, spec links (F/TA IDs), and a **measurable exit check**. Blocked-only-by-decisions: if a `TBD-you` blocks you, open a decision request (AGENT.md §8) and stop.

Status: `pending` → `done` (date). `CURRENT` = first non-done task.

---

## M0 — Foundation

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-001 | Solution + project scaffold per TA-2.1 (wa.domain, wa.application, wa.infrastructure, wa.api, wa.workers, tests, src/wa.web Vite+React+TS stub, `global.json` pinning .NET 10) | TA-2, TA-2.6, ADR-015/016 | `dotnet build` green on VS 2026; all 5 src projects + 2 test projects exist; web builds | done 2026-08-31 |
| T-002 | Local tooling: `docker-compose.local.yml` (SQL 2022 + Azurite), launchSettings with §5.2 env vars, `Program.cs` health endpoint (DB + SB ping), Serilog wiring | AGENT.md §5, TA-4.2#— | `GET /health` green with Docker up; Serilog lines visible | done 2026-08-31 |
| T-003 | CI: PR gate (build, unit, integration placeholder, web lint/test/build), ACR push for dev | TA-12.1 | PR gate runs green on a sample PR | done 2026-08-31 |
| T-004 | Domain model + full TA-3.2 DDL as EF migrations in `wa.infrastructure/Persistence`; seed `Plan` (free/pro/business per Part 2 constants) + default `FeatureFlag` rows; `WaDbContext` | TA-3.2, TA-3.4, TA-3.7, ADR-015 | `dotnet ef database update` creates all tables on Azurite/SQL; seed rows verifiable; wa.domain has zero external refs | done 2026-09-01 |
| T-005 | Limits Registry: `LimitsRecord`, `ILimitsProvider`, `PlansCache` + `FlagsCache` (30 s TTL), flag override mechanics | TA-3.4, F-TRF-007 | Unit tests: resolution per plan, flag override wins, TTL expiry honored | done 2026-09-01 |
| T-006 | Event backbone: TA-5.2 envelope, `IEventPublisher` + outbox row, SB adapter + in-memory test fake, dedup by eventId | TA-5, TA-0.2(9) | Integration test: publish → consume with fake; dedup on replay | done 2026-09-02 |
| T-007 | Blob foundation: `IBlobStore`, SAS minting per TA-3.6, staging/transfers containers + lifecycle rules (dev Azurite), staging path helper | TA-3.5, TA-3.6 | Integration test: mint `cwr` SAS → upload block → read back | done 2026-09-03 |
| T-008 | Pipeline foundation: correlation ID (W3C), Problem+JSON error mapping (closed code list), CORS, rate limits (API layer), telemetry SDK (TA-10.2 event helper) | TA-4.1, TA-10, F-TRF-012 | Integration test: 404/500 return Problem+JSON with correlationId; `upload_started`-style event emitted | done 2026-09-10 |

## M1 — Upload & Link

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-009 | `CreateDraftCommand` + endpoint 1: draft creation, per-file `cwr` SAS | F-TRF-001, TA-4.2#1, TA-7.1 | AC-001-2 (limit pre-check at API), integration test: draft → SAS upload → blob exists | pending |
| T-010 | `FinalizeTransferCommand` + endpoint 2: idempotency, server-side blob copy, `Transfer(0)` + `FileItem` + `BlobRef`, `upload_completed` | F-TRF-001, F-TRF-002, TA-4.2#2, TA-7.1 | AC-001-1, AC-002-4 (collision), EC-002-1 (idempotency replay returns same transfer) | pending |
| T-011 | `SendTransferCommand` + endpoint 3: emails validation/normalization, password hashing, `ExpiresAt = now + RETENTION_DAYS`, `transfer.created` | F-TRF-002, F-TRF-006-3/7, TA-4.2#3 | AC-002-1…002-3; `transfer.created` payload matches TA-5.3 | pending |
| T-012 | Landing page (upload surface): drop zone, file list, per-file/overall progress via UploadEngine, pre-checks, toasts, "Send." button | F-TRF-001, TA-8.3, UI-Reference screens 1 | AC-001-1…001-4 in browser (Playwright manual pass); block-level retry on injected failure | pending |
| T-013 | Sender link screen + send form + confirmation screen: copy field, emails, password, note, "Send transfer", `/t/{linkId}/sent` | F-TRF-002, UI-Reference screens 2–3 | Manual pass: full guest flow end-to-end in dev (draft→finalize→send→copy) | pending |

## M2 — Download, Expiry, Email

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-014 | Recipient page: endpoint 4, file list, status screens (active/expired/not-found/download-limit), `downloadsLeft` | F-TRF-003, TA-4.2#4, TA-7.2 | AC-003-1, AC-003-3, AC-003-4 (404 vs 410 bodies identical) | pending |
| T-015 | Password gate: endpoint 5 unlock JWT, sessionStorage token, wrong-password shake + telemetry | F-TRF-003-6, TA-4.2#5, TA-9.1 | AC-003-2 (refresh does not re-prompt); `password_correct/wrong` events | pending |
| T-016 | Download URLs: endpoint 6 single-file SAS (30 min), `DownloadsCount` increment + `DownloadEvent`, download-cap check (`Status=3`) | F-TRF-003, TA-4.2#6, TA-7.2 | AC-003-5; Range-resume works on a 500 MB blob (EC-003-1) | pending |
| T-017 | Download-all: endpoint 7 + `f-zip` function (streaming, cap, `_1`/`_2` de-dup, 202 polling, `zip.generated`) | F-TRF-004, TA-6.6, TA-4.2#7 | AC-004-1 (exactly one zip), AC-004-2 (cap hidden), EC-004-1/2 | pending |
| T-018 | Expiry jobs: `f-expire` + `f-delete-transfers` + `f-delete-blobs` with `JobRun` claim; RefCount decrement; `transfer.expired`/`transfer.deleted` | F-TRF-005, TA-6.3–6.5, TA-7.4 | AC-005-1…005-3; double-run idempotency test | pending |
| T-019 | Email: `f-email` (Stubble templates, CH sender, retry/DLQ), unsubscribe + `EmailSuppression`, i18n template selection | F-TRF-006, TA-6.7 | AC-006-1…006-3; dev: log-only sender switch visible | pending |
| T-020 | Free-tier limits end-to-end: server-side enforcement at finalize/send, storage quota check, "limit reached" screens, flag-driven values | F-TRF-007, TA-3.4 | AC-007-1…007-2; quota meter visible in admin overview | pending |

## M3 — Accounts, My Files, Re-send, Admin

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-021 | Auth: signup/login/magic-link/forgot/logout/me (endpoints 8–13), session cookie, `AppUser`/`AuthToken` usage, GDPR account delete (re-home transfers) | F-TRF-008, TA-4.2#8–13, TA-9.1 | AC-008-1…008-3; EC-008-2 (transfer survives account delete) | pending |
| T-022 | My Files: endpoints 14–15, list + detail + filter + cursor paging + delete (endpoint 17) | F-TRF-009, TA-4.2#14–15,17 | AC-009-1…009-3; EC-009-2 idempotent delete | pending |
| T-023 | Re-send: endpoint 16, `FILES_GONE` check, BlobRef `RefCount++`, `SupersededBy` | F-TRF-010, TA-7.3, ADR-009 | AC-010-1…010-2; storage not duplicated (verify blob count) | pending |
| T-024 | Admin: endpoints 20–23 (overview 5-min cache, transfers search, users, flags, suppressions), `AuditLog` + `admin.action` | F-TRF-011, TA-4.2#20–23 | AC-011-1…011-2; all mutations audit-logged with actor | pending |
| T-025 | Telemetry dashboards + alerts (TA-10.3) + error screens (F-TRF-013: Ref ID, degraded states for Blob/email down) | F-012, F-013, TA-10 | All 5 dashboards live in dev App Insights; AC-013-1 | pending |

## M4 — Hardening & Launch

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-026 | i18n full pass: 8 locales, detection, fallback + `translation_missing`, localized emails | F-TRF-014, TA-8.5 | AC-014-1…014-2; no hardcoded strings (lint) | pending |
| T-027 | Dark mode + a11y (WCAG 2.1 AA on P0 surfaces) + mobile web pass | F-TRF-015…017, UI-Reference | No flash-of-wrong-theme; keyboard-only full flow; mobile matrix green in staging | pending |
| T-028 | Playwright e2e suite vs staging (guest loop, password, expiry via flag override, My Files CRUD, i18n spot-check) | TA-14.1 | E2E green on staging deploy | pending |
| T-029 | Load test (k6): 5 GB synthetic, API p50/p99, recipient TTFB from 2 zones | TA-14.1, TA-15 | Upload < 10 min @ 100 Mbps; SLOs met | pending |
| T-030 | Beta → launch: seed prod flags per confirmed D-* decisions, runbook, launch gate checklist | 04-production-plan §12 | Launch gate 100 % green | pending |

---

## Notes

- T-001/T-002 were partially described in `Getting-Started-VS2026.md` (manual commands) — the files you create now are the *real* T-001/T-002 deliverables.
- Tasks map to `features/` spec files: e.g. T-012 → `features/Phase 0-MVP/F-TRF-001/F-TRF-001-upload-surface.md` + its user story files (same folder). Layout: `features/<Phase>/<feature-id>/`.
- Any task may spawn sub-decisions → Part 3 decision log in `Open-Decisions-and-Constants.md`.

## M4.5 — Phase 1: Pro / Monetization (added 2026-08-28)

Spec source: `02-feature-plan.md` §3 + the full Phase 1 specs in `features/` (F-BIL-001…003, F-PRF-001…004; stories US-018…024).

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-031 | Plan catalog: `PriceJson` on `Plan` rows + seed (Free/Pro/Business, D-07 placeholders), JSON validation, `UNKNOWN_PLAN` | F-BIL-001 (FR-018-1…3), US-018-01/03 | AC-018-1 (seed rows), AC-018-4 (checkout reads row prices); plan screen renders from data | pending |
| T-032 | `PlanContext` middleware + plan resolution + enforcement unification; `ACTIVE_TRANSFERS_EXCEEDED` code; guest → Free | F-BIL-003, F-BIL-001-5, F-TRF-007 | AC-020-1/4/5; code review: no scattered plan checks | pending |
| T-033 | Stripe Checkout + Customer Portal (endpoints 18, test mode D-12): `CreateCheckoutSessionCommand`, `GetPortalSessionQuery`, Stripe Price wiring per `PriceJson` | F-BIL-002 (FR-019-1/3), US-019-01/02, F-BIL-001 (FR-018-3) | AC-019-1 (plan applied ≤ 60 s in test mode); portal opens for a customer; `BILLING_UNAVAILABLE` on Stripe outage | pending |
| T-034 | Stripe webhook handler: `Stripe-Signature` verify, `StripeEvent` dedup, `HandleStripeWebhookCommand` (single entry), mirror upsert, `plan.changed` | F-BIL-002 (FR-019-2/4/5/8), US-019-03 | AC-019-3 (duplicate → one application), AC-019-4 (unknown type → 200, no change) | pending |
| T-035 | Billing lifecycle: `past_due` email sequence 0/3/7 d, 14-d grace (`GraceEndsAtUtc`), downgrade to Free at grace end, `resumed` restore; billing emails via `f-email` | F-BIL-002 (FR-019-2/6/7), US-019-04/05/06 | AC-019-2 (full sequence with fake clock); "You're back on Free" email sent; resume cancels pending email | pending |
| T-036 | Plan screen: current plan, storage/active-transfers meters (live), upgrade CTA, banners (pro / past_due / grace countdown) | F-BIL-003 (FR-020-3), F-BIL-002 UI, UI-Reference §5.7 | AC-020-3; meters match live scoped queries; banner states render | pending |
| T-037 | Branding: `AppUser.OrgName`/`OrgLogoBlobPath` migration, `PUT /auth/me/logo`, logo SAS minting, recipient header + email rendering, plan gating | F-PRF-001, US-021-01…04 | AC-021-1…021-5; migration up/down; Free tier shows no org slot | pending |
| T-038 | Scheduled send: `TransferStatus.Scheduled` (value 5, migration), link-screen toggle + 5 min/14 d validation, `ScheduledSendAtUtc`, timer sub-step (own `JobRun` key), `schedule_lag_seconds` | F-PRF-002, US-022-01…04 | AC-022-1…022-6 (fake clock + 10 s timer override); pre-due recipient line | pending |
| T-039 | Download analytics: `GET /transfers/{id}/analytics` (query + DTO), daily/country/browser + per-file + top-5 panel, privacy note, `UserAgentFamily` column, delete events with transfer | F-PRF-003, US-023-01…04 | AC-023-1…023-5; response contains no raw IP/UA; `IX_DownEvent_T` used | pending |
| T-040 | Ads: `adsEnabled` in public transfer DTO, ad slot component (loading/filled/failed/absent), `feature.ads` global flag (default off, D-14), metrics + alert | F-PRF-004, US-024-01…04 | AC-024-1…024-5 (Playwright: blocked ad host → invisible collapse); staging verified with flag off | pending |

---

## M5 — Phase 2: Collect, Sign, Albums (added 2026-08-30)

Spec source: `02-feature-plan.md` + the full Phase 2 specs in `features/Phase 2-Collect-Sign-Albums/` (F-COL-001…005, F-SGN-001…004, F-ALB-001…004; stories US-025…037). **Ordering within M5 is significant:** F-COL-001 (T-041/042) first — everything else in Phase 2 reuses the shared upload engine (TA-8.3) and the Phase 1 plan gate (F-BIL-003). Sign (F-SGN-*) is independent of Collect; Albums (F-ALB-*) reuse the Collect/Sign patterns.

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-041 | `Collection` entity + DDL (migration + ADR note), `CreateCollectionCommand` + `POST /api/v1/collections` (25), 8-char linkId, plan gate `Features.collect` (Free → "Collect requires Pro") | F-COL-001 (FR-025-1…5), US-025-01/04, TA-3.2 | AC-025-1 (create → public GET 200), AC-025-3 (unique linkId), AC-025-4 (plan gate) | pending |
| T-042 | Collection link screen + per-field setup UI + My Collections list (`GET /api/v1/collections` (26), `ListMyCollectionsQuery`), `collection.created` event + telemetry | F-COL-001 (FR-025-2/6), US-025-02/03 | AC-025-2 (guest opens collection page, no account); My Files renders Collections section | pending |
| T-043 | Submissions: guest draft + finalize (endpoints 28/29), `CollectionEntry` + `FileItem` generalization (nullable `CollectionEntryId`, migration + ADR), owner-plan limit, `collection.entry.created` + collector notification | F-COL-002 (FR-026-1…7), US-026-01/02/03 | AC-026-1…026-5 (entry row, double submission, over-limit pre-check, confirmation email, PastDue accept) | pending |
| T-044 | Submissions dashboard (`/collections/{id}`, endpoints 30–33), per-entry zip (`f-zip` scope extension, 202 pattern), status transitions (Received/Accepted/Declined/Done), search + cursor paging | F-COL-003 (FR-027-1…6), US-027-01…04 | AC-027-1…027-4 (list, per-entry zip exact, search, Decline no-modal) | pending |
| T-045 | Due date & grace: `DUE_GRACE_DAYS` config constant, `Open → PastDue → Closed` timer sub-step inside `f-expire` (own `JobRun` key), late flag, re-open, past-due/closed banners | F-COL-004 (FR-028-1…6), US-028-01…03 | AC-028-1…028-5 (banner, late accept, auto-close, re-open, JobRun de-dup) | pending |
| T-046 | Completion & notification: Done/Accepted/Declined email fan-out via `f-email` (templates `collection_done/accepted/declined/new_submission`), per-(address, owner) suppression, resubmit link | F-COL-005 (FR-029-1…7), US-029-01…03 | AC-029-1…029-5 (done email, decline resubmit link, replay de-dup, suppression, no-email silent) | pending |
| T-047 | Sign: `Document`/`DocumentRecipient` DDL (migration + ADR), `CreateDocumentCommand` + `POST /api/v1/documents` (34), ordered signers, `document.created`, sender tracking view `GET /sign/{linkId}` (35) | F-SGN-001 (FR-030-1/2/5/6), US-030-01/02 | AC-030-1 (signer 1 email now, signer 2 after signer 1), AC-030-3 (tracking shows per-signer status) | pending |
| T-048 | docx→PDF conversion (`DOCX_CONVERT_FAILED`), `VoidDocumentCommand` + `POST /api/v1/documents/{id}/void` (36) + voided state screen, plan gate `Features.sign` (Free → "Sign requires Pro") | F-SGN-001 (FR-030-3/4/7), US-030-03/04 | AC-030-2 (docx→PDF), AC-030-4 (void stops pending emails), AC-030-5 (plan gate) | pending |
| T-049 | Signer UX: sign page (draw/type/upload), `Signature` DDL (migration), AgreedOn date stamp, single-use token, decline + reason, PII hash of signer name | F-SGN-002 (FR-031-1…7), US-031-01…04 | AC-031-1…031-5 (draw/type rows, not-your-turn, single-use, decline) | pending |
| T-050 | Audit trail: `AuditEntry` DDL (append-only, migration), viewed (24 h de-dup)/signed/declined/voided, HMAC IP hash (no raw IP), plain-language statement, sender timeline | F-SGN-003 (FR-032-1…6), US-032-01…03 | AC-032-1…032-5 (view-once, sign atomic w/ Signature row, no raw IP, void actor, 24 h de-dup) | pending |
| T-051 | Final document: last signature → `Completed`, async PDF merge `documents/{id}/final.pdf` (202 pattern), 7-day SAS, `final_ready` email to all parties, `document.completed`/`document.final_failed` | F-SGN-004 (FR-033-1…6), US-033-01…03 | AC-033-1…033-4 (final.pdf w/ signatures + dates, all parties emailed once, sender download, retry on failure) | pending |
| T-052 | Album create: `Album` DDL (no expiry, migration + ADR), `CreateAlbumCommand` + `POST /api/v1/albums` (39) + My Albums (40), password + per-album cap, plan gate `Features.albums`, storage meter | F-ALB-001 (FR-034-1…6), US-034-01…03 | AC-034-1…034-4 (GET 200 no expiry, password gate, Free gate, cap rejection) | pending |
| T-053 | Album share: persistent link + "doesn't expire" copy, email invite `POST /api/v1/albums/{id}/invite` (41, `album_invite` template), `album.invited`, suppression per (address, owner) | F-ALB-002 (FR-035-1…5), US-035-01…03 | AC-035-1…035-4 (two invites, loads after 30 d, persistence copy, suppression) | pending |
| T-054 | Album contribute: contributor token + re-issue, `AlbumItem`/`ContributorToken` DDL (migration), draft + finalize (endpoints 42/43), grid + lightbox, close/re-open, owner + contributor upload | F-ALB-003 (FR-036-1…6), US-036-01…04 | AC-036-1…036-5 (contributor upload, owner upload, lightbox nav, closed state, token invalidation) | pending |
| T-055 | Album mobile: swipeable grid, full-bleed lightbox (100dvh), per-platform download, 300 px thumbnails, video `playsinline`, TTI < 1.5 s on 4G (50 items) | F-ALB-004 (FR-037-1…6), US-037-01…03 | AC-037-1…037-5 (grid render, swipe, iOS/Android download, video) | pending |

---

## M6 — Phase 3: Enterprise (added 2026-08-28)

Spec source: `02-feature-plan.md` §5 + the full Phase 3 specs in `features/` (F-ENT-001…006; stories US-038…043). **Ordering within M6 is significant:** T-056 (org foundation) before everything; T-057 (SSO) before T-060 (SCIM); T-061 (subdomain) and T-063 (workspaces) are the "later half" of Phase 3 and can follow M5.

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-056 | Org foundation: `Organization`/`OrgMember`/`OrgAuditEntry` DDL + migration, `CreateOrganizationCommand` (implicit General workspace), members (invite/role/remove + `org_invite` template), audit query + CSV export, `PrimaryOrgId` on `AppUser` | F-ENT-003 (FR-040-1…7), US-040-01/02/03 | AC-040-1…040-4; CSV export = filtered rows exactly; last-admin rule; audit row per mutation (transactional test) | pending |
| T-057 | SSO OIDC: `IdpConfig` DDL, detect/start/callback endpoints, PKCE + token validation, JIT enrollment, `sso.login` | F-ENT-001 (FR-038-1/2/7), US-038-01/02 | AC-038-1; JIT idempotent per ExternalId; fake-OIDC integration green |
| T-058 | SAML + enforcement + groups + logout: SAML metadata parse, ACS (signature/aud), `RequiredGroup` check, `Enforced` toggle, best-effort IdP logout | F-ENT-001 (FR-038-3…6), US-038-03/04/05/06 | AC-038-2/3/4/5; assertion fixtures (valid/bad-sig/wrong-EntityId); enforcement blocks local login |
| T-059 | Residency: `StorageAccount`/`ResidencyAnomaly` + `BlobRef` columns, org-scoped SAS routing, report endpoint, `f-residency-check` | F-ENT-004 (FR-041-1…6), US-041-01/02/03 | AC-041-1…041-4 (two Azurite accounts locally); misplaced blob flagged once |
| T-060 | SCIM: users + groups endpoints, provisioning secret (KV, rotate/grace), `ScimGroup` tables, entitlement name-matching | F-ENT-002 (FR-039-1…8), US-039-01/02/03 | AC-039-1…039-5; re-hire revival; secret rotation 24 h grace; pagination |
| T-061 | Custom subdomain: `OrgDomain` + claim/verify/lifecycle, Front Door custom domain + `ITlsProvisioner`, 301 rule | F-ENT-005 (FR-042-1…7), US-042-01/02/03 | AC-042-1…042-5 (fake TLS + DNS stub); 301 on default domain |
| T-062 | Workspaces: `Workspace` DDL + CRUD, per-workspace plan + overrides, resolution order in `ResolvePlanLimitsQuery`, My Files workspace filter, per-workspace meters | F-ENT-006 (FR-043-1…7), US-043-01/02/03/04 | AC-043-1…043-5; quota isolation (two workspaces); General invariants |
| T-063 | Org settings screens: Security (SSO/SAML), Provisioning (SCIM), Data (residency), Domain, Members, Workspaces, Audit (CSV), region selector on create | F-ENT-001…006 UI notes, UI-Reference §5.6/5.7 extensions | All screens render from the T-056…062 endpoints; dark mode + a11y parity (F-TRF-015/016) |
| T-064 | E2E + enterprise load: Playwright (org → SSO fake → SCIM push → send from workspace → audit export), k6 10k-org check job scaling note | TA-14.1, F-ENT-001…006 test plans | E2E green on staging; `f-residency-check` scales note at 1k/10k orgs |


---

## M7 — Phase X: Cross-Cutting (added 2026-08-28)

Spec source: `02-feature-plan.md` §6 + the full Phase X specs in `features/` (F-XCT-001…005; stories US-044…048). **Ordering within M7 is significant:** T-065 (flag seed + codes) before T-066/T-067/T-068 (each gated feature needs its flag); T-069 (PII gate) can land any time after the first event DTOs exist and should be finished **before launch**, because it's a build gate, not a feature.

| ID | Scope | Spec links | Exit check | Status |
|---|---|---|---|---|
| T-065 | Flag service completion: seed `feature.search`, `feature.dataExport`, `feature.emailSettings` keys; `ResolveFlag(key, default)`; 404 `FEATURE_DISABLED` + `FLAG_UNKNOWN`/`FLAG_INVALID` codes (TA-4.1.3); Flags screen effective-value view | F-XCT-001 (FR-044-1/3/5), US-044-01/02 | AC-044-1…044-3 (unit + integration); all seeded keys resolvable; gate-off = UI absent + 404 | pending |
| T-066 | Search: `SearchText` computed column + full-text index (owner-scoped `?q=` on endpoint 14, admin search on 21), escaping, `q`-cursor, `feature.search` gate, `Q_TOO_LONG` | F-XCT-002 (FR-045-1…8), US-045-01 | AC-045-1…045-3; owner scoping test (A never sees B); flag off hides box; D-21 backend confirmed before index shape freezes | pending |
| T-067 | Email settings: `AppUser.EmailNotificationsEnabled` (NULL=on) migration + `PATCH /auth/me` additive field; `f-email` send-time check (owner notifications only; recipients + billing exempt); `feature.emailSettings` gate | F-XCT-003 (FR-046-1…8), US-046-01/02 | AC-046-1…046-3; one in-flight message window documented; flag off = column inert | pending |
| T-068 | GDPR export + erasure: `DataExport` table + `f-data-export` timer (zip: profile.json + transfers.json, 24-h SAS, 7-day lifecycle), `EXPORT_IN_FLIGHT` 409, `feature.dataExport` gate; `f-gdpr-sweep` nightly (idempotent per userId, 90-day AI retention); admin **Erasures** screen + CSV; delete-modal telemetry line | F-XCT-004 (FR-047-1…8), US-047-01/02/03 | AC-047-1…047-3; zip fixture verified; sweep `pending → swept` observable; D-22 retention confirmed | pending |
| T-069 | PII policy gate: `[Pii]` attribute + `ErasureScope.AllColumns` closed list; convention tests (event DTOs, log templates); CI grep on event-helper call sites; erasure-cascade sync test | F-XCT-005 (FR-048-1…8), US-048-01/02/03 | AC-048-1…048-3; build red on unmarked fixture, green after marking; cascade covers every marked column | pending |
