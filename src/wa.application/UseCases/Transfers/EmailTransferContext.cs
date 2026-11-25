using wa.application.Ports;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Transfer context needed for email notification (TA-6.7). OwnerAppUserId = US-021-02 branding gate input (null for guest sends). US-022-04: Status + ScheduledSendAtUtc feed the f-email notify gate (defaults = legacy immediate send).</summary>
/// <param name="TransferId">Identifier of the transfer of the email transfer context.</param>
/// <param name="SenderName">The sender name of the email transfer context.</param>
/// <param name="SenderEmail">The sender email of the email transfer context.</param>
/// <param name="ExpiresAtUtc">The expires at UTC of the email transfer context.</param>
/// <param name="LinkId">Identifier of the link of the email transfer context.</param>
/// <param name="Files">The files of the email transfer context.</param>
/// <param name="OwnerAppUserId">Identifier of the owner app user of the email transfer context.</param>
/// <param name="Status">The status of the email transfer context.</param>
/// <param name="ScheduledSendAtUtc">The scheduled send at UTC of the email transfer context.</param>
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
