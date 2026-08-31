# Preflight Checklist (human-side dependencies)

**Last updated:** 2026-08-23
Items the AI cannot create itself. Owner: `you` unless marked `AI`. Sequenced as gates against milestones.

| ID | Item | Owner | Needed by | Notes |
|---|---|---|---|---|
| P-00 | Dev machine: VS 2026 + .NET 10 SDK + Node 20 LTS + Docker Desktop (WSL2) + Git | you | M0 start | Verify: `dotnet --list-sdks` shows 10.x; see Getting-Started-VS2026.md |
| P-01 | Azure subscription + billing | you | M0 start (dev resources can wait to T-019+) | Pay-as-you-go or free tier; budget alert at $50 |
| P-02 | GitHub org + private repo + Actions | you | T-003 (CI) | Push permissions for CI; OIDC to ACR/Key Vault later |
| P-03 | ACR (dev) via IaC; OIDC login for CI | you + AI | T-003 | Bicep creates it; you click "give Azure OIDC" once |
| P-04 | Domain + DNS account (Cloudflare/Route53) | you | staging deploy | CNAMEs for Front Door; dev/staging use default hostnames first |
| P-05 | Front Door custom domain + cert (prod) | you | prod deploy | Staging/dev: default hostnames |
| P-06 | Communication Hub (dev via IaC; prod manual) | you | T-019 (email) | `no-reply@protodrop.com` must resolve (D-04) |
| P-07 | SPF / DKIM / DMARC for mail domain | you | first real send | Deliverability (production plan §11 risk) |
| P-08 | Stripe account (test mode) + webhook endpoint URL (staging) | you | F-BIL-002 (Phase 1) | Test API key + webhook signing secret to Key Vault |
| P-09 | Stripe (prod) + live webhook | you | launch | — |
| P-10 | Playwright/Chromium availability in CI | you | T-028 | GitHub Actions ubuntu-latest has it; no action needed (verify once) |
| P-11 | k6 license (open-source is fine) | you | T-029 | OSS k6 suffices |
| P-12 | Key Vault: create via IaC; OIDC for CI | you + AI | T-002/T-003 | Secrets injected by CI |
| P-13 | App Insights (dev/staging) via IaC | AI | T-002 | Connection string to launchSettings (dev) / Container App (staging) |
| P-14 | ToS / Privacy texts + email `From` domain (D-17, D-04) | you | launch | Legal review |
| P-15 | Pen-test (P1 findings) | you | launch gate | External or internal |
| P-16 | Admin seed emails for prod (D-18) | you | prod deploy | In Key Vault / config, not in repo |
| P-17 | Restore drill runbook + first drill | you + AI | launch gate | Production plan §7 |
