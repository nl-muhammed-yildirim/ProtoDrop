using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Email store port — implemented by EmailStore adapter.</summary>
public interface IEmailStore
{
    /// <summary>Loads the transfer and its files in the shape the notification email needs.</summary>
    /// <param name="transferId">Transfer id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The context, or <see langword="null"/> when the transfer does not exist.</returns>
    Task<EmailTransferContext?> GetTransferWithFilesAsync(Guid transferId, CancellationToken ct);
    /// <summary>Loads the recipients of a transfer, including those already notified.</summary>
    /// <param name="transferId">Transfer id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The recipient rows; empty when the transfer has none.</returns>
    Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(Guid transferId, CancellationToken ct);
    /// <summary>Finds which of the given addresses have opted out of mail from the sender or from any sender.</summary>
    /// <param name="addresses">Recipient addresses to check.</param>
    /// <param name="senderEmail">Sender address; suppressions for this sender and general suppressions both count.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The subset of <paramref name="addresses"/> that must not be emailed.</returns>
    Task<IReadOnlySet<string>> GetSuppressedAddressesAsync(IReadOnlyCollection<string> addresses, string? senderEmail, CancellationToken ct);
    /// <summary>Stamps the recipients as notified so a redelivered event does not email them twice.</summary>
    /// <param name="recipientIds">Ids of the recipient rows that were emailed.</param>
    /// <param name="notifiedAtUtc">UTC instant stored as the notification time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkNotifiedAsync(IReadOnlyCollection<Guid> recipientIds, DateTime notifiedAtUtc, CancellationToken ct);
}
