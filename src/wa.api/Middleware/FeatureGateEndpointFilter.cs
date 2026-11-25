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
    /// <summary>Adds an endpoint filter that answers 404 FEATURE_DISABLED while the given phase gate is off or absent.</summary>
    /// <typeparam name="TBuilder">Endpoint convention builder type, returned for chaining.</typeparam>
    /// <param name="builder">The endpoint or group the gate applies to.</param>
    /// <param name="gateKey">One of the <see cref="FeatureGates"/> keys.</param>
    /// <returns>The same <paramref name="builder"/>, so calls can be chained.</returns>
    /// <exception cref="ArgumentException">Thrown at startup when <paramref name="gateKey"/> is not a known gate key.</exception>
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
