using wa.application.Ports;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Transfer context needed for email notification (TA-6.7). OwnerAppUserId = US-021-02 branding gate input (null for guest sends). US-022-04: Status + ScheduledSendAtUtc feed the f-email notify gate (defaults = legacy immediate send).</summary>
/// <param name="TransferId">Transfer id.</param>
/// <param name="SenderName">Sender name shown in the email; PII (TA-9.4).</param>
/// <param name="SenderEmail">Sender address used as reply-to; null for anonymous senders; PII (TA-9.4).</param>
/// <param name="ExpiresAtUtc">UTC instant the transfer expires, shown as days remaining.</param>
/// <param name="LinkId">Public 8-character link id used to build the download link.</param>
/// <param name="Files">Names and sizes of the files listed in the email.</param>
/// <param name="OwnerAppUserId">Owning user, used for the branding check; null for guest sends.</param>
/// <param name="Status">Stored transfer status byte; defaults to Active, the legacy immediate send.</param>
/// <param name="ScheduledSendAtUtc">Scheduled send time; null for immediate sends.</param>
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
