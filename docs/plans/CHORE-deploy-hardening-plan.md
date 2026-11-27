# CHORE-deploy-hardening — plan

Source: `docs/reviews/PREPROD-readiness-audit.md` (untracked copy) items X-2, X-5 / P3-LOGSENDER, D-07 follow-up, plus the SPA API base needed by the one-host Front Door staging (PR #711). Owner authorized 2026-11-26 ("fix them all"). Branch `fix/CHORE-deploy-hardening` (from origin/develop 5b6bf45). Do NOT touch persistence stores (PR #712) or `infra/` (PR #711).

## 1. Goal

Make the build deployable behind one Front Door host and harden it: the SPA reads its API base from VITE_WA_API_BASE, nginx and the API emit security headers (CSP per TA-9.5), `Wa:Email:Sender=log` fails fast outside Local/Development and stops logging full addresses, and the Pro seed carries D-07's 20 GB. **Exit check:** the local gate is green (AGENT.md §4, minus the Docker-run integration suite, which runs in CI) and a recursive grep for "localhost:8080" over the .ts/.tsx files in src/wa.web/src (excluding *.test.* files) matches only `src/wa.web/src/core/config/apiBase.ts`.

## 2. Design & Decisions

**S1. API base (TA-8.2 self-contained features, TA-1.4 environments).** One leaf module `src/wa.web/src/core/config/apiBase.ts` exports API_BASE = `resolveApiBase(import.meta.env.VITE_WA_API_BASE)`. Blank or missing gives `http://localhost:8080/api/v1`, and a trailing `/` is stripped. Locally, in vitest and in e2e nothing changes, so the ~29 test assertions on the literal `http://localhost:8080/api/v1/...` keep passing without edits. Every hard-coded constant becomes a template literal API_BASE + "/...". Exported names that other files import stay the same (ME_BASE in `ProfilePage.tsx:19` is imported by DangerZone). `src/wa.web/src/vite-env.d.ts` adds a typed `ImportMetaEnv.VITE_WA_API_BASE?: string`. tsconfig already has `"types": ["vite/client"]`, so no triple-slash reference is needed. Vite inlines `VITE_*` from `the process environment` at build time, so the Dockerfile adds `ARG VITE_WA_API_BASE=http://localhost:8080/api/v1` plus `ENV` before `RUN npm run build`. The CD passes `/api/v1`. Out of scope: `src/wa.web/e2e/seed-transfer.ts` (Node-side helper against the local stack).

**S2. SPA headers (TA-9.5).** `src/wa.web/nginx.conf` becomes an envsubst template. The Dockerfile copies it to `/etc/nginx/templates/default.conf.template`. The official image's `20-envsubst-on-templates.sh` renders it to `/etc/nginx/conf.d/default.conf` at container start and substitutes only variables that are defined in the environment, so nginx's own `$uri` is left alone. Two runtime env vars, both with defaults set by ENV in the final stage:
- WA_BLOB_ORIGIN, default `http://127.0.0.1:10000 http://localhost:10000` (Azurite. StagingList/RecipientStore re-address to `http://127.0.0.1:10000`, `RecipientStore.ts:47`).
- WA_API_ORIGIN, default `http://localhost:8080` (local compose SPA calls the API cross-origin). Staging sets `'self'` because it is same-origin.

CSP: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: ${WA_BLOB_ORIGIN}; connect-src 'self' ${WA_API_ORIGIN} ${WA_BLOB_ORIGIN} https://dc.services.visualstudio.com; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`. The App Insights host is the fixed one in `clientTelemetry.ts:33`. Also: `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY`, `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`. **Trap:** a location with its own `add_header` drops every inherited one (`/assets/` at `nginx.conf:15`). So the full header block is repeated, with `always`, in BOTH locations. A vitest contract test parses the template and fails if any `location` block lacks any of the five headers.
- **`script-src 'self'` breaks the inline pre-paint theme script in `src/wa.web/index.html:12-24`.** Decision: move it verbatim to `src/wa.web/public/theme-init.js` and load it with a classic blocking `<script src="/theme-init.js"></script>` in `<head>`. It still runs before first paint, so AC-015-1/2 hold. This beats a `sha256-` hash, which breaks on any whitespace edit. Vite copies `public/` as-is and only warns about a non-module script. The contract test also asserts that `index.html` has no inline `<script>` without `src`.
- The Vite dev server (`npm run dev`) sends no CSP and is unaffected. Playwright runs against :5173 (Vite or the compose nginx). Under nginx, the defaults allow `localhost:8080` plus both Azurite hosts, so the flows keep working.
- AdSense (`adsenseLoader.ts:6`) is blocked by this CSP. It is off unless VITE_WA_ADSENSE_CLIENT is set at build time (D-14: ads off at launch). Widening the CSP is a follow-up for when ads ship (Open Questions).

**S3. API headers (TA-9.5, TA-2.1 rule 11 middleware layout).** New `src/wa.api/Middleware/SecurityHeadersMiddleware.cs` sets, before `_next`, on every response: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`. The API serves JSON only: no `text/html` or `Results.Content` in src/wa.api. `Strict-Transport-Security: max-age=2592000` is added only when the environment is neither Development nor `Local`.
- **Deviation from "UseHsts()":** `UseHsts` writes the header only when `Request.IsHttps`. Container Apps/Front Door terminate TLS and the app has no `UseForwardedHeaders`, so `UseHsts` would never fire, and turning on forwarded headers is a separate security decision. Emitting the header directly is safe because browsers ignore HSTS over plain HTTP (RFC 6797 §8.1). `max-age=2592000` (30 d) is `HstsOptions`' framework default, so no new constant is invented. No `includeSubDomains`/`preload`.
- The middleware is registered FIRST in `Program.cs` (before `CorsAllowListMiddleware`, `Program.cs:24`), so preflight 204s and limiter 429s also carry the headers. No middleware calls `Response.Clear()` (grep clean), so headers set up front survive GlobalError rewrites.

**S4. Log-sender guard (Open-Decisions Part 3 2026-11-17 open item, audit P3-LOGSENDER, TA-9.4).**
- `EmailSenderSelector` gets `public static void EnsureSenderAllowed(IConfiguration)`. If `Wa:Email:Sender == "log"` and the host environment is not `Development`/`Local` (case-insensitive), it throws `InvalidOperationException`. The message names the keys and the environment name, never a secret.
- The environment name is read from configuration, first non-blank of AZURE_FUNCTIONS_ENVIRONMENT, DOTNET_ENVIRONMENT, ASPNETCORE_ENVIRONMENT. Unset means Production (ASP.NET convention). The workers' `Program.cs:7` adds env vars unprefixed to app configuration, and the isolated worker `HostBuilder` does not reliably map AZURE_FUNCTIONS_ENVIRONMENT into `HostingEnvironment`, so reading configuration is deterministic and testable without changing `AddWorkerServices`' signature.
- **Fail fast:** `AddWorkerServices` calls it eagerly at composition, before the lazy `IEmailSender` factory at `WorkerServices.cs:173`, so the host refuses to start. `Create` also calls it, as a second guard.
- `LogEmailSender` passes `EmailMask.Mask(...)` values for `To` and `ReplyTo` (`src/wa.domain/Privacy/EmailMask.cs:12`, the existing approved transform "d****@x.com" from US-047-03). Template and parameter names stay unchanged so `check-telemetry-pii.mjs` R7 is not disturbed. Bodies stay, because dev needs the magic links and the guard confines them to dev.
- The API never composes `EmailSenderSelector`. Only the workers and one integration test do.

**S5. D-07 seed (TA-3.4 Limits Registry, golden rule 4).** Limits resolve from the `FeatureFlag` override over `Plan.LimitsJson` (`PlanCatalogIntegrationTests.cs:71-100` compares both), so BOTH seeds change:
- `PlanConfiguration.cs:47` Pro `maxTransferSize`/`maxSingleFile`: `10737418240` → `21474836480`.
- `FeatureFlagConfiguration.cs:76-77` `limits.pro.maxSingleFile`/`limits.pro.maxTransferSize`: `"10737418240"` → `"21474836480"`.
- Then generate a migration (`UpdateData` up / down back to 10 GB). This is a data-only, add-only change with no schema change.
- Prices verified unchanged: Pro 900/9000, Business 1900/19000 cents (`PlanConfiguration.cs:50,61`) = D-07 $9/$90, $19/$190.

## 3. Implementation Plan

1. **apiBase module.** Create `src/wa.web/src/core/config/apiBase.ts` (DEFAULT_API_BASE, `resolveApiBase(raw)`, API_BASE), `src/wa.web/src/core/config/apiBase.test.ts` (undefined, `''`, `'  '` → default; `'/api/v1/'` → `/api/v1`; `'https://x.test/api/v1'` unchanged; API_BASE equals default under vitest), and `src/wa.web/src/vite-env.d.ts`.
2. **Replace 20 literals** (rows in FILES FOR CODER). Import paths: `features/*/` → `'../../core/config/apiBase'`; `core/features/`, `core/upload/` → `'../config/apiBase'`; `App.tsx` → `'./core/config/apiBase'`. Keep constant names. Example: FEATURES_URL becomes the template literal API_BASE + "/features". In files whose local const is itself named API_BASE, delete the const and import it. Update stale "no env vars yet" comments to cite CHORE-deploy-hardening. Verify: `cd src/wa.web && npm run lint && npm run test:run && npm run build`, plus the grep from §1.
3. **Theme bootstrap out of index.html.** Create `src/wa.web/public/theme-init.js` with the script body copied verbatim from `index.html:13-23`, keeping the T-015-01 comment as a JS comment. In `index.html`, replace the inline `<script>…</script>` with `<script src="/theme-init.js"></script>` and keep the HTML comment above it.
4. **nginx template + Dockerfile.** Rewrite `src/wa.web/nginx.conf` (target content under FILES FOR CODER). Dockerfile: `ARG VITE_WA_API_BASE=http://localhost:8080/api/v1` + `ENV VITE_WA_API_BASE=$VITE_WA_API_BASE` in the build stage before `RUN npm run build`. In the final stage: `COPY src/wa.web/nginx.conf /etc/nginx/templates/default.conf.template` (replaces the conf.d COPY), and `ENV WA_API_ORIGIN="http://localhost:8080" WA_BLOB_ORIGIN="http://127.0.0.1:10000 http://localhost:10000"`. Keep `EXPOSE 5173` and the CMD, so the stock entrypoint still runs the templates. Create `src/wa.web/src/securityHeaders.test.ts`, which reads `../nginx.conf` and `../index.html` (fs precedent `touchTargets.test.ts:8-11`). It asserts:
   - each `location … { }` block contains all 5 `add_header` names with `always`;
   - the CSP contains `script-src 'self'`, `frame-ancestors 'none'`, `${WA_BLOB_ORIGIN}`, `${WA_API_ORIGIN}`, `https://dc.services.visualstudio.com`;
   - `index.html` has no `<script>` without a `src` attribute.

   Verify: `npm run test:run`, `npm run build` (check that `dist/theme-init.js` exists).
5. **API middleware.** Create `SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)` (sealed, primary ctor with `_next`/`_emitHsts` fields, XML docs). Wire it as the first `app.UseMiddleware<…>()` in `Program.cs`, before line 24, with a TA-9.5 comment. Create `tests/wa.api.integration/SecurityHeadersMiddlewareTests.cs`:
   - (a) a `DefaultHttpContext` fact with a nested stub `IHostEnvironment` (`EnvironmentName` = `Staging`): all 4 headers + HSTS `max-age=2592000`;
   - (b) a theory over `Development`, `Local`, `local`: 4 headers, no `Strict-Transport-Security`;
   - (c) a pipeline fact via `WebApplicationFactory<Program>` (static-ctor env vars exactly as `PipelineTests.cs:25-30`): `GET /no-such-route-hardening` → 404 and carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, no HSTS (factory env = Development).

   Verify: `dotnet build tests/wa.api.integration` (Docker is down. (a) and (b) need no containers, and the Coder may run dotnet test on tests/wa.api.integration filtered to FullyQualifiedName~SecurityHeadersMiddlewareTests).
6. **Email guard + masking.** In `EmailSenderSelector.cs` add:
   - `public const string LogSender = "log";`
   - `internal static readonly string[] EnvironmentKeys = ["AZURE_FUNCTIONS_ENVIRONMENT", "DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT"];` (or private plus a public doc'd accessor; Coder's choice);
   - `public static void EnsureSenderAllowed(IConfiguration configuration)`; `Create` calls it first.

   In `LogEmailSender.cs`, line 18 becomes `LogEmailSent(_logger, EmailMask.Mask(message.To) ?? "(none)", message.From, EmailMask.Mask(message.ReplyTo) ?? "(none)", …)` with `using wa.domain.Privacy;` and an updated class summary (TA-9.4). In `WorkerServices.cs`, insert `EmailSenderSelector.EnsureSenderAllowed(configuration);` before line 173, with a fail-fast comment. Tests:
   - `EmailSenderSelectorTests`: the `Create_Log` config gains `("DOTNET_ENVIRONMENT", "Development")`; its assertion becomes `To: t****@x.com` with no raw `to@x.com`. New theory `Create_Log_OutsideDevelopment_Throws` (env unset, `Production`, `Staging`): the message contains `Wa:Email:Sender`. New theory `Create_Log_AllowedEnvironment_ReturnsLogSender` over each of the 3 keys × `Development`/`Local`. New fact: `EnsureSenderAllowed` with a non-log sender in Production does not throw.
   - `WorkerServicesTests.SendNotificationEmailsHandler_Resolves` (line 76): add `settings["DOTNET_ENVIRONMENT"] = "Development"`. New fact `LogSender_InProduction_FailsAtComposition`: `AddWorkerServices` throws `InvalidOperationException` naming `Wa:Email:Sender`.
   - `EmailLogSenderE2ETests.RunWorkerAsync` config (line ~248): add `["DOTNET_ENVIRONMENT"] = "Development"`. Recipient assertions compare to `EmailMask.Mask(...)`: line 123 → `{ "a****@example.com", "b****@example.com" }`. Line 124 is removed, because masked ann == masked alice. FR-006-3 is still proven by `Count == 2` (line 122) plus the DB check at line 149. Lines 155, 171, 193, 208 → `EmailMask.Mask("…")`.

   Verify: `dotnet test tests/wa.workers.unit`, `node scripts/check-telemetry-pii.mjs && node scripts/check-telemetry-pii.mjs selftest` (from repo root).
7. **D-07 migration.** Edit the two seed sources. Update the `D-07 stand-in (ESCALATIONS.md E-004)` comments to "D-07 confirmed 2026-11-26: Pro 20 GB". Then, from the worktree root, run the AGENT.md §5.3 migrations-add command with name ProLimitsTwentyGb (project wa.infrastructure, startup project wa.api); the design-time factory is `src/wa.infrastructure/Persistence/DesignTime/WaDbContextFactory.cs`. Check that the generated `Up` has exactly 3 `UpdateData` calls (Plan pro LimitsJson + 2 FeatureFlag values) and that `Down` reverts to 10737418240. If the global dotnet-ef 9.0.8 refuses the net10 project, install dotnet-ef 10.x as a tool; that adds no NuGet package to the repo. Tests:
   - `SeedDataIntegrationTest.cs:75`: Pro LimitsJson → 21474836480 ×2.
   - `PlanUsageEndpointTests.cs:135-136` → `21474836480L`.
   - `PlanCatalogIntegrationTests.cs:90,100`: the edit value must differ from the new seed → `"32212254720"` / `32_212_254_720L` (30 GB test value).
   - Web: `loadPlanLimits.test.ts:7` `PRO = 21474836480`, `planLimits.test.ts:24-25` → `21474836480` (labelled "Pro seed").

   Verify: `dotnet build src/wa.slnx`, `dotnet build tests/wa.api.integration`.
8. **Full local gate:**
   - `dotnet test tests/wa.domain.unit`
   - `dotnet test tests/wa.application.unit`
   - `dotnet test tests/wa.workers.unit`
   - `dotnet build tests/wa.api.integration`
   - dotnet format src/wa.slnx in verify-no-changes mode (AGENT.md §4)
   - `cd src/wa.web && npm run lint && npm run test:run && npm run build`
   - `node ../../scripts/check-theme-tokens.mjs`
   - `node ../../scripts/check-contrast.mjs`
   - the PII script pair

   Commit everything in one `feat: CHORE-deploy-hardening — …` commit.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.web/src/core/config/apiBase.ts` | CREATE | DEFAULT_API_BASE, `resolveApiBase`, API_BASE from VITE_WA_API_BASE |
| `src/wa.web/src/core/config/apiBase.test.ts` | CREATE | resolveApiBase cases (step 1) |
| `src/wa.web/src/vite-env.d.ts` | CREATE | `interface ImportMetaEnv { readonly VITE_WA_API_BASE?: string }` (global, no import/export) |
| `src/wa.web/src/App.tsx` | MODIFY | line 96 logout URL → template literal API_BASE + "/auth/logout" |
| `src/wa.web/src/core/features/featureGates.ts` | MODIFY | line 13 FEATURES_URL |
| `src/wa.web/src/core/upload/loadPlanLimits.ts` | MODIFY | line 5 PLAN_URL |
| `src/wa.web/src/features/admin/AdminErasuresPage.tsx` | MODIFY | line 10 local API_BASE → import |
| `src/wa.web/src/features/admin/AdminFlagsPage.tsx` | MODIFY | line 12 same |
| `src/wa.web/src/features/admin/AdminOverviewPage.tsx` | MODIFY | line 10 same |
| `src/wa.web/src/features/admin/AdminShell.tsx` | MODIFY | line 18 same (+comment line 17) |
| `src/wa.web/src/features/admin/AdminSuppressionsPage.tsx` | MODIFY | line 12 same |
| `src/wa.web/src/features/admin/AdminTransfersPage.tsx` | MODIFY | line 14 same |
| `src/wa.web/src/features/admin/AdminUsersPage.tsx` | MODIFY | line 13 same |
| `src/wa.web/src/features/auth/AuthPage.tsx` | MODIFY | line 16 same |
| `src/wa.web/src/features/billing/PlanPage.tsx` | MODIFY | line 10 BILLING_BASE = template literal API_BASE + "/billing" |
| `src/wa.web/src/features/files/downloadsAnalytics.ts` | MODIFY | line 9 local API_BASE → import |
| `src/wa.web/src/features/files/FilesPage.tsx` | MODIFY | line 24 same |
| `src/wa.web/src/features/landing/StagingList.tsx` | MODIFY | lines 33, 35 endpoints (keep `{draftId}` placeholder) |
| `src/wa.web/src/features/profile/ProfilePage.tsx` | MODIFY | line 19 exported ME_BASE = template literal API_BASE + "/auth/me" (keep export) |
| `src/wa.web/src/features/recipient/RecipientStore.ts` | MODIFY | lines 29, 31, 33, 35 endpoints (keep `{linkId}`/`{fileId}`) |
| `src/wa.web/src/features/sender/LinkScreen.tsx` | MODIFY | line 43 SEND_ENDPOINT |
| `src/wa.web/src/features/sender/ReSendScreen.tsx` | MODIFY | line 29 RESEND_ENDPOINT |
| `src/wa.web/src/features/sender/schedule.ts` | MODIFY | line 4 PLAN_URL |
| `src/wa.web/public/theme-init.js` | CREATE | verbatim pre-paint theme script from index.html |
| `src/wa.web/index.html` | MODIFY | inline script → `<script src="/theme-init.js"></script>` |
| `src/wa.web/nginx.conf` | MODIFY | envsubst template with security headers in every location |
| `src/wa.web/Dockerfile` | MODIFY | build ARG/ENV VITE_WA_API_BASE; template COPY; WA_* ENV defaults |
| `src/wa.web/src/securityHeaders.test.ts` | CREATE | nginx/index.html CSP contract test (step 4) |
| `src/wa.api/Middleware/SecurityHeadersMiddleware.cs` | CREATE | headers + env-gated HSTS |
| `src/wa.api/Program.cs` | MODIFY | `app.UseMiddleware<SecurityHeadersMiddleware>();` before line 24 |
| `tests/wa.api.integration/SecurityHeadersMiddlewareTests.cs` | CREATE | step 5 (a)(b)(c) |
| `src/wa.infrastructure/Email/EmailSenderSelector.cs` | MODIFY | `EnsureSenderAllowed`, env keys, `Create` calls guard |
| `src/wa.infrastructure/Email/LogEmailSender.cs` | MODIFY | mask To / ReplyTo via EmailMask |
| `src/wa.workers/WorkerServices.cs` | MODIFY | eager `EnsureSenderAllowed(configuration)` before line 173 |
| `tests/wa.workers.unit/Email/EmailSenderSelectorTests.cs` | MODIFY | env key on log case, masking assert, guard theories |
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | env on line 76 test; new fail-at-composition fact |
| `tests/wa.api.integration/Events/EmailLogSenderE2ETests.cs` | MODIFY | env key in RunWorkerAsync; masked recipient asserts |
| `src/wa.infrastructure/Persistence/EntityConfigurations/PlanConfiguration.cs` | MODIFY | Pro LimitsJson 20 GB (line 47) + comment line 46 |
| `src/wa.infrastructure/Persistence/EntityConfigurations/FeatureFlagConfiguration.cs` | MODIFY | lines 76-77 → "21474836480"; comment line 67 |
| `src/wa.infrastructure/Persistence/Migrations/{timestamp}_ProLimitsTwentyGb.cs` | CREATE | generated by `dotnet ef migrations add` |
| `src/wa.infrastructure/Persistence/Migrations/{timestamp}_ProLimitsTwentyGb.Designer.cs` | CREATE | generated |
| `src/wa.infrastructure/Persistence/Migrations/WaDbContextModelSnapshot.cs` | MODIFY | regenerated (lines ~756, ~763, ~942) |
| `tests/wa.api.integration/SeedDataIntegrationTest.cs` | MODIFY | line 75 Pro LimitsJson 20 GB |
| `tests/wa.api.integration/PlanUsageEndpointTests.cs` | MODIFY | lines 135-136 → 21474836480L |
| `tests/wa.api.integration/PlanCatalogIntegrationTests.cs` | MODIFY | lines 90, 100 edit value → 30 GB |
| `src/wa.web/src/core/upload/loadPlanLimits.test.ts` | MODIFY | line 7 PRO |
| `src/wa.web/src/core/upload/planLimits.test.ts` | MODIFY | lines 24-25 Pro seed value |

**`src/wa.web/nginx.conf`: target content** (the whole header block must stay identical in both locations):

```nginx
# CHORE-deploy-hardening (TA-9.5): envsubst template — the nginx image renders /etc/nginx/templates/*.template at start.
# ${WA_API_ORIGIN} / ${WA_BLOB_ORIGIN} come from the container env (Dockerfile defaults = local compose).
# A location with its own add_header drops ALL inherited ones — every location repeats the full block.
server {
    listen 5173;
    root /usr/share/nginx/html;
    index index.html;

    location / {
        add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: ${WA_BLOB_ORIGIN}; connect-src 'self' ${WA_API_ORIGIN} ${WA_BLOB_ORIGIN} https://dc.services.visualstudio.com; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'" always;
        add_header X-Content-Type-Options "nosniff" always;
        add_header Referrer-Policy "strict-origin-when-cross-origin" always;
        add_header X-Frame-Options "DENY" always;
        add_header Permissions-Policy "camera=(), microphone=(), geolocation=(), payment=()" always;
        try_files $uri $uri/ /index.html;
    }

    location /assets/ {
        expires 1y;
        add_header Cache-Control "public, immutable";
        # (same five add_header lines as above, verbatim)
    }
}
```

**`EmailSenderSelector.Create` (line 29, current):** `if (configuration[SenderKey] == "log") return new LogEmailSender(...)`. Add `EnsureSenderAllowed(configuration);` as the first statement, and make the comparison use the new `LogSender` const. Shape of the guard:

```csharp
public static void EnsureSenderAllowed(IConfiguration configuration)
{
    if (!string.Equals(configuration[SenderKey], LogSender, StringComparison.Ordinal))
        return;
    var environment = EnvironmentKeys.Select(k => configuration[k]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Local", StringComparison.OrdinalIgnoreCase))
        return;
    throw new InvalidOperationException(
        $"{SenderKey}={LogSender} is allowed only when the host environment is Local or Development "
        + $"(AZURE_FUNCTIONS_ENVIRONMENT / DOTNET_ENVIRONMENT); current: '{environment ?? "(unset = Production)"}'.");
}
```

The existing `Create_Log` test keeps `"log"` matching ordinal (today it is `==`, also ordinal). Add `<exception cref="InvalidOperationException">` to the docs of both `Create` and `EnsureSenderAllowed`.

**`LogEmailSender.SendAsync` (line 16-21):** see step 6. `wa.infrastructure` already reaches `wa.domain` through `wa.application` (onion, TA-2.3).

**`WorkerServices.cs:170-175` (current):** the comment block plus `services.AddSingleton<IEmailSender>(sp => EmailSenderSelector.Create(...))`. Insert the eager guard call above it. The comment at line 170-172 should now say "log is refused outside Local/Development at composition (CHORE-deploy-hardening)".

**`Program.cs:23-24` (current):** the CORS comment plus `app.UseMiddleware<CorsAllowListMiddleware>();`. Insert above them:
`app.UseMiddleware<SecurityHeadersMiddleware>(); // TA-9.5: first, so preflight 204s and limiter 429s also carry the headers`.

**`EmailLogSenderE2ETests.cs`:** the `Mail.To` parsed at line 342 is now the masked value, so every `m.To == "x@example.com"` / `Assert.Equal("x@example.com", …To)` compares against `EmailMask.Mask("x@example.com")` (add `using wa.domain.Privacy;`). DB assertions on `r.Address` (lines 147-149, 183-184, 198) stay raw, because they read SQL, not logs.

## 4. Open Questions / Risks

**Config surfaces for the orchestrator to log** (Open-Decisions Part 3 + TA-13.1 / a web-config row; TA-9.5's "no CSP header is emitted by code yet" note becomes stale):
- VITE_WA_API_BASE: web build arg, default `http://localhost:8080/api/v1`, staging `/api/v1`.
- WA_API_ORIGIN: nginx runtime, default `http://localhost:8080`, staging `'self'`.
- WA_BLOB_ORIGIN: nginx runtime, default Azurite pair, Azure `https://<account>.blob.core.windows.net` (storage.bicep output `blobEndpoint`).
- Workers now read AZURE_FUNCTIONS_ENVIRONMENT / DOTNET_ENVIRONMENT / ASPNETCORE_ENVIRONMENT. These are existing platform variables, not new ones.

**Infra / config follow-ups (orchestrator, outside the Coder's boundary):**
- (a) `docker-compose.local.yml` `wa.workers`: add `AZURE_FUNCTIONS_ENVIRONMENT: Development`. Without it the local workers refuse `Wa:Email:Sender=log` at startup, unless Core Tools sets it, which cannot be verified here with Docker down. Also refresh the `wa.web` comment ("SPA hardcodes …").
- (b) PR #711 apps.bicep web container: env `WA_API_ORIGIN='self'`, `WA_BLOB_ORIGIN=<blobEndpoint>`. Without them the staging CSP allows localhost/Azurite and blocks real blob uploads.
- (c) PR #711 `main.bicep:38` uses `emailSender = 'log'` for env `dev`. Either set `AZURE_FUNCTIONS_ENVIRONMENT=Development` on the dev function app or use `ch` in dev, otherwise dev workers will not start.
- (d) `cd-staging.yml` passes the build arg VITE_WA_API_BASE=/api/v1 (as stated).

**Other risks and open questions:**
- **AdSense vs CSP:** enabling ads later needs `script-src`/`frame-src`/`img-src`/`connect-src` Google hosts. That is a security-shaped widening, to be decided when D-14 flips. It is not blocking now.
- **nginx not verifiable locally** (Docker down, and CI builds only the API image, `ci.yml:135`). The vitest contract test covers the header-inheritance trap and the template variables. A `docker build -f src/wa.web/Dockerfile .` plus a curl of the headers belongs to the staging smoke.
- **E2E masking weakens one assertion:** ann/alice both mask to `a****@example.com`. The sender-skip proof moves to count + DB (`NotifiedAtUtc` null for alice), which is equivalent coverage.
- **HSTS deviation** from the literal "UseHsts()" (reason in S3). If the owner prefers forwarded headers + `UseHsts`, that is a separate decision (trusting X-Forwarded-* is security-shaped).
- **D-07 `UpdateData`** overwrites any operator-edited `limits.pro.max*` flag value on migrate. No staging/prod DB exists yet, so this is acceptable. Note it in the runbook if it lands after the first deploy.
- **dotnet-ef 9.0.8 vs EF 10:** if generation fails, use a 10.x tool. Never hand-write the Designer.
- **Theme script move:** the `vite build` warning "can't be bundled without type=module" is expected. `public/` assets are not hashed, so `theme-init.js` is served from `location /` (no immutable cache). That is correct for an unhashed file.
