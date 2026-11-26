# Pre-production readiness audit

**Date:** 2026-11-25 · **Scope:** read-only audit of `develop` (origin/develop, after PR #708) plus the open PR #709. Nothing was changed except this file, which is not committed.
**Priority key:** **blocker** = launch gate (04-production-plan §12) cannot go green without it · **major** = real risk or a large gap, fix before staging sign-off · **minor** = tidy-up, or a doc that has drifted.

---

## 0. Headline

The code is far ahead of the docs. Phase 0 (F-TRF-001…017), Phase 1 (F-BIL-001…003, F-PRF-001…004) and most of Phase X (F-XCT-001/002/004, US-046-01) are merged into `develop`. But **nothing exists past the PR gate**: no IaC, no deploy pipeline, no staging, no App Insights sink, no Key Vault wiring, no k6 and no e2e in CI. The four open M4 launch tasks (T-025, T-028, T-029, T-030) and the staging half of T-027 all depend on a staging environment that does not exist yet. In code, the one launch blocker is the **singleton `WaDbContext`**.

---

## 1. Open decisions

### 1a. Part 1 items not `confirmed` / `default-accepted` (`docs/Open-Decisions-and-Constants.md`)

| ID | Question | Evidence | Blocks | Recommended option |
|---|---|---|---|---|
| D-07 | Pro/Business limits and prices (placeholder "20/50 GB & 100 GB; $9/$19/$49 (TBD)" is ambiguous: two Pro sizes, three prices) | Open-Decisions:20, Part 2 `MAX_TRANSFER_SIZE` Pro = TBD (:43) | T-030 prod flag seed; live Stripe prices (T-033 live mode); D-19; legal text (D-17) | Pick **one** Pro tier (e.g. 50 GB / $9) and Business 100 GB / $49. Confirm after the CG-1 unit-economics check (T-029). |
| D-09 | Cost ceiling (~$1,800/mo at 10 TB/day) | :22; prod plan §6 CG-1/CG-2 (:76-77) | CG-1 (T-029), CG-2 (first prod month), launch gate | Accept as the target. Add Azure budget alerts at 50/80/100 % in IaC and re-baseline at CG-2. |
| D-12 | Stripe account (test mode until launch) | :25; Preflight P-08/P-09 (:16-17) | Staging webhook e2e for F-BIL-002; T-030 | Create the test account now (staging needs a webhook URL + signing secret in KV). Live account at launch. |
| D-13 | Communication Hub resource (dev via IaC, prod manual) | :26 (says "P-09" but CH is Preflight **P-06**, :14) | T-029 email burst; first real send; P-07 | As specced: dev/staging in Bicep, prod created manually with a verified `protodrop.com` domain. Fix the P-09 → P-06 reference. |
| D-16 | i18n launch language set | :29 | T-030 (launch copy and QA scope S-5.1) | Code already ships all 8 bundles (T-014-01). Launch with **en/nl/de/tr** reviewed by a native speaker. Keep the other 4 behind review, or ship them marked "beta". |
| D-17 | GDPR legal texts (ToS/Privacy) | :30; Preflight P-14 (:22) | Launch gate (§12 :144) | Draft now. The text must state D-19 and D-22 retention, the analytics/IP-hash behaviour (F-PRF-003) and ads (F-PRF-004). |
| D-18 | Prod admin seed emails | :31; Preflight P-16 (:24) | Prod deploy (T-030) | Put `Wa__AdminEmails` in the Key Vault / app setting at deploy, never in the repo. |
| D-19 | Pro/Business retention 30/90 d | :32 | Same as D-07; legal text | Confirm 30/90 together with D-07. |
| D-22 | Data-export retention (24 h link / 7-day blob) | :35 | T-068 close-out ("D-22 retention confirmed", Milestone-Backlog:144); D-17 | Confirm 24 h / 7 d. Code already does this (F-XCT-004 merged). |

### 1b. Open items in the Part 3 log (pending user input or unfinished AI follow-ups)

| Ref | Question | Evidence | Blocks | Recommended option |
|---|---|---|---|---|
| P3-ORG | `ORG_NAME_MAX_LENGTH` as a domain constant, not in the Limits Registry | Open-Decisions:139 "pending user confirmation" | none (F-PRF-001 merged) | Confirm as is (it is a schema width, like `DisplayNameMaxLength`). |
| P3-BILL-ADR | TA-18/TA-17 ADR line for `BILLING_UNAVAILABLE` "not yet written — before merge to main" | Open-Decisions:124; the code is listed in TA-4.1.3 (03-technical-architecture:474) but has no ADR row (TA-17 :1013-1038) | develop → main promotion | Docs-only fix: add the ADR row. |
| P3-STRIPE-TOL | Stripe webhook timestamp tolerance of 300 s is a private constant: "Confirm or move to Part 2" | Open-Decisions:126 | none | Confirm as a code constant (it is Stripe's default) and add it to the Part 2 technical-constants table. |
| P3-GRACE | 14-day billing grace is a code constant, not a registry value | Open-Decisions:127 | none | Keep it as a code constant. Add it to the Part 2 technical table for visibility. |
| P3-UPGRADE | Pro "Upgrade" goes to the Billing Portal, but US-019-02 says hide Upgrade when subscribed | Open-Decisions:141 | T-036 UX sign-off | Hide Upgrade for subscribed users and show "Manage billing" (portal) instead. |
| P3-LOGSENDER | `LogEmailSender` (dev `Wa:Email:Sender=log`) logs full addresses and bodies: "open for the user" | Open-Decisions:121; `src/wa.infrastructure/Email/LogEmailSender.cs` | Prod PII posture | Allow `log` only when the environment is Local/Development (fail at startup otherwise) and redact addresses. |
| P3-BIL-DEFER | F-BIL-002 deferred items (no DLQ intake for Stripe, no `billing_grace_ending` send time, row lock on past_due write, no customer reuse on re-upgrade, …) | Open-Decisions:129 | none for the gate, but a launch risk | Make one follow-up task before live Stripe (P-09). The past_due row lock and customer reuse matter most. |
| P3-DLC | `download_completed` not emitted server-side (client pipeline only) | Open-Decisions:111, :112 | T-025 link-funnel dashboard accuracy | Accept. Make sure the client ingestion key (`VITE_WA_AI_KEY`) is set in the staging/prod builds. |
| P3-STRICT | `<StrictMode>` removed as a dev workaround for the singleton `WaDbContext` race | Open-Decisions:107; `src/wa.web/src/main.tsx:10` | see §3a S-1 (blocker) | Fix the DbContext lifetime (S-1), then put StrictMode back. |

### 1c. New decisions this audit surfaced (escalate per AGENT.md §8)

| Ref | Question | Evidence | Recommended option |
|---|---|---|---|
| N-1 | Web hosting: TA says Static Web Apps (`wa-prod-swa`, TA-11.2 :872, TA-12.1 step 2), but the repo ships an nginx container (`src/wa.web/Dockerfile`, `nginx.conf`) | 03-technical-architecture:860-875 | SWA as specced (free per-PR previews, Front Door origin). Keep the nginx image for local compose only. |
| N-2 | One secret, many uses: `Wa:Jwt:Secret` signs the session, unlock JWTs and unsubscribe tokens **and** is the IP-HMAC key (`src/wa.api/ApiServices.cs:147-161`). The 90-day rotation in TA-9.3 (:785) would break every unsubscribe link and the continuity of IP hashes. | ApiServices.cs:147-161; TA-9.4 (:795) | Split it into `wa-jwt-secret` (rotating) and a long-lived `wa-hmac-pii` key for unsubscribe tokens and the IP hash. This is security-shaped, so it needs a DECISION. |
| N-3 | SAS signing uses the account key (`ApiServices.cs:51`), while TA-9.5 (:800) says workers use managed identity and Blob has "no public network access" | ApiServices.cs:51; TA-9.5 | Phase 1: account key from KV (as specced in TA-9.3). Later: user-delegation SAS via managed identity (needs `Azure.Identity` → ADR). |

### 1d. Doc drift to fix (minor)

- `Milestone-Backlog.md` is stale. T-031…T-036 and T-038…T-040 show `pending` (:79-88), but F-BIL-001…003 and F-PRF-002…004 are merged (PRs #38-#40, #49, #702, #703). T-065 and T-068 show `pending` (:141, :144), but F-XCT-001 and F-XCT-004 are merged (PRs #42, #43). T-067 is half done (US-046-01 merged in PR #706; US-046-02 / AC-046-3 open). T-057…T-064 rows have **no status column** (:123-130). The header still says "Last updated 2026-10-13" (:3).
- `04-production-plan.md` has drifted too. §8 points the pen-test to Preflight **P-16** (:93), but in Preflight it is **P-15** (:23). §6 CG-1 says "before T-019 load test" (:76); the load test is **T-029**. §1 uses `example.com` domains (:14), but D-02 is `protodrop.com`.
- Prod plan §1/§2 say `main` auto-promotes to staging (:17), but `main` is 48 commits behind `develop` (last commit on main: `16afb03 chnages`).

---

## 2. Missing for a full test run

| # | Gap | Evidence | Priority |
|---|---|---|---|
| T-1 | **No staging environment** for the TA-14.1 e2e and load tiers (TA-14.1 :964-965; Milestone T-028/T-029 :61-62) | no `infra/`, no CD workflow (see §3d) | **blocker** |
| T-2 | **No k6 scripts** anywhere in the repo (5 GB upload, API p50/p99, TTFB from 2 zones, 1k email burst) | T-029 (:62); Open-Decisions:118 moved the burst there; `find` returns no `*.k6.js` | **blocker** |
| T-3 | **No Playwright e2e in CI.** 12 specs in `src/wa.web/e2e/` run only against the local stack (decision D11, Open-Decisions:106). `ci.yml` has no e2e job (:9-135). | ci.yml; playwright.config.ts | major |
| T-4 | The TA-14.1 e2e scenarios are not all present. Guest/password/recipient/mobile/a11y specs exist, but there is no **"expiry via admin flag override"**, no **"My Files CRUD"** and no **i18n (de) spot-check** spec. | `src/wa.web/e2e/*.spec.ts` (ariaLive, error-screens, focusRing, keyboard, lifecycle, mobile, recipient, staging, theme-parity) vs TA-14.1 :964 | major |
| T-5 | Unfinished features have no tests: **AC-046-3** (billing exempt from email setting, US-046-02) and **AC-048-1…3** (T-069 PII policy gate) | grep of AC ids in tests/ + wa.web: no hits | major (T-069 is "before launch", Milestone:137) |
| T-6 | Spec ACs with **no test that cites the ID** (behaviour may be covered under another name, but nothing traces to it): AC-002-4 (linkId collision; `FinalizeTransferHandlerTests` has collision tests without the ID), AC-004-1, AC-004-3 (zip de-dup `_1`/`_2`), AC-012-2, AC-014-1, AC-016-2 (screen reader, manual), AC-016-4 (colour-blind, manual), AC-052-1/3/4, AC-054-1, AC-055-2, AC-056-3 (only `ChaosEndpointTests` sends 429; no real auth-burst test). 29 of 140 Phase 0/1/X/Foundation ACs have no citation; AC-049/050/051 are tooling and are fine. | AC diff: `docs/features/**` vs `tests/**`, `src/wa.web/{src,e2e}` | minor (traceability), major for AC-056-3 and AC-004-3 if they really are untested |
| T-7 | Integration tests cannot run locally: Docker is down, and the AGENT.md §4 gate needs Testcontainers (docs/AGENT.md:§4). They only run in CI. | AGENT.md §4 | minor (environment) |
| T-8 | PR #709 (PipelineTests `Wa__Jwt__Secret`) is still open. Running `PipelineTests` on its own fails without it. | `gh pr list` | minor |
| T-9 | Coverage target **≥ 90 %** of domain/application (TA-14.1 :962) is not measured. `coverlet.collector` is referenced, but CI has no threshold or report. | ci.yml:42-51 | minor |
| T-10 | `prettier` CI step is disabled (`if: false`) | ci.yml:26-29 | minor |
| T-11 | CI builds only the `wa-api` image. The workers image (`src/wa.workers/Dockerfile`), web image and migrate image (`src/wa.api/Dockerfile.migrate`) are never built in CI, and no image is pushed to ACR. | ci.yml:129-135 | minor (major once CD exists) |
| T-12 | CI runs only on `pull_request`. Nothing runs on push to `develop`/`main`, so there is no post-merge signal. | ci.yml:5-7 | minor |
| T-13 | TA-14.1 rule "every TA-4.2 endpoint ≥ 1 happy + ≥ 1 error test" is not audited by any script (not verified in this audit). | TA-14.1 :963 | minor |

---

## 3. Missing for pre-production

### 3a. Infra / config

| # | Gap | Evidence | Priority |
|---|---|---|---|
| S-1 | **The API registers `WaDbContext` as a singleton.** EF contexts are not thread-safe, so concurrent requests throw "A second operation was started…" (seen in e2e; it is why StrictMode was removed). At prod scale (`min 1 / max 10`, TA-11.2) this means random 500s. | `src/wa.api/ApiServices.cs:62` (comment: "no request scope in the current API surface"), :64-70 adapters; Open-Decisions:107 | **blocker** |
| S-2 | **No IaC.** TA-11.4 specifies `infra/bicep/main/{env}.bicep` + modules, and the prod plan §2.5 says IaC runs in the same pipeline with a weekly drift check. The repo has no `infra/` folder. | 03-technical-architecture:887-889; 04-production-plan:32 | **blocker** |
| S-3 | **No Key Vault integration.** No KV config provider and no `Azure.Identity` package. Secrets (`Wa:Jwt:Secret`, `Wa:Blob:AccountKey`, `Wa:Zip:SignKey`, Stripe) are read straight from configuration. App settings can hold KV references (`@Microsoft.KeyVault(...)`) only if IaC sets them. | package list (no Azure.Identity / KeyVault); TA-9.3 :781-790; TA-12.2 "CI reads KV via OIDC" | major (blocker if KV refs are not set in IaC) |
| S-4 | No environment config: only `appsettings.json` / `appsettings.Development.json` (logging only). No staging/prod config surface is defined outside TA-13.1. | `src/wa.api/appsettings*.json` | major |
| S-5 | The rate limiter is in memory and per instance (`AddInMemoryRateLimiting`). With scale-out to 10 instances, the 5/min auth limit becomes 50/min. The WAF `RateLimitRule` set (TA-9.5 :803) must exist in Front Door IaC to be the real first layer. | ApiServices.cs:126-127; TA-9.5 | major (depends on WAF in S-2) |
| S-6 | DR: SQL PITR, GRS on prod blob, soft-delete 14 d and SB 30 d retention must be set in IaC. RTO 4 h / RPO 1 h has no runbook. | 04-production-plan:81-85; TA-11.2 :864-866 | major |
| S-7 | Branch model: TA-12.3 has `main` = staging and `release/*` → prod tag, but `main` is 48 commits behind `develop` and was last touched by "chnages" (`16afb03`). Promotion flow is not defined for the develop-based workflow. | TA-12.3; `git rev-list origin/main..origin/develop` = 48 | minor |

### 3b. Security / PII

| # | Gap | Evidence | Priority |
|---|---|---|---|
| X-1 | **Pen-test not done** (launch gate) | 04-production-plan:93, :140; Preflight P-15 (:23) | **blocker** |
| X-2 | **No CSP or security headers are emitted.** TA-9.5 itself says "no CSP header is emitted by code yet; this is spec text only". The web `nginx.conf` sets only `Cache-Control`. No HSTS, `X-Content-Type-Options`, `Referrer-Policy` or `frame-ancestors`. Needed in SWA `staticwebapp.config.json` or as Front Door rules, plus API response headers. | 03-technical-architecture:802; `src/wa.web/nginx.conf:15`; no matches for `Content-Security-Policy`/`UseHsts` in `src/` | major |
| X-3 | **T-069 PII policy gate** (`[Pii]` attribute, erasure-cascade sync test) is not started. The backlog says it "should be finished **before launch**". | Milestone-Backlog:137, :145 | major |
| X-4 | Key reuse and rotation conflict for `Wa:Jwt:Secret` (decision N-2) | ApiServices.cs:147-161; TA-9.3 :785 | major |
| X-5 | `LogEmailSender` can log full emails if prod is ever set to `Wa:Email:Sender=log` (P3-LOGSENDER) | `src/wa.workers/WorkerServices.cs:170`; Open-Decisions:121 | minor |
| X-6 | F-XCT-003 is half done: US-046-02 / AC-046-3 (billing and recipient emails exempt) has no test, which leaves a compliance gap for owner-notification opt-out | Milestone-Backlog:143; T-5 | minor |
| X-7 | SVG logos are an accepted risk (Open-Decisions:135). It relies on the blob origin being cookie-less **and** on the CSP in X-2. Re-check once X-2 lands. | TA-9.6 :812 | minor |

### 3c. Observability

| # | Gap | Evidence | Priority |
|---|---|---|---|
| O-1 | **No App Insights sink.** Serilog writes to console only (`Serilog.Sinks.Console`). No App Insights / OpenTelemetry / Azure Monitor package, and `Wa:AppInsights:ConnectionString` (TA-13.1 :941, required) is not read anywhere in `src/`. All server telemetry (`WaTelemetryReporter` → ILogger) goes nowhere in Azure unless Container Apps log scraping is set up, and the dashboards query `customEvents`. | package list; `src/wa.application/Telemetry/WaTelemetryReporter.cs:18`; grep `AppInsights` in src = 0 hits | **blocker** |
| O-2 | **T-025 pending.** Dashboard and alert JSON exist only as docs (`docs/dashboards/*.json`, `docs/alerts/*.json` incl. `action-group.json`) and are not deployed. Exit check: "All 5 dashboards live in dev App Insights; AC-013-1". | Milestone-Backlog:53 | **blocker** |
| O-3 | On-call and alert channel not live (prod plan §4; launch gate :142) | 04-production-plan:50-55, :142 | **blocker** |
| O-4 | Functions telemetry: `host.json` / worker App Insights wiring needs checking once O-1 is chosen (not verified here) | `src/wa.workers/host.json` | minor |
| O-5 | The client telemetry ingestion key `VITE_WA_AI_KEY` must be injected per environment at build time, otherwise the client events (growth CTA, `upload_failed`, `download_completed`) stay `console.debug` | Open-Decisions:112 | minor |

### 3d. Deploy

| # | Gap | Evidence | Priority |
|---|---|---|---|
| P-1 | **No `cd-staging.yml` / `cd-prod.yml`** (IaC apply → migrate → deploy workers → api → web → smoke → Playwright; prod: tag + approval + canary 20 % → 100 %) | TA-12.1 :895-913; 04-production-plan:28-32; `.github/workflows/` contains only `ci.yml` | **blocker** |
| P-2 | No ACR push or OIDC federation (ci.yml comment: "no push at M0 (… E-003)") | ci.yml:129; Preflight P-03 (:11) | **blocker** |
| P-3 | Migrations before cutover: `Dockerfile.migrate` exists, but no pipeline runs it. No smoke test ("create + expire a test transfer via API") exists. | TA-12.2 :917; TA-12.1 step 3 | **blocker** (part of P-1) |
| P-4 | Rollback runbook (previous image tag) and launch runbook (T-030) not written | 04-production-plan:31; Milestone-Backlog:63 | major |
| P-5 | Prod flag seed per confirmed D-* (T-030) depends on §1a | Milestone-Backlog:63 | major |

### 3e. Manual QA (`docs/QA-SCRIPTS.md`)

Only **S-1.1** has a recorded PASS (QA-SCRIPTS:38). S-1.2 is covered by the same automated run. No PASS is recorded for:

| Script | Line | Priority |
|---|---|---|
| S-1.3 send form + confirmation | :45 | major |
| S-2.1 recipient states, S-2.2 password gate, S-2.3 download + Range-resume (partly automated in `recipient.spec.ts`) | :57, :64, :71 | major |
| S-3.1 free-tier limit screens | :83 | major |
| S-4.1 auth loop (needs real email in staging) | :92 | major |
| S-5.1 i18n spot-check (8 locales, `translation_missing` = 0) | :102 | major |
| S-5.2 dark mode + keyboard-only + **screen reader** (AC-016-2) | :106 | major |
| S-5.3 **real-device** iPhone Safari + Android Chrome, on staging (T-027 exit: "mobile matrix green in staging") | :111-124 | **blocker** (T-027 exit check) |
| M4.5 Phase 1 quick checks (plan gate, billing emails, ads off) | :127-131 | major |

### 3f. Human-side preflight (`docs/Preflight-Checklist.md`, which has no status column; none is marked done)

| ID | Item | Line | Needed for | Priority |
|---|---|---|---|---|
| P-01 | Azure subscription + $50 budget alert | :9 | everything in §3a | **blocker** |
| P-03 / P-12 / P-13 | ACR + Key Vault + App Insights via IaC, plus the one-time OIDC grant | :11, :20, :21 | P-1, P-2, O-1 | **blocker** |
| P-04 | Domain + DNS account | :12 | staging custom host (optional), prod | major (blocker for prod) |
| P-05 | Front Door custom domain + cert (prod) | :13 | prod | **blocker** (prod) |
| P-06 | Communication Hub (prod manual) — D-13 | :14 | T-029 burst, S-4.1, launch | **blocker** |
| P-07 | SPF / DKIM / DMARC | :15 | first real send; launch gate :144 | **blocker** |
| P-08 | Stripe test account + staging webhook secret → KV — D-12 | :16 | staging billing QA (M4.5) | major |
| P-09 | Stripe live + live webhook | :17 | launch | **blocker** (if Pro sells at launch) |
| P-14 | ToS / Privacy — D-17 | :22 | launch gate | **blocker** |
| P-15 | Pen-test | :23 | launch gate | **blocker** |
| P-16 | Prod admin seed emails — D-18 | :24 | prod deploy | major |
| P-17 | Restore drill runbook + first drill | :25 | launch gate :141 | **blocker** |
| P-10 / P-11 | Playwright in CI, k6 OSS | :18-19 | T-028/T-029 | minor (no cost, just verify) |

---

## 4. Top-10 next steps (in order)

1. **Fix the `WaDbContext` lifetime** (`ApiServices.cs:62`): use a scoped context or `IDbContextFactory` per operation, then put `<StrictMode>` back. This is the only launch blocker in code, and every load or e2e result is unreliable until it is fixed.
2. **Tidy up:** merge PR #709, update the `Milestone-Backlog.md` statuses (Phase 1 / Phase X rows, T-057…T-064 status column), fix the prod-plan and preflight cross-references, write the `BILLING_UNAVAILABLE` ADR row, and agree how `develop` gets promoted to `main`.
3. **Owner decisions:** close D-07/D-19 (prices and retention), D-22, D-16, D-18, D-09, plus P3-ORG, P3-UPGRADE and the new N-1 (SWA vs container) and N-2 (split HMAC keys).
4. **Human preflight, round 1:** P-01 subscription, P-03/P-12 OIDC grant, P-08 Stripe test account, P-04 domain.
5. **Bicep IaC** (`infra/bicep`, TA-11.4) for dev/staging: SQL, Blob (lifecycle, soft-delete), SB, KV, App Insights, ACR, Container Apps, Flex Functions, SWA, Front Door + WAF rate rules, budget alerts.
6. **Wire runtime config:** an App Insights sink (an ADR for the package), KV references / managed identity for secrets, and CSP + security headers on the SPA and API.
7. **`cd-staging.yml`:** IaC apply → ACR push (all 4 images) → migrate → deploy workers → api → web → smoke test. Then move the existing Playwright specs into a staging e2e job and add the missing TA-14.1 scenarios (expiry via flag, My Files CRUD, de spot-check). This covers T-028.
8. **T-025 + T-069:** deploy the 5 dashboards and alerts with an action group and on-call channel, and finish the PII policy gate (plus US-046-02 / AC-046-3).
9. **T-029 k6 + CG-1:** 5 GB upload, API p50/p99, TTFB from 2 zones, 1k email burst against staging with real SB, Functions and Communication Hub (needs P-06/P-07). Then confirm D-09.
10. **Launch gate (T-030):** real-device S-5.3 and the remaining QA-SCRIPTS passes on staging, 48 h of staging SLOs, pen-test (P-15), restore drill (P-17), legal texts (P-14), Stripe live (P-09), `cd-prod.yml` with approval and canary, prod flag seed.
