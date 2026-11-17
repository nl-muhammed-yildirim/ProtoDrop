# Testing Azure Functions Worker HTTP functions (wa.workers)

Use when a T-xxx task adds/modifies a wa.workers function or its unit tests. All five worker functions (Zip/Expire/DeleteTransfers/DeleteBlobs/Email) are HTTP-triggered and share the SDK-specific pitfalls below. This repo uses hand-stubs, NOT Moq.

## Reading request headers: use TryGetValues, not GetValues
`req.Headers.GetValues(name)` THROWS `InvalidOperationException` when the header is absent — it does NOT return null/empty. The class `HttpHeadersCollection` (Worker 2.0.0) is sealed and inherits BCL `System.Net.Http.Headers.HttpHeaders`, whose `GetValues` throws on a missing key. So:
- Always read optional headers via `req.Headers.TryGetValues(name, out var values)` — returns `false` with `values == null` when absent; the existing `if (values is null) return false` guard then works as intended.
- A pre-existing pattern of `GetValues("x-job-sign")` + a null check has the same latent 500-instead-of-401 bug — fix it in the same pass you touch that function, and mirror the fix to sibling auth checks (JobAuth.cs) so the class stays consistent.
- To confirm absence behavior rather than guessing: instantiate `new HttpHeadersCollection()` in a scratch program and call both accessors (see SDK dump below).

## Getting exact SDK type shapes for hand-rolled test stubs
When a new worker-function test must construct an HttpRequestData/HttpResponseData/FunctionContext and the build fails with CS0535 (missing interface members) or wrong-ctor errors, STOP guessing the shapes — dump them from the real SDK:
1. Create a scratch console project that `<ProjectReference>`s `src/wa.workers/wa.workers.csproj` (it pins Worker 2.0.0, so you get the exact versions in play).
2. In Program.cs, reflect over the types to print base-ctor signatures + abstract members, OR just instantiate and call to learn runtime behavior (e.g. `h.TryGetValues("missing", out var v)` → ok=false, values=NULL).
3. Mirror those shapes in your stubs. Known-correct shapes for Worker 2.0.0:
   - HttpRequestData / HttpResponseData base ctors take a FunctionContext parameter.
   - FunctionContext is abstract — properties InvocationId, FunctionId, TraceContext, BindingContext, RetryContext, InstanceServices (IServiceProvider?), FunctionDefinition, Items (IDictionary<object,object>). Stub all of them.
   - HttpCookies is abstract: override Append(string,string), Append(IHttpCookie), CreateNew().
   - IChangeToken requires HasChanged + ActiveChangeCallbacks (bool props) and RegisterChangeCallback(Action<object?>, object?) returning IDisposable. NullChangeToken is NOT in this Primitives version — use an inline no-op change-token stub instead.
4. Delete the scratch project before committing (it lives under scratch/, not the repo).

## Correlation scope for worker functions (MEL only)
wa.workers has NO Serilog reference — correlation goes through Microsoft.Extensions.Logging BeginScope, NOT Serilog. Set it once per invocation: `_logger.BeginScope(new Dictionary<string,object?> { ["CorrelationId"] = tp.CorrelationId, ["TraceParent"] = tp.ToHeader() })`. The property names must match the API's Serilog keys (CorrelationId/TraceParent) or log lines won't join. Derive the W3C traceparent from the envelope CorrelationId when it is a 32-hex W3C trace-id (request-published events encode it via GetCorrelationGuid()), otherwise generate fresh; parent-id must be exactly 16 hex (`Guid.NewGuid().ToString("n")[..16]`) — W3CTraceParser.Parse rejects a 32-char parent-id.
