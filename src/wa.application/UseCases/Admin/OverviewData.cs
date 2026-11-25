namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: the four aggregate numbers + recent transfers list (AC-011-1).</summary>
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
