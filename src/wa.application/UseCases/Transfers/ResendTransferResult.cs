using wa.application.Limits;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/resend result — the new draft with pre-filled fields for T-010-03's UI.</summary>
/// <param name="TransferId">Id of the new draft transfer.</param>
/// <param name="LinkId">Public 8-character link id of the new draft.</param>
/// <param name="Status">Stored status number of the draft.</param>
/// <param name="ExpiresAtUtc">UTC instant the draft would expire; set again at send time.</param>
/// <param name="TotalBytes">Total size of the files in bytes.</param>
/// <param name="FileCount">Number of files in the transfer.</param>
/// <param name="Recipients">Addresses copied from the original, for the sender to edit; PII (TA-9.4).</param>
/// <param name="Note">Note copied from the original; null when none.</param>
/// <param name="SenderName">Sender name copied from the original; PII (TA-9.4).</param>
/// <param name="SenderEmail">Sender address copied from the original; null for anonymous senders; PII (TA-9.4).</param>
/// <param name="PasswordRequired">True when the original is password protected and the copy keeps that password.</param>
/// <param name="Files">The files of the new draft, which share the original's blobs.</param>
public sealed record ResendTransferResult(
    Guid TransferId,
    string LinkId,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<string> Recipients,
    string? Note,
    string SenderName,
    string? SenderEmail,
    bool PasswordRequired,
    IReadOnlyList<ResendFileItem> Files);
