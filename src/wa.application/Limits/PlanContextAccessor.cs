using wa.application.Ports;

namespace wa.application.Limits;

/// <summary>
/// Scoped accessor: the middleware binds the session user (or guest) once; the first GetAsync resolves and memoizes for the request.
/// Guest / missing user row → Free (FR-020-4); soft-deleted user → last-known plan (EC-020-3). Unknown plan code propagates as PlanUnknownException.
/// </summary>
public sealed class PlanContextAccessor(IUserPlanStore users, ILimitsProvider limits, IFeaturesProvider features) : IPlanContextAccessor
{
    private bool _bound;
    private Guid? _userId;
    private Task<PlanContext>? _resolved;

    /// <summary>Binds the session user id (null = guest). Exactly once per scope.</summary>
    public void Bind(Guid? userId)
    {
        if (_bound) throw new InvalidOperationException("PlanContext is already bound for this request.");
        _userId = userId;
        _bound = true;
    }

    public async Task<PlanContext> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!_bound) throw new InvalidOperationException("PlanContext read before it was bound for this request.");

        var task = _resolved ??= ResolveAsync(cancellationToken);
        try
        {
            return await task;
        }
        catch
        {
            // Failures are not cached (same rule as the limits caches): a retry within the request re-resolves.
            if (ReferenceEquals(_resolved, task)) _resolved = null;
            throw;
        }
    }

    private async Task<PlanContext> ResolveAsync(CancellationToken cancellationToken)
    {
        var code = _userId is { } id
            ? await users.GetPlanCodeAsync(id, cancellationToken) ?? ILimitsProvider.GuestPlanCode
            : ILimitsProvider.GuestPlanCode;

        // Sequential on purpose: both providers share singleton caches / DbContext.
        var resolvedLimits = await limits.Resolve(code, cancellationToken);
        var resolvedFeatures = await features.Resolve(code, cancellationToken);
        return new PlanContext(code, resolvedLimits, resolvedFeatures);
    }
}
