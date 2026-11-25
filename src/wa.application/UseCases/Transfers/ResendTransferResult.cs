using wa.application.Limits;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/resend result — the new draft with pre-filled fields for T-010-03's UI.</summary>
/// <param name="TransferId">Identifier of the transfer of the resend transfer result.</param>
/// <param name="LinkId">Identifier of the link of the resend transfer result.</param>
/// <param name="Status">The status of the resend transfer result.</param>
/// <param name="ExpiresAtUtc">The expires at UTC of the resend transfer result.</param>
/// <param name="TotalBytes">The total bytes of the resend transfer result.</param>
/// <param name="FileCount">The file count of the resend transfer result.</param>
/// <param name="Recipients">The recipients of the resend transfer result.</param>
/// <param name="Note">The note of the resend transfer result.</param>
/// <param name="SenderName">The sender name of the resend transfer result.</param>
/// <param name="SenderEmail">The sender email of the resend transfer result.</param>
/// <param name="PasswordRequired">Whether password required of the resend transfer result.</param>
/// <param name="Files">The files of the resend transfer result.</param>
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
