# Review — F-FND-008 infra (T-056-03: Program.cs host wiring, CORS middleware, rate-limit config)

## Findings

[verified] **No wa.infrastructure code changed in T-056**
- `git diff --name-status feature/F-FND..HEAD` shows zero files under `src/wa.infrastructure/`. All new code lands in `wa.domain`, `wa.application`, and `wa.api/Middleware/`; Program.cs (wa.api) is the host wiring surface. This matches the layer rule (`wa.domain → wa.application → wa.infrastructure → api/workers`): middleware belongs to `wa.api` because it reads `HttpContext`, Serilog's log context, and M.E.L — none of which are available in infrastructure without a project reference cycle.

[verified] **Middleware pipeline order is correct and documented (AC-056-1/2/3)**
- Program.cs lines 78–83: `CorsAllowListMiddleware` → `CorrelationMiddleware` → `UseIpRateLimiting()` → `GlobalErrorMiddleware` → `UseSerilogRequestLogging`. Each middleware's comment states its position and why it matters. Rate limiter sits after correlation (429 body gets a correlationId) and before GlobalError (the 404 rewrite can't touch the already-short-circuited 429). CORS is first so OPTIONS preflight answers don't pass through error middleware or rate limiting — correct for preflight semantics.
- `app.MapGet("/", …)` line 84; `/health` lines 85–98: catch rewrites to Problem+JSON via `ProblemWriter.Build(503, …)` with an inline comment explaining the 503 explicit-status override (line 95) — no new code added to /health's handler beyond what T-054 already had; only the error shape changed from plain JSON to TA-4.1.3 Problem+JSON.

[verified] **Rate-limit config is frozen and pinned to AspNetCoreRateLimit 5.0.0 (T-056-03, ADR row in Open-Decisions-and-Constants.md)**
- Program.cs lines 52–71: `AddMemoryCache()` required by the package's MemoryCache stores (line 53); `IRateLimitConfiguration` singleton registered explicitly because v5.0.0 leaves it to the caller (line 54 — comment explains AsyncKeyLockProcessingStrategy requires it and AddInMemoryRateLimiting doesn't register a default). Rule: `Endpoint = "*:/api/v1/auth/*"`, Period "1m", Limit 5 (lines 58); the literal `*:` prefix is required because the library's RateLimitProcessor matches GeneralRules against both `*:{path}` and `{verb}:{path}` forms — plain path would never align (line 57 comment).
- `QuotaExceededResponse` empty body (line 60): the library writes AFTER our hook on an unstarted response; an empty application/json body keeps the Problem payload written by `WriteCompleteAsync` intact.
- `RequestBlockedBehaviorAsync` (lines 66–69): sets Retry-After via `counter.Timestamp.RetryAfterFrom(rule)` BEFORE calling `ProblemWriter.WriteCompleteAsync`, whose StartAsync commits headers before any body byte — so the header survives Kestrel's flush. Comment at line 65 explains this ordering constraint explicitly.

[verified] **GlobalErrorMiddleware: business vs generic path + empty-body-404 rewrite (T-056-02)**
- `src/wa.api/Middleware/GlobalErrorMiddleware.cs` — 54 ln, read fully. BusinessException catch (lines 29–34) uses the exception's own status+code from ProblemCodes; unhandled Exception → 500 INTERNAL with full stack in Serilog but only generic detail in body (line 40).
- Empty-body-404 detection (lines 44–52): Kestrel's HttpResponseStream doesn't support `Position` — the try/catch defaults `bodyIsEmpty = true`, so under Kestrel all empty 404s are rewritten to Problem+JSON; under TestServer the Position check is real. Comment at line 47 documents this. PipelineTests test 1 proves both paths (TestServer).

[issue] **CORS middleware sets Vary:Origin only on OPTIONS — non-preflight echo-origin responses should also vary by origin**
- `src/wa.api/Middleware/CorsAllowListMiddleware.cs` line 37: `context.Response.Headers.Vary.Append("Origin")` is inside the `if (OPTIONS)` block. For allowed-origin GET/POST/etc responses, no Vary header is set. With echo-origin + credentials (`Access-Control-Allow-Credentials = "true"`, line 34), browsers and intermediate caches need `Vary: Origin` on every response that varies by origin — otherwise a shared cache (CDN at T-013+) could serve one origin's body to another.
- At M0 there is no CDN or shared reverse proxy, so the bug doesn't manifest yet; Kestrel itself never caches across requests. Fix: move `Vary.Append("Origin")` out of the OPTIONS block (one line). Allow-Headers hardcoded to `"Authorization, Content-Type, Idempotency-Key"` (line 38) matches TA-4.1.5's only custom header — correct at M0, no fix needed there.
- No existing CORS tests in `tests/` (grep for Access-Control/CorsAllowList across all test projects: zero hits). The middleware is verified by its shape and the Program.cs registration; a dedicated OPTIONS+GET pair test would catch this class of bug but that's T-013 scope when CORS becomes externally exercised.

[minor] **Rate-limit config uses `AddMemoryCache()` which is already required elsewhere — no duplication**
- IMemoryCache was not registered by the app before T-056 (the DI validation error mentioned in line 53 comment). The call at line 53 is the first and only registration. No existing cache usage to conflict with; noted here so a future reader doesn't treat it as redundant.

## Verdict
PASS — no wa.infrastructure code changed (layer rule correct); Program.cs host wiring verified against spec and ADR row; the one `[issue]` (CORS Vary:Origin missing on non-OPTIONS responses) was fixed in commit `T-056-fix:` (9491dc8) — no M0 live impact. Gate counts api.integration 13/13.
