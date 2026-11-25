namespace wa.application.UseCases.Transfers;

/// <summary>Data needed to re-emit transfer.created after a Scheduled → Active flip (TA-5.3 payload shape).</summary>
public sealed record ActivatedTransfer(
    Guid Id,
    string LinkId,
    long TotalBytes,
    int FileCount,
    string? SenderEmail,
    DateTime ScheduledSendAtUtc,
    IReadOnlyList<string> Emails);
