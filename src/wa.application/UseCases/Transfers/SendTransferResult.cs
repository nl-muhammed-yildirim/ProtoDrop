namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/send result (TA-4.2#3 response shape).</summary>
/// <param name="TransferId">Id of the sent transfer.</param>
/// <param name="LinkId">Public 8-character link id.</param>
/// <param name="Status">Stored status number: active for an immediate send, scheduled for a later one.</param>
/// <param name="ExpiresAtUtc">UTC instant the transfer expires, stamped at send time from the plan's retention.</param>
/// <param name="TotalBytes">Total size of the files in bytes.</param>
/// <param name="FileCount">Number of files in the transfer.</param>
/// <param name="Recipients">Normalized, de-duplicated recipient addresses; PII (TA-9.4).</param>
/// <param name="ScheduledSendAtUtc">The scheduled send time; null for an immediate send.</param>
public sealed record SendTransferResult(
    Guid TransferId,
    string LinkId,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<string> Recipients,
    DateTime? ScheduledSendAtUtc = null);
