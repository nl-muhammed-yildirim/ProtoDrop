# Staging verification tooling (T-028 Playwright `@staging`, T-029 k6 load)

Plan: `docs/plans/T-028-T-029-tooling-plan.md`. Nothing here runs against a local stack by default; both suites target a deployed staging environment.

## Owner prerequisite: confirm the e2e admin account once (T-028 `expiry-flag.spec.ts`)

The `expiry-flag.spec.ts` scenario needs a SuperAdmin session to override a flag. The admin role requires a **confirmed email**, and on staging the confirmation arrives by email as a magic link. The suite cannot (and does not try to) confirm the account itself. One-time owner steps:

1. Pick a mailbox you control and put it in the repository variable `ADMIN_EMAILS` (so `Wa__AdminEmails` lists it) and in the `staging` environment secrets `E2E_ADMIN_EMAIL` / `E2E_ADMIN_PASSWORD`.
2. Run the Playwright job once. Setup creates the account (signup, then login) but the role is not granted yet, so `expiry-flag.spec.ts` **skips** with the reason "logs in but lacks the admin role ... CONFIRMED email".
3. On the staging site open `/account`, request a magic link for that email ("Email me a magic link"), and open the link from the mailbox. This redeems the link and confirms the email.
4. Re-run the job. Setup logs in again, `GET /admin/flags` now returns 200, and the scenario runs.

Without the secrets, the spec skips with "E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD not set". The other four specs never need an admin.

Playwright env: `E2E_BASE_URL` (required; the Front Door URL), `E2E_API_URL` (optional; defaults to the same origin), `E2E_ADMIN_EMAIL`, `E2E_ADMIN_PASSWORD`, `E2E_FLAGS_TTL_SECONDS` (default 30). Run from `src/wa.web`: `E2E_BASE_URL=https://... npx playwright test -g @staging`. `e2e/.auth/` and `test-results/` hold cookies and tokens: never upload them as artifacts. Note `expiry-flag.spec.ts` sets `limits.free.maxDownloads=1` for roughly 30 to 60 seconds, and restores it in `finally`; a crashed runner could leave it at 1 (restore it in Admin, Flags).

## k6 load scripts

k6 built-ins only, no npm. k6 is a binary installed in CI (`grafana/setup-k6-action`); locally install it yourself. Validate the scripts with `k6 inspect tests/load/<script>.js`.

Two origins on purpose:

- `API_BASE_URL`: the **direct** Container Apps API origin (the `apiFqdn` output of `cd-staging.yml`). Load goes here, because Front Door's WAF blocks above 30 / 10 / 10 requests per minute per IP on public, draft and auth paths, which would measure the WAF instead of the API.
- `PUBLIC_BASE_URL`: the Front Door origin, used for recipient TTFB (what users actually hit).

| Script | Required env | Optional env | Thresholds |
|---|---|---|---|
| `api-latency.js` | `API_BASE_URL`, `LOAD_USER_EMAIL`, `LOAD_USER_PASSWORD` | `RATE` (20 req/s per scenario), `DURATION` (5m) | `http_req_duration{kind:api}` p50 < 100 ms, p99 < 500 ms; `http_req_failed{kind:api}` < 1% |
| `recipient-ttfb.js` | `API_BASE_URL` (seeding), `PUBLIC_BASE_URL` | `ZONE` (label), `DURATION` (3m) | `http_req_waiting{kind:page}` and `{kind:public_api}` p95 < 500 ms |
| `upload-5gb.js` | `API_BASE_URL`, `LOAD_USER_EMAIL`, `LOAD_USER_PASSWORD` | none | `upload_duration_ms` max < 600000 (10 min); `upload_mbps` reported, not gated |
| `email-burst.js` | `API_BASE_URL`, `BURST_RECIPIENT_TEMPLATE` (with `{n}`), `RECIPIENTS_PER_SEND` | `TOTAL` (1000), `BURST_RUN_ID`, `PREP_PACE_SECONDS` (1), `E2E_ADMIN_EMAIL` / `E2E_ADMIN_PASSWORD` (cross-check) | `http_req_duration{kind:send}` p95 < 1000 ms (API accept) |

Every script writes `summary.json` and prints one line per threshold (`ok` / `FAILED`). `email-burst.js` also writes `burst-manifest.json`.

Run example:

```
k6 run -e API_BASE_URL=https://<apiFqdn> -e LOAD_USER_EMAIL=... -e LOAD_USER_PASSWORD=... tests/load/api-latency.js
```

`LOAD_USER_*` is a synthetic account (for example `k6-load@example.com`); the script logs in once and creates the account on first use. Spend the auth budget carefully: the API allows 5 requests per minute per IP on `/auth/*`.

### Recipient TTFB from 2 zones (TA-14.1)

OSS k6 runs from one machine. Run the script twice with different `ZONE` labels (for example a GitHub-hosted runner as `gh-us`, and your own machine or a second-region runner as `eu`), then compare the two `summary.json` files. Each run applies the same p95 < 500 ms threshold. Grafana Cloud k6 load zones would be a new paid service (escalation, see the plan).

### Upload 5 GB (TA-15)

The upload is a **guest** draft + finalize (no session), so it is not charged against any storage quota. Guest drafts use the free plan's limits; `/billing/plan` needs a session, so `setup()` reads the sizes from the load user's `GET /billing/plan` after asserting its `planCode` is `free`. The size is `limits.maxTransferSize`, split into files of at most `limits.maxSingleFile`. One 8 MiB buffer is reused for every block, so memory stays flat. A GitHub runner's uplink is typically faster than 100 Mbps, so the 10-minute gate is lenient there. Use `upload_mbps` to extrapolate, and run once from a real 100 Mbps line for the honest proof. Each run leaves a 5 GB guest draft that expires after 30 days.

### Email burst: what k6 can and cannot prove

- k6 measures only the **API accept** latency of each send. It cannot see an inbox.
- `RECIPIENTS_PER_SEND` must equal the plan limit `limits.free.maxEmails` (Admin, Flags). It sizes the VU count at init, where no HTTP is allowed. When `E2E_ADMIN_EMAIL` / `E2E_ADMIN_PASSWORD` are set and that account has the admin role, `setup()` cross-checks it against the live flag and fails on a mismatch. `TOTAL` / `RECIPIENTS_PER_SEND` = number of sends (50 with the defaults).
- `BURST_RECIPIENT_TEMPLATE` must point at mailboxes you own (for example plus-addressing: `me+burst{n}@yourdomain`). Never send to `example.com`: it bounces and hurts sender reputation. The script refuses to run without the template.
- Set a unique `BURST_RUN_ID` per run (for example the GitHub run id). The trace id of send `i` is `md5("<BURST_RUN_ID>:<i>")`, and `burst-manifest.json` lists all of them.
- Staging uses an Azure-managed email domain, whose send quotas are very low. A 1k burst will be throttled; a realistic burst needs a custom domain or a quota raise (Preflight P-06/P-07).

#### Send to hand-off (post-run KQL, App Insights)

The manifest's `traceIds` are the W3C trace ids the sends carried (`traceparent`). Paste them into the `traces` list. Verify the join on the first run; if `operation_Id` is empty for the worker's `email_sent` events, fall back to a time-window join (`endedAt` minus `testRunDurationMs` to `endedAt` plus 5 minutes).

```kusto
let traces = dynamic([ /* paste burst-manifest.json "traceIds" here */ ]);
let sends = requests
    | where operation_Id in (traces) and name has "send"
    | project operation_Id, sendAt = timestamp;
let mails = customEvents
    | where name == "email_sent" and operation_Id in (traces)
    | summarize lastMailAt = max(timestamp), mails = count() by operation_Id;
sends
| join kind=inner mails on operation_Id
| extend latencySec = datetime_diff('second', lastMailAt, sendAt)
| summarize p50 = percentile(latencySec, 50), p95 = percentile(latencySec, 95), sends = count(), mails = sum(mails)
```

This measures send to provider hand-off, not delivery to the inbox. True send to inbox (TA-15, p95 < 60 s) additionally needs deliverable mailboxes and Communication Services diagnostic logs (Email status update logs to Log Analytics). `communication.bicep` has no diagnostic setting today, so that is an infra follow-up.

### Known limits

- The API is reachable directly (external ingress, no Front Door restriction). That is deliberate for load, and a separate infra-review item.
- Do not run `api-latency.js` through Front Door: the WAF would throttle it and the thresholds would fail for the wrong reason.

## Owner setup checklist (GitHub environment `staging`)

Everything below is an owner step; the workflows only read these values.

| Name | Kind | Value |
|---|---|---|
| `STAGING_API_URL` | variable | Full `https://` origin of the API Container App (the `apiFqdn` output of `cd-staging.yml`), no trailing slash. This is the `API_BASE_URL` of the k6 scripts (direct origin, bypasses Front Door). |
| `PUBLIC_URL` | variable | Front Door origin (`https://...`), used as `PUBLIC_BASE_URL` and as `E2E_BASE_URL`. |
| `ADMIN_EMAILS` | variable | Must include the e2e admin address (`Wa__AdminEmails`). |
| `E2E_ADMIN_EMAIL`, `E2E_ADMIN_PASSWORD` | secrets | The e2e admin login; confirm the email once by magic link (see above). |
| `LOAD_USER_EMAIL`, `LOAD_USER_PASSWORD` | secrets | A synthetic load account (for example `k6-load@example.com`); created on first use. |
| `BURST_RECIPIENT_TEMPLATE` | secret | Must contain `{n}` and point at mailboxes you own, for example `me+burst{n}@yourdomain`. |
| `RECIPIENTS_PER_SEND` | required input of `email-burst.js` | Must equal the plan's `limits.free.maxEmails` (Admin, Flags); it is not discovered at run time. |

`summary.json` is built from a whitelist (`metrics`, `state`, `root_group`); `setup_data` (which carries the load user's session token) is never written. `burst-manifest.json` contains the run id and trace ids only.
