using MediatR;
using wa.application.UseCases.Health;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the "/" hello route, GET /health and the Development-only chaos route, moved verbatim from Program.cs in source order.
/// </summary>
internal static class SystemEndpoints
{
    /// <summary>
    /// Maps the "/" hello route, GET /health and the Development-only chaos route. Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="isDevelopment">True when the host environment is Development; only then the chaos route is mapped.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
    {
        endpoints.MapGet("/", () => "Hello World!");
        endpoints.MapGet("/health", async (ISender sender, CancellationToken ct) =>
        {
            try
            {
                var result = await sender.Send(new HealthQuery(), ct);
                return Results.Json(new { status = result.Status, db = result.Db, sb = result.Sb });
            }
            catch (Exception)
            {
                // Reason is logged by the handler (T-050-04). Body is TA-4.1.3 Problem+JSON via the shared writer; code list is closed — no DB_DOWN, so INTERNAL.
                // 503 is an explicit-status override: ProblemCodes pins INTERNAL at 500, but component-unreachable answers ServiceUnavailable (HealthFailureTests' contract).
                return Results.Json(ProblemWriter.Build(503, ProblemCodes.Internal, "A required component is unreachable."), statusCode: 503);
            }
        });

        // T-013-01 (FR-013-2): dev-only chaos endpoint — forces both failure shapes for E2E without prod surface.
        // kind=internal → InvalidOperationException → GlobalErrorMiddleware → 500 INTERNAL Problem+JSON.
        // kind=rate_limited → ProblemWriter.WriteCompleteAsync(429, RATE_LIMITED) — byte-identical to the real limiter breach path (L115).
        if (isDevelopment)
        {
            endpoints.MapGet("/api/v1/dev/chaos", async (HttpContext httpContext, HttpRequest request) =>
            {
                var kind = request.Query["kind"].FirstOrDefault();
                switch (kind)
                {
                    case "internal":
                        throw new InvalidOperationException("T-013-01 chaos: forced internal failure"); // bubbles to GlobalErrorMiddleware → 500 INTERNAL
                    case "rate_limited":
                        await ProblemWriter.WriteCompleteAsync(httpContext, 429, ProblemCodes.RateLimited, "Too many requests — limit is 5 per minute.");
                        return; // WriteCompleteAsync already commits headers + writes the body — do NOT also return Results.Json (double-serialize)
                    default:
                        throw new BusinessException(ProblemCodes.Validation, "kind must be one of internal / rate_limited.");
                }
            });
        }

        return endpoints;
    }
}
