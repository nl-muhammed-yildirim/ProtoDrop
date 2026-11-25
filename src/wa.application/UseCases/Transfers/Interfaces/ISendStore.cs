using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-002-04: minimal DB surface the send handler needs.
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface ISendStore
{
    /// <summary>Finds the transfer by ID.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The transfer, or <see langword="null"/> when there is none.</returns>
    Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct);
    /// <summary>Saves the send.</summary>
    /// <param name="transfer">The transfer.</param>
    /// <param name="recipients">The recipients.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct);

    /// <summary>T-010-01: read all recipient email addresses stored for a transfer.</summary>
    /// <remarks>
    /// T-010-01 (AC-010-1): three new members for the re-send use case — no changes to existing signatures.
    /// </remarks>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching text value items.</returns>
    Task<IReadOnlyList<string>> GetRecipientAddressesAsync(Guid transferId, CancellationToken ct);

    /// <summary>T-010-01 (AC-010-1): list file item rows (metadata only, no EF entities) ordered by SortOrder.</summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching file item row items.</returns>
    Task<IReadOnlyList<FileItemRow>> ListFileItemsByTransferAsync(Guid transferId, CancellationToken ct);

    /// <summary>
    /// T-010-01 (AC-010-1): one transaction — insert the new draft Transfer + new FileItem rows + the materialized
    /// snapshot EmailRecipient rows (the editable pending state US-010-02 reads), then increment RefCount on each shared
    /// BlobRef via guarded ExecuteUpdate (same raw-SQL guard style as PublicTransferStore.RecordSingleFileDownloadAsync;
    /// rowsAffected &lt; 1 → InvalidOperationException). T-010-02 will insert its FILES_GONE gate inside this same transaction.
    /// </summary>
    /// <param name="draft">The draft.</param>
    /// <param name="fileItems">The file items.</param>
    /// <param name="recipients">The recipients.</param>
    /// <param name="sharedBlobRefs">The shared blob refs.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<EmailRecipient> recipients, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct);
}
