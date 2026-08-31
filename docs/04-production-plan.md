# Production Plan — ProtoDrop File Transfer Platform

**Version 0.2** | **Last updated:** 2026-08-23
**Stack:** .NET 10 (Minimal API) · Clean (onion) architecture · CQRS via MediatR 12.5 · Azure · React 18 + TypeScript (Vite) · Visual Studio 2026
**Companions:** `03-technical-architecture.md` (TA-*) — this document references TA IDs instead of restating details.

---

## 1. Environments

| | dev | staging | prod |
|---|---|---|---|
| Purpose | AI/local dev loop | Pre-prod, e2e gate | Live |
| Domain | `*.dev.example.com` | `*.staging.example.com` | `example.com`, `www.`, `api.` |
| Sizing | local Docker (SQL + Azurite) per AGENT.md §5 | prod-shaped, ½ scale | TA-11.2 |
| Data | seeded | synthetic, prod-shaped | live |
| Promoted by | `main` (auto) | `main` (auto) | tag `v*` + approval |

Rules:
- Migrations always run **before** API cutover (TA-12.2).
- Staging is the **e2e gate** for prod (Playwright, TA-14.1).
- Prod deploy: canary 20 % → 100 % (TA-12.1).

## 2. CI/CD

See TA-12 for the full pipeline. Production-plan rules on top:

1. PR gate: lint + unit + integration + web tests (TA-12.1).
2. Staging deploy gate: smoke + Playwright e2e green.
3. Prod promotion: manual approval + last 48 h of staging SLOs green (TA-15).
4. Rollback = previous image tag; migrations are add-only (TA-12.2).
5. IaC (Bicep) applied in the same pipeline (TA-11.4); drift check weekly.

## 3. SLOs & error budgets

Targets: TA-15. Alerts: TA-10.3.

| SLO | Target | Error budget (30 d) |
|---|---|---|
| API availability | 99.9 % | 43 min |
| Upload success rate | 99 % | 10 % of uploads |
| Recipient page TTFB p95 | < 500 ms global | — |
| Expiry job lag | < 15 min | — |
| Email delivery p95 | < 60 s | — |

**Error-budget policy:** if 50 % of the monthly budget is consumed, freeze feature deploys (only hotfixes) until the window rolls.

## 4. On-call & incident response

- **On-call:** one primary, rotating weekly (AI dev project: you + paged via the alert channel).
- **Channels:** alert → shared chat (Teams/Slack) with the Ref ID (correlationId) in every page.
- **Severity mapping:** P1 = TA-10.3 P1 alerts; P2 = the rest.
- **Incident template:** summary, detection, impact (SLO breached?), timeline, mitigation, rollback?, follow-ups.
- **Status pages:** only for prod outages; status URL in the email footer (P1).
- **Review:** every P1 gets a blameless post-mortem within 3 business days.

## 5. Observability

Full contract: TA-10. Production rules:

- Dashboards (TA-10.4) are part of the release — no feature ships without its telemetry.
- Correlation ID in every error page ("Ref: {id8}") and in every DLQ alert.
- Weekly "business" review of the Business dashboard (TA-10.4.5).

## 6. Capacity & cost

Cost model: TA-16 (~$1,800/mo at 10 TB/day). Controls, in priority order:

1. **Egress** — biggest line; P2 CDN download path (ADR-006) is the structural fix.
2. **Retention days** — plan pricing lever; shorter free retention = direct savings.
3. **Lifecycle rules** — `staging/*` 24 h, `transfers/*` 30 d safety net (TA-3.5).
4. **Functions** — Flex Consumption, per-function scale (TA-6.2).
5. **SQL** — B-Server Auto; rightsizing review quarterly.

**Cost gates:**
- CG-1 (before T-019 load test): unit economics per 1 TB transferred must fit inside Pro pricing (decision D-09 in `Open-Decisions-and-Constants.md`).
- CG-2 (first month in prod): actuals vs TA-16 within ±30 %, else re-forecast and document.

## 7. Data & DR

- **Backups:** SQL PITR (14 d), Geo-Redundant Storage for Blob (prod), Service Bus 30 d retention.
- **RTO/RPO:** RTO 4 h, RPO 1 h (prod).
- **Restore drill:** quarterly, documented runbook, measured RTO recorded.
- **DR region:** prod `westeurope` primary; DR = restore in `northeurope` from GRS (D-08).
- **Migrations:** add-only within a release (TA-3.7); backfill jobs idempotent.

## 8. Security & compliance

- TLS 1.2+ at the edge (Front Door), AES-256 at rest (Blob/SQL defaults).
- PII policy: TA-9.4. GDPR: account delete + erasure of `EmailSuppression` rows; data export P1.
- Secrets: Key Vault only (TA-9.3/9.5); rotation per TA-9.3.
- Rate limits: TA-9.5 (WAF + API layer).
- Pen-test: before public launch (Preflight P-16).
- **Data residency (Enterprise P3):** org-level region flag (TA-20, F-ENT-004).

## 9. Team & RACI (MVP)

| Role | Headcount | Scope |
|---|---|---|
| AI developer (this assistant) | 1 | All code, tests, migrations, IaC per `Milestone-Backlog.md` |
| You (owner/operator) | 1 | Decisions (D-*), Azure/GitHub/Stripe accounts, budget, launch call |
| DevOps (you, part-time) | — | CI secrets, Key Vault, DNS, Front Door custom domain |
| QA (you + Playwright) | — | E2E scenarios per TA-14.1, load tests |

**RACI note:** the AI is *Responsible* for everything code-shaped; *Accountable* remains with you; escalation boundary in `AGENT.md` §8.

## 10. Timeline (12-week MVP)

Aligns with `Milestone-Backlog.md` (T-001…T-030):

> **Scope note:** the table above is the 12-week MVP (M0–M4, T-001…T-030). Post-MVP milestones in the backlog — M4.5 (Phase 1, T-031…040), M5 (Phase 2, T-041…055), M6 (Phase 3, T-056…064), M7 (Phase X, T-065…069) — run after the MVP launch and do not change the launch gate.

| Week | Milestone | Exit |
|---|---|---|
| 1–2 | M0: scaffold, local tooling, domain + DDL, registries, event/blob/pipeline foundations | T-001…T-008 done |
| 3–4 | M1: upload + link + finalize (API + UI) | T-009…T-013 done |
| 5–6 | M2: recipient + zip + expiry + email + limits | T-014…T-020 done |
| 7–8 | M3: accounts, My Files, re-send, admin, telemetry | T-021…T-025 done |
| 9 | M4: hardening (i18n, dark, a11y, mobile, e2e, load) | T-026…T-029 done |
| 10–12 | Private beta → public launch | T-030 done, launch gate green |

## 11. Risk register

Architecture risks: TA-19. Production risks:

| Risk | L | I | Mitigation |
|---|---|---|---|
| Egress cost overrun | High | High | TA-16 controls; CDN ADR-006; per-plan metering |
| Free-tier abuse (storage/egress farms) | Med | Med | `ACTIVE_TRANSFERS_MAX`, per-IP rate limits, admin kill |
| Stripe webhook loss/duplication | Low | High | Idempotent by event id (TA-7.5); replay admin action |
| Email domain deliverability | Med | Med | SPF/DKIM/DMARC before first real send (Preflight P-07) |
| Single-operator bus factor | Med | Med | This doc set + AGENT.md are the operating manual; decision log in `Open-Decisions-and-Constants.md` |
| AI-only drift (silent decisions) | Med | Med | Autonomy boundary in AGENT.md; decision log is append-only and reviewed weekly |

## 12. Launch gate checklist

- [ ] All T-001…T-030 `done` in `Milestone-Backlog.md`
- [ ] Load test green (k6, TA-14.1) — CG-1 passed
- [ ] SLOs green for 48 h in staging
- [ ] Pen-test findings ≤ P1 all fixed (P-16)
- [ ] Backups + restore drill documented
- [ ] On-call + alert channels live
- [ ] Cost forecast reviewed (CG-2 baseline)
- [ ] Legal: ToS/Privacy live (Preflight P-14), mail domain SPF/DKIM/DMARC (Preflight P-07)
- [ ] All `TBD-you` decisions in `Open-Decisions-and-Constants.md` are `confirmed`

## 13. Change log

- **v0.2 (2026-08-23):** .NET 10 / clean (onion) / MediatR 12.5 / VS 2026 stack line; task IDs aligned to Milestone-Backlog.
- **v0.1:** initial production plan.
