namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: the four aggregate numbers + recent transfers list (AC-011-1).</summary>
/// <param name="ActiveTransfers">The active transfers of the overview data.</param>
/// <param name="StorageBytesUsed">The storage bytes used of the overview data.</param>
/// <param name="NewUsersToday">The new users today of the overview data.</param>
/// <param name="EmailsSent7d">The emails sent7d of the overview data.</param>
/// <param name="EmailsFailed7d">The emails failed7d of the overview data.</param>
/// <param name="EmailPendingCount">The email pending count of the overview data.</param>
/// <param name="EmailFailedTotal">The email failed total of the overview data.</param>
/// <param name="LastSuccessfulDeliveryAge">The last successful delivery age of the overview data.</param>
/// <param name="RecentTransfers">The recent transfers of the overview data.</param>
public record OverviewData(
    long ActiveTransfers,
    long StorageBytesUsed,
    int NewUsersToday,
    int EmailsSent7d,
    int EmailsFailed7d,
    int EmailPendingCount,
    int EmailFailedTotal,
    DateTime? LastSuccessfulDeliveryAge,
    IReadOnlyList<RecentTransferItem> RecentTransfers);
