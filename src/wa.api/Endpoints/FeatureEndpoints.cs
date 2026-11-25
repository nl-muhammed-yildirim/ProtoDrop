using MediatR;
using wa.application.UseCases.Features;

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
