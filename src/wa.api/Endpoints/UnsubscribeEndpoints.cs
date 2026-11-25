using AspNetCoreRateLimit;
using Azure.Storage;
using Azure.Storage.Blobs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Runtime.CompilerServices;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Admin; // T-011-01: AdminRolePolicy (gate + role field)
using wa.application.UseCases.Auth;
using wa.application.UseCases.Features;
using wa.application.UseCases.Health;
using wa.application.UseCases.Sample;
using wa.application.UseCases.Transfers;
using wa.domain; // T-006-03: EmailSuppression entity type for admin routes
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.domain.Telemetry; // T-006-03: admin_action event name for suppression removal
using wa.infrastructure.Blob;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;
using wa.api.Middleware;
using Serilog;
using Serilog.Context;
using Serilog.Events;

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
