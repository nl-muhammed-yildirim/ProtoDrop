using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Email store port — implemented by EmailStore adapter.</summary>
public interface IEmailStore
{
    Task<EmailTransferContext?> GetTransferWithFilesAsync(Guid transferId, CancellationToken ct);
    Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(Guid transferId, CancellationToken ct);
    Task<IReadOnlySet<string>> GetSuppressedAddressesAsync(IReadOnlyCollection<string> addresses, string? senderEmail, CancellationToken ct);
    Task MarkNotifiedAsync(IReadOnlyCollection<Guid> recipientIds, DateTime notifiedAtUtc, CancellationToken ct);
}
