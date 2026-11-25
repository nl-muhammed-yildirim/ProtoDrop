namespace wa.application.UseCases.Transfers;

/// <summary>Data needed to re-emit transfer.created after a Scheduled → Active flip (TA-5.3 payload shape).</summary>
/// <param name="Id">Transfer id.</param>
/// <param name="LinkId">Public 8-character link id.</param>
/// <param name="TotalBytes">Total size of the files in bytes.</param>
/// <param name="FileCount">Number of files in the transfer.</param>
/// <param name="SenderEmail">Sender address for replies; null for anonymous senders; PII (TA-9.4).</param>
/// <param name="ScheduledSendAtUtc">The send time the transfer was scheduled for.</param>
/// <param name="Emails">Recipient addresses to notify; PII (TA-9.4).</param>
public sealed record ActivatedTransfer(
    Guid Id,
    string LinkId,
    long TotalBytes,
    int FileCount,
    string? SenderEmail,
    DateTime ScheduledSendAtUtc,
    IReadOnlyList<string> Emails);
