using MediatR;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: handler — returns the cached snapshot (no store call here; the cache owns the TTL).</summary>
/// <param name="cache">Snapshot cache that owns the refresh interval.</param>
public sealed class GetOverviewQueryHandler(OverviewSnapshotCache cache) : IRequestHandler<GetOverviewQuery, GetOverviewResult>
{
    /// <inheritdoc />
    public async Task<GetOverviewResult> Handle(GetOverviewQuery request, CancellationToken cancellationToken)
    {
        var snapshot = await cache.GetAsync(cancellationToken);
        return new GetOverviewResult(
            snapshot.Data.ActiveTransfers,
            snapshot.Data.StorageBytesUsed,
            snapshot.Data.NewUsersToday,
            snapshot.Data.EmailsSent7d,
            snapshot.Data.EmailsFailed7d,
            snapshot.Data.EmailPendingCount,
            snapshot.Data.EmailFailedTotal,
            snapshot.Data.LastSuccessfulDeliveryAge,
            snapshot.SnapshotAtUtc,
            snapshot.Data.RecentTransfers);
    }
}
