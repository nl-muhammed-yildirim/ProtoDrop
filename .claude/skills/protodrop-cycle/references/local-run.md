# Running the app locally for manual QA

Order matters; each step depends on the previous one. The API does NOT auto-migrate, and wa.workers is a separate process.

## 5. Azurite CORS (after Docker up — required before any browser upload)

The web client uploads file blocks **directly** from the browser to blob storage (`http://127.0.0.1:10000/staging`), bypassing the API's CORS middleware — every such upload starts with an OPTIONS preflight that Azurite rejects unless a policy exists, and local Azurite ships with **none**. Symptom in the browser: `CorsPreflightFailure` / 403 on upload while `/health` and the API look fine. The rule is ephemeral (in-memory) — re-apply after every container restart:

```
cd C:/Users/myild/source/repos/ProtoDrop/src/wa.web
cp <skill>/scripts/set-azurite-cors.cjs ./set-cors-tmp.cjs && node set-cors-tmp.cjs && rm set-cors-tmp.cjs
```

The script must run from `src/wa.web` (module resolution) as a `.cjs` copy (the package is ESM). The SDK's property shape for this version is `{ cors: [{ allowedOrigins, allowedMethods, allowedHeaders, exposedHeaders, maxAgeInSeconds }] }` — **strings, not arrays**, and the field name is `cors`, NOT `corsPolicy`: wrong names serialize to an empty `<StorageServiceProperties/>` that Azurite accepts with 200/202 while storing nothing (a silent no-op that looks like success). Verify afterwards with a browser-style preflight — must return 200 + `Access-Control-Allow-Origin: http://localhost:5173`: `curl -si -X OPTIONS "http://127.0.0.1:10000/devstoreaccount1/staging/testblob?comp=block&blockid=AQ==" -H "Origin: http://localhost:5173" -H "Access-Control-Request-Method: PUT"`. The repo has no committed setup for this yet — a local-dev gap worth surfacing as a follow-up task (persist the rule via a compose init step or Getting-Started note).

## 1. Docker (SQL 2022 + Azurite)

```
docker compose -f docker-compose.local.yml up -d
```
Containers: `protodrop-sql-1`, `protodrop-azurite-1`. SQL takes ~30–60 s before it accepts connections — poll with sqlcmd. **The container's sqlcmd is at `/opt/mssql-tools18/bin/sqlcmd`** (the older `/opt/mssql-tools/...` path 404s on this image):

```
docker exec protodrop-sql-1 /opt/mssql-tools18/bin/sqlcmd -S localhost,1433 -U sa -P 'YourStrong!Passw0rd' -C -Q "SELECT name FROM sys.databases" -b
```

## 2. Database (only when the `wa` DB is missing — first run or after a drop)

```ncd C:/Users/myild/source/repos/ProtoDrop
dotnet ef database update --project src/wa.infrastructure --startup-project src/wa.api
```
Applies all migrations + Plan/FeatureFlag seeds in one shot (~10 s). If `wa` already exists and is current it reports no migrations to apply — safe either way. The API process itself never migrates, so skipping this on a fresh DB means every request 500s.

The compose stack also runs a one-shot `migrate` service on `up` (`Dockerfile.migrate`, same `dotnet ef database update`). It can fail even when SQL is up and reachable from the network — .NET SNI throws TCP error 35 at TDS pre-login (adding TrustServerCertificate=True to the string advances it only as far as 'Login failed for user sa') against host.docker.internal, while raw bash TCP (`nc`) from inside that same container works AND wa.api's own EF connection through the same string works. When it fails, don't debug the container: re-run the host-side command above — SQL is published at localhost:1433 so the host reaches it directly. Two consequences that cost diagnosis time: (a) wa.api depends on migrate via service-started only — a failed migrate does NOT delay or fail the API, so an empty schema runs and every DB endpoint 500s with 'Invalid object name' while /health still reports db:ok; (b) `docker compose down` drops the SQL volume (no named volumes in this compose file), so after ANY down/up cycle re-run migrations even if they applied before.

## 3. API (:8080)

```
cd C:/Users/myild/source/repos/ProtoDrop/src/wa.api && dotnet run --launch-profile Local   # terminal background=true
```
`--launch-profile Local` is what loads every env var from `Properties/launchSettings.json`: DB connection string, Azurite blob URL + devstoreaccount1 key, JWT secret, CORS origin (http://localhost:5173), zip sign key — and the admin identity **`Wa:AdminEmails=dev@example.com`** (sign up with that email for SuperAdmin; `role` appears in `/auth/me`). Bare `dotnet run` without the profile starts with no env vars → DB connection fails.

Readiness: `curl -s http://localhost:8080/health` → `{"status":"ok","db":"ok","sb":"skipped"}`. **`"sb":"skipped"` is normal locally** (in-memory publisher, no Service Bus). **`db: ok` proves connectivity, not schema**: the health probe runs `SELECT 1` and auto-creates an empty DB as a dev convenience — on a fresh stack every real query fails with 'Invalid object name' until migrations have applied. Verify schema with an actual write (signup) or by listing sys.tables.

### Probing auth endpoints from git-bash
Two traps when verifying signup/login with `curl -d` from the Hermes terminal (git-bash): MSYS can mangle the JSON body — the symptom is Email binding while Password never does, and both PascalCase and camelCase bodies failing identically with the password-length error. When in doubt, send exact bytes via python urllib instead (`urllib.request.Request(url, data=json.dumps({...}).encode(), headers={'Content-Type':'application/json'})`). Distinguish the failure class: if BOTH casings of a field fail to bind, the body was mangled by the shell; if only one casing fails, binding is case-sensitive. And `/auth/*` is rate-limited 5/min per IP — after ~5 probes wait 60 s or you'll misread a 429 as an app failure.

### Admin QA identity (Docker stack)
The seeded SuperAdmin account is **`dev@example.com` / `TestPass!23`** — created once via `POST /auth/signup` against the Docker API; works as long as the SQL container/DB persists. Verify with cookie-based curl: `/auth/login` → save Set-Cookie → `GET /auth/me` returns `"role":"superadmin"`. No Operator emails are configured in compose, so this identity has full write access.

### API-level CORS is exact-match on origin (the 'CORS exception' symptom class)
`CorsAllowListMiddleware` echoes Access-Control-* headers only when the request's Origin EXACTLY matches an entry of `Wa:Cors:AllowedOrigins` — locally that list is exactly `http://localhost:5173`. Any other origin (`http://127.0.0.1:5173`, another port) gets plain pass-through with no headers at all, and its preflight falls through to routing (405) → the browser raises a real CORS error on every API call even though curl looks fine. When 'CORS exception' is reported: first establish which origin the tab actually used — localhost vs 127.0.0.1 in the URL bar — then verify header presence per candidate origin (`curl -si <endpoint> -H "Origin: http://localhost:5173"` and repeat with 127.0.0.1). If headers ARE present on the response, CORS is passing and the reported error was a masked app failure (e.g. the empty-schema 500 above) — reproduce in-page (a fetch from page context reads the real status + body) instead of re-diagnosing CORS.

## 4. Web (:5173)

```
cd C:/Users/myild/source/repos/ProtoDrop/src/wa.web && npm run dev   # terminal background=true; Vite ready in <1 s
```
Readiness: `curl -sI http://localhost:5173` → `HTTP/1.1 200 OK`. (node_modules are committed-installed in the repo — no install step unless deps changed.)

## All apps in Docker (docker-compose.local.yml) — full stack served from containers
The compose file hosts SQL + Azurite plus all three app services: `wa.api` (:8080), `wa.web` (:5173), `wa.workers` (:8081).

```
cd C:/Users/myild/source/repos/ProtoDrop && docker compose -f docker-compose.local.yml up -d
```
Readiness: the same curl checks as sections 3 and 4, plus a workers route (below). After source changes rebuild only what changed — `docker compose -f docker-compose.local.yml build <service>` then `up -d --force-recreate <service>`. Verify with `dotnet build` on the C# project BEFORE rebuilding the image: a compile error costs a full multi-layer Docker build.

### wa.web (:5173) is a BUILT nginx image, not Vite dev
The compose `wa.web` service builds the SPA (`npm run build`) and serves `/usr/share/nginx/html` — no source mount. Host-side edits under `src/wa.web/src/**` do NOT hot-reload: rebuild + recreate before browser-testing (`docker compose ... build wa.web && up -d --force-recreate wa.web`). Confirm which code is actually live by grepping the container's bundle or fetching a module URL — don't assume your host edit is running (an edited login form that still behaved like old code cost one full diagnosis cycle).

### Browser-side auth verification (the "session expired" symptom class)
When curl proves the API works but the admin gate shows 'Sign in to continue / session has expired': reproduce in a real browser, then read the cookie jar via CDP — `document.cookie` misses HttpOnly cookies: `cdp('Network.getCookies', urls=["http://localhost:8080"])`. An empty jar after a successful login means the Set-Cookie was dropped client-side; check EVERY auth fetch (login/signup POSTs included, not just /auth/me) for cross-origin `credentials:'include'`. Route facts that cost round-trips: sign-in is `/account` — there is NO `/auth` route and it falls through to the landing/upload page; admin screens are `/admin/overview|transfers|users|flags|suppressions` (no bare `/admin`).

### Layout/scrollbar symptom class ("page doesn't fit / scrollbars")
Measure before fixing: compare `document.documentElement.scrollWidth/scrollHeight` vs `innerWidth/innerHeight` in the browser — that is document-level overflow. Walk the DOM for elements wider than the viewport (`getBoundingClientRect().width > innerWidth`) to find the culprit; a flex header at viewport+padding width means missing border-box sizing. Current state of `src/wa.web/src/index.css`: universal `*, *::before, *::after { box-sizing: border-box }` reset (any `width:100%` + padding/border element overflows without it) and a fixed-viewport shell — `#root` is 100dvh flex column, `main` fills the space below the TopBar with internal scrolling (`overflow-y:auto`, min-height:0), short routes centered via auto margins on `main > *`. After that fix, **`main.scrollHeight > main.clientHeight` is expected internal scroll** (e.g. admin overview) — don't re-diagnose it as page overflow; only the documentElement numbers matter for "no scrollbars".

### wa.workers (:8081) — two root causes that recur if Program.cs or the Dockerfile is regenerated
- **The isolated worker's Program.cs must call `.ConfigureFunctionsWorkerDefaults()`** (extension from Microsoft.Azure.Functions.Worker, already imported) on HostBuilder. Without it no gRPC channel registers: func waits 60 s then logs "The operation has timed out" + "Failed to start language worker process", while the worker dotnet process stays alive with zero established connections — looks like a hang, is missing host wiring.
- **Core Tools in a container must bind all interfaces.** v4.16.0 replaced `--listenRemote` with `--address` (default 127.0.0.1); loopback is unreachable through Docker port mapping — from the host curl returns 000 while logs look healthy. Use `func host start --port <p> --address 0.0.0.0`. Check flags against `func host start --help` before assuming one exists (v4.16 has no `--listenRemote`).
- **Diagnostic orientation:** in dotnet-isolated, func is the gRPC *server* (owns the listener on a random port P); the worker connects back as a client via `--functions-uri http://127.0.0.1:P/` (visible in the worker's cmdline). Check ESTABLISHED conns (state 01) on the WORKER (`/proc/<pid>/net/tcp`), not LISTEN sockets; a curl that gets an HTTP/2 404 from :8081 only proves func is listening. Socket-inode→PID tracing via `/proc/*/fd` readlinks is how you attribute a listener to its owner.
- **Expected local failures in the workers log (not bugs):** timer triggers fail with "Could not create BlobContainerClient for ScheduleMonitor" (no blob storage) and f-email fails on missing `Wa:ServiceBus:ConnectionString`. Exercise those jobs via their HTTP routes instead: POST `/api/jobs/expire`, `/api/jobs/delete-transfers`, `/api/jobs/delete-blobs`; zip at `/api/zip/{transferId}`. A 401 `{"code":"Unauthenticated","details":"Invalid or missing signature."}` on a bogus call proves the full func → gRPC worker round-trip works.

## What does NOT run automatically
- **wa.workers** hosts f-zip, the expiry/deletion timers, and f-email dispatch. The API alone never zips a download-all request or runs scheduled expiry. In the Docker stack it is a compose service (above); on the dotnet-run path start it separately (`dotnet run` in `src/wa.workers`). Emails are log-only in dev — the link/URL is printed to that process's console. **In the Docker stack the magic-link CTA does NOT surface anywhere**: the API publishes through an in-memory test-double publisher with no local drain, and f-email needs a Service Bus connection string — so for admin QA use password login (`POST /auth/signup` then `/auth/login`) instead of promising a link appears in any log.
- Until workers run: force expiry by SQL edit on the Transfer row (`ExpiresAtUtc = GETDATE()-1s`), and don't promise the user "emails arrive" — verify which process is actually dispatching first rather than asserting from memory.

## Manual test scenarios (the human is QA)
`docs/QA-SCRIPTS.md` holds click-by-click scripts per milestone (guest upload, send form, recipient states, password gate, download+resume, limits, auth loop) plus the §0 force-condition helpers (5 GB file via `fsutil`, expiry SQL, download-cap values). A FAIL goes back into PROGRESS.md as an open item with exact repro steps.
