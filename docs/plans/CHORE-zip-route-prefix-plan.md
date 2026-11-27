# CHORE-zip-route-prefix — fix API→workers zip trigger URL + route binding (issue #727)

## 1. Goal

Make "Download all" actually start f-zip: the API posts to `{Wa:ZipFunction:BaseUrl}/api/zip/{id:n}` and the isolated worker binds the `{transferId}` route value. Exit check: `dotnet test tests/wa.application.unit` and `dotnet test tests/wa.workers.unit` green, with new tests pinning the exact trigger URL and the route-template/parameter-name match.

## 2. Design & Decisions

Two independent defects, both confirmed in source:

1. **Missing `api` prefix.** `src/wa.application/UseCases/Transfers/GetDownloadAllUrlHandler.cs:120` builds `{baseUrl}/zip/{id}`. `src/wa.workers/host.json` sets no `extensions.http.routePrefix`, so the Functions host serves HTTP triggers under the default `api/` prefix. Base URLs carry no `/api` (`infra/bicep/modules/apps.bicep:119`, `docker-compose.local.yml:58`), so the caller must append `api/`. No config/infra change.
2. **Route value never bound.** `src/wa.workers/ZipFunction.cs:54` declares `Route = "zip/{transferId}"`, but the parameter is `string transferIdParam`. The isolated worker binds route values by parameter **name**, so the value is always null and `Guid.TryParse` (line 70) answers 404 every time. Fix: rename the parameter to `transferId`.

**Decision D1: a shared `ZipHandOff` contract class in wa.application.** This mirrors the `EmailHandOff` shape from the unmerged `feature/CHORE-local-email` branch (constants plus a pure helper). Where it lives:
- The caller is in wa.application. wa.workers references wa.infrastructure (`src/wa.workers/wa.workers.csproj:18`), which pulls in wa.application and wa.domain transitively. So wa.application is the lowest layer both sides can reach. This respects the onion dependency rule (AGENT.md §3 rule 7). wa.domain would also work, but HTTP routing is not a domain concept.
- File: `src/wa.application/UseCases/Transfers/ZipHandOff.cs`, next to its only caller (namespace `wa.application.UseCases.Transfers`). ZipFunction already has `using wa.application.UseCases.Transfers;` (`src/wa.workers/ZipFunction.cs:12`).
- Members, kept minimal:
  - `public const string RoutePrefix = "api";` (the Functions host default prefix; host.json sets none).
  - `public const string Route = "zip/{transferId}";` (the worker's `[HttpTrigger(Route = ZipHandOff.Route)]` uses it. Attribute arguments must be constants, so this works and the two sides cannot drift.)
  - `public static string BuildTriggerUrl(string baseUrl, Guid transferId)` returns `$"{baseUrl.TrimEnd('/')}/{RoutePrefix}/{Route.Replace("{transferId}", transferId.ToString("n"), StringComparison.Ordinal)}"`.
- Not moved (out of scope): the `Wa:ZipFunction:BaseUrl` / `Wa:Zip:SignKey` config keys and the `x-zip-sign` header name. Moving them would be a drive-by refactor. They are listed under Open Questions.

**Decision D2: testability through the pure builder, with no DI change.** `TriggerZipGeneration` keeps its inline `new HttpClient()` and fire-and-forget shape, because adding IHttpClientFactory is out of scope. The URL becomes the result of one pure static call. That call is unit-tested in wa.application.unit, so the exact wire URL is pinned without network mocking.

**Decision D3: a reflection regression test in wa.workers.unit.** It asserts two things: (a) the `HttpTriggerAttribute.Route` on `ZipFunction.Run` equals `ZipHandOff.Route`; (b) every `{placeholder}` in that route has a parameter on `Run` with exactly that name. Check (b) would have caught defect 2, and it is generic, so later route templates are covered too. wa.workers.unit already sees Functions Worker types through its project reference (`tests/wa.workers.unit/ZipFunctionCorrelationTests.cs:4`). `HttpTriggerAttribute` lives in namespace `Microsoft.Azure.Functions.Worker` (package `Microsoft.Azure.Functions.Worker.Extensions.Http`, already referenced at `src/wa.workers/wa.workers.csproj:7`). No new packages (golden rule 1).

TA references: TA-6.6 step 1 (f-zip `POST /zip/{transferId}`, route relative to the host prefix), TA-6.2 (f-zip is an HTTP trigger), TA-7.2 (download-all triggers zip generation). The log messages do not change, so the PII rules (TA-9.4) are unaffected.

## 3. Implementation Plan

1. **Create the contract.** `src/wa.application/UseCases/Transfers/ZipHandOff.cs`: a `public static class ZipHandOff` with XML docs (`RoutePrefix`, `Route`, `BuildTriggerUrl` as in D1). Verify: `dotnet build src/wa.application`.
2. **Fix the caller.** `src/wa.application/UseCases/Transfers/GetDownloadAllUrlHandler.cs:120`: replace the interpolated string with `ZipHandOff.BuildTriggerUrl(baseUrl, transferId)`. Change nothing else (headers, logging and fire-and-forget stay as they are).
3. **Fix the worker binding.** `src/wa.workers/ZipFunction.cs`:
   - line 54: `Route = ZipHandOff.Route`, and rename the parameter `string transferIdParam` to `string transferId`;
   - line 70: `Guid.TryParse(transferId, out var id)`;
   - line 49: rename the `<param name="transferIdParam">` doc tag to `transferId`. Line 46 summary: say `POST api/zip/{transferId}`. Line 69 comment: keep it accurate.
   - Check that no local in `Run` already uses the name `transferId`. If one does, rename the local, never the parameter. Existing callers pass the value positionally (`tests/wa.workers.unit/ZipFunctionCorrelationTests.cs:241,254,276`), so they need no change.
   Verify: `dotnet build src/wa.workers`.
4. **Application test.** `tests/wa.application.unit/Transfers/ZipHandOffTests.cs` (namespace `wa.application.unit.Transfers`, xunit):
   - `BuildTriggerUrl_AppendsApiPrefixAndNFormatId`: base `http://host.docker.internal:8081`, fixed Guid → `http://host.docker.internal:8081/api/zip/<32-hex>`.
   - `BuildTriggerUrl_TrimsTrailingSlash`: base `https://fa.example.net/` → exactly one `/` before `api` (`https://fa.example.net/api/zip/<32-hex>`).
   - `BuildTriggerUrl_DoesNotDoublePrefix_WhenBaseHasNoApi`: assert that the result contains `/api/zip/` exactly once and does not contain `/api/api/`.
   - `Route_IsZipTransferIdTemplate`: `Assert.Equal("zip/{transferId}", ZipHandOff.Route)` and `Assert.Equal("api", ZipHandOff.RoutePrefix)`.
   Verify: dotnet test tests/wa.application.unit (filter: FullyQualifiedName~ZipHandOff).
5. **Workers route test.** `tests/wa.workers.unit/ZipFunctionRouteTests.cs` (namespace `wa.workers.unit`):
   - `Run_TriggerRoute_EqualsSharedContract`: `typeof(ZipFunction).GetMethod(nameof(ZipFunction.Run))`. Find the parameter whose `GetCustomAttribute<HttpTriggerAttribute>()` is non-null, then assert `attr.Route == ZipHandOff.Route`.
   - `Run_EveryRoutePlaceholder_HasMatchingParameterName`: match `\{(\w+)(:[^}]*)?\}` with `System.Text.RegularExpressions.Regex` over the route, collect the names, and assert that each one is in `Run`'s `GetParameters().Select(p => p.Name)` (ordinal). The current bug fails this assertion with "transferId".
   - `Run_TriggerIsPostOnly`: assert `attr.Methods` contains exactly "post" (case-insensitive). This guards the method the caller uses.
   Verify: `dotnet test tests/wa.workers.unit`.
6. **Gate.** `dotnet test tests/wa.application.unit`, `dotnet test tests/wa.workers.unit`, `dotnet test tests/wa.domain.unit` (a sanity check, untouched). wa.api.integration is not affected: no integration test references the zip trigger URL (I grepped tests for `/zip/`, `ZipFunction:BaseUrl` and `GetDownloadAllUrl` and found nothing). Then one `feat: CHORE-zip-route-prefix …` commit containing exactly the 5 files below.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Transfers/ZipHandOff.cs` | CREATE | Static contract: `RoutePrefix = "api"`, `Route = "zip/{transferId}"`, pure `BuildTriggerUrl(string baseUrl, Guid transferId)`. |
| `src/wa.application/UseCases/Transfers/GetDownloadAllUrlHandler.cs` | MODIFY | Line 120: build the request URL with `ZipHandOff.BuildTriggerUrl(baseUrl, transferId)`. |
| `src/wa.workers/ZipFunction.cs` | MODIFY | Line 54: `Route = ZipHandOff.Route`, parameter `transferIdParam` renamed to `transferId`. Line 70 uses the new name. Fix the docs at lines 46/49/69. |
| `tests/wa.application.unit/Transfers/ZipHandOffTests.cs` | CREATE | URL builder tests (api prefix, n-format id, trailing slash, no double prefix, constants). |
| `tests/wa.workers.unit/ZipFunctionRouteTests.cs` | CREATE | Reflection: trigger Route == `ZipHandOff.Route`; every route placeholder has a same-named `Run` parameter; POST only. |

**`src/wa.application/UseCases/Transfers/GetDownloadAllUrlHandler.cs`**: the current code (lines 107–132):

```csharp
    private async Task TriggerZipGeneration(Guid transferId, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _config["Wa:ZipFunction:BaseUrl"];
            if (string.IsNullOrEmpty(baseUrl))
            {
                LogZipBaseUrlNotConfigured(_logger, transferId);
                return;
            }

            var signKey = _config["Wa:Zip:SignKey"] ?? string.Empty;
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/zip/{transferId.ToString("n")}");
            request.Headers.Add("x-zip-sign", signKey);
            // T-012-01: forward the ambient W3C traceparent ...
            request.Headers.Add("traceparent", CorrelationContext.CurrentRequest?.ToHeader() ?? W3CTraceParser.New().ToHeader());

            await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) { LogZipTriggerFailed(_logger, ex, transferId); }
    }
```

Change line 120 only, to `new HttpRequestMessage(HttpMethod.Post, ZipHandOff.BuildTriggerUrl(baseUrl, transferId))`. The handler and `ZipHandOff` share a namespace, so no `using` is needed. Optionally add a short `CHORE-zip-route-prefix` comment.

**`src/wa.workers/ZipFunction.cs`**: the current code (lines 46–71):

```csharp
    /// <summary>HTTP entry point <c>POST zip/{transferId}</c>: ...</summary>
    /// <param name="context">Function invocation context.</param>
    /// <param name="req">The HTTP request, ...</param>
    /// <param name="transferIdParam">Route value: id of the transfer to zip.</param>
    /// <returns>...</returns>
    [Function("Zip")]
    public async Task<HttpResponseData> Run(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "zip/{transferId}")] HttpRequestData req, string transferIdParam)
    {
        ...
        // Parse transferId as Guid → 404 if malformed (bound from route template zip/{transferId})
        if (!Guid.TryParse(transferIdParam, out var id))
            return CreateProblemResponse(req, 404, ProblemCodes.NotFound, "Transfer not found.");
```

Context:
- The `using wa.application.UseCases.Transfers;` at line 12 is already present.
- Keep `AuthorizationLevel.Anonymous`. The x-zip-sign check stays the auth mechanism (TA-6.6 step 1).
- Callers of `Run` are positional only (`tests/wa.workers.unit/ZipFunctionCorrelationTests.cs:241`, `:254`, `:276`), so they need no change.
- The Functions source generator reads `Route` from the attribute metadata. A `const` reference compiles to the same metadata as the literal.

## 4. Open Questions / Risks

- **R1: routePrefix assumption.** The fix assumes host.json keeps the default `api` prefix. If someone later sets `extensions.http.routePrefix`, `ZipHandOff.RoutePrefix` must change too. The XML doc on `RoutePrefix` must say so. A test that reads host.json is possible but is left out (it would be file I/O in a unit test, which is scope creep).
- **R2: local live verification.** Before this fix, the local compose answered 401 for `/api/zip/x` (not signed) and 404 for `/zip/x`. That matches the diagnosis. The orchestrator runs the end-to-end "Download all produces a zip" check after review, which is outside the unit gate.
- **OQ1 (follow-up, not in scope):** fold `Wa:ZipFunction:BaseUrl`, `Wa:Zip:SignKey` and `x-zip-sign` into `ZipHandOff`, as `EmailHandOff` does once `feature/CHORE-local-email` merges.
- **OQ2 (docs):** TA-6.6 step 1 reads `POST /zip/{transferId}`. The orchestrator may add "(host prefix `api/`)" in docs close-out. That is not a Coder file.
