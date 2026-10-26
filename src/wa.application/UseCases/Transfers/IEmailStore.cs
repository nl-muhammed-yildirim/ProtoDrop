using wa.application.Ports;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Transfer context needed for email notification (TA-6.7).</summary>
public sealed record EmailTransferContext(
    Guid TransferId,
    string SenderName,
    string? SenderEmail,
    DateTime ExpiresAtUtc,
    string LinkId,
    IReadOnlyList<FileEntry> Files);

/// <summary>Email store port — implemented by EmailStore adapter.</summary>
public interface IEmailStore
{
    Task<EmailTransferContext?> GetTransferWithFilesAsync(Guid transferId, CancellationToken ct);
    Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(Guid transferId, CancellationToken ct);
    Task<IReadOnlySet<string>> GetSuppressedAddressesAsync(IReadOnlyCollection<string> addresses, string? senderEmail, CancellationToken ct);
    Task MarkNotifiedAsync(IReadOnlyCollection<Guid> recipientIds, DateTime notifiedAtUtc, CancellationToken ct);
}
