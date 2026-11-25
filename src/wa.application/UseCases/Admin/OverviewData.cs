namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: the aggregate numbers of the admin overview plus the recent-transfers list (AC-011-1).</summary>
/// <param name="ActiveTransfers">Number of transfers currently active.</param>
/// <param name="StorageBytesUsed">Total bytes held by active transfers.</param>
/// <param name="NewUsersToday">Accounts created since the start of the current UTC day.</param>
/// <param name="EmailsSent7d">Emails sent in the last seven days.</param>
/// <param name="EmailsFailed7d">Emails that failed in the last seven days.</param>
/// <param name="EmailPendingCount">Emails queued but not yet sent.</param>
/// <param name="EmailFailedTotal">Emails that failed permanently, all-time.</param>
/// <param name="LastSuccessfulDeliveryAge">UTC instant of the last successful email delivery; null when none.</param>
/// <param name="RecentTransfers">The most recently created transfers.</param>
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
