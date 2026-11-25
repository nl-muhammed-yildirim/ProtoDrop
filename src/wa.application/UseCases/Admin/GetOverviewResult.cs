namespace wa.application.UseCases.Admin;

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
