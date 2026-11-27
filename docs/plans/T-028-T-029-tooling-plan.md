# T-028 / T-029 tooling — Playwright `@staging` suite + k6 load scripts

**Type:** M4 milestone tooling (Milestone-Backlog rows T-028 and T-029, `docs/Milestone-Backlog.md:61-62`). Owner authorized the Claude pipeline for pre-prod work on 2026-11-26.
**Branch:** `feature/T-028-T-029-staging-tests` (worktree `.claude/worktrees/e2e`, cut from origin/develop @ 415fc13, which includes PR #711 `infra/bicep` + `.github/workflows/cd-staging.yml`). **Plan date:** 2026-11-26.
**Scope note:** this task builds the suites so they run against staging. Running them green on staging (the row exit checks) waits for Azure provisioning (Preflight P-01/P-03/P-12) and for the two SPA blockers in §5 R1/R2.
**Escalations:** two DECISION blocks in §5 (staging admin account, second k6 zone). Neither blocks the tooling. No new npm/NuGet package: k6 is a CI binary installed by a GitHub Action, not a package (§2 D9).

## 1. Goal

Add (a) a Playwright `@staging` suite covering the five TA-14.1 E2E scenarios over HTTP and UI only, against any base URL, and (b) k6 scripts plus a dispatch workflow for the TA-14.1 load bullets, with TA-15 targets as k6 thresholds.
**Exit check (this task):** `npm run lint && npm run test:run && npm run build` stay green. Playwright list mode (`npx playwright test` plus the list option, see §3 step 7) lists the existing local specs **and** the new `@staging` specs. With E2E_BASE_URL set, list mode with `-g @staging` lists only the staging projects. The local projects are unchanged (same files, same globalSetup). The k6 scripts pass `k6 inspect` in CI (k6 is not installed locally, see §3 step 9).

## 2. Design & Decisions

**D1. A separate `e2e/remote/` directory and separate projects. The existing local specs are not touched.** The existing specs need the local stack: direct SQL (`e2e/sql.ts`), Azurite SDK seeding (`e2e/seed-transfer.ts:15-27`) and the Azurite CORS globalSetup (`e2e/global-setup.ts`). The new specs use only HTTP and UI, so they live in src/wa.web/e2e/remote/ and run as three projects: `staging-setup`, then `staging`, then `staging-teardown` (Playwright project `dependencies` / `teardown`). The local projects get `testIgnore` for `remote/`. The old `e2e/staging.spec.ts` is a local-stack spec despite its name. It has no `@staging` tag, so the CD grep filter for `@staging` never selects it. Leave it alone (renaming it would be a drive-by change).

**D2. Config switch on E2E_BASE_URL** (`src/wa.web/playwright.config.ts`).
- **Unset (local):** today's config, byte-for-byte, for the three local projects. The staging projects are present too, so list mode shows them, but every remote test calls `test.skip(!REMOTE, …)`. A local `npx playwright test` reports them as skipped, and nothing else changes.
- **Set (CD):** baseURL = E2E_BASE_URL. Only the staging projects are defined, because the local specs need SQL. globalSetup is kept only when the host is `localhost` or `127.0.0.1`. The Azurite CORS rule is needed only there, so the remote suite can also self-validate against a running local stack with `E2E_BASE_URL=http://localhost:5173`. In CD the globalSetup must not run: it would try to reach Azurite at `127.0.0.1:10000` and fail the job (`e2e/global-setup.ts:16-29`). The current `cd-staging.yml` e2e job would hit exactly that failure today.
- **The API base for the HTTP helpers** reuses the existing E2E_API_URL convention (`e2e/seed-transfer.ts:22`, a value without `/api/v1`). Remote default: same origin as E2E_BASE_URL, because Front Door routes `/api/*` and `/*` on one host (`infra/bicep/modules/frontdoor.bicep:1`). Local default: `http://localhost:8080`.

**D3. Every test title carries `@staging`, setup and teardown tests included.** The grep filter (`-g`) also applies to dependency projects. Without the tag, the setup would be filtered out in CD and the dependents would run with no auth state.

**D4. Accounts and the auth rate budget.** The API allows 5 requests per minute per IP on `*:/api/v1/auth/*` (`src/wa.api/ApiServices.cs:129`). Front Door allows 10 per minute on `/api/v1/auth/` (`infra/bicep/modules/frontdoor.bicep:94`). So the suite makes all auth calls once, in `staging-setup`, and nowhere else.
- **Owner:** a fresh account per run, `e2e-owner-<runId>@example.com` (TA-14.3: no PII). The password comes from `crypto.randomUUID()`. runId = GITHUB_RUN_ID or else `Date.now()`, plus 4 random hex characters.
- **Calls:** `POST /auth/signup` (API, creates the row), then **UI** sign-in at `/account` (`src/wa.web/src/features/auth/authRoute.ts:7`; sign-in is the default tab, `AuthPage.tsx:28`). The UI sign-in is required: signup sets **no** cookie (`src/wa.api/Endpoints/AuthEndpoints.cs:24-45`, no `Cookies.Append`), while login sets wa.session (`:126-133`) and the SPA writes the protoDrop.session mirror. `storageState` then holds both and is saved to `e2e/.auth/owner.json`. The `sessionToken` from the login response body (`:136-144`) is saved too, for HTTP calls.
- **Admin:** E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD. If both are set, setup makes one `POST /auth/login`. Only on `401 WRONG_PASSWORD` does it bootstrap the account: `POST /auth/signup` plus a second login. The token goes to `e2e/.auth/session.json`. If either is unset, the admin-dependent spec skips with a clear reason. The account gets its SuperAdmin role from `Wa__AdminEmails` (`infra/bicep/modules/apps.bicep:118`, set from vars.ADMIN_EMAILS in `cd-staging.yml`). See DECISION 1.
- **Teardown:** `DELETE /api/v1/auth/me` with the owner cookie (`src/wa.api/Endpoints/AccountEndpoints.cs:24-38`, 200).
- **Budget:** 2 owner + 1–2 admin + 1 teardown calls = at most 5 per run, and the teardown runs minutes later. Defence: the helper `authFetch` retries **once** on 429 and honours `Retry-After` (capped at 65 s).

**D5. HTTP calls send an explicit `Cookie: wa.session=<token>` header, not the request-context cookie jar.** The cookie is `Secure` (`AuthEndpoints.cs:129`). An explicit header behaves the same on https staging and on http localhost.

**D6. Seeding goes through the real public API, with no DB.** `seedTransfer(api, { files, password?, sessionToken?, emails? })`:
1. `POST /api/v1/transfers/draft` with `{ files: [{ name, sizeBytes, contentType }] }` → `{ draftId, files[{fileId, uploadUrl}] }` (`TransferWriteEndpoints.cs:20-33`).
2. For each file, a single-shot `PUT uploadUrl` with `x-ms-blob-type: BlockBlob` and `x-ms-version: 2024-08-04` (the SAS is `sp=cwr`). If the minted URL is `http:` (Azurite, local only), re-sign the protocol row first, with the same string-to-sign as `src/wa.web/src/features/landing/StagingList.tsx:74-86`, using node:crypto HMAC and the Azurite dev key already in `e2e/global-setup.ts:15`. Staging minted URLs are https and are used as-is.
3. `POST /transfers/draft/{draftId}/finalize` with header `Idempotency-Key: e2e-<draftId>` and body `{ files: [{ fileId, name, sizeBytes, contentType }] }` (`TransferWriteEndpoints.cs:37-75`) → `{ id, linkId, publicUrl }`.
4. `POST /transfers/{id}/send` with `{ emails: [], senderName: 'E2E', password? }` (`:80-94`). An empty `emails` list is a link-only send.

If `sessionToken` is passed, every call carries the owner cookie, so the transfer is owned (finalize and send read the owner from the session, `:83`). File contents are small, deterministic text with a unique name per run. Draft calls per run: about 5, under the Front Door limit of 10 per minute (`frontdoor.bicep:95`).

**D7. Scenario mapping (TA-14.1 E2E bullet, `docs/03-technical-architecture.md` TA-14.1).**

| Spec | Scenario | Path | Key assertions |
|---|---|---|---|
| `guest-loop.spec.ts` | guest upload → link → download on a second context (no cookie) | **UI** upload on `/` (`input[type=file]`, `.sl-send`, `StagingList.tsx:276`). LinkScreen `.ls-url` (`LinkScreen.tsx:280`) shows the link; `.ls-send-btn` (`:426`) activates it. Then a fresh `browser.newContext()` (empty storage) opens the link. | `.rp-row` count 1 and `.rp-name` = file name (`RecipientPage.tsx:287-295`). Clicking `.rp-download` fires a Playwright `download` event, and the downloaded bytes equal the uploaded bytes. The second context's cookies contain no wa.session. |
| `password.spec.ts` | password flow | API seed with a password. Recipient context. | Before unlock: `.rp-password-card` visible, `.rp-list` hidden (`RecipientPage.tsx:221`). Wrong password → `.rp-password-error` (`:258`). Correct password (`#rp-password-input`, `.rp-password-submit`, `:226-260`) → `.rp-list` visible. After reload `.rp-list` is still visible. Mirrors local `recipient.spec.ts:57-77`. |
| `expiry-flag.spec.ts` | expiry via admin flag override | See D8. | `.rp-terminal` visible (`RecipientPage.tsx:197`), `.rp-list` and `.rp-row` absent. The public GET returns 410 with code MAX_DOWNLOADS_REACHED (`PublicTransferEndpoints.cs:46-47`). |
| `my-files.spec.ts` | My Files CRUD | Owner `storageState`. **C:** owned API seed. **R:** `/files` (`filesRoute.ts:9`) shows a `.mf-row` with the name (`FilesPage.tsx:352`). **U:** the Resend button (aria-label files.action.resend, `:364`) leads to the resend screen; `.ls-send-btn` sends; back on `/files` there are 2 rows with the name. **D:** the delete button (aria-label files.action.deleteRow, `:374`) opens the modal; `.pf-modal-confirm` (`:52`) confirms. | After delete: one fewer row. Public GET on the deleted link returns 404. Labels are read from `en.json` (D10), never hard-coded. |
| `i18n-de.spec.ts` | i18n spot-check (de) | `browser.newContext({ locale: 'de-DE' })`. Detection order: account setting, then navigator.language (`src/wa.web/src/core/i18n/init.ts:7-8,67`). | Landing title = de landing.dropzone.title. On a seeded link: Download all = de recipient.active.downloadAll. On an unknown link (`/t/ZZZZZZZZ`): terminal CTA = de recipient.terminal.cta. No console warning matching `[i18n] missing key` (`init.ts:95`). The `<html lang>` check is optional: only if the app sets it (Coder checks; do not add it to the app). |

**D8. "Expiry via admin flag override" uses the maxDownloads flag, not retention.** True time-based expiry cannot be observed inside a test.
- Retention is in whole days. A limit flag must be greater than 0 (`src/wa.application/Limits/FlagValueRules.cs:63-77`). Expiry is stamped at send as `now + RetentionDays` (`src/wa.domain/Transfers/SendTimeLimits.cs`). The f-expire job, including the signed HTTP trigger `POST /api/jobs/expire` (`JobAuth`, `x-job-sign` against `Wa:Jobs:SignKey`), only expires rows already past `ExpiresAtUtc`.
- The flag override that **does** drive a terminal recipient state inside a test is `limits.free.maxDownloads = 1`. It is stamped at send (`SendTimeLimits.cs`, `transfer.MaxDownloads = limits.MaxDownloads`). One counted download flips the row to Status 3, the download limit (`GetFileDownloadUrlHandler.cs:66-72`, `GetPublicTransferHandler.cs:74-75`).
- Time-based expiry stays proven by the integration tests and the local `lifecycle.spec.ts` (T-005-04, SQL-driven). Logged in §5 R5 as a documented interpretation.

Spec steps:
1. Admin `GET /api/v1/admin/flags` (`AdminEndpoints.cs:329-337`, wire `{ key, value, … }`). Read the current value of limits.free.maxDownloads, so no literal limit appears (golden rule 2).
2. `PUT /api/v1/admin/flags/limits.free.maxDownloads` with raw body `1` (`AdminEndpoints.cs:341-348`, raw string body, SuperAdmin only).
3. Poll: seed a guest transfer, then `GET /api/v1/public/transfers/{linkId}` until `downloadsLeft === 1` (`GetPublicTransferHandler.cs:104`). Re-seed every 10 s, for at most E2E_FLAGS_TTL_SECONDS (default 30, the `Wa:Flags:TtlSeconds` fallback at `ApiServices.cs:62`) + 30 s. The flag cache is per replica (`PlansCache`), so polling is more reliable than one fixed sleep.
4. **`finally`: PUT the original value back**, right after step 3. This is safe because live transfers never re-resolve their limits (FR-018-4, `SendTimeLimits.cs` guard).
5. Recipient context: count one download through the API, `GET …/files/{fileId}/download-url` → 200. Then `page.goto('/t/{linkId}')` and assert per D7.

The download is triggered through the API, not a UI click, so this scenario is independent of the SPA SAS bug (§5 R1). The guest loop covers the UI download. Side effect: for about 30–60 s, other staging free-plan sends also get maxDownloads=1. Staging only; documented.

**D9. k6 location, shape and CI dependency.**
- **Location:** scripts live in tests/load/. That keeps them out of src/wa.web, so no eslint or tsc, and it matches the "tests" tree. MSBuild ignores them: `tests/Directory.Build.props` affects only projects, and `src/wa.slnx` lists projects explicitly. Plain ES-module JavaScript, k6 built-ins only (`k6/http`, `k6/metrics`, `k6/encoding`, `k6/crypto`). No npm, no bundler.
- **CI dependency:** `grafana/setup-k6-action` is a GitHub Action that installs the OSS k6 binary on the runner. It is not an npm or NuGet package, so golden rule 1 does not apply. Preflight P-11 already says "OSS k6 suffices".
- **Targets:**
  - API_BASE_URL = the **direct** Container Apps API FQDN (`cd-staging.yml` output `apiFqdn`, ingress `external: true`, `apps.bicep:89-90`). This is for load. Through Front Door, the WAF blocks above 30, 10 and 10 requests per minute per IP on public, draft and auth paths (`frontdoor.bicep:93-98`). Load through Front Door would measure the WAF, not the API.
  - PUBLIC_BASE_URL = Front Door. This is for recipient TTFB, which is what users actually hit. `/t/{linkId}` is on the SPA route, so it is not WAF-limited. The public API probe through Front Door is paced at 20 per minute, under the 30-per-minute limit.

| Script | What | Thresholds (TA-15 / TA-14.1) |
|---|---|---|
| `api-latency.js` | Two `constant-arrival-rate` scenarios, RATE default 20/s for DURATION default 5m. **public:** `GET /api/v1/public/transfers/{linkId}` for a link seeded in `setup()`. **authed:** `GET /api/v1/transfers` and `GET /api/v1/transfers/{id}` with the load user cookie. Never `/auth/me`, which is rate-limited. | `http_req_duration{kind:api}`: `p(50)<100`, `p(99)<500`. `http_req_failed{kind:api}`: `rate<0.01`. |
| `recipient-ttfb.js` | `GET ${PUBLIC_BASE_URL}/t/{linkId}` (HTML) at 1/s for 3m. Plus a public API probe through Front Door at 20/min. Every request is tagged `zone:${ZONE}`. | `http_req_waiting{kind:page}`: `p(95)<500`. `http_req_waiting{kind:public_api}`: `p(95)<500`. |
| `upload-5gb.js` | 1 VU, 1 iteration. Size = the load user's limits.maxTransferSize from `GET /api/v1/billing/plan` (`BillingEndpoints.cs:100-111`), which is the free plan's 5 GB. Taking it from there avoids a literal limit. The flow: guest draft (no cookie) → `PUT {uploadUrl}&comp=block&blockid=…` in rounds of http.batch with 4 parallel puts → `PUT {uploadUrl}&comp=blocklist` (XML) → finalize. The block size and parallelism match the SPA (`e2e/helpers.ts:8` 8 MiB, `UploadEngine.ts:22` `MAX_PARALLEL_BLOCKS = 4`). **One** 8 MiB `ArrayBuffer` is allocated once and reused for every block, so the 5 GB never sits in memory. Block ids: `b64encode(String(i).padStart(6,'0'))`, all the same length. | Custom Trend `upload_duration_ms` (draft start → finalize 201): `max<600000`, the 10-minute target. Trend `upload_mbps` is reported, not gated. Request timeouts: block PUT 120s, finalize 600s (server-side copy). |
| `email-burst.js` | `setup()` (setupTimeout 15m) prepares `ceil(TOTAL/perSend)` finalized guest transfers, each with one tiny file. TOTAL defaults to 1000. perSend = limits.free.maxEmails read from the admin `GET /admin/flags`, or the RECIPIENTS_PER_SEND env. With the free plan's 20 that is 50 sends. The default function then fires all sends at once (`per-vu-iterations`, VUs = sends, 1 iteration). Each send carries its own `traceparent` (`00-<32hex>-<16hex>-01`). Recipients come from BURST_RECIPIENT_TEMPLATE (`{n}` placeholder, required, the script fails fast when unset). `handleSummary` writes `burst-manifest.json`: traceIds, send start/end. | `http_req_duration{kind:send}`: `p(95)<1000` (API accept). **Send → inbox p95 < 60 s is evaluated after the run** with the KQL in `tests/load/README.md` (§5 R4). k6 cannot see an inbox. |

**D10. No hard-coded UI strings in specs.** Labels and copy are read from `src/wa.web/src/core/i18n/locales/{en,de}.json`. These are flat keys, for example `"landing.staging.send"` at `de.json:26`. Read them with `JSON.parse(fs.readFileSync(new URL('../../src/core/i18n/locales/en.json', import.meta.url), 'utf8'))`. This is ESM (`package.json` `"type": "module"`) and follows the import.meta.url pattern in `e2e/global-setup.ts:11`.

**D11. Secrets never land in artifacts.** The staging projects use `trace: 'off'` and `screenshot: 'only-on-failure'`. A trace records API request headers, which include the session cookie. The repo is public, and Actions artifacts are downloadable. `e2e/.auth/` is gitignored and must never be uploaded.

**Codebase-design check.** `remote/api.ts` is the one deep module for staging HTTP: auth, retry, seeding and flags. The specs only call `seedTransfer`, `login…` and `withFlagOverride`. They never build URLs or cookies themselves. On the k6 side, `tests/load/lib/api.js` is the same seam. No production module changes.

## 3. Implementation Plan

1. **`src/wa.web/e2e/remote/env.ts` (CREATE).** Exports:
   - REMOTE: true when E2E_BASE_URL is set.
   - BASE_URL and API_URL per D2.
   - IS_LOCAL_HOST, RUN_ID (D4), ADMIN (`{email,password} | null`), FLAGS_TTL_SECONDS.
   - AUTH_DIR: `e2e/.auth`, resolved via import.meta.url.
   - `requireRemote()`: wraps `test.skip(!REMOTE, 'E2E_BASE_URL not set — @staging suite runs only against a deployed URL')`.

   **Verify:** `tsc -b` does **not** cover e2e (`tsconfig.app.json` include is `src`). Type checking for e2e comes from Playwright's loader in list mode, and from eslint.
2. **`src/wa.web/e2e/remote/api.ts` (CREATE).** Built on `request.newContext({ baseURL: API_URL })` (Playwright `APIRequestContext`, no new deps). Functions:
   - `authFetch` (one 429 retry per D4).
   - `signup(email,pw)`, `login(email,pw) → token`.
   - `sessionHeaders(token)` → `{ Cookie: 'wa.session=' + token }`.
   - `seedTransfer(opts) → { id, linkId, fileIds, names }` (D6, including the Azurite http re-sign helper).
   - `getFlag(adminToken,key)`, `setFlag(adminToken,key,value)`, `withFlagOverride(adminToken,key,value,fn)` (try/finally restore).
   - `publicGet(linkId)`, `mintDownload(linkId,fileId)`, `deleteMe(token)`.
   - `readSession()` / `writeSession()` for `e2e/.auth/session.json`.

   Throw with the response status and body on any unexpected non-2xx, so failures are self-explaining. No `any` (eslint tseslint.configs.recommended).
3. **`src/wa.web/e2e/remote/auth.setup.ts` (CREATE).** `test('@staging setup — owner + admin sessions', …)` with `requireRemote()`:
   - Owner signup through the API, then UI sign-in at `/account`: `#auth-email`, `#auth-password`, `.au-continue-btn` (`AuthPage.tsx:206-236`). Wait until the URL is `/`.
   - `page.context().storageState({ path: AUTH_DIR/owner.json })`.
   - The owner token comes from page.waitForResponse on `/auth/login` (body `sessionToken`).
   - Admin login or bootstrap per D4.
   - `writeSession({ ownerEmail, ownerToken, adminToken })`.

   **`src/wa.web/e2e/remote/auth.teardown.ts` (CREATE):** `test('@staging teardown — delete run owner', …)` calls `deleteMe(ownerToken)` and tolerates 401 and 404.
4. **The five specs (CREATE)** per D7/D8, each with `test.describe('… @staging', …)`, `requireRemote()`, and unique file names `e2e-${RUN_ID}-<scenario>.txt`.
   - `guest-loop.spec.ts`: build the fixture with `makeSparseFiles` from `../helpers` (`e2e/helpers.ts:24`, 8 KiB is fine), or write a small file in the temp dir. Compare bytes with `download.path()`.
   - `password.spec.ts`: wrong password, then right password.
   - `expiry-flag.spec.ts`: `test.skip(!ADMIN, 'E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD not set')`; `test.setTimeout(180_000)`.
   - `my-files.spec.ts`: `test.use({ storageState: owner.json })`.
   - `i18n-de.spec.ts`: listen to `page.on('console')` for missing-key warnings.
5. **`src/wa.web/playwright.config.ts` (MODIFY)** per D2/D3/D11. See §4 for the exact current file and the target shape.
6. **`src/wa.web/.gitignore` (MODIFY):** under the existing Playwright block, add `e2e/.auth/`.
7. **Web gate:**
   ```
   cd src/wa.web
   npm run lint && npm run test:run && npm run build
   npx playwright test <LIST> -g @staging            # nothing set: local + staging projects exist; the @staging tests are listed
   npx playwright test <LIST>                        # nothing set: every local spec is still listed (same count as before + the new ones)
   E2E_BASE_URL=https://example.invalid npx playwright test <LIST> -g @staging   # only staging-setup / staging / staging-teardown
   ```
   `<LIST>` is Playwright's list option: two ASCII hyphens followed by the word list (written this way only because verify_plan.py reads double-hyphen words as CSS tokens). The third command must show no `chromium` or `mobile-*` project. On Windows PowerShell set `$env:E2E_BASE_URL='https://example.invalid'` first and `Remove-Item Env:E2E_BASE_URL` after. List mode runs neither globalSetup nor tests. Do not run the suite: Docker is down.
8. **k6 (CREATE)**:
   - `tests/load/lib/config.js`: env parsing, required-var guard, ZONE tag, the TA-15 targets as named constants, each with a comment citing TA-15. Also a shared `summaryHandler(name)` that every script exports as `handleSummary`: it returns `{ 'summary.json': JSON.stringify(data, null, 2), stdout: <one line per threshold: name, ok/FAILED> }`. That is built-ins only, with no jslib remote import.
   - `tests/load/lib/api.js`: `login`, `signupIfNeeded`, `createDraft`, `putBlob` (small), `putBlocks` (D9), `commitBlockList`, `finalize`, `send`, `planLimits`, `adminFlag`, `newTraceparent` (`k6/crypto` `randomBytes`, hex).
   - The four scripts per D9.
   - `tests/load/README.md`: env vars per script, run commands, the KQL for send→inbox, what stays manual.
9. **k6 validation:** k6 is **not installed** on this machine (`which k6` → none). The Coder does not install it, because that is an owner machine change. Validation is CI-only: the orchestrator's `k6 inspect` job (§4 orchestrator follow-ups). The Coder checks syntax with `node -c tests/load/<file>.js` for every file. They are ES modules, so `node -c` parses them. k6-only imports are not resolved, which is acceptable as a syntax gate.
10. **Commit:** one `feat: T-028/T-029 tooling — Playwright @staging suite + k6 load scripts` containing only the FILES FOR CODER paths.

## 4. Files

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.web/playwright.config.ts` | MODIFY | E2E_BASE_URL switch: conditional globalSetup, baseURL, local projects ignore `remote/`, three staging projects (D2/D3/D11) |
| `src/wa.web/.gitignore` | MODIFY | add `e2e/.auth/` under the Playwright block |
| `src/wa.web/e2e/remote/env.ts` | CREATE | env resolution + `requireRemote()` (step 1) |
| `src/wa.web/e2e/remote/api.ts` | CREATE | HTTP helper module: auth with 429 retry, seeding, flags, public/download, session file (step 2, D4–D6) |
| `src/wa.web/e2e/remote/auth.setup.ts` | CREATE | `@staging` setup: owner signup + UI login + storageState, admin login/bootstrap (step 3) |
| `src/wa.web/e2e/remote/auth.teardown.ts` | CREATE | `@staging` teardown: delete run owner |
| `src/wa.web/e2e/remote/guest-loop.spec.ts` | CREATE | guest UI upload → link → second-context download (D7) |
| `src/wa.web/e2e/remote/password.spec.ts` | CREATE | password gate wrong/right/refresh (D7) |
| `src/wa.web/e2e/remote/expiry-flag.spec.ts` | CREATE | maxDownloads flag override → terminal state + 410 (D8) |
| `src/wa.web/e2e/remote/my-files.spec.ts` | CREATE | My Files create/read/resend/delete (D7) |
| `src/wa.web/e2e/remote/i18n-de.spec.ts` | CREATE | de locale spot-check from de.json keys (D7, D10) |
| `tests/load/lib/config.js` | CREATE | env + TA-15 target constants |
| `tests/load/lib/api.js` | CREATE | k6 API/blob helpers (D9) |
| `tests/load/api-latency.js` | CREATE | public + authed read latency, p50/p99 thresholds |
| `tests/load/recipient-ttfb.js` | CREATE | recipient page TTFB via Front Door, zone-tagged |
| `tests/load/upload-5gb.js` | CREATE | streamed-block 5 GB upload, 10-min threshold |
| `tests/load/email-burst.js` | CREATE | 1k-recipient burst + manifest for KQL |
| `tests/load/README.md` | CREATE | how to run, env, KQL, manual steps |

**`src/wa.web/playwright.config.ts`: current file (whole, lines 1-26):**

```ts
import { defineConfig, devices } from '@playwright/test';

// T-001-09: the local stack (API :8080, Vite :5173, SQL + Azurite) is launched externally —
// docker-compose.local.yml + supervised services — so no webServer key here.
export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts', // Azurite ships with CORS off; set the Vite-origin rule before any test.
  timeout: 90_000, // AC-001-1 uploads ~2 GiB of sparse blocks to Azurite; AC-001-3 adds the full 7.5 s backoff schedule.
  expect: { timeout: 30_000 },
  workers: 1, // rows live in one page's module state — serial keeps drafts deterministic.
  fullyParallel: false,
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' }, testIgnore: /mobile\.spec\.ts/ },
    { name: 'mobile-iphone-14', use: { ...devices['iPhone 14'] }, testMatch: /mobile\.spec\.ts/ },
    {
      name: 'mobile-pixel-7',
      use: { ...devices['Pixel 7'], permissions: ['clipboard-read', 'clipboard-write'] },
      testMatch: /mobile\.spec\.ts/,
    },
  ],
});
```

Target shape (keep the existing comments; the local entries stay identical except for the added `remote/` ignore):

```ts
const remoteBaseUrl = process.env.E2E_BASE_URL?.trim().replace(/\/+$/, '') || undefined
const localHost = !remoteBaseUrl || /^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(remoteBaseUrl)
const REMOTE_DIR = /[\\/]remote[\\/]/

const localProjects = [
  { name: 'chromium', use: { browserName: 'chromium' as const }, testIgnore: [/mobile\.spec\.ts/, REMOTE_DIR] },
  { name: 'mobile-iphone-14', use: { ...devices['iPhone 14'] }, testMatch: /mobile\.spec\.ts/ },
  { name: 'mobile-pixel-7', use: { ...devices['Pixel 7'], permissions: ['clipboard-read', 'clipboard-write'] }, testMatch: /mobile\.spec\.ts/ },
]
const stagingUse = { browserName: 'chromium' as const, trace: 'off' as const, screenshot: 'only-on-failure' as const }
const stagingProjects = [
  { name: 'staging-setup', testMatch: /remote[\\/]auth\.setup\.ts/, use: stagingUse, teardown: 'staging-teardown' },
  { name: 'staging-teardown', testMatch: /remote[\\/]auth\.teardown\.ts/, use: stagingUse },
  { name: 'staging', testMatch: /remote[\\/].*\.spec\.ts/, dependencies: ['staging-setup'], use: stagingUse, retries: 0 },
]
// defineConfig({ …, globalSetup: localHost ? './e2e/global-setup.ts' : undefined,
//   use: { baseURL: remoteBaseUrl ?? 'http://localhost:5173', trace: 'retain-on-failure' },
//   projects: remoteBaseUrl ? stagingProjects : [...localProjects, ...stagingProjects] })
```

Context for the config change:
- The mobile projects already only match `mobile.spec.ts`, so they never pick up `remote/`.
- The default `testDir` stays `./e2e`. Setup and teardown files are not `*.spec.ts`, so the local `chromium` project's default testMatch (`*.spec.ts` / `*.test.ts`) never selects them.
- The `teardown` project property needs Playwright ≥ 1.34. Installed: `^1.64.1` (`package.json:24`).

**`src/wa.web/.gitignore`: current Playwright block (end of file):**

```
# Playwright (T-001-09): local-stack artifacts, never committed.
test-results/
playwright-report/
```
Add `e2e/.auth/` with a comment: "T-028: staging session state (cookies/tokens) — never committed or uploaded".

**Non-obvious context for the CREATE files** (already discovered, so no re-search is needed):
- Wire shapes: draft `TransferWriteEndpoints.cs:33`, finalize `:62`, send `:111` (`{ id, linkId, status, expiresAt, … }`). Signup and login `AuthEndpoints.cs:29-36,136-144`. Admin flags list `AdminEndpoints.cs:336`. Plan limits `BillingEndpoints.cs:106-113`. Public GET: 200 with `downloadsLeft` (`GetPublicTransferHandler.cs:104`), 410 or 404 Problem+JSON (`PublicTransferEndpoints.cs:42-50`).
- Finalize needs `Idempotency-Key`: the endpoint reads it at `TransferWriteEndpoints.cs:39`, and the middleware replays it (`IdempotencyMiddleware.cs:103-108`). Use a unique key per draft.
- Guest draft size limits are always free-plan (`CreateDraftHandler.cs:41`).
- Use limits.free.maxDownloads exactly as the flag key (TA-13.2 seeded keys).
- k6: `http.put(url, arrayBuffer, { headers, timeout })`. `http.batch([...])` takes the same per-request params. Put Block List body: `<?xml version="1.0" encoding="utf-8"?><BlockList><Latest>{id}</Latest>…</BlockList>`.

**Orchestrator follow-ups (not Coder files).** The orchestrator owns `.github/workflows/*` and docs.
1. **`.github/workflows/cd-staging.yml`, e2e job:** add `E2E_ADMIN_EMAIL: ${{ secrets.E2E_ADMIN_EMAIL }}` and `E2E_ADMIN_PASSWORD: ${{ secrets.E2E_ADMIN_PASSWORD }}` to the `Playwright @staging` step env. If a report artifact is ever added, upload `playwright-report/` only. Never upload `test-results/` or `e2e/.auth/`.
2. **New `.github/workflows/load-staging.yml`:** `workflow_dispatch` with inputs `script` (choice: api-latency, recipient-ttfb, upload-5gb, email-burst) and `zone` (string, default `gh-${{ runner.os }}`). Settings: `environment: staging`, `runs-on: ubuntu-latest`. Steps:
   - `actions/checkout@v4`
   - `grafana/setup-k6-action@v1`
   - `k6 run tests/load/${{ inputs.script }}.js` (each script's `handleSummary` writes `summary.json`), with env `API_BASE_URL: ${{ vars.STAGING_API_URL }}`, `PUBLIC_BASE_URL: ${{ vars.PUBLIC_URL }}`, `LOAD_USER_EMAIL/PASSWORD`, `E2E_ADMIN_EMAIL/PASSWORD`, BURST_RECIPIENT_TEMPLATE (secrets), `ZONE: ${{ inputs.zone }}`
   - upload `summary.json` and `burst-manifest.json`. These contain no tokens.
3. **`.github/workflows/ci.yml`:** add a cheap `k6-inspect` job: setup-k6-action, then `for f in tests/load/*.js; do k6 inspect "$f" > /dev/null; done`. It needs no Azure. It is the PR-time self-validation for the k6 scripts.
4. **Docs close-out:** add the new env vars, secrets and owner steps to `Preflight-Checklist.md`; note D8 (expiry interpretation) and the DECISION outcomes in `Open-Decisions-and-Constants.md` Part 3; add the TA-14.1 E2E/Load notes in `03-technical-architecture.md`. REPO-FACTS has no closed-set change.

## 5. Open Questions / Risks

```
DECISION: How does staging get the e2e admin account (needed only by expiry-flag.spec.ts)?
CONTEXT: The expiry scenario needs a SuperAdmin to PUT a flag. Role = membership in Wa__AdminEmails (vars.ADMIN_EMAILS). Credentials must be GitHub environment secrets. Staging is public, and the signup endpoint currently issues a session for an EXISTING email without checking the password (R3), so any listed admin email is a takeover target until R3 is fixed.
OPTIONS:
  A) Dedicated synthetic admin e2e-admin@example.com in vars.ADMIN_EMAILS + secrets E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD; the suite bootstraps it via signup on first run — only after R3 is fixed.
  B) No admin on staging; expiry-flag.spec.ts stays skipped and the scenario is proven only by integration tests — T-028 exit check stays partially unmet.
RECOMMEND: A, gated on the R3 fix — it is the only way to run "expiry via admin flag override" on staging; the spec skips cleanly until the secrets exist.
BLOCKS: T-028 exit check only (tooling ships either way)
```

```
DECISION: How is "recipient TTFB from 2 zones" (TA-14.1) produced with OSS k6?
CONTEXT: OSS k6 runs from one machine; k6 Cloud load zones are a paid Grafana Cloud service (new external account + cost line). The script is zone-agnostic (ZONE tag), so either option works without code change.
OPTIONS:
  A) Two OSS runs: GitHub-hosted runner (US) + one run from the owner's machine or a second-region runner (EU), each with its own ZONE label; compare the summaries manually.
  B) Grafana Cloud k6 with two load zones — new service account and monthly cost (escalation per AGENT.md §7).
RECOMMEND: A — no new service or cost, and the evidence is the same thresholds per zone.
BLOCKS: none
```

- **R1, launch blocker (SPA, not in this task's scope): the SPA always re-signs SAS URLs with the Azurite dev key.** Upload: `StagingList.tsx:58-86` (`reSignForHttp`, called unconditionally at `:167`). Download: `RecipientStore.ts:60-…` (`browserDownloadUrl`, used at `:425`, `:482`). Both rewrite the minted URL to `/devstoreaccount1/...` signed with the Azurite key. Against real Azure Blob that returns 403, so **every browser upload and download on staging fails**. PR #713 (`fix/CHORE-deploy-hardening`, commit 218b039) only swaps the API base; the re-sign stays. `guest-loop.spec.ts` will fail on staging until this is fixed, which is the correct e2e outcome. Recommendation: a separate CHORE fix that re-signs only when the minted URL is `http:`. Other specs avoid browser blob traffic on purpose (D6/D8).
- **R2, verify before the first staging run:** the Storage account CORS must allow the Front Door origin for browser PUT and GET, and the PR #713 CSP `connect-src` must allow the blob host. Otherwise browser uploads fail even after R1 is fixed. Neither is in this task.
- **R3, security finding, critical: signup returns a session for an existing account without checking the password.** `SignupHandler.cs:36-82`: for a known email, the code sets `user = existing` and mints a token. The token is in the response body (`AuthEndpoints.cs:35`), and the session middleware accepts it as the wa.session cookie (`SessionMiddleware.cs:36`). Anyone who knows an email gets that account, including admins listed in `Wa__AdminEmails`. Related: the UI sign-up path never sets the cookie (`AuthEndpoints.cs:24-45`), so a UI sign-up leaves the user "signed in" client-side only. Both need a separate security fix before staging is exposed (AGENT.md §7 security-shaped → owner decision). The suite does not rely on either behaviour (D4 uses login for every session).
- **R4, email burst honesty.**
  - k6 measures API accept latency only.
  - Send → provider hand-off: the README ships KQL over App Insights. It joins the k6 `traceparent` trace ids (TA-10 correlation, T-012-01 worker log context) to the `email_sent` telemetry, and reports p95(email_sent time − send time). The join must be verified on the first run. Fallback: a time-window join.
  - True send → inbox needs deliverable mailboxes and Communication Services diagnostic logs (Email status update logs to Log Analytics). Today communication.bicep has no diagnostic setting, so that is an infra follow-up.
  - Staging uses an **Azure-managed email domain** (`communication.bicep:14-22`), whose send quotas are very low. A 1k burst will be throttled. A realistic burst needs a custom domain or a quota raise (P-06/P-07), which is owner-side.
  - BURST_RECIPIENT_TEMPLATE must point at owner-controlled mailboxes, for example plus-addressing. Sending to example.com would bounce and hurt sender reputation. The script refuses to run without it.
- **R5, expiry interpretation (D8).** The scenario proves the admin flag override driving a terminal recipient state (download limit, 410). It does not prove time-based expiry, which cannot be observed in a test because retention is ≥ 1 day. Record this in Open-Decisions Part 3 at close-out.
- **R6, upload target realism.** A GitHub runner's uplink is not 100 Mbps. It is usually faster, which makes `max<600000` lenient. `upload_mbps` is reported so the owner can extrapolate, and a run from a 100 Mbps line is the honest proof (manual). Each run leaves a 5 GB guest Draft transfer, which expires after 30 days (`FinalizeTransferHandler.cs:125`), costing about $0.10 per run in storage.
- **R7, the API is directly reachable, bypassing the WAF** (`apps.bicep:89-90`, external ingress with no Front Door ID restriction). D9 uses this for load on purpose. As a security posture it belongs in a separate infra review.
- **R8, shared staging state.** `expiry-flag.spec.ts` changes a global free-plan flag for about 60 s, then restores it in `finally`. A crashed runner could leave it at 1. Setup could add a defensive restore, but that needs the original value persisted. Accepted risk; noted in the README.
