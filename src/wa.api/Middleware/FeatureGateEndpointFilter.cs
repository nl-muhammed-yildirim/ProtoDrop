using Microsoft.AspNetCore.Http;
using wa.application.Limits;
using wa.domain.Errors;

namespace wa.api.Middleware;

/// <summary>
/// US-044-02 (FR-044-3, FR-044-7, AC-044-2): phase-gate endpoint filter. A route registered with <c>.RequireFeature(FeatureGates.X)</c>
/// answers 404 FEATURE_DISABLED (through <see cref="GlobalErrorMiddleware"/>, TA-4.1.3) while the gate flag is off or absent; the handler never runs.
/// One flag read per request, silent by design (no log line, no telemetry — TA-10.2 closed list).
/// </summary>
public static class FeatureGateEndpointFilter
{
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string gateKey) where TBuilder : IEndpointConventionBuilder
    {
        if (!FeatureGates.All.Contains(gateKey, StringComparer.Ordinal))
            throw new ArgumentException($"'{gateKey}' is not a phase-gate key (see FeatureGates.All).", nameof(gateKey)); // startup fail-fast: no typo'd gates

        builder.AddEndpointFilter(async (context, next) =>
        {
            var flags = context.HttpContext.RequestServices.GetRequiredService<IFlagResolver>();
            var on = await flags.ResolveFlag(gateKey, FeatureGates.DefaultWhenAbsent, context.HttpContext.RequestAborted);
            if (!on)
                throw new BusinessException(ProblemCodes.FeatureDisabled, ProblemCodes.TitleOf(ProblemCodes.FeatureDisabled));
            return await next(context);
        });
        return builder;
    }
}
