namespace wa.application.UseCases.Transfers;

/// <summary>Data needed to re-emit transfer.created after a Scheduled → Active flip (TA-5.3 payload shape).</summary>
/// <param name="Id">Identifier of the activated transfer.</param>
/// <param name="LinkId">Identifier of the link of the activated transfer.</param>
/// <param name="TotalBytes">The total bytes of the activated transfer.</param>
/// <param name="FileCount">The file count of the activated transfer.</param>
/// <param name="SenderEmail">The sender email of the activated transfer.</param>
/// <param name="ScheduledSendAtUtc">The scheduled send at UTC of the activated transfer.</param>
/// <param name="Emails">The emails of the activated transfer.</param>
public sealed record ActivatedTransfer(
    Guid Id,
    string LinkId,
    long TotalBytes,
    int FileCount,
    string? SenderEmail,
    DateTime ScheduledSendAtUtc,
    IReadOnlyList<string> Emails);
