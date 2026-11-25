using wa.application.Limits;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/resend result — the new draft with pre-filled fields for T-010-03's UI.</summary>
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
