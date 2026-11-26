# infra — Bicep IaC (TA-11.4)

One resource group per environment (`wa-dev-rg`, `wa-staging-rg`, `wa-prod-rg`), one template:

```
infra/bicep/main.bicep            entry point (targetScope = resourceGroup)
infra/bicep/main/{env}.bicepparam per-environment params (no secrets)
infra/bicep/modules/*.bicep       identity, monitoring (LAW + App Insights + action group + budget), keyvault,
                                  acr, storage, sql, servicebus, communication, apps, frontdoor, alerts
infra/scripts/seed-secrets.sh     seeds Key Vault secrets Bicep cannot generate (idempotent)
```

## What it creates (TA-11.2 shape)

| Module | Resources | Notes |
|---|---|---|
| identity | user-assigned identity `wa-{env}-id` | used by api, web, workers, migrate job: KV read, ACR pull, SQL admin, blob data |
| monitoring | Log Analytics, App Insights, action group, RG budget | budget at 50/80/100 % (D-09); prod default $1,800 |
| keyvault | Key Vault (RBAC) | identity = Secrets User, CI principal = Secrets Officer |
| acr | ACR Basic | images `wa-api`, `wa-web`, `wa-migrate` |
| storage | StorageV2 (GRS in prod), containers `staging/transfers/exports/avatars/deploy` | lifecycle 24 h / 30 d / 7 d, soft delete 14 d, CORS for the SPA origin, account key → KV `wa-blob-key` |
| sql | SQL server (Entra-only) + serverless DB `wa` | identity is Entra admin → `Authentication=Active Directory Managed Identity`, PITR 14 d |
| servicebus | Standard, topics `core` (+`email`) and `billing` (+`billing-mirror`) | connection string → KV `wa-sb-connection` |
| communication | ACS + Email + Azure-managed domain (dev/staging only, D-13) | connection string → KV `wa-ch-connection` |
| apps | Container Apps env, `wa-{env}-api`, `wa-{env}-web`, job `wa-{env}-migrate`, Flex Consumption Function App | all secrets are KV references; api min 1 / max 10 |
| frontdoor | Front Door Standard + WAF | one host: `/api/*` + `/health` → api, `/*` → web; WAF rate limits from TA-9.5 |
| alerts | DLQ > 0 (SB metric), 5xx > 5 % (Front Door metric) | the other four TA-10.3 alerts need an app telemetry sink (T-025) |

## One-time setup (owner, Preflight P-01/P-03/P-12)

1. Subscription + resource group: `az group create -n wa-staging-rg -l westeurope`.
2. App registration for GitHub OIDC with a federated credential for `repo:<owner>/ProtoDrop:environment:staging`.
   Give it **Owner** on the resource group (Bicep creates role assignments; Contributor is not enough).
3. GitHub environment `staging` with secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
   `AZURE_DEPLOYER_OBJECT_ID` (the service principal's object id), and optionally `STRIPE_SECRET_KEY`,
   `STRIPE_WEBHOOK_SECRET` (test mode, D-12). Variables: `ADMIN_EMAILS`, `ALERT_EMAILS` (JSON array, e.g. `["ops@protodrop.com"]`).
4. Run the **CD staging** workflow (manual dispatch). It deploys the platform, seeds secrets, builds and pushes
   images, deploys the apps, runs migrations, deploys the workers and runs the smoke test.

Prod (`cd-prod`) is not wired yet: it needs P-05 (custom domain + cert), P-06/P-07 (Communication Services with
the verified domain + SPF/DKIM/DMARC), P-09 (live Stripe) and a manual approval gate on the `prod` environment.

## Not covered yet (tracked in docs/reviews/PREPROD-readiness-audit.md)

- VNet injection / private endpoints for SQL (TA-11.2 prod): SQL allows Azure services through the firewall for now.
- App telemetry sink in code (App Insights / OpenTelemetry package not approved yet). Container logs go to Log
  Analytics; Functions host logs go to App Insights.
- Custom domains on Front Door (P-04/P-05).
