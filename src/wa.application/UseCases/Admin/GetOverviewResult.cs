namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: flat result — four numbers + snapshot timestamp + recent list (AC-011-1).</summary>
/// <param name="ActiveTransfers">The active transfers of the get overview result.</param>
/// <param name="StorageBytesUsed">The storage bytes used of the get overview result.</param>
/// <param name="NewUsersToday">The new users today of the get overview result.</param>
/// <param name="EmailsSent7d">The emails sent7d of the get overview result.</param>
/// <param name="EmailsFailed7d">The emails failed7d of the get overview result.</param>
/// <param name="EmailPendingCount">The email pending count of the get overview result.</param>
/// <param name="EmailFailedTotal">The email failed total of the get overview result.</param>
/// <param name="LastSuccessfulDeliveryAge">The last successful delivery age of the get overview result.</param>
/// <param name="SnapshotAtUtc">The snapshot at UTC of the get overview result.</param>
/// <param name="RecentTransfers">The recent transfers of the get overview result.</param>
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
