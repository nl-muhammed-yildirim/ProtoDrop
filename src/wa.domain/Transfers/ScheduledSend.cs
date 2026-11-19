namespace wa.domain.Transfers;

/// <summary>
/// US-022-04 (FR-022-4, FR-022-5, FR-022-7): pure predicates for the scheduled-send lifecycle.
/// The store SQL (ScheduledSendStore) mirrors <see cref="IsDue"/> / <see cref="CanActivate"/> because EF cannot translate
/// these calls; the integration facts prove the two agree.
/// </summary>
public static class ScheduledSend
{
    /// <summary>True when the row is Scheduled and its due time has been reached (due &lt;= now).</summary>
    public static bool IsDue(byte status, DateTime? scheduledSendAtUtc, DateTime nowUtc) =>
        status == TransferStatus.Scheduled && scheduledSendAtUtc is not null && scheduledSendAtUtc.Value <= nowUtc;

    /// <summary>True when a due Scheduled row may flip to Active: not yet expired (expiry wins, AC-022-5).</summary>
    public static bool CanActivate(byte status, DateTime? scheduledSendAtUtc, DateTime expiresAtUtc, DateTime nowUtc) =>
        IsDue(status, scheduledSendAtUtc, nowUtc) && expiresAtUtc > nowUtc;

    /// <summary>
    /// f-email gate: a transfer created without a schedule keeps the legacy path; a scheduled one may notify only
    /// while its DB status is Active (so never before due, and never once Expired / DownloadLimit / Deleted).
    /// </summary>
    public static bool MayNotify(byte status, DateTime? scheduledSendAtUtc) =>
        scheduledSendAtUtc is null || status == TransferStatus.Active;

    /// <summary>Whole seconds between the due time and the activation time, clamped at 0.</summary>
    public static long LagSeconds(DateTime activatedAtUtc, DateTime dueUtc) =>
        Math.Max(0L, (activatedAtUtc - dueUtc).Ticks / TimeSpan.TicksPerSecond);
}
