using MediatR;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-03 (TA-4.2#20 / FR-011-6): GET /api/v1/admin/overview — four aggregates + recent transfers list, cached 5 min.
/// Email counts are the documented approximation (US-011-01 Technical notes): sent = count of EmailRecipient.NotifiedAtUtc in the last 7 days; failed = count of email.failed outbox rows in the last 7 days.
/// </summary>
public sealed record GetOverviewQuery : IRequest<GetOverviewResult>;

/// <summary>T-011-03: flat result — four numbers + snapshot timestamp + recent list (AC-011-1).</summary>
public record GetOverviewResult(
    long ActiveTransfers,
    long StorageBytesUsed,
    int NewUsersToday,
    int EmailsSent7d,
    int EmailsFailed7d,
    int EmailPendingCount,
    int EmailFailedTotal,
    DateTime? LastSuccessfulDeliveryAge,
    DateTime SnapshotAtUtc,
    IReadOnlyList<RecentTransferItem> RecentTransfers);

/// <summary>T-011-03: handler — returns the cached snapshot (no store call here; the cache owns the TTL).</summary>
public sealed class GetOverviewQueryHandler(OverviewSnapshotCache cache) : IRequestHandler<GetOverviewQuery, GetOverviewResult>
{
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
