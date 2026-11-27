# Pre-production readiness — status after the fix pass (2026-11-27)

Follow-up to `docs/reviews/PREPROD-readiness-audit.md` (2026-11-25). The owner asked to "fix them all" on 2026-11-26 and chose the Claude pipeline (Architect → Coder → Reviewer, one PR per item). This page shows what landed, what is waiting for a merge, and what only the owner can do.

## 1. Code and config work

| Audit item | PR | State | Notes |
|---|---|---|---|
| Merge #709, back-fill the backlog, ADR-019, doc drift | #709, #710 | merged | Backlog T-031…T-040, T-065, T-068 marked done; prod-plan cross-references fixed |
| Owner decisions (D-07, D-09, D-16, D-19, D-22, ORG_NAME, Manage billing, Key Vault ADR) | #710 | merged | Open-Decisions Part 1 and Part 3, 2026-11-26 |
| S-1 singleton `WaDbContext` (launch blocker) | #712 | merged | Per-call `IDbContextFactory`; `ConcurrentDbAccessTests`; StrictMode back |
| S-2 IaC, P-1…P-3 deploy pipeline | #711 | merged | `infra/bicep` (TA-11.2 shape), `cd-staging.yml`, CI `iac` job. Validated by `az bicep build` only — never deployed |
| X-2 CSP and security headers, web API base, log-sender guard, D-07 Pro 20 GB seed | #713 | **open, CI green except integration** | Needs merging together with or after #711 (already merged) |
| X-3 PII policy gate (T-069) | #714 | merged | `[Pii]` markers, `ErasureScope.AllColumns` = 15, R8 event-payload rule |
| Signup account takeover, blob SAS re-sign on real Azure | #715 | merged | Found while planning T-028 |
| Admin needs a confirmed email, pre-account hijack, session epoch | #716 | **open — merge before any deploy** | `develop` today still lets the first signup with an admin email become SuperAdmin, and keeps an attacker's pre-registration session alive. Migration `AppUserSessionsValidAfter` |
| T-028 Playwright `@staging` + T-029 k6 tooling | #717 | **open** | Reviewed through 3 rounds; never run, because no staging exists |
| US-046-02 recipients unaffected (T-067 slice 2) | — | in progress | |

**Not done, needs an owner decision first:**
- **App telemetry sink (O-1, T-025):** the App Insights / OpenTelemetry package was not approved. Container logs reach Log Analytics, and the Functions host logs reach App Insights. The four customEvents-based alerts and the 5 dashboards (`docs/alerts`, `docs/dashboards`) can't work until the server sends `customEvents`. The DLQ and 5xx alerts are deployed through Bicep.
- **Erasure scrub (T-069 D2):** account delete leaves 10 of the 15 PII columns in place (AuthToken email, Transfer.SenderEmail, audit and outbox rows, suppressions, idempotency replay bodies). It is recorded column by column in `ErasureScope`; scrubbing needs a decision on what "erased" means for audit and suppression rows.
- **Pii telemetry slot (T-069 D1):** implemented as option A (no raw address in telemetry at all). Confirm, or choose B.
- **Free-text `Transfer.Note`:** whether it counts as PII under FR-048-2.
- **HMAC key split, Static Web Apps:** not approved, so they stay as they are (one `Wa:Jwt:Secret`, nginx container).

**Known follow-ups (no decision needed):** prod `cd-prod.yml` (needs P-05/06/07/09); VNet/private endpoints for SQL; Communication Services diagnostic logs for the email-burst KQL; signup sets no `wa.session` cookie (only the body token); `AppDbContextFactory` and `WaDbContextFactory` are duplicate classes; the direct API Container App host bypasses the WAF.

## 2. Owner-only steps, in order

1. **Merge #716, then #713, then #717** (and the US-046-02 PR when it lands). Do not deploy `develop` before #716.
2. **Azure (P-01):** subscription, and `az group create -n wa-staging-rg -l westeurope`.
3. **GitHub OIDC (P-03/P-12):** an app registration with a federated credential for `repo:<owner>/ProtoDrop:environment:staging`, given **Owner** on the resource group. GitHub environment `staging`:
   - Secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_DEPLOYER_OBJECT_ID`; optional `STRIPE_SECRET_KEY` and `STRIPE_WEBHOOK_SECRET` (test mode, P-08); `E2E_ADMIN_EMAIL`, `E2E_ADMIN_PASSWORD`, `LOAD_USER_EMAIL`, `LOAD_USER_PASSWORD`, `BURST_RECIPIENT_TEMPLATE`.
   - Variables: `ADMIN_EMAILS` (including the e2e admin), `ALERT_EMAILS` (a JSON array), and after the first run `PUBLIC_URL` and `STAGING_API_URL`.
   - Details are in `infra/README.md` and `tests/load/README.md`.
4. **Run "CD staging"** (manual dispatch). Then confirm each admin email once by magic link (admins need a confirmed email now).
5. **Re-run "CD staging"** so the `@staging` e2e suite runs with a working admin. This is T-028's exit check.
6. **Email (P-06/P-07):** a Communication Services domain for `protodrop.com` with SPF, DKIM and DMARC. The 1k email burst needs it, or a raised quota.
7. **Load (T-029):** run "Load staging" for `api-latency`, `recipient-ttfb` (from two zones), `upload-5gb` and `email-burst`, then CG-1 against D-09.
8. **Manual QA:** the QA-SCRIPTS passes on staging. S-5.3 on a real iPhone and Android device is T-027's exit check.
9. **Launch gate (production plan §12):**
   - Domain and Front Door custom domain (P-04/P-05)
   - Live Stripe (P-09)
   - ToS and Privacy texts (P-14, D-17). The privacy sentence in US-048-03 must not claim full erasure today.
   - Prod admin emails (P-16, D-18)
   - Pen-test (P-15)
   - Restore drill (P-17)
   - On-call channel
   - 48 h of green staging SLOs
   - T-030 prod flag seed
