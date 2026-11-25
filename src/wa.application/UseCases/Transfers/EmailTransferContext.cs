using wa.application.Ports;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Transfer context needed for email notification (TA-6.7). OwnerAppUserId = US-021-02 branding gate input (null for guest sends). US-022-04: Status + ScheduledSendAtUtc feed the f-email notify gate (defaults = legacy immediate send).</summary>
public sealed record EmailTransferContext(
    Guid TransferId,
    string SenderName,
    string? SenderEmail,
    DateTime ExpiresAtUtc,
    string LinkId,
    IReadOnlyList<FileEntry> Files,
    Guid? OwnerAppUserId = null,
    byte Status = TransferStatus.Active,
    DateTime? ScheduledSendAtUtc = null);
