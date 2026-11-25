using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Email store port — implemented by EmailStore adapter.</summary>
public interface IEmailStore
{
    /// <summary>Gets the transfer with files.</summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The email transfer context, or <see langword="null"/> when there is none.</returns>
    Task<EmailTransferContext?> GetTransferWithFilesAsync(Guid transferId, CancellationToken ct);
    /// <summary>Gets the recipients.</summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching email recipient items.</returns>
    Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(Guid transferId, CancellationToken ct);
    /// <summary>Gets the suppressed addresses.</summary>
    /// <param name="addresses">The addresses.</param>
    /// <param name="senderEmail">The sender email.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching text value items.</returns>
    Task<IReadOnlySet<string>> GetSuppressedAddressesAsync(IReadOnlyCollection<string> addresses, string? senderEmail, CancellationToken ct);
    /// <summary>Marks the notified.</summary>
    /// <param name="recipientIds">Identifiers of the recipient.</param>
    /// <param name="notifiedAtUtc">The notified at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkNotifiedAsync(IReadOnlyCollection<Guid> recipientIds, DateTime notifiedAtUtc, CancellationToken ct);
}
