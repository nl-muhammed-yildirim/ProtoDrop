using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the public unsubscribe route, moved verbatim from Program.cs in source order.
/// </summary>
internal static class UnsubscribeEndpoints
{
    /// <summary>
    /// Maps the public unsubscribe route. Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapUnsubscribeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // T-006-03 (FR-006-5): public unsubscribe — GET /unsubscribe/{token}. Token = Base64Url(payload).Base64Url(HMACSHA256(payload, Wa:Jwt:Secret)).
        // Payload = "address|senderEmail". On success: insert EmailSuppression (idempotent via UQ_ES) + 200. Tampered/unknown → 404 + warn telemetry.
        endpoints.MapGet("/unsubscribe/{token}", async (string token, wa.infrastructure.Persistence.UnsubscribeService unsubscribe, CancellationToken ct) =>
        {
            var result = await unsubscribe.HandleAsync(token, ct);
            return result.Success
                ? Results.Ok() // 200 — the SPA shows the success card + redirects to /
                : Results.NotFound(ProblemWriter.Build(404, ProblemCodes.NotFound, "Not found.")); // 404 — don't leak detail (TA-9.6)
        });

        return endpoints;
    }
}
