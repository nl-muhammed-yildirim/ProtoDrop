# T-008 rate-limits investigation (AspNetCoreRateLimit 5.0.0) — downloaded source notes

These .cs files are verbatim copies of `stefanprodan/AspNetCoreRateLimit` (master,
~5.0.x) downloaded for API-verification while implementing T-008 "rate limits
(API layer)". Not project source.

## How to wire the IP rate limiter
- `services.AddInMemoryRateLimiting()` → registers `IIpPolicyStore`, `IClientPolicyStore`,
  `IRateLimitCounterStore` (all `MemoryCache*` singletons) + `IProcessingStrategy` =
  `AsyncKeyLockProcessingStrategy`.
- `app.UseIpRateLimiting()` → adds `IpRateLimitMiddleware`.
- `IpRateLimitMiddleware` ctor takes `IOptions<IpRateLimitOptions>`; it passes
  `options?.Value` to the base `RateLimitMiddleware`, which runs the rules.
- Counting is **per client IP**: `IpCounterKeyBuilder.Build` =
  `{RateLimitCounterPrefix}_{ClientIp}_{Period}`. `ClientIp` resolved via
  `IpResolvers`: `IpHeaderResolveContributor(RealIpHeader, default "X-Real-IP")` then
  `IpConnectionResolveContributor`; first non-empty wins. (No reverse proxy in dev →
  connection IP.)
- The processor first finds a policy whose `Ip` interval contains the client IP
  (`IpParser.ContainsIp`), collects its `Rules`, then calls base
  `GetMatchingRules(identity, rules)`.

## Rule matching (base RateLimitProcessor.GetMatchingRules)
- If `EnableEndpointRateLimiting == true`:
  - path:    `*:{path}`  matched against `rule.Endpoint` (wildcards default: `*` any, `?` one)
  - verb:    `{verb}:{path}` (e.g. `post:/api/v1/auth/login`)
- Else: only rules with `Endpoint == "*"` (global) are used.
- For a rule to match a specific endpoint you MUST set `EnableEndpointRateLimiting = true`.
- `WildcardMatcher.IsMatch` handles `*` (greedy any) and `?` (single char).

## 429 response path (RateLimitMiddleware.ReturnQuotaExceededResponse)
On `counter.Count > rule.Limit` and `rule.Limit > 0`:
- `Retry-After` header set to seconds until reset (unless `DisableRateLimitHeaders`).
- `RequestBlockedBehaviorAsync` (opt) invoked.
- `ReturnQuotaExceededResponse(context, rule, retryAfter)`:
  - `message = string.Format(QuotaExceededResponse?.Content ?? QuotaExceededMessage ??
      "API calls quota exceeded! maximum admitted {0} per {1}.", rule.Limit,
      rule.PeriodTimespan ?? rule.Period, retryAfter)`  → format args: {0}=limit, {1}=time,
      {2}(extra)=retryAfter.
  - `Retry-After` header set (unless DisableRateLimitHeaders).
  - `StatusCode = QuotaExceededResponse?.StatusCode ?? HttpStatusCode (default 429)`.
  - `ContentType = QuotaExceededResponse?.ContentType ?? "text/plain"`.
  - Writes `message` (plain string, NOT Problem+JSON by default).
- On non-429 (under limit) requests, sets `X-Rate-Limit-Limit/Remaining/Reset` headers
  (longest period), unless `DisableRateLimitHeaders`.

### How to emit Problem+JSON for 429 (TA-4.1.3)
Set `IpRateLimitOptions.QuotaExceededResponse`:
- `ContentType = "application/problem+json"`
- `StatusCode = 429`
- `Content` = a JSON string with a `{0}` (limit) / `{1}` (period) placeholder if you want
  the numbers, otherwise a static string. Because the lib does `string.Format` on it, a
  JSON body with no other `{`/`}` can be used verbatim. Note: NO `correlationId` is
  injected by the library — if TA-4.1.3 requires `correlationId` on every error, a custom
  `RequestBlockedBehaviorAsync` (or a light wrapper middleware) is needed to add it.

## RateLimitOptions knobs used
- `HttpStatusCode` default 429; `QuotaExceededMessage`, `QuotaExceededResponse`
  (ContentType/Content/StatusCode?), `RateLimitCounterPrefix` default "crlc".
- `StackBlockedRequests` (order rules by period ascending; default false).
- `EnableEndpointRateLimiting` (default false), `EnableRegexRuleMatching` (default false),
  `DisableRateLimitHeaders` (default false), `RealIpHeader` default "X-Real-IP",
  `ClientIdHeader` default "X-ClientId".
- `IpRateLimitPolicy { Ip (interval e.g. "192.168.1.0/24" or single IP), Rules }`.
- `RateLimitRule { Endpoint, Period (e.g. "1m" / "1s"/"1h"), Limit, MonitorMode,
  QuotaExceededResponse? }`.
