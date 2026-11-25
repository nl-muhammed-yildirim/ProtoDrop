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
/// CHORE-code-style-2 (TA-2.1 / TA-2.4): the phase-gate features route, moved verbatim from Program.cs in source order.
/// </summary>
internal static class FeatureEndpoints
{
    /// <summary>
    /// Maps the phase-gate features route. Registration order is the former Program.cs order.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/>, so calls can be chained.</returns>
    public static IEndpointRouteBuilder MapFeatureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // US-044-02 (FR-044-7, AC-044-2): the six phase-gate booleans for the SPA's FeatureGate — anonymous, no PII, no telemetry (TA-10.2).
        endpoints.MapGet("/api/v1/features", async (ISender sender, CancellationToken ct) =>
            Results.Json(await sender.Send(new GetFeatureGatesQuery(), ct)));

        return endpoints;
    }
}
